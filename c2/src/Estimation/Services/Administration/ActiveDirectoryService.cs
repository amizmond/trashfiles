using System.DirectoryServices.AccountManagement;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace Estimation.Services.Administration;

public class UserDetailedInfo
{
    public string? DisplayName { get; set; }

    public string? SamAccountName { get; set; }

    public string? EmployeeId { get; set; }

    public string? EmailAddress { get; set; }
}

public enum AdLookupStatus
{
    Found,
    NotFound,
    Failed
}

public sealed record AdLookupResult(AdLookupStatus Status, UserDetailedInfo? User = null)
{
    public static AdLookupResult NotFound { get; } = new(AdLookupStatus.NotFound);

    public static AdLookupResult Failed { get; } = new(AdLookupStatus.Failed);

    public static AdLookupResult Found(UserDetailedInfo user) => new(AdLookupStatus.Found, user);
}

public interface IActiveDirectoryService
{
    Task<AdLookupResult> GetUserAsync(string? windowsUserName, bool skipCache = false);
}

internal sealed record AdAccountName(string? Domain, string Name, bool IsUserPrincipalName)
{
    public string CacheKey => $"ad-user:{Domain}\\{Name}".ToUpperInvariant();

    public static AdAccountName? Parse(string? windowsUserName)
    {
        var name = windowsUserName?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var slashIndex = name.IndexOf('\\');
        if (slashIndex >= 0)
        {
            var domain = name[..slashIndex].Trim();
            var account = name[(slashIndex + 1)..].Trim();
            if (account.Length == 0 || account.Contains('\\'))
            {
                return null;
            }

            return new AdAccountName(domain.Length == 0 ? null : domain, account, false);
        }

        var atIndex = name.IndexOf('@');
        if (atIndex < 0)
        {
            return new AdAccountName(null, name, false);
        }

        if (atIndex == 0 || atIndex == name.Length - 1 || name.IndexOf('@', atIndex + 1) >= 0)
        {
            return null;
        }

        return new AdAccountName(null, name, true);
    }
}

public class ActiveDirectoryService : IActiveDirectoryService
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan AnswerCacheDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FailureCacheDuration = TimeSpan.FromSeconds(30);

    private readonly IMemoryCache _cache;
    private readonly Func<AdAccountName, AdLookupResult> _lookup;
    private readonly TimeSpan _timeout;

    public ActiveDirectoryService(IMemoryCache cache)
        : this(cache, LookupInDirectory, DefaultTimeout)
    {
    }

    internal ActiveDirectoryService(IMemoryCache cache, Func<AdAccountName, AdLookupResult> lookup, TimeSpan timeout)
    {
        _cache = cache;
        _lookup = lookup;
        _timeout = timeout;
    }

    public async Task<AdLookupResult> GetUserAsync(string? windowsUserName, bool skipCache = false)
    {
        var account = AdAccountName.Parse(windowsUserName);
        if (account is null)
        {
            return AdLookupResult.NotFound;
        }

        var key = account.CacheKey;
        if (!skipCache && _cache.TryGetValue(key, out AdLookupResult? cached) && cached is not null)
        {
            return cached;
        }

        var lookup = Task.Run(() => LookupSafely(account, windowsUserName));
        try
        {
            var result = await lookup.WaitAsync(_timeout);
            Remember(key, result);
            return result;
        }
        catch (TimeoutException)
        {
            Log.Warning("Active Directory lookup for {WindowsUserName} did not answer within {Timeout}", windowsUserName, _timeout);
            Remember(key, AdLookupResult.Failed);
            _ = lookup.ContinueWith(
                finished => Remember(key, finished.Result),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
            return AdLookupResult.Failed;
        }
    }

    private void Remember(string key, AdLookupResult result)
    {
        var duration = result.Status == AdLookupStatus.Failed ? FailureCacheDuration : AnswerCacheDuration;
        _cache.Set(key, result, duration);
    }

    private AdLookupResult LookupSafely(AdAccountName account, string? windowsUserName)
    {
        try
        {
            return _lookup(account);
        }
        catch (MultipleMatchesException ex)
        {
            Log.Warning(ex, "Active Directory has more than one account for {WindowsUserName}", windowsUserName);
            return AdLookupResult.NotFound;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Active Directory lookup failed for {WindowsUserName}", windowsUserName);
            return AdLookupResult.Failed;
        }
    }

    private static AdLookupResult LookupInDirectory(AdAccountName account)
    {
        if (!OperatingSystem.IsWindows())
        {
            return AdLookupResult.Failed;
        }

        using var context = account.Domain is null
            ? new PrincipalContext(ContextType.Domain)
            : new PrincipalContext(ContextType.Domain, account.Domain);
        var identityType = account.IsUserPrincipalName ? IdentityType.UserPrincipalName : IdentityType.SamAccountName;
        using var user = UserPrincipal.FindByIdentity(context, identityType, account.Name);

        if (user is null)
        {
            return AdLookupResult.NotFound;
        }

        return AdLookupResult.Found(new UserDetailedInfo
        {
            DisplayName = user.DisplayName,
            SamAccountName = user.SamAccountName,
            EmployeeId = user.EmployeeId,
            EmailAddress = user.EmailAddress,
        });
    }
}

using Estimation.Core.Administration.Services;
using Estimation.Services.Administration;

namespace Estimation.Services.TeamPlanning;

public class SelfServiceContext
{
    public required string WindowsUserName { get; init; }

    public required string DisplayName { get; init; }

    public required int HumanResourceId { get; init; }

    public required IReadOnlyList<SelfServiceTeam> Teams { get; init; }

    public bool IsMemberOf(int teamId) => Teams.Any(t => t.TeamId == teamId);
}

public sealed record SelfServiceResolution(SelfServiceContext? Context, bool DirectoryUnavailable = false)
{
    public static SelfServiceResolution None { get; } = new((SelfServiceContext?)null);

    public static SelfServiceResolution Unavailable { get; } = new(null, true);
}

public interface ISelfServiceResolver
{
    Task<SelfServiceResolution> ResolveAsync();
}

public class SelfServiceResolver : ISelfServiceResolver
{
    private readonly IWindowsAuthService _windowsAuth;
    private readonly IActiveDirectoryService _adService;
    private readonly IAppAuthorizationService _appAuth;

    public SelfServiceResolver(
        IWindowsAuthService windowsAuth,
        IActiveDirectoryService adService,
        IAppAuthorizationService appAuth)
    {
        _windowsAuth = windowsAuth;
        _adService = adService;
        _appAuth = appAuth;
    }

    public async Task<SelfServiceResolution> ResolveAsync()
    {
        var userName = await _windowsAuth.GetUserName();
        if (string.IsNullOrWhiteSpace(userName))
        {
            return SelfServiceResolution.None;
        }

        var lookup = await _adService.GetUserAsync(userName);
        if (lookup.Status == AdLookupStatus.Failed)
        {
            return SelfServiceResolution.Unavailable;
        }

        var adInfo = lookup.User;
        if (adInfo is null
            || (string.IsNullOrWhiteSpace(adInfo.EmployeeId) && string.IsNullOrWhiteSpace(adInfo.SamAccountName)))
        {
            return SelfServiceResolution.None;
        }

        var selfService = await _appAuth.FindSelfServiceUserAsync(adInfo.EmployeeId, adInfo.SamAccountName);
        if (selfService is null)
        {
            return SelfServiceResolution.None;
        }

        return new SelfServiceResolution(new SelfServiceContext
        {
            WindowsUserName = userName,
            DisplayName = string.IsNullOrWhiteSpace(adInfo.DisplayName) ? userName : adInfo.DisplayName,
            HumanResourceId = selfService.HumanResourceId,
            Teams = selfService.Teams
        });
    }
}

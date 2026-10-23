using System.DirectoryServices.AccountManagement;
using System.Runtime.Versioning;
using Estimation.Services.Administration;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Estimation.Tests.Services;

public class ActiveDirectoryServiceTests
{
    private static readonly UserDetailedInfo Jane = new() { DisplayName = "Jane Doe", SamAccountName = "jane", EmployeeId = "E123" };

    [Theory]
    [InlineData(@"CORP\jane", "CORP", "jane", false)]
    [InlineData(@"  CORP \ jane  ", "CORP", "jane", false)]
    [InlineData(@"\jane", null, "jane", false)]
    [InlineData("jane", null, "jane", false)]
    [InlineData("jane@corp.com", null, "jane@corp.com", true)]
    public void A_windows_user_name_is_split_into_domain_and_account(string input, string? domain, string name, bool isUpn)
    {
        var account = AdAccountName.Parse(input);

        Assert.NotNull(account);
        Assert.Equal(domain, account.Domain);
        Assert.Equal(name, account.Name);
        Assert.Equal(isUpn, account.IsUserPrincipalName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"CORP\")]
    [InlineData(@"CORP\ ")]
    [InlineData(@"CORP\sub\jane")]
    [InlineData("@corp.com")]
    [InlineData("jane@")]
    [InlineData("jane@corp@com")]
    public void A_name_that_cannot_be_an_account_is_rejected(string? input)
    {
        Assert.Null(AdAccountName.Parse(input));
    }

    [Fact]
    public void The_cache_key_ignores_case()
    {
        Assert.Equal(AdAccountName.Parse(@"corp\Jane")!.CacheKey, AdAccountName.Parse(@"CORP\jane")!.CacheKey);
        Assert.NotEqual(AdAccountName.Parse(@"CORP\jane")!.CacheKey, AdAccountName.Parse("jane")!.CacheKey);
    }

    [Fact]
    public async Task A_found_user_is_returned_and_cached()
    {
        var calls = 0;
        var service = Create(_ => { calls++; return AdLookupResult.Found(Jane); });

        var first = await service.GetUserAsync(@"CORP\jane");
        var second = await service.GetUserAsync(@"corp\JANE");

        Assert.Equal(AdLookupStatus.Found, first.Status);
        Assert.Same(Jane, first.User);
        Assert.Same(first, second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Skipping_the_cache_asks_the_directory_again()
    {
        var calls = 0;
        var service = Create(_ => { calls++; return AdLookupResult.Found(Jane); });

        await service.GetUserAsync(@"CORP\jane");
        await service.GetUserAsync(@"CORP\jane", skipCache: true);

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task An_unknown_user_is_not_found()
    {
        var service = Create(_ => AdLookupResult.NotFound);

        var result = await service.GetUserAsync(@"CORP\ghost");

        Assert.Equal(AdLookupStatus.NotFound, result.Status);
        Assert.Null(result.User);
    }

    [Fact]
    public async Task A_name_that_cannot_be_an_account_is_not_sent_to_the_directory()
    {
        var calls = 0;
        var service = Create(_ => { calls++; return AdLookupResult.Found(Jane); });

        var result = await service.GetUserAsync(@"CORP\");

        Assert.Equal(AdLookupStatus.NotFound, result.Status);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task A_directory_error_is_a_failure_not_a_missing_user()
    {
        var service = Create(_ => throw new InvalidOperationException("down"));

        var result = await service.GetUserAsync(@"CORP\jane");

        Assert.Equal(AdLookupStatus.Failed, result.Status);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task An_ambiguous_account_is_not_found()
    {
        var service = Create(_ => throw new MultipleMatchesException("two"));

        var result = await service.GetUserAsync(@"CORP\jane");

        Assert.Equal(AdLookupStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task A_failure_is_cached_briefly_so_the_directory_is_not_hammered()
    {
        var calls = 0;
        var service = Create(_ => { calls++; throw new InvalidOperationException("down"); });

        await service.GetUserAsync(@"CORP\jane");
        var second = await service.GetUserAsync(@"CORP\jane");

        Assert.Equal(AdLookupStatus.Failed, second.Status);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task A_slow_directory_fails_after_the_timeout_and_its_late_answer_is_kept()
    {
        using var release = new ManualResetEventSlim();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var calls = 0;
        var service = new ActiveDirectoryService(cache, _ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                release.Wait(TimeSpan.FromSeconds(10));
            }
            return AdLookupResult.Found(Jane);
        }, TimeSpan.FromMilliseconds(50));

        var slow = await service.GetUserAsync(@"CORP\jane");
        Assert.Equal(AdLookupStatus.Failed, slow.Status);

        release.Set();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        AdLookupResult later;
        do
        {
            await Task.Delay(10);
            later = await service.GetUserAsync(@"CORP\jane");
        }
        while (later.Status != AdLookupStatus.Found && DateTime.UtcNow < deadline);

        Assert.Equal(AdLookupStatus.Found, later.Status);
        Assert.Equal(1, calls);
    }

    private static ActiveDirectoryService Create(Func<AdAccountName, AdLookupResult> lookup) =>
        new(new MemoryCache(new MemoryCacheOptions()), lookup, TimeSpan.FromSeconds(5));
}

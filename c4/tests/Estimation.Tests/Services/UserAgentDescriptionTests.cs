using Estimation.Services.Administration;
using Xunit;

namespace Estimation.Tests.Services;

public class UserAgentDescriptionTests
{
    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36 Edg/141.0.0.0", "Edge 141 on Windows")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36", "Chrome 141 on Windows")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:144.0) Gecko/20100101 Firefox/144.0", "Firefox 144 on Windows")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.6 Safari/605.1.15", "Safari 18 on macOS")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_6 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/141.0 Mobile/15E148 Safari/604.1", "Chrome 141 on iOS")]
    [InlineData("Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Mobile Safari/537.36", "Chrome 141 on Android")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36 OPR/125.0.0.0", "Opera 125 on Windows")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Trident/7.0; rv:11.0) like Gecko", "Internet Explorer on Windows")]
    [InlineData("curl/8.4.0", "Unknown browser")]
    public void Common_browsers_get_a_short_name(string userAgent, string expected)
    {
        Assert.Equal(expected, UserAgentDescription.Describe(userAgent));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_user_agent_has_no_description(string? userAgent)
    {
        Assert.Null(UserAgentDescription.Describe(userAgent));
    }
}

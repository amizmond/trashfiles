namespace Estimation.Services.Administration;

public static class UserAgentDescription
{
    public static string? Describe(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return null;
        }

        var browser = Browser(userAgent);
        var os = OperatingSystem(userAgent);
        return os is null ? browser : $"{browser} on {os}";
    }

    private static string Browser(string ua)
    {
        if (TryVersion(ua, "Edg/", out var version) || TryVersion(ua, "EdgA/", out version) || TryVersion(ua, "EdgiOS/", out version))
        {
            return Named("Edge", version);
        }

        if (TryVersion(ua, "OPR/", out version))
        {
            return Named("Opera", version);
        }

        if (TryVersion(ua, "Firefox/", out version) || TryVersion(ua, "FxiOS/", out version))
        {
            return Named("Firefox", version);
        }

        if (TryVersion(ua, "CriOS/", out version) || TryVersion(ua, "Chrome/", out version))
        {
            return Named("Chrome", version);
        }

        if (ua.Contains("Safari/", StringComparison.Ordinal) && TryVersion(ua, "Version/", out version))
        {
            return Named("Safari", version);
        }

        if (ua.Contains("Trident/", StringComparison.Ordinal) || ua.Contains("MSIE ", StringComparison.Ordinal))
        {
            return "Internet Explorer";
        }

        return "Unknown browser";
    }

    private static string? OperatingSystem(string ua)
    {
        if (ua.Contains("Windows", StringComparison.Ordinal))
        {
            return "Windows";
        }

        if (ua.Contains("iPhone", StringComparison.Ordinal) || ua.Contains("iPad", StringComparison.Ordinal))
        {
            return "iOS";
        }

        if (ua.Contains("Android", StringComparison.Ordinal))
        {
            return "Android";
        }

        if (ua.Contains("CrOS", StringComparison.Ordinal))
        {
            return "ChromeOS";
        }

        if (ua.Contains("Macintosh", StringComparison.Ordinal) || ua.Contains("Mac OS X", StringComparison.Ordinal))
        {
            return "macOS";
        }

        if (ua.Contains("Linux", StringComparison.Ordinal))
        {
            return "Linux";
        }

        return null;
    }

    private static string Named(string name, string version)
        => version.Length > 0 ? $"{name} {version}" : name;

    private static bool TryVersion(string ua, string token, out string version)
    {
        var start = ua.IndexOf(token, StringComparison.Ordinal);
        if (start < 0)
        {
            version = string.Empty;
            return false;
        }

        start += token.Length;
        var end = start;
        while (end < ua.Length && char.IsAsciiDigit(ua[end]))
        {
            end++;
        }

        version = ua[start..end];
        return true;
    }
}

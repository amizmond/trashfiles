using Microsoft.Extensions.Options;

namespace Estimation.Services.Shared;

public sealed class ProfilePhotoOptions
{
    public const string SectionName = "ProfilePhotos";

    public string? UrlTemplate { get; set; }
}

public interface IProfilePhotoUrls
{
    bool IsEnabled { get; }

    string? For(string? employeeNumber);
}

public sealed class ProfilePhotoUrls(IOptionsMonitor<ProfilePhotoOptions> options) : IProfilePhotoUrls
{
    private const string Placeholder = "{BRID}";

    public bool IsEnabled => !string.IsNullOrWhiteSpace(options.CurrentValue.UrlTemplate);

    public string? For(string? employeeNumber) => Build(options.CurrentValue.UrlTemplate, employeeNumber);

    internal static string? Build(string? template, string? employeeNumber)
    {
        var url = template?.Trim();
        var brid = employeeNumber?.Trim();
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(brid))
        {
            return null;
        }

        if (!url.Contains("://", StringComparison.Ordinal) && !url.StartsWith('/'))
        {
            url = "//" + url;
        }

        var escaped = Uri.EscapeDataString(brid);
        return url.Contains(Placeholder, StringComparison.OrdinalIgnoreCase)
            ? url.Replace(Placeholder, escaped, StringComparison.OrdinalIgnoreCase)
            : url + escaped;
    }
}

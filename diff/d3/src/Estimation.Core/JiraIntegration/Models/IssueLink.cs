namespace Estimation.Core.JiraIntegration.Models;

public class IssueLink
{
    public const int MaxLinkIdLength = 20;
    public const int MaxKeyLength = 100;
    public const int MaxTextLength = 255;

    public int Id { get; set; }

    public string JiraLinkId { get; set; } = null!;

    public string TypeName { get; set; } = null!;

    public string? OutwardLabel { get; set; }

    public string? InwardLabel { get; set; }

    public string FromKey { get; set; } = null!;

    public string ToKey { get; set; } = null!;
}

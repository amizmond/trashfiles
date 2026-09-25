using Estimation.Core.JiraIntegration.Models;
using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Resources.Models;
using Estimation.Core.Train.Services;

namespace Estimation.Core.JiraIntegration.Client.JiraSync;

public sealed class JiraSyncConvertContext
{
    public required EstimationDbContext Db { get; init; }
    public required ArtMatcher Matcher { get; init; }
    public bool IsNew { get; init; }
    public required IReadOnlyList<Team> Teams { get; init; }
    public required Dictionary<string, Pi> PisByName { get; init; }

    public required List<string> Warnings { get; init; }
}

public interface IJiraValueConverter
{
    Task ApplyAsync(
        JiraIssue target,
        JiraIssueResponse source,
        JiraSyncConvertContext context,
        CancellationToken cancellationToken);
}

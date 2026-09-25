using Estimation.Core.Train.Models;
using Estimation.Core.Train.Services;

namespace Estimation.Core.JiraIntegration.Models;

public sealed record JiraSyncKeyArt(int ArtId, string ArtName, string? Components, string? Labels)
{
    public bool IsFiltered => JiraValueFilter.Parse(Components).IsActive || JiraValueFilter.Parse(Labels).IsActive;

    public string FilterDescription => ArtJiraKey.DescribeFilters(JiraListValues.Parse(Components), JiraListValues.Parse(Labels));
}

public sealed record JiraSyncKeyOverview(
    string JiraKey,
    JiraSyncKey? Settings,
    IReadOnlyList<JiraSyncKeyArt> Arts,
    IReadOnlyList<ArtKeyConflict> Conflicts)
{
    public bool IsUsedByArt => Arts.Count > 0;

    public bool IsConfigured => Settings is not null;
}

public sealed record JiraSyncKeySaveResult(JiraSyncKey Settings, bool WatermarkReset);

public sealed record JiraSyncKeyItemCounts(int Features, int BusinessOutcomes, int PortfolioEpics, int StrategicObjectives)
{
    public int Total => Features + BusinessOutcomes + PortfolioEpics + StrategicObjectives;
}

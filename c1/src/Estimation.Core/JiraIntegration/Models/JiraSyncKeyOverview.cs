using Estimation.Core.Train.Services;

namespace Estimation.Core.JiraIntegration.Models;

public sealed record JiraSyncKeyArt(int ArtId, string ArtName, string? Components, string? Labels)
{
    public bool IsFiltered => !string.IsNullOrWhiteSpace(Components) || !string.IsNullOrWhiteSpace(Labels);

    public string FilterDescription
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Components))
            {
                parts.Add($"components: {string.Join(", ", Split(Components))}");
            }
            if (!string.IsNullOrWhiteSpace(Labels))
            {
                parts.Add($"labels: {string.Join(", ", Split(Labels))}");
            }
            return string.Join("; ", parts);
        }
    }

    private static string[] Split(string csv) =>
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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

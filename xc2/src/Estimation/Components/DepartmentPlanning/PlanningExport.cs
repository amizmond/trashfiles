using System.Globalization;
using Estimation.Core.DepartmentPlanning.Services;
using Estimation.Core.JiraIntegration.Models;
using Estimation.Excel;

namespace Estimation.Components.DepartmentPlanning;

public static class PlanningExport
{
    public const string LineLevel = "ART line";
    public const string TotalLevel = "Total";
    public const string RunsOutHere = "Runs out here";
    public const string OverCapacity = "Yes";

    public static DepartmentPlanningExportInfo Info(PlanningTree tree, bool teamColumns, string department, string pi, string filters, DateTime exportedUtc) =>
        new(
            department,
            pi,
            PlanningFormat.LensName(tree.Lens),
            filters,
            (double)tree.TotalCapacitySp,
            exportedUtc,
            tree.Lens == PlanningLens.Drawdown,
            Teams(tree, teamColumns).Select(t => t.Name).ToList());

    public static List<DepartmentPlanningExportRow> Rows(PlanningTree tree, bool teamColumns)
    {
        var teams = Teams(tree, teamColumns);
        var rows = new List<DepartmentPlanningExportRow>();
        foreach (var root in tree.Roots)
        {
            Collect(tree, teams, root, new Parents(), rows);
        }

        return rows;
    }

    public static DepartmentPlanningExportRow Total(PlanningTree tree, bool teamColumns)
    {
        var total = tree.Total;
        var drawdown = tree.Lens == PlanningLens.Drawdown;
        return new DepartmentPlanningExportRow
        {
            Level = TotalLevel,
            Title = $"{tree.FeaturesShown} of {tree.FeaturesInScope} features shown, each counted once",
            Arts = drawdown ? null : total.Arts.ToString(CultureInfo.InvariantCulture),
            Teams = total.Teams,
            BusinessEpics = total.Features,
            Done = total.Done,
            ProgressPercent = total.Progress * 100,
            Demand = (double)total.DemandSp,
            Planned = (double)total.PlannedSp,
            NotPlanned = (double)total.UnplannedSp,
            Capacity = drawdown ? (double)tree.TotalCapacitySp : null,
            ShareOfCapacityPercent = tree.TotalCapacitySp > 0 ? (double)(total.PlannedSp / tree.TotalCapacitySp) * 100 : null,
            Dependencies = total.Dependencies,
            DependenciesAtRisk = total.DependenciesAtRisk,
            HygieneFailingFeatures = total.HygieneFailingFeatures,
            TeamPlanned = Teams(tree, teamColumns).Select(t => (double?)tree.PlannedSpByTeam.GetValueOrDefault(t.Id)).ToList(),
        };
    }

    private static IReadOnlyList<PlanningTeam> Teams(PlanningTree tree, bool teamColumns) => teamColumns ? tree.Teams : [];

    private sealed record Parents(string? Art = null, string? Objective = null, string? Epic = null, string? Outcome = null);

    private static void Collect(PlanningTree tree, IReadOnlyList<PlanningTeam> teams, PlanningRow row, Parents parents, List<DepartmentPlanningExportRow> rows)
    {
        if (row.Kind == PlanningRowKind.LineMarker)
        {
            rows.Add(new DepartmentPlanningExportRow
            {
                Level = LineLevel,
                Art = parents.Art,
                Title = row.Title,
                Capacity = (double?)row.CapacitySp,
            });
            return;
        }

        var drawdown = tree.Lens == PlanningLens.Drawdown;
        var name = string.IsNullOrEmpty(row.JiraId) ? row.Title : $"{row.JiraId} {row.Title}";
        parents = row.Level switch
        {
            PlanningLevel.Art => parents with { Art = row.Title },
            PlanningLevel.StrategicObjective => parents with { Objective = name },
            PlanningLevel.PortfolioEpic => parents with { Epic = name },
            PlanningLevel.BusinessOutcome => parents with { Outcome = name },
            _ => parents,
        };

        var isFeature = row.Level == PlanningLevel.BusinessEpic;
        rows.Add(new DepartmentPlanningExportRow
        {
            Level = PlanningFormat.LevelName(row.Level),
            Art = parents.Art,
            StrategicObjective = parents.Objective,
            PortfolioEpic = parents.Epic,
            BusinessOutcome = parents.Outcome,
            BusinessEpic = isFeature ? name : null,
            JiraId = row.JiraId,
            Title = row.Title,
            Status = row.Status,
            Rag = RagStatuses.Normalize(row.RagStatus) ?? (isFeature ? null : row.Metrics.WorstRag),
            Ranks = drawdown || row.Ranks.Count == 0 ? null : string.Join(", ", row.Ranks.Select(r => $"{r.ArtAbbreviation} {r.Rank}")),
            Rank = drawdown && row.Ranks.Count > 0 ? row.Ranks[0].Rank : null,
            Line = Line(row),
            Arts = drawdown ? null : string.Join(", ", tree.Arts.Where(a => row.TeamCells.Keys.Any(a.TeamIds.Contains)).Select(a => a.Name)),
            AlsoUnder = row.AlsoUnder.Count == 0 ? null : string.Join(", ", row.AlsoUnder),
            Teams = row.Metrics.Teams,
            BusinessEpics = row.Metrics.Features,
            Done = row.Metrics.Done,
            ProgressPercent = row.Metrics.Progress * 100,
            Demand = (double)row.Metrics.DemandSp,
            Planned = (double)row.Metrics.PlannedSp,
            NotPlanned = (double)row.Metrics.UnplannedSp,
            Drawdown = (double?)row.CumulativePlannedSp,
            OverCapacity = row.CapacityRunsOutHere ? RunsOutHere : row.OverCapacity ? OverCapacity : null,
            Capacity = (double?)row.CapacitySp,
            ShareOfCapacityPercent = row.ShareOfCapacity is { } share ? share * 100 : null,
            Dependencies = row.Metrics.Dependencies,
            DependenciesAtRisk = row.Metrics.DependenciesAtRisk,
            HygieneFailingFeatures = row.Metrics.HygieneFailingFeatures,
            Funding = row.FundingStatus,
            TargetEnd = row.ShownTargetEnd,
            TeamPlanned = teams.Select(t => (double?)ShownCell(row, t)?.PlannedSp).ToList(),
            TeamDrawdown = drawdown ? teams.Select(t => (double?)row.TeamCells.GetValueOrDefault(t.Id)?.CumulativeSp).ToList() : [],
        });

        foreach (var child in row.Children)
        {
            Collect(tree, teams, child, parents, rows);
        }
    }

    private static string? Line(PlanningRow row) =>
        row.Level == PlanningLevel.Art
            ? null
            : row.IsBelowArtLine
                ? "Below"
                : row.Metrics.DemandSp == 0
                    ? null
                    : row.Metrics.UnplannedSp == 0
                        ? "Above"
                        : row.Metrics.PlannedSp == 0
                            ? "Below"
                            : "Partly";

    private static PlanningTeamCell? ShownCell(PlanningRow row, PlanningTeam team) =>
        row.TeamCells.TryGetValue(team.Id, out var cell) && (cell.DemandSp != 0 || cell.CumulativeSp is not null) ? cell : null;
}

using System.Globalization;
using Estimation.Core.DepartmentPlanning.Services;
using Estimation.Core.JiraIntegration.Models;
using MudBlazor;

namespace Estimation.Components.DepartmentPlanning;

public sealed record PlanningColumn(
    string Key,
    string Header,
    string Tooltip,
    PlanningSort? Sort,
    bool Numeric,
    bool DefaultVisible,
    PlanningLens? OnlyLens = null)
{
    public bool DefaultDescending => Numeric && Sort is not PlanningSort.Priority and not PlanningSort.Rag and not PlanningSort.TargetEnd;
}

public static class PlanningColumns
{
    public const string Rank = "rank";
    public const string Status = "status";
    public const string Rag = "rag";
    public const string Epics = "epics";
    public const string Progress = "progress";
    public const string Demand = "demand";
    public const string Planned = "planned";
    public const string Unplanned = "unplanned";
    public const string Cumulative = "cumulative";
    public const string Share = "share";
    public const string Dependencies = "dependencies";
    public const string Hygiene = "hygiene";
    public const string Arts = "arts";
    public const string Teams = "teams";
    public const string TargetEnd = "target";
    public const string Funding = "funding";

    public static readonly IReadOnlyList<PlanningColumn> All =
    [
        new(Rank, "Rank", "Position of the portfolio epic or business outcome in its ART's ranking for this PI. Red means below the ART line: no capacity is reserved for it.", PlanningSort.Priority, false, true),
        new(Status, "Status", "Jira status.", null, false, true),
        new(Rag, "RAG", "Delivery confidence reported in Jira (Red, Amber, Green). A second dot shows the worst RAG among the features underneath.", PlanningSort.Rag, false, true),
        new(Epics, "Features", "Features planned on the teams in scope for this PI, and how many of them are done.", PlanningSort.Features, true, true),
        new(Progress, "Progress", "Share of the features that are done. The bar shows done, in progress, blocked and not started.", PlanningSort.Progress, true, true),
        new(Demand, "Demand", "Story points asked of the teams in scope for this PI.", PlanningSort.Demand, true, true),
        new(Planned, "Planned", "Story points above every line: the teams plan to deliver them in this PI.", PlanningSort.Planned, true, true),
        new(Unplanned, "Not planned", "Story points below a team or ART line: they do not fit into this PI.", PlanningSort.Unplanned, true, true),
        new(Cumulative, "Drawdown", "How much of the ART's capacity is used up to here: planned story points of this item plus every higher-ranked item of the ART, counting every feature on the team boards even while filters or the search hide some. Red once it exceeds the ART's capacity, which is shown next to the ART name and on its line; the ART row shows the ART's total.", null, true, true, PlanningLens.Drawdown),
        new(Share, "Share of capacity", "Planned story points as a share of the capacity: the ART's capacity in the capacity drawdown, the capacity of every team in scope in the hierarchy.", PlanningSort.Share, true, true),
        new(Dependencies, "Dependencies", "Links to other features in Jira. At risk means a dependency is planned out of order or late, by the Dependency Map rules.", PlanningSort.Dependencies, true, true),
        new(Hygiene, "Hygiene issues", "Features failing at least one hygiene rule of their ART, such as missing acceptance criteria or estimates.", PlanningSort.Hygiene, true, true),
        new(Arts, "ARTs", "ARTs whose teams carry the features.", null, false, false, PlanningLens.Objectives),
        new(Teams, "Teams", "Teams carrying the features.", null, true, false),
        new(TargetEnd, "Target end", "Latest target end date of the features, or the item's own target end.", PlanningSort.TargetEnd, false, false),
        new(Funding, "Funding", "Funding status from Jira.", null, false, false),
    ];

    public static IReadOnlyList<PlanningColumn> For(PlanningLens lens) =>
        All.Where(c => c.OnlyLens is null || c.OnlyLens == lens).ToList();

    public static HashSet<string> Defaults() => All.Where(c => c.DefaultVisible).Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
}

public static class PlanningFormat
{
    public static string Sp(decimal value) =>
        value == decimal.Truncate(value)
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.#", CultureInfo.InvariantCulture);

    public static string Sp(decimal? value) => value is null ? "—" : Sp(value.Value);

    public static string Days(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    public static string Percent(double? value) =>
        value is null ? "—" : (value.Value * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    public static string Date(DateTime? value) => value?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? "—";

    public static string LevelTag(PlanningLevel level) => level switch
    {
        PlanningLevel.Art => "ART",
        PlanningLevel.StrategicObjective => "SO",
        PlanningLevel.PortfolioEpic => "PE",
        PlanningLevel.BusinessOutcome => "BO",
        _ => "F",
    };

    public static string LevelName(PlanningLevel level) => level switch
    {
        PlanningLevel.Art => "ART",
        PlanningLevel.StrategicObjective => "Strategic objective",
        PlanningLevel.PortfolioEpic => "Portfolio epic",
        PlanningLevel.BusinessOutcome => "Business outcome",
        _ => "Feature",
    };

    public static string LevelPlural(PlanningLevel level) => level switch
    {
        PlanningLevel.Art => "ARTs",
        PlanningLevel.StrategicObjective => "Strategic objectives",
        PlanningLevel.PortfolioEpic => "Portfolio epics",
        PlanningLevel.BusinessOutcome => "Business outcomes",
        _ => "Features",
    };

    public static string RagClass(string? rag) => RagStatuses.Normalize(rag) switch
    {
        RagStatuses.Red => "rag-red",
        RagStatuses.Amber => "rag-amber",
        RagStatuses.Green => "rag-green",
        null => "rag-none",
        _ => "rag-other",
    };

    public static Color StatusColor(PlanningStatusClass statusClass) => statusClass switch
    {
        PlanningStatusClass.Done => Color.Success,
        PlanningStatusClass.Blocked => Color.Error,
        PlanningStatusClass.InProgress => Color.Info,
        _ => Color.Default,
    };

    public static string LensName(PlanningLens lens) => lens == PlanningLens.Drawdown ? "Capacity drawdown" : "Hierarchy";

    public static IReadOnlyList<(int Level, string Label, string Hint)> OpenToLevels(PlanningLens lens) => lens == PlanningLens.Drawdown
        ?
        [
            (0, "ART", "Agile release trains"),
            (1, "PE/BO", "Each ART's ranked portfolio epics or business outcomes, with the ART as a column"),
            (2, "BO", "Business outcomes, with the ART and portfolio epic as columns"),
            (3, "F", "Features, with the ART, portfolio epic and business outcome as columns"),
        ]
        :
        [
            (0, "SO", "Strategic objectives"),
            (1, "PE", "Portfolio epics, with the strategic objective as a column"),
            (2, "BO", "Business outcomes, with the strategic objective and portfolio epic as columns"),
            (3, "F", "Features, with the strategic objective, portfolio epic and business outcome as columns"),
        ];

    public static IReadOnlyList<(PlanningLevel Level, string Name)> LevelPath(PlanningLens lens) => lens == PlanningLens.Drawdown
        ?
        [
            (PlanningLevel.Art, "ART"),
            (PlanningLevel.PortfolioEpic, "ranked portfolio epics / business outcomes"),
            (PlanningLevel.BusinessOutcome, "business outcomes"),
            (PlanningLevel.BusinessEpic, "features"),
        ]
        :
        [
            (PlanningLevel.StrategicObjective, "strategic objective"),
            (PlanningLevel.PortfolioEpic, "portfolio epic"),
            (PlanningLevel.BusinessOutcome, "business outcome"),
            (PlanningLevel.BusinessEpic, "feature"),
        ];

    public static string LevelPathTitle(IEnumerable<(PlanningLevel Level, string Name)> path)
    {
        var title = string.Join(" › ", path.Select(step => step.Name));
        return title.Length == 0 ? title : char.ToUpperInvariant(title[0]) + title[1..];
    }

    public static string AncestorHeader(PlanningLevel level) => level switch
    {
        PlanningLevel.Art => "ART",
        PlanningLevel.StrategicObjective => "Strategic objective",
        PlanningLevel.PortfolioEpic => "Portfolio epic",
        PlanningLevel.BusinessOutcome => "Business outcome",
        _ => LevelName(level),
    };
}

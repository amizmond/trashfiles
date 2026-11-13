using Estimation.HrReporting.Models;

namespace Estimation.HrReporting.Services;

public enum OrgHealthValueKind
{
    Count,
    Percent,
    Split,
    Counts,
    Average
}

public enum OrgHealthBetter
{
    Neither,
    Higher,
    Lower
}

public sealed class OrgHealthKpi
{
    private readonly Func<OrgHealthScope, OrgHealthValue?> _evaluate;

    public OrgHealthKpi(
        string key,
        string category,
        string label,
        OrgHealthValueKind kind,
        OrgHealthBetter better,
        bool shownByDefault,
        string definition,
        Func<OrgHealthScope, OrgHealthValue?> evaluate)
    {
        Key = key;
        Category = category;
        Label = label;
        Kind = kind;
        Better = better;
        ShownByDefault = shownByDefault;
        Definition = definition;
        _evaluate = evaluate;
    }

    public string Key { get; }

    public string Category { get; }

    public string Label { get; }

    public OrgHealthValueKind Kind { get; }

    public OrgHealthBetter Better { get; }

    public bool ShownByDefault { get; }

    public string Definition { get; }

    public bool IsPercent => Kind is OrgHealthValueKind.Percent or OrgHealthValueKind.Split;

    public OrgHealthValue? Evaluate(OrgHealthScope? scope) => scope is null ? null : _evaluate(scope);

    public override string ToString() => Label;
}

public sealed record OrgHealthTargetRule
{
    public static readonly OrgHealthTargetRule None = new(null, null, null);

    private OrgHealthTargetRule(string? text, OrgHealthComparison? comparison, decimal? value)
    {
        Text = text;
        Comparison = comparison;
        Value = value;
    }

    public string? Text { get; }

    public OrgHealthComparison? Comparison { get; }

    public decimal? Value { get; }

    public bool HasRule => Comparison is not null && Value is not null;

    public bool IsEmpty => Text is null && !HasRule;

    public static OrgHealthTargetRule Create(string? text, OrgHealthComparison? comparison, decimal? value)
    {
        var cleanText = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (cleanText is { Length: > OrgHealthTarget.TextMaxLength })
        {
            cleanText = cleanText[..OrgHealthTarget.TextMaxLength].TrimEnd();
        }

        return comparison is null || value is null
            ? new OrgHealthTargetRule(cleanText, null, null)
            : new OrgHealthTargetRule(cleanText, comparison, value);
    }

    public static OrgHealthTargetRule From(OrgHealthTarget stored) => Create(stored.Text, stored.Comparison, stored.Value);

    public bool? IsMet(decimal shown) => !HasRule
        ? null
        : Comparison switch
        {
            OrgHealthComparison.Above => shown > Value,
            OrgHealthComparison.AtLeast => shown >= Value,
            OrgHealthComparison.Below => shown < Value,
            _ => shown <= Value
        };

    public string Display(OrgHealthKpi kpi, IFormatProvider culture)
    {
        if (Text is not null)
        {
            return Text;
        }

        return HasRule ? RuleText(kpi, culture) : "n/a";
    }

    public string RuleText(OrgHealthKpi kpi, IFormatProvider culture) => !HasRule
        ? string.Empty
        : $"{Symbol(Comparison!.Value)} {Value!.Value.ToString("0.##", culture)}{(kpi.IsPercent ? "%" : string.Empty)}";

    public static string Symbol(OrgHealthComparison comparison) => comparison switch
    {
        OrgHealthComparison.Above => ">",
        OrgHealthComparison.AtLeast => "≥",
        OrgHealthComparison.Below => "<",
        _ => "≤"
    };
}

public static class OrgHealthKpis
{
    public const string Workforce = "Workforce";
    public const string Objectives = "Org Health Objectives";
    public const string AgileOrgDesign = "Agile Org Design";
    public const string CountryKeyPrefix = "country:";

    public static readonly IReadOnlyList<string> Categories = [Workforce, Objectives, AgileOrgDesign];

    private static readonly string PermList = string.Join(", ", HeadcountRules.PermTypes);
    private static readonly string TeamRule =
        "Teams as on Teams Analysis: operational ART people with a train, without the trains and teams hidden there.";
    private static readonly string RoleRule =
        $"The agile role is the {HeadcountColumns.TargetAgileRole.Header}, or the Workday {HeadcountColumns.Job.Header} when {HeadcountColumns.TargetAgileRole.Header} is empty.";

    public static readonly OrgHealthKpi Headcount = new(
        "headcount", Workforce, "Overall headcount (excl. non-operational & fixed price)",
        OrgHealthValueKind.Count, OrgHealthBetter.Neither, true,
        $"Operational people, without {HeadcountRules.FixedPriceType}.",
        s => OrgHealthValue.Count(s.Operational - s.FixedPrice));

    public static readonly OrgHealthKpi FixedPrice = new(
        "fixed-price", Workforce, "Fixed price headcount",
        OrgHealthValueKind.Count, OrgHealthBetter.Lower, true,
        $"Operational people whose {HeadcountColumns.ColleagueNonPermType.Header} is {HeadcountRules.FixedPriceType}.",
        s => OrgHealthValue.Count(s.FixedPrice));

    public static readonly OrgHealthKpi NonOperational = new(
        "non-operational", Workforce, "Non-operational headcount",
        OrgHealthValueKind.Count, OrgHealthBetter.Neither, false,
        $"People whose {HeadcountColumns.OperationalStatus.Header} is not {HeadcountRules.Operational}. No other KPI counts them.",
        s => OrgHealthValue.Count(s.People - s.Operational));

    public static readonly OrgHealthKpi PermExclFixedPrice = new(
        "perm-excl-fixed-price", Objectives, "Perm / Non-Perm (excl. fixed price)",
        OrgHealthValueKind.Split, OrgHealthBetter.Higher, true,
        $"Perm ({PermList}) as a share of operational people without fixed price.",
        s => OrgHealthValue.Ratio(OrgHealthValueKind.Split, s.Perm, s.Operational - s.FixedPrice));

    public static readonly OrgHealthKpi PermInclFixedPrice = new(
        "perm-incl-fixed-price", Objectives, "Perm / Non-Perm (incl. fixed price)",
        OrgHealthValueKind.Split, OrgHealthBetter.Higher, true,
        $"Perm ({PermList}) as a share of all operational people, fixed price included.",
        s => OrgHealthValue.Ratio(OrgHealthValueKind.Split, s.Perm, s.Operational));

    public static readonly OrgHealthKpi Engineering = new(
        "engineering", Objectives, "Engineering % / Non-Eng %",
        OrgHealthValueKind.Split, OrgHealthBetter.Higher, true,
        $"Operational people whose {HeadcountColumns.EngNonEng.Header} is {HeadcountRules.Engineering}, as a share of all operational people.",
        s => OrgHealthValue.Ratio(OrgHealthValueKind.Split, s.Engineering, s.Operational));

    public static readonly OrgHealthKpi OutsideTrain = new(
        "outside-train", Objectives, "% Outside of train",
        OrgHealthValueKind.Percent, OrgHealthBetter.Lower, true,
        $"Operational people whose {HeadcountColumns.ArtNonArt.Header} is not {HeadcountRules.Art}, as a share of all operational people.",
        s => OrgHealthValue.Ratio(OrgHealthValueKind.Percent, s.Operational - s.Art, s.Operational));

    public static readonly OrgHealthKpi TeamSize = new(
        "team-size", Objectives, $"% Feature teams sized {HeadcountRules.SmallTeamLimit}-{HeadcountRules.LargeTeamLimit}",
        OrgHealthValueKind.Percent, OrgHealthBetter.Higher, true,
        $"Share of feature teams with {HeadcountRules.SmallTeamLimit} to {HeadcountRules.LargeTeamLimit} people. {TeamRule}",
        s => OrgHealthValue.Ratio(OrgHealthValueKind.Percent, s.TeamsSized, s.Teams, OrgHealthValue.Teams));

    public static readonly OrgHealthKpi CoLocated = new(
        "co-located", Objectives, "% Feature teams co-located (≤ 2 countries)",
        OrgHealthValueKind.Percent, OrgHealthBetter.Higher, true,
        $"Share of feature teams whose people work in one or two countries. {TeamRule}",
        s => OrgHealthValue.Ratio(OrgHealthValueKind.Percent, s.TeamsCoLocated, s.Teams, OrgHealthValue.Teams));

    public static readonly OrgHealthKpi LowFriction = new(
        "low-friction", Objectives, "Team co-location friction (% teams with low or no friction)",
        OrgHealthValueKind.Percent, OrgHealthBetter.Higher, true,
        $"Share of feature teams with no friction (one country) or low friction (two or three countries, neither India nor USA). {TeamRule}",
        s => OrgHealthValue.Ratio(OrgHealthValueKind.Percent, s.TeamsLowFriction, s.Teams, OrgHealthValue.Teams));

    public static readonly OrgHealthKpi AverageTeamSize = new(
        "average-team-size", Objectives, "Average feature team size",
        OrgHealthValueKind.Average, OrgHealthBetter.Neither, false,
        $"People in feature teams divided by the number of teams. {TeamRule}",
        s => OrgHealthValue.Average(s.PeopleInTeams, s.Teams));

    public static readonly OrgHealthKpi TrainsTeams = new(
        "trains-teams", AgileOrgDesign, "# Trains | # Teams",
        OrgHealthValueKind.Counts, OrgHealthBetter.Neither, true,
        $"Trains and feature teams. {TeamRule}",
        s => OrgHealthValue.Many(s.Trains, s.Teams));

    public static readonly OrgHealthKpi Lpm = Role("lpm", "# LPM | in WD", HeadcountRules.LeanPortfolioManager, s => s.Lpm);

    public static readonly OrgHealthKpi Ste = Role("ste", "# STE | in WD", HeadcountRules.SolutionTrainEngineer, s => s.Ste);

    public static readonly OrgHealthKpi Rte = Role("rte", "# RTEs | in WD", HeadcountRules.ReleaseTrainEngineer, s => s.Rte);

    public static readonly OrgHealthKpi ScrumMasters = new(
        "scrum-masters", AgileOrgDesign, "# Scrum Masters | Full time | in WD",
        OrgHealthValueKind.Counts, OrgHealthBetter.Neither, true,
        $"First number: operational people with a Scrum Master agile role. Full time: of them, those who are only Scrum Master (not part time, not combined with another role). In WD: operational people whose Workday Job is {HeadcountRules.ScrumMaster.WorkdayJob}. {RoleRule}",
        s => OrgHealthValue.Many(s.ScrumMasters.People, s.ScrumMasters.FullTime, s.ScrumMasters.InWorkday));

    private static readonly IReadOnlyDictionary<string, OrgHealthTargetRule> Defaults =
        new Dictionary<string, OrgHealthTargetRule>(StringComparer.OrdinalIgnoreCase)
        {
            [PermExclFixedPrice.Key] = OrgHealthTargetRule.Create("75% / 25%", OrgHealthComparison.AtLeast, 75),
            [Engineering.Key] = OrgHealthTargetRule.Create("> 70% / 30%", OrgHealthComparison.Above, 70),
            [OutsideTrain.Key] = OrgHealthTargetRule.Create("< 10%", OrgHealthComparison.Below, 10),
            [TeamSize.Key] = OrgHealthTargetRule.Create("> 50% (tbc)", OrgHealthComparison.Above, 50),
            [CoLocated.Key] = OrgHealthTargetRule.Create("> 75% (tbc)", OrgHealthComparison.Above, 75),
            [LowFriction.Key] = OrgHealthTargetRule.Create("> 50% (tbc)", OrgHealthComparison.Above, 50),
            [Lpm.Key] = OrgHealthTargetRule.Create("4 | 4", null, null),
            [Ste.Key] = OrgHealthTargetRule.Create("4 | 4", null, null)
        };

    public static IReadOnlyList<OrgHealthKpi> Catalog(IEnumerable<string> countries)
    {
        var kpis = new List<OrgHealthKpi> { Headcount, FixedPrice, NonOperational };
        kpis.AddRange(countries.Select(Country));
        kpis.AddRange(
        [
            PermExclFixedPrice,
            PermInclFixedPrice,
            Engineering,
            OutsideTrain,
            TeamSize,
            CoLocated,
            LowFriction,
            AverageTeamSize,
            TrainsTeams,
            Lpm,
            Ste,
            Rte,
            ScrumMasters
        ]);
        return kpis;
    }

    public static OrgHealthKpi Country(string country) => new(
        CountryKeyPrefix + country, Workforce, $"{country} (incl. fixed price)",
        OrgHealthValueKind.Percent, OrgHealthBetter.Neither, true,
        $"Operational people in {country}, fixed price included, as a share of all operational people.",
        s => OrgHealthValue.Ratio(OrgHealthValueKind.Percent, s.Country(country), s.Operational));

    public static OrgHealthTargetRule DefaultTarget(string kpiKey) =>
        Defaults.TryGetValue(kpiKey, out var rule) ? rule : OrgHealthTargetRule.None;

    public static IReadOnlyDictionary<string, OrgHealthTargetRule> Targets(
        IEnumerable<OrgHealthKpi> kpis, IEnumerable<OrgHealthTarget> stored)
    {
        var saved = new Dictionary<string, OrgHealthTargetRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in stored)
        {
            saved[target.KpiKey] = OrgHealthTargetRule.From(target);
        }

        var result = new Dictionary<string, OrgHealthTargetRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var kpi in kpis)
        {
            result[kpi.Key] = saved.TryGetValue(kpi.Key, out var rule) ? rule : DefaultTarget(kpi.Key);
        }

        return result;
    }

    private static OrgHealthKpi Role(string key, string label, AgileRole role, Func<OrgHealthScope, OrgHealthRoleTally> tally) => new(
        key, AgileOrgDesign, label,
        OrgHealthValueKind.Counts, OrgHealthBetter.Neither, true,
        $"First number: operational people whose agile role is {string.Join(" or ", role.Names)}. In WD: operational people whose Workday Job is {role.WorkdayJob}. {RoleRule}",
        s =>
        {
            var t = tally(s);
            return OrgHealthValue.Many(t.People, t.InWorkday);
        });

    internal static string Percent(decimal shown, IFormatProvider culture) =>
        (shown / 100).ToString("0%", culture);
}

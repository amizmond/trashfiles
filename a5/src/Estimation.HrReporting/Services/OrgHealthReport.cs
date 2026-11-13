using Estimation.HrReporting.Models;

namespace Estimation.HrReporting.Services;

public enum OrgHealthTrend
{
    Same,
    Up,
    Down
}

public enum OrgHealthTone
{
    Neutral,
    Good,
    Bad
}

public sealed class OrgHealthRoleTally
{
    public int People { get; internal set; }

    public int FullTime { get; internal set; }

    public int InWorkday { get; internal set; }
}

public sealed class OrgHealthScope
{
    private readonly Dictionary<string, int> _countries = new(StringComparer.OrdinalIgnoreCase);

    internal OrgHealthScope(string? domain) => Domain = domain;

    public string? Domain { get; }

    public int People { get; private set; }

    public int Operational { get; private set; }

    public int FixedPrice { get; private set; }

    public int Perm { get; private set; }

    public int Engineering { get; private set; }

    public int Art { get; private set; }

    public IReadOnlyDictionary<string, int> Countries => _countries;

    public int Trains { get; private set; }

    public int Teams { get; private set; }

    public int PeopleInTeams { get; private set; }

    public int TeamsSized { get; private set; }

    public int TeamsCoLocated { get; private set; }

    public int TeamsLowFriction { get; private set; }

    public OrgHealthRoleTally Lpm { get; } = new();

    public OrgHealthRoleTally Ste { get; } = new();

    public OrgHealthRoleTally Rte { get; } = new();

    public OrgHealthRoleTally ScrumMasters { get; } = new();

    public int Country(string country) => _countries.GetValueOrDefault(country);

    internal void Add(HeadcountRow row)
    {
        People++;
        if (!OrgHealthReport.IsOperational(row))
        {
            return;
        }

        Operational++;
        if (HeadcountRules.IsOneOf(row.ColleagueNonPermType, OrgHealthReport.FixedPriceTypes))
        {
            FixedPrice++;
        }

        if (HeadcountRules.IsOneOf(row.ColleagueNonPermType, HeadcountRules.PermTypes))
        {
            Perm++;
        }

        if (HeadcountRules.IsOneOf(row.EngNonEng, OrgHealthReport.EngineeringValues))
        {
            Engineering++;
        }

        if (OrgHealthReport.IsArt(row))
        {
            Art++;
        }

        var country = HeadcountMetrics.Display(HeadcountMetrics.Key(row.Country));
        _countries[country] = _countries.GetValueOrDefault(country) + 1;

        var roles = HeadcountRules.AgileRoles(row);
        Count(Lpm, HeadcountRules.LeanPortfolioManager, roles, row);
        Count(Ste, HeadcountRules.SolutionTrainEngineer, roles, row);
        Count(Rte, HeadcountRules.ReleaseTrainEngineer, roles, row);
        Count(ScrumMasters, HeadcountRules.ScrumMaster, roles, row);
        if (HeadcountRules.IsFullTimeScrumMaster(roles))
        {
            ScrumMasters.FullTime++;
        }
    }

    internal void AddTeams(TeamsAnalysisDomain domain)
    {
        Trains += domain.Trains;
        Teams += domain.Teams.Count;
        PeopleInTeams += domain.People;
        TeamsSized += domain.Teams.Count(t => t.Size == TeamsAnalysis.SizeBands[1]);
        TeamsCoLocated += domain.Teams.Count(t => t.Locations <= OrgHealthReport.CoLocatedCountries);
        TeamsLowFriction += domain.Teams.Count(t => t.Friction is "none" or "low");
    }

    private static void Count(OrgHealthRoleTally tally, AgileRole role, IReadOnlyList<AgileRoleName> roles, HeadcountRow row)
    {
        if (role.Matches(roles))
        {
            tally.People++;
        }

        if (role.IsWorkdayJob(row))
        {
            tally.InWorkday++;
        }
    }
}

public sealed class OrgHealthSnapshot
{
    private readonly Dictionary<string, OrgHealthScope> _domains;

    internal OrgHealthSnapshot(
        OrgHealthScope total,
        Dictionary<string, OrgHealthScope> domains,
        IReadOnlyList<string> countries,
        int hiddenTeamEntries)
    {
        Total = total;
        _domains = domains;
        Domains = OrgHealthReport.Order(domains.Keys);
        Countries = countries;
        HiddenTeamEntries = hiddenTeamEntries;
    }

    public OrgHealthScope Total { get; }

    public IReadOnlyList<string> Domains { get; }

    public IReadOnlyList<string> Countries { get; }

    public int HiddenTeamEntries { get; }

    public OrgHealthScope? Scope(string? domain) =>
        domain is null ? Total : _domains.GetValueOrDefault(domain.Trim());
}

public sealed class OrgHealthValue
{
    public const string People = "people";
    public const string Teams = "teams";

    private OrgHealthValue(OrgHealthValueKind kind, decimal number, IReadOnlyList<int> counts, int? part, int? whole, string unit)
    {
        Kind = kind;
        Number = number;
        Counts = counts;
        Part = part;
        Whole = whole;
        Unit = unit;
    }

    public OrgHealthValueKind Kind { get; }

    public decimal Number { get; }

    public IReadOnlyList<int> Counts { get; }

    public int? Part { get; }

    public int? Whole { get; }

    public string Unit { get; }

    public decimal Shown => Kind switch
    {
        OrgHealthValueKind.Percent or OrgHealthValueKind.Split => Math.Round(Number * 100, 0, MidpointRounding.AwayFromZero),
        OrgHealthValueKind.Average => Math.Round(Number, 1, MidpointRounding.AwayFromZero),
        _ => Number
    };

    public static OrgHealthValue Count(int value) => new(OrgHealthValueKind.Count, value, [value], null, null, People);

    public static OrgHealthValue? Ratio(OrgHealthValueKind kind, int part, int whole, string unit = People) =>
        whole <= 0 ? null : new OrgHealthValue(kind, (decimal)part / whole, [], part, whole, unit);

    public static OrgHealthValue? Average(int total, int count) =>
        count <= 0 ? null : new OrgHealthValue(OrgHealthValueKind.Average, (decimal)total / count, [], total, count, Teams);

    public static OrgHealthValue Many(params int[] counts) =>
        new(OrgHealthValueKind.Counts, counts.Length == 0 ? 0 : counts[0], counts, null, null, People);

    public string Format(IFormatProvider culture) => Kind switch
    {
        OrgHealthValueKind.Percent => OrgHealthKpis.Percent(Shown, culture),
        OrgHealthValueKind.Split => $"{OrgHealthKpis.Percent(Shown, culture)} / {OrgHealthKpis.Percent(100 - Shown, culture)}",
        OrgHealthValueKind.Average => Shown.ToString("0.0", culture),
        OrgHealthValueKind.Counts => string.Join(" | ", Counts.Select(c => c.ToString("N0", culture))),
        _ => Number.ToString("N0", culture)
    };

    public string? Describe(IFormatProvider culture) => Kind switch
    {
        OrgHealthValueKind.Percent or OrgHealthValueKind.Split when Part is not null && Whole is not null =>
            $"{Part.Value.ToString("N0", culture)} of {Whole.Value.ToString("N0", culture)} {Unit}, {Number.ToString("0.0%", culture)}",
        OrgHealthValueKind.Average when Part is not null && Whole is not null =>
            $"{Part.Value.ToString("N0", culture)} people in {Whole.Value.ToString("N0", culture)} teams",
        _ => null
    };
}

public sealed class OrgHealthCell
{
    public OrgHealthCell(OrgHealthValue? older, OrgHealthValue? newer, OrgHealthTrend trend, OrgHealthTone tone, bool? targetMet)
    {
        Older = older;
        Newer = newer;
        Trend = trend;
        Tone = tone;
        TargetMet = targetMet;
    }

    public OrgHealthValue? Older { get; }

    public OrgHealthValue? Newer { get; }

    public OrgHealthTrend Trend { get; }

    public OrgHealthTone Tone { get; }

    public bool? TargetMet { get; }
}

public sealed class OrgHealthLine
{
    public OrgHealthLine(OrgHealthKpi kpi, OrgHealthTargetRule target, IReadOnlyList<OrgHealthCell> cells)
    {
        Kpi = kpi;
        Target = target;
        Cells = cells;
    }

    public OrgHealthKpi Kpi { get; }

    public OrgHealthTargetRule Target { get; }

    public IReadOnlyList<OrgHealthCell> Cells { get; }
}

public sealed class OrgHealthTable
{
    public static readonly OrgHealthTable Empty = new([], [], false);

    public OrgHealthTable(IReadOnlyList<string?> scopes, IReadOnlyList<OrgHealthLine> lines, bool compared)
    {
        Scopes = scopes;
        Lines = lines;
        Compared = compared;
    }

    public IReadOnlyList<string?> Scopes { get; }

    public IReadOnlyList<OrgHealthLine> Lines { get; }

    public bool Compared { get; }
}

public static class OrgHealthReport
{
    public const int CoLocatedCountries = 2;

    internal static readonly string[] FixedPriceTypes = [HeadcountRules.FixedPriceType];
    internal static readonly string[] EngineeringValues = [HeadcountRules.Engineering];
    private static readonly string[] OperationalValues = [HeadcountRules.Operational];
    private static readonly string[] ArtValues = [HeadcountRules.Art];

    public static bool IsOperational(HeadcountRow row) => HeadcountRules.IsOneOf(row.OperationalStatus, OperationalValues);

    public static bool IsArt(HeadcountRow row) => HeadcountRules.IsOneOf(row.ArtNonArt, ArtValues);

    public static OrgHealthSnapshot Build(
        IReadOnlyCollection<HeadcountRow> rows, IReadOnlyCollection<TeamsAnalysisExclusion> exclusions)
    {
        var total = new OrgHealthScope(null);
        var domains = new Dictionary<string, OrgHealthScope>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var key = HeadcountMetrics.Key(row.ImhL07);
            if (!domains.TryGetValue(key, out var scope))
            {
                scope = new OrgHealthScope(key);
                domains[key] = scope;
            }

            total.Add(row);
            scope.Add(row);
        }

        var teams = TeamsAnalysis.Build(rows.Where(r => IsOperational(r) && IsArt(r)).ToList(), exclusions);
        foreach (var domain in teams.Domains)
        {
            if (domains.TryGetValue(domain.Domain, out var scope))
            {
                scope.AddTeams(domain);
            }

            total.AddTeams(domain);
        }

        var countries = total.Countries
            .OrderByDescending(c => c.Value)
            .ThenBy(c => c.Key == HeadcountMetrics.Blank)
            .ThenBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
            .Select(c => c.Key)
            .ToList();

        return new OrgHealthSnapshot(total, domains, countries, teams.Domains.Sum(d => d.Hidden.Count));
    }

    public static IReadOnlyList<string> Domains(OrgHealthSnapshot newer, OrgHealthSnapshot? older) =>
        Order(older is null ? newer.Domains : newer.Domains.Concat(older.Domains));

    public static IReadOnlyList<string> Countries(OrgHealthSnapshot newer, OrgHealthSnapshot? older)
    {
        var result = newer.Countries.ToList();
        if (older is not null)
        {
            result.AddRange(older.Countries.Where(c => !result.Contains(c, StringComparer.OrdinalIgnoreCase)));
        }

        return result;
    }

    public static OrgHealthTable Compare(
        OrgHealthSnapshot newer,
        OrgHealthSnapshot? older,
        IReadOnlyList<OrgHealthKpi> kpis,
        IReadOnlyList<string?> scopes,
        IReadOnlyDictionary<string, OrgHealthTargetRule> targets)
    {
        var lines = kpis
            .Select(kpi =>
            {
                var target = targets.TryGetValue(kpi.Key, out var rule) ? rule : OrgHealthTargetRule.None;
                var cells = scopes
                    .Select(scope => Cell(kpi, target, older is null ? null : kpi.Evaluate(older.Scope(scope)), kpi.Evaluate(newer.Scope(scope))))
                    .ToList();
                return new OrgHealthLine(kpi, target, cells);
            })
            .ToList();

        return new OrgHealthTable(scopes, lines, older is not null);
    }

    public static OrgHealthCell Cell(OrgHealthKpi kpi, OrgHealthTargetRule target, OrgHealthValue? older, OrgHealthValue? newer)
    {
        var trend = older is null || newer is null || kpi.Kind == OrgHealthValueKind.Counts
            ? OrgHealthTrend.Same
            : newer.Shown > older.Shown ? OrgHealthTrend.Up
            : newer.Shown < older.Shown ? OrgHealthTrend.Down
            : OrgHealthTrend.Same;

        var tone = trend == OrgHealthTrend.Same || kpi.Better == OrgHealthBetter.Neither
            ? OrgHealthTone.Neutral
            : (trend == OrgHealthTrend.Up) == (kpi.Better == OrgHealthBetter.Higher) ? OrgHealthTone.Good
            : OrgHealthTone.Bad;

        return new OrgHealthCell(older, newer, trend, tone, newer is null ? null : target.IsMet(newer.Shown));
    }

    internal static IReadOnlyList<string> Order(IEnumerable<string> domains) =>
        domains
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(d => d.Length == 0)
            .ThenBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList();
}

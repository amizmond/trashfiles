using Estimation.HrReporting.Models;

namespace Estimation.HrReporting.Services;

public sealed class TeamsAnalysisTeam
{
    public TeamsAnalysisTeam(string train, string team, IReadOnlyList<int> countryCounts, string friction)
    {
        Train = train;
        Team = team;
        CountryCounts = countryCounts;
        Total = countryCounts.Sum();
        Locations = countryCounts.Count(c => c > 0);
        Friction = friction;
        Size = TeamsAnalysis.SizeBand(Total);
    }

    public string Train { get; }

    public string Team { get; }

    public IReadOnlyList<int> CountryCounts { get; }

    public int Total { get; }

    public int Locations { get; }

    public string Friction { get; }

    public string Size { get; }

    public decimal Share(int country) => Total == 0 ? 0 : (decimal)CountryCounts[country] / Total;
}

public sealed record TeamsAnalysisHidden(TeamsAnalysisExclusion Exclusion, int People);

public sealed class TeamsAnalysisDomain
{
    public TeamsAnalysisDomain(string domain, IReadOnlyList<TeamsAnalysisTeam> teams, IReadOnlyList<TeamsAnalysisHidden> hidden)
    {
        Domain = domain;
        Teams = teams;
        Hidden = hidden;
    }

    public string Domain { get; }

    public string Display => HeadcountMetrics.Display(Domain);

    public IReadOnlyList<TeamsAnalysisTeam> Teams { get; }

    public IReadOnlyList<TeamsAnalysisHidden> Hidden { get; }

    public int Trains => Teams.Select(t => t.Train).Distinct(StringComparer.OrdinalIgnoreCase).Count();

    public int People => Teams.Sum(t => t.Total);
}

public sealed class TeamsAnalysisResult
{
    public TeamsAnalysisResult(
        IReadOnlyList<string> countries, IReadOnlyList<TeamsAnalysisDomain> domains, int peopleWithoutTrain)
    {
        Countries = countries;
        Domains = domains;
        PeopleWithoutTrain = peopleWithoutTrain;
    }

    public IReadOnlyList<string> Countries { get; }

    public IReadOnlyList<TeamsAnalysisDomain> Domains { get; }

    public int PeopleWithoutTrain { get; }
}

public static class TeamsAnalysis
{
    public const string TotalColumn = "Total";
    public const string ShareColumn = "% of teams";

    public static readonly IReadOnlyList<string> FrictionLevels = ["none", "low", "med", "high"];

    public static readonly IReadOnlyList<string> SizeBands =
    [
        $"under {HeadcountRules.SmallTeamLimit}",
        $"{HeadcountRules.SmallTeamLimit}-{HeadcountRules.LargeTeamLimit}",
        $"over {HeadcountRules.LargeTeamLimit}"
    ];

    public static TeamsAnalysisResult Build(
        IReadOnlyCollection<HeadcountRow> rows, IReadOnlyCollection<TeamsAnalysisExclusion> exclusions)
    {
        var withTrain = rows.Where(HasTrain).ToList();

        var countries = withTrain
            .GroupBy(r => HeadcountMetrics.Display(HeadcountMetrics.Key(r.Country)), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Key)
            .ToList();
        var countryIndex = countries
            .Select((c, i) => (c, i))
            .ToDictionary(x => x.c, x => x.i, StringComparer.OrdinalIgnoreCase);
        var india = countries.FindIndex(c => string.Equals(c, HeadcountRules.India, StringComparison.OrdinalIgnoreCase));
        var usa = countries
            .Select((c, i) => (c, i))
            .Where(x => HeadcountRules.IsOneOf(x.c, HeadcountRules.UsaNames))
            .Select(x => x.i)
            .ToList();

        var domains = new List<TeamsAnalysisDomain>();
        foreach (var domain in HeadcountMetrics.Domains(withTrain))
        {
            var people = withTrain
                .Where(r => string.Equals(HeadcountMetrics.Key(r.ImhL07), domain, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var applying = exclusions
                .Where(e => string.Equals(e.Domain.Trim(), domain, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var hidden = applying
                .Select(e => new TeamsAnalysisHidden(e, people.Count(p => Matches(p, e))))
                .ToList();

            var teams = people
                .Where(p => !applying.Any(e => Matches(p, e)))
                .GroupBy(p => (Train: HeadcountMetrics.Key(p.Train), Team: HeadcountMetrics.Key(p.Team)), TeamKeyComparer.Instance)
                .Select(g =>
                {
                    var counts = new int[countries.Count];
                    foreach (var person in g)
                    {
                        counts[countryIndex[HeadcountMetrics.Display(HeadcountMetrics.Key(person.Country))]]++;
                    }

                    var indiaCount = india >= 0 ? counts[india] : 0;
                    var usaCount = usa.Sum(i => counts[i]);
                    return new TeamsAnalysisTeam(g.Key.Train, g.Key.Team, counts,
                        Friction(counts.Count(c => c > 0), indiaCount, usaCount));
                })
                .OrderBy(t => t.Train, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.Team.Length == 0)
                .ThenBy(t => t.Team, StringComparer.OrdinalIgnoreCase)
                .ToList();

            domains.Add(new TeamsAnalysisDomain(domain, teams, hidden));
        }

        return new TeamsAnalysisResult(countries, domains, rows.Count - withTrain.Count);
    }

    public static string Friction(int locations, int india, int usa)
    {
        if (locations <= 1)
        {
            return "none";
        }

        if (locations >= 4 || (india > 0 && usa > 0))
        {
            return "high";
        }

        return india == 0 && usa == 0 ? "low" : "med";
    }

    public static string SizeBand(int people) =>
        people < HeadcountRules.SmallTeamLimit ? SizeBands[0]
        : people > HeadcountRules.LargeTeamLimit ? SizeBands[2]
        : SizeBands[1];

    public static bool HasTrain(HeadcountRow row) =>
        !string.IsNullOrWhiteSpace(row.Train) && !HeadcountRules.IsOneOf(row.Train, HeadcountRules.NoTrainValues);

    public static bool Matches(HeadcountRow row, TeamsAnalysisExclusion exclusion)
    {
        var value = exclusion.Kind == TeamsAnalysisExclusionKind.Train ? row.Train : row.Team;
        return string.Equals(HeadcountMetrics.Key(value), exclusion.Value.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<MetricsTable> Summary(TeamsAnalysisResult result)
    {
        var domains = result.Domains;
        var columns = domains.Select(d => d.Display).Append(TotalColumn).ToList();

        decimal?[] PerDomain(Func<TeamsAnalysisDomain, decimal> value, Func<decimal> total) =>
            domains.Select(d => (decimal?)value(d)).Append(total()).ToArray();

        decimal? Ratio(decimal part, decimal whole) => whole == 0 ? null : part / whole;

        var teams = domains.Sum(d => d.Teams.Count);
        var people = domains.Sum(d => d.People);

        decimal?[] Share(Func<TeamsAnalysisTeam, bool> predicate) =>
            domains.Select(d => Ratio(d.Teams.Count(predicate), d.Teams.Count))
                .Append(Ratio(domains.Sum(d => d.Teams.Count(predicate)), teams))
                .ToArray();

        var summary = new MetricsTable(
            "Summary",
            "Metric",
            columns,
            [
                new MetricsRow("# ARTs", MetricsRowKind.Value, PerDomain(d => d.Trains, () => domains.Sum(d => d.Trains))),
                new MetricsRow("# Teams", MetricsRowKind.Value, PerDomain(d => d.Teams.Count, () => teams)),
                new MetricsRow("People in teams", MetricsRowKind.Value, PerDomain(d => d.People, () => people)),
                new MetricsRow("Avg # people per team", MetricsRowKind.Derived,
                    domains.Select(d => Ratio(d.People, d.Teams.Count)).Append(Ratio(people, teams)).ToArray()),
                new MetricsRow("Co-located teams (≤2 countries)", MetricsRowKind.Percent, Share(t => t.Locations <= 2)),
                new MetricsRow("No or low friction", MetricsRowKind.Percent, Share(t => t.Friction is "none" or "low")),
                new MetricsRow($"Teams sized {SizeBands[1]}", MetricsRowKind.Percent, Share(t => t.Size == SizeBands[1]))
            ]);

        var maxLocations = Math.Max(4, domains.SelectMany(d => d.Teams).Select(t => t.Locations).DefaultIfEmpty(0).Max());

        return
        [
            summary,
            Distribution("Locations", "Countries per team", domains, teams,
                Enumerable.Range(1, maxLocations).Select(n => (n.ToString(), (Func<TeamsAnalysisTeam, bool>)(t => t.Locations == n)))),
            Distribution("Friction", "Friction", domains, teams,
                FrictionLevels.Select(f => (f, (Func<TeamsAnalysisTeam, bool>)(t => t.Friction == f)))),
            Distribution("Team Size", "Team size", domains, teams,
                SizeBands.Select(s => (s, (Func<TeamsAnalysisTeam, bool>)(t => t.Size == s))))
        ];
    }

    private static MetricsTable Distribution(
        string title,
        string rowHeader,
        IReadOnlyList<TeamsAnalysisDomain> domains,
        int teams,
        IEnumerable<(string Label, Func<TeamsAnalysisTeam, bool> Predicate)> buckets)
    {
        var columns = domains.Select(d => d.Display).Append(TotalColumn).Append(ShareColumn).ToList();
        var rows = buckets
            .Select(b =>
            {
                var counts = domains.Select(d => (decimal?)d.Teams.Count(b.Predicate)).ToList();
                var total = counts.Sum() ?? 0;
                counts.Add(total);
                counts.Add(teams == 0 ? null : total / teams);
                return new MetricsRow(b.Label, MetricsRowKind.Value, counts);
            })
            .ToList();

        return new MetricsTable(title, rowHeader, columns, rows, percentColumn: columns.Count - 1);
    }

    private sealed class TeamKeyComparer : IEqualityComparer<(string Train, string Team)>
    {
        public static readonly TeamKeyComparer Instance = new();

        public bool Equals((string Train, string Team) x, (string Train, string Team) y) =>
            string.Equals(x.Train, y.Train, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Team, y.Team, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Train, string Team) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Train),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Team));
    }
}

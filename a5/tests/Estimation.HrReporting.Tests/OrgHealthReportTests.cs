using System.Globalization;
using Estimation.HrReporting.Models;
using Estimation.HrReporting.Services;
using Xunit;

namespace Estimation.HrReporting.Tests;

public class OrgHealthReportTests
{
    private const string Wcr = "RFT -  Wholesale Credit Risk";
    private const string Market = "RFT - Market Risk";
    private const string FixedPrice = "Resource Augmentation (Fixed Price Non Headcount)";
    private const string TimeAndMaterial = "Resource Augmentation (Time and Material Headcount)";

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static int _next;

    private static HeadcountRow Person(
        string domain,
        string country = "India",
        string type = "Permanent/Regular",
        string? eng = "Engineering",
        string? art = "ART",
        string? train = "Credit AI",
        string? team = "Earth",
        string status = "Operational",
        string? role = null,
        string? job = null) => new()
        {
            Brid = $"P{Interlocked.Increment(ref _next)}",
            ImhL07 = domain,
            Country = country,
            ColleagueNonPermType = type,
            EngNonEng = eng,
            ArtNonArt = art,
            Train = train,
            Team = team,
            OperationalStatus = status,
            TargetAgileRole = role,
            Job = job
        };

    private static IEnumerable<HeadcountRow> People(int count, string domain, string train, string team, string country, string art = "ART", string status = "Operational") =>
        Enumerable.Range(0, count).Select(_ => Person(domain, country, train: train, team: team, art: art, status: status));

    private static string Text(OrgHealthKpi kpi, OrgHealthScope? scope) =>
        kpi.Evaluate(scope)?.Format(Invariant) ?? "—";

    private static List<HeadcountRow> WorkforceRows() =>
    [
        Person(Wcr), Person(Wcr), Person(Wcr),
        Person(Wcr, type: FixedPrice),
        Person(Wcr, "United Kingdom", TimeAndMaterial, "NonEngineering", "NonART"),
        Person(Wcr, "Czech Republic", "Graduate"),
        Person(Wcr, status: "Non-Operational"),
        Person(Market, "USA"),
        Person(Market, "United Kingdom", eng: "NonEngineering")
    ];

    [Fact]
    public void Workforce_numbers_leave_out_non_operational_people_and_split_fixed_price()
    {
        var snapshot = OrgHealthReport.Build(WorkforceRows(), []);
        var wcr = snapshot.Scope(Wcr);
        var market = snapshot.Scope(Market);

        Assert.Equal("5", Text(OrgHealthKpis.Headcount, wcr));
        Assert.Equal("1", Text(OrgHealthKpis.FixedPrice, wcr));
        Assert.Equal("1", Text(OrgHealthKpis.NonOperational, wcr));
        Assert.Equal("67%", Text(OrgHealthKpis.Country("India"), wcr));
        Assert.Equal("17%", Text(OrgHealthKpis.Country("United Kingdom"), wcr));
        Assert.Equal("0%", Text(OrgHealthKpis.Country("USA"), wcr));

        Assert.Equal("2", Text(OrgHealthKpis.Headcount, market));
        Assert.Equal("0", Text(OrgHealthKpis.FixedPrice, market));
        Assert.Equal("50%", Text(OrgHealthKpis.Country("USA"), market));

        Assert.Equal("7", Text(OrgHealthKpis.Headcount, snapshot.Total));
        Assert.Equal("50%", Text(OrgHealthKpis.Country("India"), snapshot.Total));
        Assert.Equal(["India", "United Kingdom", "Czech Republic", "USA"], snapshot.Countries);
        Assert.Equal([Wcr, Market], snapshot.Domains);
    }

    [Fact]
    public void Objectives_follow_the_metrics_rules()
    {
        var snapshot = OrgHealthReport.Build(WorkforceRows(), []);
        var wcr = snapshot.Scope(Wcr);

        Assert.Equal("80% / 20%", Text(OrgHealthKpis.PermExclFixedPrice, wcr));
        Assert.Equal("67% / 33%", Text(OrgHealthKpis.PermInclFixedPrice, wcr));
        Assert.Equal("83% / 17%", Text(OrgHealthKpis.Engineering, wcr));
        Assert.Equal("17%", Text(OrgHealthKpis.OutsideTrain, wcr));

        Assert.Equal("86% / 14%", Text(OrgHealthKpis.PermExclFixedPrice, snapshot.Total));
        Assert.Equal("75% / 25%", Text(OrgHealthKpis.PermInclFixedPrice, snapshot.Total));
        Assert.Equal("75% / 25%", Text(OrgHealthKpis.Engineering, snapshot.Total));
    }

    [Fact]
    public void Halves_round_up_like_the_slide()
    {
        var snapshot = OrgHealthReport.Build(WorkforceRows(), []);

        var outside = OrgHealthKpis.OutsideTrain.Evaluate(snapshot.Total)!;

        Assert.Equal(0.125m, outside.Number);
        Assert.Equal(13m, outside.Shown);
        Assert.Equal("13%", outside.Format(Invariant));
        Assert.Equal("1 of 8 people, 12.5%", outside.Describe(Invariant));
    }

    [Fact]
    public void Team_kpis_use_operational_art_people_and_the_hide_list()
    {
        var rows = People(5, Wcr, "T1", "A", "India")
            .Concat(People(3, Wcr, "T1", "A", "United Kingdom"))
            .Concat(People(1, Wcr, "T1", "A", "India", art: "NonART"))
            .Concat(People(1, Wcr, "T1", "A", "India", status: "Leave"))
            .Concat(People(3, Wcr, "T1", "B", "Czech Republic"))
            .Concat(People(6, Wcr, "T2", "C", "India"))
            .Concat(People(4, Wcr, "T2", "C", "USA"))
            .Concat(People(3, Wcr, "T2", "C", "United Kingdom"))
            .Concat(People(9, Wcr, "T2", "Hidden", "United Kingdom"))
            .Concat(People(10, Market, "T9", "Z", "United Kingdom"))
            .ToList();
        TeamsAnalysisExclusion[] hides = [new() { Id = 1, Domain = Wcr, Kind = TeamsAnalysisExclusionKind.Team, Value = "Hidden" }];

        var snapshot = OrgHealthReport.Build(rows, hides);
        var wcr = snapshot.Scope(Wcr);

        Assert.Equal("2 | 3", Text(OrgHealthKpis.TrainsTeams, wcr));
        Assert.Equal("33%", Text(OrgHealthKpis.TeamSize, wcr));
        Assert.Equal("67%", Text(OrgHealthKpis.CoLocated, wcr));
        Assert.Equal("33%", Text(OrgHealthKpis.LowFriction, wcr));
        Assert.Equal("8.0", Text(OrgHealthKpis.AverageTeamSize, wcr));
        Assert.Equal("1 of 3 teams, 33.3%", OrgHealthKpis.TeamSize.Evaluate(wcr)!.Describe(Invariant));
        Assert.Equal(1, snapshot.HiddenTeamEntries);

        Assert.Equal("3 | 4", Text(OrgHealthKpis.TrainsTeams, snapshot.Total));
        Assert.Equal("50%", Text(OrgHealthKpis.TeamSize, snapshot.Total));
        Assert.Equal("75%", Text(OrgHealthKpis.CoLocated, snapshot.Total));
        Assert.Equal("50%", Text(OrgHealthKpis.LowFriction, snapshot.Total));
    }

    [Fact]
    public void A_domain_without_teams_has_no_team_shares()
    {
        var snapshot = OrgHealthReport.Build([Person(Wcr, art: "NonART")], []);

        Assert.Equal("0 | 0", Text(OrgHealthKpis.TrainsTeams, snapshot.Scope(Wcr)));
        Assert.Null(OrgHealthKpis.TeamSize.Evaluate(snapshot.Scope(Wcr)));
        Assert.Null(OrgHealthKpis.AverageTeamSize.Evaluate(snapshot.Scope(Wcr)));
    }

    [Fact]
    public void Agile_roles_come_from_the_target_role_or_else_the_workday_job()
    {
        HeadcountRow[] rows =
        [
            Person(Wcr, role: "LPM", job: "Lean Portfolio Manager"),
            Person(Wcr, job: "Solution Train Engineer "),
            Person(Wcr, role: "Scrum Master", job: "Release Train Engineer"),
            Person(Wcr, role: "RTE", job: "Software Engineer"),
            Person(Wcr, role: "Scrum Master (part time)", job: "Software Engineer"),
            Person(Wcr, role: "Scrum Master, System Engineer", job: "Engineering Manager"),
            Person(Wcr, role: "Software Engineer", job: "Scrum Master"),
            Person(Wcr, job: "Scrum Master"),
            Person(Wcr, role: "Scrum Master", job: "Scrum Master", status: "Non-Operational")
        ];

        var wcr = OrgHealthReport.Build(rows, []).Scope(Wcr);

        Assert.Equal("1 | 1", Text(OrgHealthKpis.Lpm, wcr));
        Assert.Equal("1 | 1", Text(OrgHealthKpis.Ste, wcr));
        Assert.Equal("1 | 1", Text(OrgHealthKpis.Rte, wcr));
        Assert.Equal("4 | 2 | 2", Text(OrgHealthKpis.ScrumMasters, wcr));
    }

    [Theory]
    [InlineData("Scrum Master (part time)", "Scrum Master", "part time")]
    [InlineData("  RTE  ", "RTE", null)]
    [InlineData("Scrum Master (full time", "Scrum Master", "full time")]
    [InlineData("Scrum Master ()", "Scrum Master", null)]
    public void Role_names_lose_their_bracketed_qualifier(string text, string name, string? qualifier) =>
        Assert.Equal(new AgileRoleName(name, qualifier), AgileRoleName.Parse(text));

    [Fact]
    public void Combined_target_roles_are_split_on_commas()
    {
        var roles = HeadcountRules.AgileRoles(new HeadcountRow { TargetAgileRole = "Scrum Master, System Engineer; Tech Lead", Job = "Scrum Master" });

        Assert.Equal(["Scrum Master", "System Engineer", "Tech Lead"], roles.Select(r => r.Name));
        Assert.False(HeadcountRules.IsFullTimeScrumMaster(roles));
        Assert.True(HeadcountRules.IsFullTimeScrumMaster(HeadcountRules.AgileRoles(new HeadcountRow { TargetAgileRole = "Scrum Master (full time)" })));
        Assert.Empty(HeadcountRules.AgileRoles(new HeadcountRow()));
    }

    [Theory]
    [InlineData("fixed-price", 5, 8, OrgHealthTrend.Up, OrgHealthTone.Bad)]
    [InlineData("fixed-price", 1, 0, OrgHealthTrend.Down, OrgHealthTone.Good)]
    [InlineData("headcount", 10, 9, OrgHealthTrend.Down, OrgHealthTone.Neutral)]
    [InlineData("headcount", 9, 9, OrgHealthTrend.Same, OrgHealthTone.Neutral)]
    public void Count_changes_are_judged_by_the_better_direction(string key, int older, int newer, OrgHealthTrend trend, OrgHealthTone tone)
    {
        var kpi = OrgHealthKpis.Catalog([]).Single(k => k.Key == key);

        var cell = OrgHealthReport.Cell(kpi, OrgHealthTargetRule.None, OrgHealthValue.Count(older), OrgHealthValue.Count(newer));

        Assert.Equal((trend, tone), (cell.Trend, cell.Tone));
        Assert.Null(cell.TargetMet);
    }

    [Fact]
    public void Percent_changes_compare_the_shown_whole_percentages()
    {
        var target = OrgHealthTargetRule.Create(null, OrgHealthComparison.Above, 50);
        var up = OrgHealthReport.Cell(OrgHealthKpis.TeamSize, target,
            OrgHealthValue.Ratio(OrgHealthValueKind.Percent, 44, 100), OrgHealthValue.Ratio(OrgHealthValueKind.Percent, 50, 100));
        var same = OrgHealthReport.Cell(OrgHealthKpis.TeamSize, target,
            OrgHealthValue.Ratio(OrgHealthValueKind.Percent, 501, 1000), OrgHealthValue.Ratio(OrgHealthValueKind.Percent, 504, 1000));
        var worse = OrgHealthReport.Cell(OrgHealthKpis.OutsideTrain, OrgHealthKpis.DefaultTarget(OrgHealthKpis.OutsideTrain.Key),
            OrgHealthValue.Ratio(OrgHealthValueKind.Percent, 5, 100), OrgHealthValue.Ratio(OrgHealthValueKind.Percent, 9, 100));

        Assert.Equal((OrgHealthTrend.Up, OrgHealthTone.Good, (bool?)false), (up.Trend, up.Tone, up.TargetMet));
        Assert.Equal((OrgHealthTrend.Same, OrgHealthTone.Neutral, (bool?)false), (same.Trend, same.Tone, same.TargetMet));
        Assert.Equal((OrgHealthTrend.Up, OrgHealthTone.Bad, (bool?)true), (worse.Trend, worse.Tone, worse.TargetMet));
    }

    [Fact]
    public void Several_numbers_get_no_arrow_and_a_missing_side_gives_no_change()
    {
        var counts = OrgHealthReport.Cell(OrgHealthKpis.TrainsTeams, OrgHealthTargetRule.None, OrgHealthValue.Many(17, 116), OrgHealthValue.Many(18, 132));
        var noOlder = OrgHealthReport.Cell(OrgHealthKpis.Headcount, OrgHealthTargetRule.None, null, OrgHealthValue.Count(3));
        var noNewer = OrgHealthReport.Cell(OrgHealthKpis.OutsideTrain, OrgHealthKpis.DefaultTarget(OrgHealthKpis.OutsideTrain.Key),
            OrgHealthValue.Ratio(OrgHealthValueKind.Percent, 1, 2), null);

        Assert.Equal(OrgHealthTrend.Same, counts.Trend);
        Assert.Equal(OrgHealthTrend.Same, noOlder.Trend);
        Assert.Equal((OrgHealthTrend.Same, (bool?)null), (noNewer.Trend, noNewer.TargetMet));
    }

    [Fact]
    public void Compare_builds_one_cell_per_scope_and_kpi()
    {
        var newer = OrgHealthReport.Build(WorkforceRows(), []);
        var older = OrgHealthReport.Build([Person(Wcr, type: FixedPrice), Person(Wcr), Person("RFT Wholesale Risk Central", "Poland")], []);
        var kpis = OrgHealthKpis.Catalog(OrgHealthReport.Countries(newer, older));
        var targets = OrgHealthKpis.Targets(kpis, []);

        var table = OrgHealthReport.Compare(newer, older, [OrgHealthKpis.Headcount, OrgHealthKpis.FixedPrice], [null, Wcr, Market], targets);

        Assert.True(table.Compared);
        Assert.Equal([null, Wcr, Market], table.Scopes);
        var headcount = table.Lines[0];
        Assert.Equal(["2", "1", "—"], headcount.Cells.Select(c => c.Older?.Format(Invariant) ?? "—"));
        Assert.Equal(["7", "5", "2"], headcount.Cells.Select(c => c.Newer?.Format(Invariant) ?? "—"));
        Assert.Equal(OrgHealthTrend.Same, headcount.Cells[2].Trend);
        Assert.Equal(["India", "United Kingdom", "Czech Republic", "USA", "Poland"], OrgHealthReport.Countries(newer, older));
        Assert.Equal([Wcr, Market, "RFT Wholesale Risk Central"], OrgHealthReport.Domains(newer, older));
        Assert.False(OrgHealthReport.Compare(newer, null, kpis, [null], targets).Compared);
    }

    [Fact]
    public void The_catalog_keeps_the_slide_order_with_countries_after_the_headcount_rows()
    {
        var keys = OrgHealthKpis.Catalog(["India", "USA"]).Select(k => k.Key).ToList();

        Assert.Equal(
            ["headcount", "fixed-price", "non-operational", "country:India", "country:USA", "perm-excl-fixed-price",
             "perm-incl-fixed-price", "engineering", "outside-train", "team-size", "co-located", "low-friction",
             "average-team-size", "trains-teams", "lpm", "ste", "rte", "scrum-masters"],
            keys);
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.All(OrgHealthKpis.Catalog(["India"]), k => Assert.Contains(k.Category, OrgHealthKpis.Categories));
    }
}

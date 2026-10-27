using System.Text.Json;
using Estimation.Core.Capacity.Models;
using Estimation.Core.Features.DependencyMap.Services;
using Estimation.Core.Features.Models;
using Estimation.Core.JiraIntegration.Models;
using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Resources.Models;
using Estimation.Core.Shared.Services;
using Estimation.Core.Tests.Infrastructure;
using Estimation.Core.Train.Models;
using Xunit;

namespace Estimation.Core.Tests.Features.DependencyMap;

public class DependencyMapServiceTests
{
    private const int PaymentsId = 1;
    private const int CardsId = 2;
    private const int SelectedPiId = 1;
    private const int EarlierPiId = 2;
    private const int SameNamedPiId = 3;
    private const int Axis = 10;
    private const int Regal = 11;
    private const int Bravo = 12;
    private const int Zeta = 20;
    private const int Loner = 21;

    private static readonly DateTime PiStart = new(2026, 1, 5);
    private static readonly DateTime PiEnd = new(2026, 3, 8);
    private static readonly DateTime EarlierPiStart = new(2025, 10, 6);

    private readonly InMemoryDatabase _db = new();
    private readonly DependencyMapService _service;

    public DependencyMapServiceTests()
    {
        _service = new DependencyMapService(_db);
    }

    [Fact]
    public async Task A_cancelled_load_stops_instead_of_building_the_maps()
    {
        await SeedBaseAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.GetManyAsync([PaymentsId, CardsId], SelectedPiId, true, new CancellationToken(true)));
    }

    private Task SeedBaseAsync() => _db.SeedAsync(db =>
    {
        db.CapitalProjects.Add(Art(PaymentsId, "Payments", "PAY", "AMB"));
        db.CapitalProjects.Add(Art(CardsId, "Cards", "CRD", "AMB"));
        db.Pis.Add(new Pi
        {
            Id = SelectedPiId,
            Name = "PI 26.1",
            StartDate = PiStart,
            EndDate = PiEnd,
            FeatureLabels = "pi-26-1"
        });
        db.Pis.Add(new Pi
        {
            Id = EarlierPiId,
            Name = "PI 25.4",
            StartDate = EarlierPiStart,
            EndDate = PiStart.AddDays(-1)
        });
        db.Pis.Add(new Pi { Id = SameNamedPiId, Name = "PI 26.1" });
        db.Teams.Add(new Team { Id = Axis, Name = "Axis" });
        db.Teams.Add(new Team { Id = Regal, Name = "Regal" });
        db.Teams.Add(new Team { Id = Bravo, Name = "Bravo", IsArtSharedTeam = true });
        db.Teams.Add(new Team { Id = Zeta, Name = "Zeta" });
        db.Teams.Add(new Team { Id = Loner, Name = "Loner" });
        db.CapitalProjectTeams.Add(new ArtTeam { CapitalProjectId = PaymentsId, TeamId = Axis });
        db.CapitalProjectTeams.Add(new ArtTeam { CapitalProjectId = PaymentsId, TeamId = Regal });
        db.CapitalProjectTeams.Add(new ArtTeam { CapitalProjectId = PaymentsId, TeamId = Bravo });
        db.CapitalProjectTeams.Add(new ArtTeam { CapitalProjectId = CardsId, TeamId = Bravo });
        db.CapitalProjectTeams.Add(new ArtTeam { CapitalProjectId = CardsId, TeamId = Zeta });
    });

    private static Art Art(int id, string name, params string[] keys) => new()
    {
        Id = id,
        Name = name,
        JiraKeys = keys.Select(k => new ArtJiraKey { JiraKey = k }).ToList()
    };

    private static Feature Feature(int id, string? jiraId, int? piId, string? labels = null, string? projectKey = null) => new()
    {
        Id = id,
        ProjectKey = projectKey,
        JiraId = jiraId,
        Name = $"Feature {id}",
        Summary = $"Summary {id}",
        Status = "In Progress",
        PiId = piId,
        Labels = labels
    };

    private static IssueLink Link(
        string id,
        string from,
        string to,
        string typeName = "Blocks",
        string? outward = "blocks",
        string? inward = "is blocked by") => new()
    {
        JiraLinkId = id,
        TypeName = typeName,
        OutwardLabel = outward,
        InwardLabel = inward,
        FromKey = from,
        ToKey = to
    };

    private static Sprint TeamSprint(int id, int teamId, int? piId, DateTime start, int? sourceArtSprintId = null) => new()
    {
        Id = id,
        TeamId = teamId,
        PiId = piId,
        SourceArtSprintId = sourceArtSprintId,
        Name = $"Sprint {id}",
        StartDate = start,
        EndDate = start.AddDays(13),
        UatStart = start.AddDays(14),
        UatEnd = start.AddDays(15)
    };

    private async Task<DependencyMapData> GetAsync(bool includePiLabelMatches = true)
    {
        var data = await _service.GetAsync(PaymentsId, SelectedPiId, includePiLabelMatches);
        Assert.NotNull(data);
        return data;
    }

    private static DependencyFeature FeatureOf(DependencyMapData data, string key) =>
        data.Features.Single(f => f.JiraId == key);

    [Fact]
    public async Task An_unknown_art_or_pi_gives_null()
    {
        await SeedBaseAsync();

        Assert.Null(await _service.GetAsync(99, SelectedPiId));
        Assert.Null(await _service.GetAsync(PaymentsId, 99));
    }

    [Fact]
    public async Task Only_features_matched_to_the_art_and_in_the_pi_are_in_scope()
    {
        await SeedBaseAsync();
        await _db.SeedAsync(db =>
        {
            db.Features.Add(Feature(1, "PAY-1", SelectedPiId));
            db.Features.Add(Feature(2, "AMB-1", SelectedPiId));
            db.Features.Add(Feature(3, "CRD-1", SelectedPiId));
            db.Features.Add(Feature(4, "PAY-4", EarlierPiId));
            db.Features.Add(Feature(5, "XYZ-1", SelectedPiId));
            db.Features.Add(Feature(6, "pay-6", SelectedPiId));
        });

        var data = await GetAsync();

        Assert.Equal(PaymentsId, data.ArtId);
        Assert.Equal("Payments", data.ArtName);
        Assert.Equal(SelectedPiId, data.PiId);
        Assert.Equal("PI 26.1", data.PiName);
        Assert.Equal(PiStart, data.PiStart);
        Assert.Equal(["PAY-1", "pay-6"], data.Features.Select(f => f.JiraId));
        Assert.All(data.Features, f => Assert.Equal(DependencyEndKind.InScope, f.Kind));
        Assert.All(data.Features, f => Assert.True(f.InSelectedPi));
        Assert.All(data.Features, f => Assert.Equal((PaymentsId, "Payments"), (f.ArtId!.Value, f.ArtName!)));
        Assert.Empty(data.Links);
        Assert.Empty(data.Stubs);
    }

    [Fact]
    public async Task Label_matched_features_are_in_scope_only_when_asked()
    {
        await SeedBaseAsync();
        await _db.SeedAsync(db =>
        {
            db.Features.Add(Feature(1, "PAY-1", SelectedPiId));
            db.Features.Add(Feature(2, "PAY-2", null, labels: "x, PI-26-1"));
            db.Features.Add(Feature(3, "PAY-3", EarlierPiId, labels: "other"));
        });

        var withLabels = await GetAsync();
        var withoutLabels = await GetAsync(includePiLabelMatches: false);

        Assert.Equal(["PAY-1", "PAY-2"], withLabels.Features.Select(f => f.JiraId));
        Assert.Equal(["PAY-1"], withoutLabels.Features.Select(f => f.JiraId));
    }

    [Fact]
    public async Task A_feature_of_a_same_named_pi_row_is_in_scope()
    {
        await SeedBaseAsync();
        await _db.SeedAsync(db => db.Features.Add(Feature(7, "PAY-7", SameNamedPiId)));

        var data = await GetAsync(includePiLabelMatches: false);

        var feature = Assert.Single(data.Features);
        Assert.Equal("PAY-7", feature.JiraId);
        Assert.Equal(DependencyEndKind.InScope, feature.Kind);
        Assert.Equal(SameNamedPiId, feature.PiId);
        Assert.True(feature.InSelectedPi);
    }

    [Fact]
    public async Task A_feature_of_a_same_named_pi_row_keeps_its_sprints_and_capacity_line()
    {
        await SeedBaseAsync();
        await _db.SeedAsync(db =>
        {
            db.Features.Add(Feature(7, "PAY-7", SameNamedPiId));
            db.FeatureTeams.Add(new FeatureTeam { FeatureId = 7, TeamId = Axis });
            db.Sprints.Add(TeamSprint(3001, Axis, SameNamedPiId, new DateTime(2026, 2, 2)));
            db.FeatureTeamSprints.Add(new FeatureTeamSprint { FeatureId = 7, TeamId = Axis, SprintId = 3001 });
            db.TeamCapacityFeatureOrders.Add(new TeamCapacityFeatureOrder
            {
                TeamId = Axis,
                PiId = SameNamedPiId,
                FeatureId = 7,
                SortOrder = 4,
                IsIncluded = false
            });
        });

        var data = await GetAsync(includePiLabelMatches: false);

        var team = Assert.Single(Assert.Single(data.Features).Teams);
        Assert.Equal([3001], team.Sprints.Select(s => s.SprintId));
        Assert.True(team.BelowLine);
        Assert.Equal(4, team.SortOrder);
    }

    [Fact]
    public async Task Features_without_a_jira_key_are_kept_and_counted()
    {
        await SeedBaseAsync();
        await _db.SeedAsync(db =>
        {
            db.Features.Add(Feature(1, "PAY-1", SelectedPiId));
            db.Features.Add(Feature(2, null, SelectedPiId, projectKey: "PAY"));
            db.Features.Add(Feature(3, "  ", SelectedPiId, projectKey: "PAY"));
            db.IssueLinks.Add(Link("501", "PAY-1", "PAY-9"));
        });

        var data = await GetAsync();

        Assert.Equal([2, 3, 1], data.Features.Where(f => f.Kind == DependencyEndKind.InScope).Select(f => f.FeatureId));
        Assert.Null(data.Features.Single(f => f.FeatureId == 3).JiraId);
        Assert.Equal(2, data.FeaturesWithoutKey);
    }

    [Fact]
    public async Task Links_are_read_for_both_ends_once()
    {
        await SeedBaseAsync();
        await _db.SeedAsync(db =>
        {
            db.Features.Add(Feature(1, "PAY-1", SelectedPiId));
            db.Features.Add(Feature(2, "PAY-2", SelectedPiId));
            db.IssueLinks.Add(Link("502", "PAY-2", "STORY-1", "Relates", "relates to", "relates to"));
            db.IssueLinks.Add(Link("501", "PAY-1", "PAY-2"));
            db.IssueLinks.Add(Link("503", "CRD-1", "CRD-2"));
        });

        var data = await GetAsync();

        Assert.Equal(
            [
                new DependencyLinkRow("501", "Blocks", "blocks", "is blocked by", "PAY-1", "PAY-2"),
                new DependencyLinkRow("502", "Relates", "relates to", "relates to", "PAY-2", "STORY-1")
            ],
            data.Links);
    }

    [Fact]
    public async Task Links_match_in_scope_keys_trimmed_and_ignoring_case()
    {
        await SeedBaseAsync();
        await _db.SeedAsync(db =>
        {
            db.Features.Add(Feature(1, "pay-1 ", SelectedPiId, projectKey: "PAY"));
            db.IssueLinks.Add(Link("501", "PAY-1", "STORY-1"));
        });

        var data = await GetAsync();

        Assert.Equal("pay-1", Assert.Single(data.Features).JiraId);
        var link = Assert.Single(data.Links);
        Assert.Equal(("PAY-1", "STORY-1"), (link.FromKey, link.ToKey));
        Assert.Equal("STORY-1", Assert.Single(data.Stubs).Key);
    }

    [Fact]
    public async Task Linked_feature_of_the_same_art_in_another_pi_is_an_other_pi_end()
    {
        await SeedBaseAsync();
        await _db.SeedAsync(db =>
        {
            db.Features.Add(Feature(1, "PAY-1", SelectedPiId));
            db.Features.Add(Feature(4, "PAY-4", EarlierPiId));
            db.IssueLinks.Add(Link("501", "PAY-4", "PAY-1"));
        });

        var data = await GetAsync();

        var other = FeatureOf(data, "PAY-4");
        Assert.Equal(DependencyEndKind.OtherPi, other.Kind);
        Assert.Equal(EarlierPiId, other.PiId);
        Assert.Equal("PI 25.4", other.PiName);
        Assert.Equal(EarlierPiStart, other.PiStart);
        Assert.False(other.InSelectedPi);
        Assert.Equal((PaymentsId, "Payments"), (other.ArtId!.Value, other.ArtName!));
        Assert.Empty(data.Stubs);
    }

    [Fact]
    public async Task Linked_feature_without_a_pi_takes_the_pi_whose_labels_it_matches()
    {
        await SeedBaseAsync();
        await _db.SeedAsync(db =>
        {
            db.Pis.Add(new Pi { Id = 4, Name = "PI 26.3", StartDate = new DateTime(2026, 6, 1), FeatureLabels = "pi-26-2, late" });
            db.Pis.Add(new Pi { Id = 5, Name = "PI 26.2", StartDate = new DateTime(2026, 3, 9), FeatureLabels = "pi-26-2" });
            db.CapitalProjectSprints.Add(ArtSprint(101, PaymentsId, SelectedPiId, "Sprint 1", PiStart));
            db.CapitalProjectSprints.Add(ArtSprint(102, PaymentsId, SelectedPiId, "Sprint 2", PiStart.AddDays(14)));
            db.Sprints.Add(TeamSprint(1102, Axis, SelectedPiId, PiStart.AddDays(14), sourceArtSprintId: 102));
            db.Features.Add(Feature(1, "PAY-1", SelectedPiId));
            db.FeatureTeams.Add(new FeatureTeam { FeatureId = 1, TeamId = Axis });
            db.FeatureTeamSprints.Add(new FeatureTeamSprint { FeatureId = 1, TeamId = Axis, SprintId = 1102 });
            db.Features.Add(Feature(9, "PAY-9", null, labels: "pi-26-2"));
            db.Features.Add(Feature(19, "CRD-9", null, labels: "PI-26-2"));
            db.IssueLinks.Add(Link("501", "PAY-9", "PAY-1"));
            db.IssueLinks.Add(Link("502", "CRD-9", "PAY-1"));
        });

        var data = await GetAsync();

        foreach (var key in new[] { "PAY-9", "CRD-9" })
        {
            var other = FeatureOf(data, key);
            Assert.False(other.InSelectedPi);
            Assert.Equal((5, "PI 26.2", new DateTime(2026, 3, 9)), (other.PiId!.Value, other.PiName!, other.PiStart!.Value));
        }

        var index = DependencyClassifier.Build(data);
        Assert.All(index.Dependencies, d => Assert.Equal(DependencyRisk.OutOfOrder, d.Verdict.Risk));
        Assert.Equal(
            ["CRD-9 is in a later PI (PI 26.2)", "PAY-9 is in a later PI (PI 26.2)"],
            index.Dependencies.Select(d => d.Verdict.Text).Order());

        var withoutLabels = await GetAsync(includePiLabelMatches: false);
        Assert.Null(FeatureOf(withoutLabels, "PAY-9").PiName);
        Assert.Null(FeatureOf(withoutLabels, "CRD-9").PiId);
    }

    [Fact]
    public async Task Linked_feature_in_a_pi_without_a_start_takes_it_from_art_sprints_then_team_sprints_then_the_end()
    {
        await SeedBaseAsync();
        await _db.SeedAsync(db =>
        {
            db.Pis.Add(new Pi { Id = 4, Name = "PI 26.2" });
            db.Pis.Add(new Pi { Id = 5, Name = "PI 26.3" });
            db.Pis.Add(new Pi { Id = 6, Name = "PI 26.4", EndDate = new DateTime(2026, 12, 6) });
            db.Pis.Add(new Pi { Id = 7, Name = "PI 27.1" });
            db.CapitalProjectSprints.Add(ArtSprint(401, CardsId, 4, "Cards 2", new DateTime(2026, 3, 23)));
            db.CapitalProjectSprints.Add(ArtSprint(402, CardsId, 4, "Cards 1", new DateTime(2026, 3, 9)));
            db.Sprints.Add(TeamSprint(1004, Zeta, 4, new DateTime(2026, 3, 2)));
            db.Sprints.Add(TeamSprint(1005, Zeta, 5, new DateTime(2026, 6, 1)));
            db.Features.Add(Feature(1, "PAY-1", SelectedPiId));
            db.Features.Add(Feature(11, "CRD-4", 4));
            db.Features.Add(Feature(12, "CRD-5", 5));
            db.Features.Add(Feature(13, "CRD-6", 6));
            db.Features.Add(Feature(14, "CRD-7", 7));
            db.IssueLinks.Add(Link("501", "CRD-4", "PAY-1"));
            db.IssueLinks.Add(Link("502", "CRD-5", "PAY-1"));
            db.IssueLinks.Add(Link("503", "CRD-6", "PAY-1"));
            db.IssueLinks.Add(Link("504", "CRD-7", "PAY-1"));
        });

        var data = await GetAsync();

        Assert.Equal(new DateTime(2026, 3, 9), FeatureOf(data, "CRD-4").PiStart);
        Assert.Equal(new DateTime(2026, 6, 1), FeatureOf(data, "CRD-5").PiStart);
        Assert.Equal(new DateTime(2026, 12, 6), FeatureOf(data, "CRD-6").PiStart);
        Assert.Null(FeatureOf(data, "CRD-7").PiStart);
        Assert.Equal("PI 27.1", FeatureOf(data, "CRD-7").PiName);
    }

    [Fact]
    public async Task Provider_in_a_pi_without_dates_is_placed_by_its_sprints_or_not_checked()
    {
        await SeedBaseAsync();
        await _db.SeedAsync(db =>
        {
            db.Pis.Add(new Pi { Id = 4, Name = "PI 26.2" });
            db.Pis.Add(new Pi { Id = 7, Name = "PI 27.1" });
            db.CapitalProjectSprints.Add(ArtSprint(101, PaymentsId, SelectedPiId, "Sprint 1", PiStart));
            db.CapitalProjectSprints.Add(ArtSprint(401, CardsId, 4, "Cards 1", new DateTime(2026, 3, 9)));
            db.Sprints.Add(TeamSprint(1001, Axis, SelectedPiId, PiStart, sourceArtSprintId: 101));
            db.Features.Add(Feature(1, "PAY-1", SelectedPiId));
            db.FeatureTeams.Add(new FeatureTeam { FeatureId = 1, TeamId = Axis });
            db.FeatureTeamSprints.Add(new FeatureTeamSprint { FeatureId = 1, TeamId = Axis, SprintId = 1001 });
            db.Features.Add(Feature(11, "CRD-4", 4));
            db.Features.Add(Feature(14, "CRD-7", 7));
            db.IssueLinks.Add(Link("501", "CRD-4", "PAY-1"));
            db.IssueLinks.Add(Link("502", "CRD-7", "PAY-1"));
        });

        var index = DependencyClassifier.Build(await GetAsync());

        var dated = index.Dependencies.Single(d => d.Id == "f11>f1");
        Assert.Equal(PlanPosition.LaterPi, index.Nodes["f11"].Plan!.Position);
        Assert.Equal(DependencyRisk.OutOfOrder, dated.Verdict.Risk);
        Assert.Equal("CRD-4 is in a later PI (PI 26.2)", dated.Verdict.Text);

        var undated = index.Dependencies.Single(d => d.Id == "f14>f1");
        Assert.Equal(PlanPosition.OtherPiNoDates, index.Nodes["f14"].Plan!.Position);
        Assert.Equal(DependencyRisk.NotChecked, undated.Verdict.Risk);
        Assert.Equal("CRD-7 is in PI 27.1, which has no dates, so the order is not checked", undated.Verdict.Text);
    }

    [Fact]
    public async Task Selected_pi_without_a_start_takes_the_first_art_sprint_then_the_pi_sprints()
    {
        await SeedBaseAsync();
        await _db.SeedAsync(db =>
        {
            db.Pis.Add(new Pi { Id = 4, Name = "PI 26.2" });
            db.Pis.Add(new Pi { Id = 5, Name = "PI 26.3" });
            db.CapitalProjectSprints.Add(ArtSprint(401, CardsId, 4, "Cards 1", new DateTime(2026, 3, 2)));
            db.CapitalProjectSprints.Add(ArtSprint(402, PaymentsId, 4, "Sprint 1", new DateTime(2026, 3, 9)));
            db.Sprints.Add(TeamSprint(1005, Zeta, 5, new DateTime(2026, 6, 1)));
        });

        var withArtSprints = await _service.GetAsync(PaymentsId, 4);
        var withTeamSprints = await _service.GetAsync(PaymentsId, 5);

        Assert.Equal(new DateTime(2026, 3, 9), withArtSprints!.PiStart);
        Assert.Equal(new DateTime(2026, 6, 1), withTeamSprints!.PiStart);
    }

    [Fact]
    public async Task Linked_feature_of_another_art_is_an_other_art_end_with_its_art_name_and_pi_flag()
    {
        await SeedBaseAsync();
        await _db.SeedAsync(db =>
        {
            db.Features.Add(Feature(1, "PAY-1", SelectedPiId));
            db.Features.Add(Feature(11, "CRD-1", SelectedPiId));
            db.Features.Add(Feature(12, "CRD-2", EarlierPiId));
            db.Features.Add(Feature(13, "CRD-3", null, labels: "pi-26-1"));
            db.IssueLinks.Add(Link("501", "CRD-1", "PAY-1"));
            db.IssueLinks.Add(Link("502", "PAY-1", "CRD-2"));
            db.IssueLinks.Add(Link("503", "PAY-1", "CRD-3"));
        });

        var data = await GetAsync();

        var samePi = FeatureOf(data, "CRD-1");
        Assert.Equal(DependencyEndKind.OtherArt, samePi.Kind);
        Assert.Equal((CardsId, "Cards"), (samePi.ArtId!.Value, samePi.ArtName!));
        Assert.True(samePi.InSelectedPi);

        var earlierPi = FeatureOf(data, "CRD-2");
        Assert.Equal(DependencyEndKind.OtherArt, earlierPi.Kind);
        Assert.False(earlierPi.InSelectedPi);
        Assert.Equal("PI 25.4", earlierPi.PiName);

        Assert.True(FeatureOf(data, "CRD-3").InSelectedPi);
        Assert.False((await GetAsync(includePiLabelMatches: false)).Features.Single(f => f.JiraId == "CRD-3").InSelectedPi);
    }

    [Fact]
    public async Task Unassigned_linked_feature_is_an_other_art_end_without_an_art()
    {
        await SeedBaseAsync();
        await _db.SeedAsync(db =>
        {
            db.Features.Add(Feature(1, "PAY-1", SelectedPiId));
            db.Features.Add(Feature(21, "XYZ-1", SelectedPiId));
            db.Features.Add(Feature(22, "AMB-1", SelectedPiId));
            db.IssueLinks.Add(Link("501", "PAY-1", "XYZ-1"));
            db.IssueLinks.Add(Link("502", "PAY-1", "AMB-1"));
        });

        var data = await GetAsync();

        foreach (var key in new[] { "XYZ-1", "AMB-1" })
        {
            var other = FeatureOf(data, key);
            Assert.Equal(DependencyEndKind.OtherArt, other.Kind);
            Assert.Null(other.ArtId);
            Assert.Null(other.ArtName);
        }
    }

    [Fact]
    public async Task Outside_features_sharing_a_key_prefer_the_same_art_then_the_lowest_id()
    {
        await SeedBaseAsync();
        await _db.SeedAsync(db =>
        {
            db.Features.Add(Feature(1, "PAY-1", SelectedPiId));
            db.Features.Add(Feature(40, "PAY-50", SelectedPiId, projectKey: "CRD"));
            db.Features.Add(Feature(41, "PAY-50", EarlierPiId, projectKey: "PAY"));
            db.Features.Add(Feature(43, "CRD-9", SelectedPiId));
            db.Features.Add(Feature(42, "CRD-9", EarlierPiId));
            db.IssueLinks.Add(Link("501", "PAY-50", "PAY-1"));
            db.IssueLinks.Add(Link("502", "CRD-9", "PAY-1"));
        });

        var data = await GetAsync();

        Assert.Equal([1, 42, 41], data.Features.Select(f => f.FeatureId));
        Assert.Equal(DependencyEndKind.OtherPi, FeatureOf(data, "PAY-50").Kind);
    }

    [Fact]
    public async Task Linked_business_outcome_epic_and_objective_become_stubs_with_summaries()
    {
        await SeedBaseAsync();
        await _db.SeedAsync(db =>
        {
            db.Features.Add(Feature(1, "PAY-1", SelectedPiId));
            db.BusinessOutcomes.Add(new BusinessOutcome { Id = 7, JiraId = "BO-7", Summary = "Grow the base", Status = "Open" });
            db.PortfolioEpics.Add(new PortfolioEpic { Id = 3, JiraId = "PE-3", Summary = "Epic three", Status = "Funnel" });
            db.StrategicObjectives.Add(new StrategicObjective { Id = 5, JiraId = "SO-5", Summary = "Objective five" });
            db.IssueLinks.Add(Link("501", "PAY-1", "BO-7"));
            db.IssueLinks.Add(Link("502", "PE-3", "PAY-1"));
            db.IssueLinks.Add(Link("503", "SO-5", "PAY-1"));
        });

        var data = await GetAsync();

        Assert.Equal(
            [
                new DependencyStub("BO-7", DependencyStubKind.BusinessOutcome, "Grow the base", "Open"),
                new DependencyStub("PE-3", DependencyStubKind.PortfolioEpic, "Epic three", "Funnel"),
                new DependencyStub("SO-5", DependencyStubKind.StrategicObjective, "Objective five", null)
            ],
            data.Stubs);
        Assert.Single(data.Features);
    }

    [Fact]
    public async Task Unknown_key_becomes_a_not_in_tool_stub()
    {
        await SeedBaseAsync();
        await _db.SeedAsync(db =>
        {
            db.Features.Add(Feature(1, "PAY-1", SelectedPiId));
            db.IssueLinks.Add(Link("501", "PAY-1", "STORY-9"));
        });

        var data = await GetAsync();

        Assert.Equal(new DependencyStub("STORY-9", DependencyStubKind.NotInTool, null, null), Assert.Single(data.Stubs));
    }

    [Fact]
    public async Task Art_sprints_are_the_art_sprints_of_the_pi_by_start_date()
    {
        await SeedBaseAsync();
        await _db.SeedAsync(db =>
        {
            db.CapitalProjectSprints.Add(ArtSprint(102, PaymentsId, SelectedPiId, "Sprint 2", new DateTime(2026, 1, 19)));
            db.CapitalProjectSprints.Add(ArtSprint(105, PaymentsId, SelectedPiId, "IP", new DateTime(2026, 3, 2), isIp: true));
            db.CapitalProjectSprints.Add(ArtSprint(101, PaymentsId, SelectedPiId, "Sprint 1", PiStart));
            db.CapitalProjectSprints.Add(ArtSprint(201, PaymentsId, EarlierPiId, "Sprint 0", EarlierPiStart));
            db.CapitalProjectSprints.Add(ArtSprint(301, CardsId, SelectedPiId, "Cards 1", PiStart));
        });

        var data = await GetAsync();

        Assert.Equal(
            [
                new DependencyArtSprint(101, "Sprint 1", PiStart, PiStart.AddDays(13), false),
                new DependencyArtSprint(102, "Sprint 2", new DateTime(2026, 1, 19), new DateTime(2026, 2, 1), false),
                new DependencyArtSprint(105, "IP", new DateTime(2026, 3, 2), new DateTime(2026, 3, 15), true)
            ],
            data.ArtSprints);
        Assert.True(data.HasArtSprints);
    }

    [Fact]
    public async Task Pi_row_without_art_sprints_takes_them_from_same_named_rows()
    {
        await SeedBaseAsync();
        await _db.SeedAsync(db =>
        {
            db.Pis.Add(new Pi { Id = 8, Name = "PI 26.1" });
            db.CapitalProjectSprints.Add(ArtSprint(101, PaymentsId, SelectedPiId, "Sprint 1", PiStart));
            db.CapitalProjectSprints.Add(ArtSprint(102, PaymentsId, SelectedPiId, "Sprint 2", new DateTime(2026, 1, 19)));
            db.CapitalProjectSprints.Add(ArtSprint(801, PaymentsId, 8, "S1", PiStart));
        });

        var twin = await _service.GetAsync(PaymentsId, SameNamedPiId, includePiLabelMatches: false);

        Assert.Equal([101, 102], twin!.ArtSprints.Select(s => s.Id));
        Assert.Equal(PiStart, twin.PiStart);
    }

    [Fact]
    public async Task Pi_row_with_its_own_art_sprints_keeps_only_those()
    {
        await SeedBaseAsync();
        await _db.SeedAsync(db =>
        {
            db.CapitalProjectSprints.Add(ArtSprint(101, PaymentsId, SelectedPiId, "Sprint 1", PiStart));
            db.CapitalProjectSprints.Add(ArtSprint(102, PaymentsId, SelectedPiId, "Sprint 2", new DateTime(2026, 1, 19)));
            db.CapitalProjectSprints.Add(ArtSprint(301, PaymentsId, SameNamedPiId, "S1", PiStart.AddDays(-7)));
            db.CapitalProjectSprints.Add(ArtSprint(302, PaymentsId, SameNamedPiId, "S2", new DateTime(2026, 1, 12)));
        });

        var selected = await GetAsync(includePiLabelMatches: false);
        var twin = await _service.GetAsync(PaymentsId, SameNamedPiId, includePiLabelMatches: false);

        Assert.Equal([101, 102], selected.ArtSprints.Select(s => s.Id));
        Assert.Equal([301, 302], twin!.ArtSprints.Select(s => s.Id));
    }

    [Fact]
    public async Task An_art_without_sprints_in_the_pi_has_none()
    {
        await SeedBaseAsync();

        var data = await GetAsync();

        Assert.Empty(data.ArtSprints);
        Assert.False(data.HasArtSprints);
    }

    [Fact]
    public async Task Planned_sprints_outside_the_pi_are_dropped()
    {
        await SeedBaseAsync();
        await _db.SeedAsync(db =>
        {
            db.Features.Add(Feature(1, "PAY-1", SelectedPiId));
            db.FeatureTeams.Add(new FeatureTeam { FeatureId = 1, TeamId = Axis });
            db.Sprints.Add(TeamSprint(1001, Axis, SelectedPiId, new DateTime(2026, 1, 19), sourceArtSprintId: 102));
            db.Sprints.Add(TeamSprint(1002, Axis, EarlierPiId, EarlierPiStart));
            db.Sprints.Add(TeamSprint(1003, Axis, null, PiStart));
            db.Sprints.Add(TeamSprint(1004, Axis, null, new DateTime(2026, 6, 1)));
            db.FeatureTeamSprints.Add(new FeatureTeamSprint { FeatureId = 1, TeamId = Axis, SprintId = 1001 });
            db.FeatureTeamSprints.Add(new FeatureTeamSprint { FeatureId = 1, TeamId = Axis, SprintId = 1002 });
            db.FeatureTeamSprints.Add(new FeatureTeamSprint { FeatureId = 1, TeamId = Axis, SprintId = 1003 });
            db.FeatureTeamSprints.Add(new FeatureTeamSprint { FeatureId = 1, TeamId = Axis, SprintId = 1004 });
        });

        var data = await GetAsync();

        var team = Assert.Single(Assert.Single(data.Features).Teams);
        Assert.Equal(
            [
                new DependencyPlannedSprint(1003, "Sprint 1003", PiStart, PiStart.AddDays(13), null),
                new DependencyPlannedSprint(1001, "Sprint 1001", new DateTime(2026, 1, 19), new DateTime(2026, 2, 1), 102)
            ],
            team.Sprints);
    }

    [Fact]
    public async Task Outside_feature_not_in_the_pi_has_no_planned_sprints()
    {
        await SeedBaseAsync();
        await _db.SeedAsync(db =>
        {
            db.Features.Add(Feature(1, "PAY-1", SelectedPiId));
            db.Features.Add(Feature(4, "PAY-4", EarlierPiId));
            db.Features.Add(Feature(11, "CRD-1", SelectedPiId));
            db.FeatureTeams.Add(new FeatureTeam { FeatureId = 4, TeamId = Axis });
            db.FeatureTeams.Add(new FeatureTeam { FeatureId = 11, TeamId = Zeta });
            db.Sprints.Add(TeamSprint(2001, Axis, EarlierPiId, EarlierPiStart));
            db.Sprints.Add(TeamSprint(2002, Axis, SelectedPiId, PiStart));
            db.Sprints.Add(TeamSprint(2003, Zeta, SelectedPiId, PiStart));
            db.FeatureTeamSprints.Add(new FeatureTeamSprint { FeatureId = 4, TeamId = Axis, SprintId = 2001 });
            db.FeatureTeamSprints.Add(new FeatureTeamSprint { FeatureId = 4, TeamId = Axis, SprintId = 2002 });
            db.FeatureTeamSprints.Add(new FeatureTeamSprint { FeatureId = 11, TeamId = Zeta, SprintId = 2003 });
            db.IssueLinks.Add(Link("501", "PAY-4", "PAY-1"));
            db.IssueLinks.Add(Link("502", "CRD-1", "PAY-1"));
        });

        var data = await GetAsync();

        var earlier = Assert.Single(FeatureOf(data, "PAY-4").Teams);
        Assert.Equal(Axis, earlier.TeamId);
        Assert.Empty(earlier.Sprints);

        var inPi = Assert.Single(FeatureOf(data, "CRD-1").Teams);
        Assert.Equal([2003], inPi.Sprints.Select(s => s.SprintId));
    }

    [Fact]
    public async Task Teams_are_ordered_primary_first_then_by_name()
    {
        await SeedBaseAsync();
        await _db.SeedAsync(db =>
        {
            db.Features.Add(Feature(1, "PAY-1", SelectedPiId));
            db.Features.Add(Feature(2, "PAY-2", SelectedPiId));
            db.FeatureTeams.Add(new FeatureTeam { FeatureId = 1, TeamId = Axis });
            db.FeatureTeams.Add(new FeatureTeam { FeatureId = 1, TeamId = Regal, IsPrimary = true });
            db.FeatureTeams.Add(new FeatureTeam { FeatureId = 1, TeamId = Bravo });
            db.FeatureTeams.Add(new FeatureTeam { FeatureId = 2, TeamId = Regal });
            db.FeatureTeams.Add(new FeatureTeam { FeatureId = 2, TeamId = Axis, IsPrimary = false });
        });

        var data = await GetAsync();

        var first = FeatureOf(data, "PAY-1");
        Assert.Equal([Regal, Axis, Bravo], first.Teams.Select(t => t.TeamId));
        Assert.Equal([true, false, false], first.Teams.Select(t => t.IsPrimary));
        Assert.Equal([Axis, Regal], FeatureOf(data, "PAY-2").Teams.Select(t => t.TeamId));
    }

    [Fact]
    public async Task Below_line_and_sort_order_come_from_capacity_orders_of_the_pi()
    {
        await SeedBaseAsync();
        await _db.SeedAsync(db =>
        {
            db.Features.Add(Feature(1, "PAY-1", SelectedPiId));
            db.FeatureTeams.Add(new FeatureTeam { FeatureId = 1, TeamId = Axis });
            db.FeatureTeams.Add(new FeatureTeam { FeatureId = 1, TeamId = Regal });
            db.FeatureTeams.Add(new FeatureTeam { FeatureId = 1, TeamId = Bravo });
            db.TeamCapacityFeatureOrders.Add(new TeamCapacityFeatureOrder { TeamId = Axis, PiId = SelectedPiId, FeatureId = 1, SortOrder = 3, IsIncluded = false });
            db.TeamCapacityFeatureOrders.Add(new TeamCapacityFeatureOrder { TeamId = Regal, PiId = SelectedPiId, FeatureId = 1, SortOrder = 7, IsIncluded = true });
            db.TeamCapacityFeatureOrders.Add(new TeamCapacityFeatureOrder { TeamId = Bravo, PiId = EarlierPiId, FeatureId = 1, SortOrder = 1, IsIncluded = false });
        });

        var data = await GetAsync();

        var teams = Assert.Single(data.Features).Teams.ToDictionary(t => t.TeamId);
        Assert.Equal((true, 3), (teams[Axis].BelowLine, teams[Axis].SortOrder));
        Assert.Equal((false, 7), (teams[Regal].BelowLine, teams[Regal].SortOrder));
        Assert.Equal((false, (int?)null), (teams[Bravo].BelowLine, teams[Bravo].SortOrder));
    }

    [Fact]
    public async Task A_card_below_an_art_line_puts_its_features_below_the_line_on_that_art_teams()
    {
        await SeedBaseAsync();
        await _db.SeedAsync(db =>
        {
            db.CapitalProjects.Find(PaymentsId)!.Prioritization = ArtPrioritization.BusinessOutcome;
            db.CapitalProjects.Find(CardsId)!.Prioritization = ArtPrioritization.PortfolioEpic;
            db.PortfolioEpics.Add(new PortfolioEpic { Id = 7, JiraId = "EPIC-7", Summary = "Epic" });
            db.BusinessOutcomes.Add(new BusinessOutcome { Id = 1, JiraId = "PAY-900", Summary = "Payments outcome" });
            db.BusinessOutcomes.Add(new BusinessOutcome { Id = 2, JiraId = "CRD-900", Summary = "Cards outcome", PortfolioEpicId = 7 });
            var payments = Feature(1, "PAY-1", SelectedPiId);
            payments.BusinessOutcomeId = 1;
            var cards = Feature(11, "CRD-1", SelectedPiId);
            cards.BusinessOutcomeId = 2;
            db.Features.AddRange(payments, cards);
            db.FeatureTeams.Add(new FeatureTeam { FeatureId = 1, TeamId = Axis });
            db.FeatureTeams.Add(new FeatureTeam { FeatureId = 1, TeamId = Zeta });
            db.FeatureTeams.Add(new FeatureTeam { FeatureId = 11, TeamId = Zeta });
            db.IssueLinks.Add(Link("601", "CRD-1", "PAY-1"));
            db.ArtPrioritizationOrders.Add(new ArtPrioritizationOrder
            {
                CapitalProjectId = PaymentsId, PiId = SelectedPiId, Level = ArtPrioritization.BusinessOutcome,
                BusinessOutcomeId = 1, SortOrder = 1, IsIncluded = false
            });
            db.ArtPrioritizationOrders.Add(new ArtPrioritizationOrder
            {
                CapitalProjectId = CardsId, PiId = SelectedPiId, Level = ArtPrioritization.PortfolioEpic,
                PortfolioEpicId = 7, SortOrder = 1, IsIncluded = false
            });
        });

        var data = await GetAsync();

        var payments = FeatureOf(data, "PAY-1").Teams.ToDictionary(t => t.TeamId);
        Assert.True(payments[Axis].BelowLine);
        Assert.False(payments[Zeta].BelowLine);
        Assert.True(Assert.Single(FeatureOf(data, "CRD-1").Teams).BelowLine);
    }

    [Fact]
    public async Task Capacity_line_of_the_selected_pi_applies_only_to_features_in_that_pi()
    {
        await SeedBaseAsync();
        await _db.SeedAsync(db =>
        {
            db.Features.Add(Feature(1, "PAY-1", SelectedPiId));
            db.Features.Add(Feature(4, "PAY-4", EarlierPiId));
            db.Features.Add(Feature(11, "CRD-1", SelectedPiId));
            db.FeatureTeams.Add(new FeatureTeam { FeatureId = 4, TeamId = Axis });
            db.FeatureTeams.Add(new FeatureTeam { FeatureId = 11, TeamId = Zeta });
            db.TeamCapacityFeatureOrders.Add(new TeamCapacityFeatureOrder { TeamId = Axis, PiId = SelectedPiId, FeatureId = 4, SortOrder = 2, IsIncluded = false });
            db.TeamCapacityFeatureOrders.Add(new TeamCapacityFeatureOrder { TeamId = Zeta, PiId = SelectedPiId, FeatureId = 11, SortOrder = 5, IsIncluded = false });
            db.IssueLinks.Add(Link("501", "PAY-4", "PAY-1"));
            db.IssueLinks.Add(Link("502", "CRD-1", "PAY-1"));
        });

        var data = await GetAsync();

        var otherPi = Assert.Single(FeatureOf(data, "PAY-4").Teams);
        Assert.Equal((false, (int?)null), (otherPi.BelowLine, otherPi.SortOrder));

        var samePi = Assert.Single(FeatureOf(data, "CRD-1").Teams);
        Assert.Equal((true, (int?)5), (samePi.BelowLine, samePi.SortOrder));

        var index = DependencyClassifier.Build(data);
        var verdict = index.Dependencies.Single(d => d.Id == "f4>f1").Verdict;
        Assert.False(verdict.Flags.HasFlag(DependencyFlags.ProviderBelowLine));
        Assert.DoesNotContain("capacity line", verdict.Text);
    }

    [Fact]
    public async Task Team_catalogue_has_art_teams_without_features_and_marks_shared_and_other_art_teams()
    {
        await SeedBaseAsync();
        await _db.SeedAsync(db =>
        {
            db.Features.Add(Feature(1, "PAY-1", SelectedPiId));
            db.FeatureTeams.Add(new FeatureTeam { FeatureId = 1, TeamId = Axis, IsPrimary = true });
            db.FeatureTeams.Add(new FeatureTeam { FeatureId = 1, TeamId = Zeta });
            db.FeatureTeams.Add(new FeatureTeam { FeatureId = 1, TeamId = Loner });
        });

        var data = await GetAsync();

        Assert.Equal(["Axis", "Bravo", "Loner", "Regal", "Zeta"], data.Teams.Select(t => t.Name));
        var teams = data.Teams.ToDictionary(t => t.TeamId);
        Assert.Equal((true, false), (teams[Axis].InArt, teams[Axis].IsShared));
        Assert.Equal(["Payments"], teams[Axis].ArtNames);
        Assert.True(teams[Regal].InArt);
        Assert.Equal((true, true), (teams[Bravo].InArt, teams[Bravo].IsShared));
        Assert.Equal(["Cards", "Payments"], teams[Bravo].ArtNames);
        Assert.False(teams[Zeta].InArt);
        Assert.Equal(["Cards"], teams[Zeta].ArtNames);
        Assert.False(teams[Loner].InArt);
        Assert.Empty(teams[Loner].ArtNames);
    }

    [Fact]
    public async Task Scope_of_each_feature_is_the_scope_of_its_art_match()
    {
        await SeedBaseAsync();
        await _db.SeedAsync(db =>
        {
            db.Features.Add(Feature(1, "PAY-1", SelectedPiId));
            db.Features.Add(Feature(11, "CRD-1", SelectedPiId));
            db.Features.Add(Feature(21, "XYZ-1", SelectedPiId));
            db.IssueLinks.Add(Link("501", "PAY-1", "CRD-1"));
            db.IssueLinks.Add(Link("502", "PAY-1", "XYZ-1"));
        });

        var data = await GetAsync();

        var inScope = FeatureOf(data, "PAY-1").Scope;
        Assert.Equal(TrainScopeKind.Trains, inScope.Kind);
        Assert.Equal([PaymentsId], inScope.CapitalProjectIds);

        var otherArt = FeatureOf(data, "CRD-1").Scope;
        Assert.Equal(TrainScopeKind.Trains, otherArt.Kind);
        Assert.Equal([CardsId], otherArt.CapitalProjectIds);

        Assert.Equal(TrainScopeKind.AdminOrGeneralOnly, FeatureOf(data, "XYZ-1").Scope.Kind);
    }

    [Fact]
    public async Task Blank_labels_fall_back_to_the_type_name()
    {
        await SeedBaseAsync();
        await _db.SeedAsync(db =>
        {
            db.Features.Add(Feature(1, "PAY-1", SelectedPiId));
            db.IssueLinks.Add(Link("501", "PAY-1", "STORY-1", "Cloners", null, "  "));
            db.IssueLinks.Add(Link("502", "PAY-1", "STORY-2", "", null, null));
        });

        var data = await GetAsync();

        Assert.Equal(
            [
                new DependencyLinkRow("501", "Cloners", "Cloners", "Cloners", "PAY-1", "STORY-1"),
                new DependencyLinkRow("502", "(unnamed)", "(unnamed)", "(unnamed)", "PAY-1", "STORY-2")
            ],
            data.Links);
    }

    [Fact]
    public async Task Filter_row_fields_are_filled_for_in_scope_features()
    {
        await SeedBaseAsync();
        await _db.SeedAsync(db =>
        {
            var objective = new StrategicObjective { Id = 5, JiraId = "SO-5", Summary = "Objective five" };
            var epic = new PortfolioEpic { Id = 3, JiraId = "PE-3", Summary = "Epic three" };
            var outcome = new BusinessOutcome { Id = 7, JiraId = "BO-7", Summary = "Grow the base", PortfolioEpic = epic };
            db.StrategicObjectives.Add(objective);
            db.PortfolioEpics.Add(epic);
            db.StrategicObjectivePortfolioEpics.Add(new StrategicObjectivePortfolioEpic { StrategicObjective = objective, PortfolioEpic = epic });
            db.BusinessOutcomes.Add(outcome);
            db.RequirementStatuses.Add(new RequirementStatus { Id = 2, Name = "Ready" });
            db.TechnicalApprovals.Add(new TechnicalApproval { Id = 7, Name = "Approved" });
            db.UnfundedOptions.Add(new UnfundedOption { Id = 4, Name = "Funded" });

            var feature = Feature(1, "PAY-1", SelectedPiId, labels: "alpha,beta");
            feature.BusinessOutcome = outcome;
            feature.RequirementStatusId = 2;
            feature.TechnicalApprovalId = 7;
            feature.UnfundedOptionId = 4;
            feature.Dependencies = "Needs the gateway";
            feature.ExternalDependencies = true;
            feature.RagStatus = "Amber";
            feature.Ranking = 12;
            db.Features.Add(feature);

            var outside = Feature(11, "CRD-1", SelectedPiId);
            outside.BusinessOutcome = outcome;
            outside.RequirementStatusId = 2;
            db.Features.Add(outside);
            db.IssueLinks.Add(Link("501", "PAY-1", "CRD-1"));
        });

        var data = await GetAsync();

        var row = FeatureOf(data, "PAY-1");
        Assert.Equal(("BO-7", "Grow the base"), (row.BusinessOutcomeJiraId, row.BusinessOutcomeSummary));
        Assert.Equal(("PE-3", "Epic three"), (row.PortfolioEpicJiraId, row.PortfolioEpicSummary));
        Assert.Equal([new FeatureFilterRef("SO-5", "Objective five")], row.StrategicObjectives);
        Assert.Equal("Ready", row.RequirementStatus);
        Assert.Equal("Approved", row.TechnicalApproval);
        Assert.Equal("Funded", row.FundingStatus);
        Assert.Equal("alpha,beta", row.Labels);
        Assert.Equal("Needs the gateway", row.FreeTextDependencies);
        Assert.True(row.ExternalDependencies);
        Assert.Equal("Amber", row.RagStatus);
        Assert.Equal(12, row.Ranking);
        Assert.Equal(("Feature 1", "Summary 1", "In Progress"), (row.Name, row.Summary, row.Status));

        var outsideRow = FeatureOf(data, "CRD-1");
        Assert.Null(outsideRow.BusinessOutcomeJiraId);
        Assert.Null(outsideRow.PortfolioEpicJiraId);
        Assert.Empty(outsideRow.StrategicObjectives);
        Assert.Null(outsideRow.RequirementStatus);
    }

    [Fact]
    public async Task Loaded_data_builds_an_index_with_planned_verdicts()
    {
        await SeedBaseAsync();
        await _db.SeedAsync(db =>
        {
            db.CapitalProjectSprints.Add(ArtSprint(101, PaymentsId, SelectedPiId, "Sprint 1", PiStart));
            db.CapitalProjectSprints.Add(ArtSprint(102, PaymentsId, SelectedPiId, "Sprint 2", PiStart.AddDays(14)));
            db.Sprints.Add(TeamSprint(1001, Axis, SelectedPiId, PiStart, sourceArtSprintId: 101));
            db.Sprints.Add(TeamSprint(1102, Regal, SelectedPiId, PiStart.AddDays(14), sourceArtSprintId: 102));
            db.Features.Add(Feature(1, "PAY-1", SelectedPiId));
            db.Features.Add(Feature(2, "PAY-2", SelectedPiId));
            db.Features.Add(Feature(4, "PAY-4", EarlierPiId));
            db.FeatureTeams.Add(new FeatureTeam { FeatureId = 1, TeamId = Regal, IsPrimary = true });
            db.FeatureTeams.Add(new FeatureTeam { FeatureId = 2, TeamId = Axis, IsPrimary = true });
            db.FeatureTeamSprints.Add(new FeatureTeamSprint { FeatureId = 1, TeamId = Regal, SprintId = 1102 });
            db.FeatureTeamSprints.Add(new FeatureTeamSprint { FeatureId = 2, TeamId = Axis, SprintId = 1001 });
            db.IssueLinks.Add(Link("501", "PAY-1", "PAY-2"));
            db.IssueLinks.Add(Link("502", "PAY-4", "PAY-2"));
            db.IssueLinks.Add(Link("503", "PAY-2", "STORY-1"));
        });

        var index = DependencyClassifier.Build(await GetAsync());

        Assert.Equal(["f1>f2", "f4>f2", "f2>k:STORY-1"], index.Dependencies.Select(d => d.Id));
        Assert.Equal(
            [DependencyRisk.OutOfOrder, DependencyRisk.AtRisk, DependencyRisk.NotChecked],
            index.Dependencies.Select(d => d.Verdict.Risk));
        Assert.Equal("PAY-1 ends in Sprint 2, after PAY-2 starts in Sprint 1", index.Dependencies[0].Verdict.Text);
        Assert.Equal("PAY-4 is from an earlier PI (PI 25.4) and is still open", index.Dependencies[1].Verdict.Text);
        Assert.Equal(["t11"], index.Nodes["f1"].LaneIds);
    }

    [Fact]
    public async Task Several_arts_in_one_call_give_the_same_maps_as_one_call_per_art()
    {
        await SeedBaseAsync();
        await _db.SeedAsync(db =>
        {
            db.CapitalProjects.Find(PaymentsId)!.Prioritization = ArtPrioritization.BusinessOutcome;
            db.CapitalProjects.Find(CardsId)!.Prioritization = ArtPrioritization.PortfolioEpic;
            db.CapitalProjects.Add(new Art { Id = 3, Name = "Keyless" });
            db.CapitalProjectTeams.Add(new ArtTeam { CapitalProjectId = 3, TeamId = Loner });
            db.Pis.Add(new Pi { Id = 4, Name = "PI 26.2", StartDate = new DateTime(2026, 3, 9), FeatureLabels = "pi-26-2" });
            db.CapitalProjectSprints.Add(ArtSprint(101, PaymentsId, SelectedPiId, "Sprint 1", PiStart));
            db.CapitalProjectSprints.Add(ArtSprint(102, PaymentsId, SelectedPiId, "Sprint 2", PiStart.AddDays(14)));
            db.CapitalProjectSprints.Add(ArtSprint(301, CardsId, SameNamedPiId, "Cards 1", PiStart.AddDays(7)));
            db.Sprints.Add(TeamSprint(1001, Axis, SelectedPiId, PiStart, sourceArtSprintId: 101));
            db.Sprints.Add(TeamSprint(1102, Regal, SelectedPiId, PiStart.AddDays(14), sourceArtSprintId: 102));
            db.Sprints.Add(TeamSprint(2001, Zeta, SelectedPiId, PiStart.AddDays(7)));
            db.Sprints.Add(TeamSprint(2002, Bravo, EarlierPiId, EarlierPiStart));
            db.PortfolioEpics.Add(new PortfolioEpic { Id = 3, JiraId = "PE-3", Summary = "Epic three", Status = "Funnel" });
            db.PortfolioEpics.Add(new PortfolioEpic { Id = 7, JiraId = "EPIC-7", Summary = "Epic" });
            db.BusinessOutcomes.Add(new BusinessOutcome { Id = 1, JiraId = "BO-1", Summary = "Payments outcome" });
            db.BusinessOutcomes.Add(new BusinessOutcome { Id = 2, JiraId = "BO-2", Summary = "Cards outcome", PortfolioEpicId = 7 });
            db.StrategicObjectives.Add(new StrategicObjective { Id = 5, JiraId = "SO-5", Summary = "Objective five" });

            var payments = Feature(1, "PAY-1", SelectedPiId);
            payments.BusinessOutcomeId = 1;
            var cards = Feature(11, "CRD-1", SelectedPiId);
            cards.BusinessOutcomeId = 2;
            db.Features.AddRange(
                payments,
                Feature(2, "PAY-2", SelectedPiId),
                Feature(3, null, SelectedPiId, projectKey: "PAY"),
                Feature(4, "PAY-4", EarlierPiId),
                Feature(5, "PAY-5", null, labels: "pi-26-1"),
                cards,
                Feature(12, "CRD-2", EarlierPiId),
                Feature(13, "CRD-3", null, labels: "pi-26-2"),
                Feature(21, "XYZ-1", SelectedPiId),
                Feature(22, "AMB-1", SelectedPiId));
            db.FeatureTeams.AddRange(
                new FeatureTeam { FeatureId = 1, TeamId = Axis, IsPrimary = true },
                new FeatureTeam { FeatureId = 1, TeamId = Zeta },
                new FeatureTeam { FeatureId = 2, TeamId = Regal },
                new FeatureTeam { FeatureId = 4, TeamId = Axis },
                new FeatureTeam { FeatureId = 11, TeamId = Zeta, IsPrimary = true },
                new FeatureTeam { FeatureId = 11, TeamId = Bravo },
                new FeatureTeam { FeatureId = 12, TeamId = Bravo },
                new FeatureTeam { FeatureId = 21, TeamId = Loner });
            db.FeatureTeamSprints.AddRange(
                new FeatureTeamSprint { FeatureId = 1, TeamId = Axis, SprintId = 1001 },
                new FeatureTeamSprint { FeatureId = 2, TeamId = Regal, SprintId = 1102 },
                new FeatureTeamSprint { FeatureId = 11, TeamId = Zeta, SprintId = 2001 },
                new FeatureTeamSprint { FeatureId = 12, TeamId = Bravo, SprintId = 2002 });
            db.TeamCapacityFeatureOrders.AddRange(
                new TeamCapacityFeatureOrder { TeamId = Regal, PiId = SelectedPiId, FeatureId = 2, SortOrder = 3, IsIncluded = false },
                new TeamCapacityFeatureOrder { TeamId = Zeta, PiId = SelectedPiId, FeatureId = 11, SortOrder = 5, IsIncluded = true },
                new TeamCapacityFeatureOrder { TeamId = Zeta, PiId = SameNamedPiId, FeatureId = 1, SortOrder = 2, IsIncluded = false });
            db.ArtPrioritizationOrders.AddRange(
                new ArtPrioritizationOrder
                {
                    CapitalProjectId = PaymentsId, PiId = SelectedPiId, Level = ArtPrioritization.BusinessOutcome,
                    BusinessOutcomeId = 1, SortOrder = 1, IsIncluded = false
                },
                new ArtPrioritizationOrder
                {
                    CapitalProjectId = CardsId, PiId = SelectedPiId, Level = ArtPrioritization.PortfolioEpic,
                    PortfolioEpicId = 7, SortOrder = 1, IsIncluded = false
                });
            db.IssueLinks.AddRange(
                Link("501", "PAY-1", "CRD-1"),
                Link("502", "PAY-4", "PAY-2"),
                Link("503", "CRD-2", "CRD-1"),
                Link("504", "CRD-3", "PAY-1"),
                Link("505", "PAY-2", "BO-1"),
                Link("506", "PE-3", "CRD-1"),
                Link("507", "SO-5", "PAY-5"),
                Link("508", "CRD-1", "STORY-9"),
                Link("509", "PAY-2", "STORY-9"),
                Link("510", "PAY-5", "XYZ-1"),
                Link("511", "AMB-1", "CRD-1"),
                Link("512", "STORY-1", "STORY-2"));
        });

        int[] artIds = [PaymentsId, CardsId, 3, 99];
        foreach (var includePiLabelMatches in new[] { true, false })
        {
            var maps = await _service.GetManyAsync(artIds, SelectedPiId, includePiLabelMatches);

            Assert.Equal(artIds, maps.Keys);
            foreach (var artId in artIds)
            {
                Assert.Equal(Json(await _service.GetAsync(artId, SelectedPiId, includePiLabelMatches)), Json(maps[artId]));
            }
        }

        var all = await _service.GetManyAsync(artIds, SelectedPiId);
        var paymentsMap = all[PaymentsId]!;
        var cardsMap = all[CardsId]!;
        Assert.Equal([null, "PAY-1", "PAY-2", "PAY-5", "CRD-1", "CRD-3", "PAY-4", "XYZ-1"], paymentsMap.Features.Select(f => f.JiraId));
        Assert.Equal(DependencyEndKind.OtherArt, FeatureOf(paymentsMap, "CRD-1").Kind);
        Assert.Equal(["BO-1", "SO-5", "STORY-9"], paymentsMap.Stubs.Select(s => s.Key));
        Assert.Equal(["CRD-1", "AMB-1", "CRD-2", "PAY-1"], cardsMap.Features.Select(f => f.JiraId));
        Assert.Equal(DependencyEndKind.OtherArt, FeatureOf(cardsMap, "PAY-1").Kind);
        Assert.Equal(["PE-3", "STORY-9"], cardsMap.Stubs.Select(s => s.Key));
        Assert.Equal([301], cardsMap.ArtSprints.Select(s => s.Id));
        Assert.Empty(all[3]!.Features);
        Assert.Equal(["Loner"], all[3]!.Teams.Select(t => t.Name));
        Assert.Null(all[99]);
    }

    [Fact]
    public async Task One_call_maps_each_requested_art_once_and_gives_null_for_an_unknown_pi()
    {
        await SeedBaseAsync();

        var unknownPi = await _service.GetManyAsync([PaymentsId, CardsId], 99);
        var repeated = await _service.GetManyAsync([CardsId, 99, CardsId], SelectedPiId);

        Assert.Equal([PaymentsId, CardsId], unknownPi.Keys);
        Assert.All(unknownPi.Values, map => Assert.Null(map));
        Assert.Equal([CardsId, 99], repeated.Keys);
        Assert.Equal("Cards", repeated[CardsId]!.ArtName);
        Assert.Null(repeated[99]);
        Assert.Empty(await _service.GetManyAsync([], SelectedPiId));
    }

    private static string Json(DependencyMapData? data) => JsonSerializer.Serialize(data);

    private static ArtSprint ArtSprint(int id, int artId, int piId, string name, DateTime start, bool isIp = false) => new()
    {
        Id = id,
        CapitalProjectId = artId,
        PiId = piId,
        Name = name,
        StartDate = start,
        EndDate = start.AddDays(13),
        UatStart = start.AddDays(14),
        UatEnd = start.AddDays(15),
        IsIpSprint = isIp
    };
}

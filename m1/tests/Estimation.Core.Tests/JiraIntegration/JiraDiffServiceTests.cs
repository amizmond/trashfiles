using Estimation.Core.Features.Models;
using Estimation.Core.JiraIntegration.Client;
using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Resources.Models;
using Estimation.Core.Train.Models;
using Xunit;

namespace Estimation.Core.Tests.JiraIntegration;

public class JiraDiffServiceTests
{
    private static readonly JiraDiffService Service = new();

    private static Team Team(int id, string name, string? fullName = null) =>
        new() { Id = id, Name = name, FullName = fullName };

    private static JiraIssueResponse Jira(Action<JiraIssueResponse> configure) => Jira("PROJ-1", configure);

    private static JiraIssueResponse Jira(string key = "PROJ-1", Action<JiraIssueResponse>? configure = null)
    {
        var response = new JiraIssueResponse
        {
            Key = key,
            Summary = "Shared summary",
            Description = "Shared description",
            AcceptanceCriteria = "Shared criteria",
            NavigatorId = "NAV-1",
            Status = "In Progress",
            Labels = new List<string> { "alpha" },
            TargetStart = new DateTime(2026, 7, 1),
            TargetEnd = new DateTime(2026, 9, 30),
            StoryPoints = 8
        };

        configure?.Invoke(response);
        return response;
    }

    private static Feature Db(Action<Feature>? configure = null)
    {
        var feature = new Feature
        {
            Id = 1,
            JiraId = "PROJ-1",
            ProjectKey = "PROJ",
            Summary = "Shared summary",
            Description = "Shared description",
            AcceptanceCriteria = "Shared criteria",
            NavigatorId = "NAV-1",
            Status = "In Progress",
            Labels = "alpha",
            TargetStart = new DateTime(2026, 7, 1),
            TargetEnd = new DateTime(2026, 9, 30),
            StoryPoints = 8
        };

        configure?.Invoke(feature);
        return feature;
    }

    private static FeatureDiffResult DiffOne(
        JiraIssueResponse jira,
        Feature db,
        IReadOnlyCollection<string>? piNames = null,
        IReadOnlyCollection<Team>? teams = null) =>
        Service.DiffFeatures(
            new[] { jira },
            new Dictionary<string, Feature> { [jira.Key] = db },
            piNames ?? Array.Empty<string>(),
            teams ?? Array.Empty<Team>());

    private static JiraDiff Single(FeatureDiffResult result, string property) =>
        Assert.Single(result.Diffs, d => d.PropertyName == property);

    [Fact]
    public void An_identical_feature_produces_no_diffs()
    {
        var result = DiffOne(Jira(), Db());

        Assert.Empty(result.Diffs);
        Assert.Empty(result.TeamsNotFound);
        Assert.Empty(result.PisToCreate);
    }

    [Fact]
    public void A_jira_issue_with_no_local_counterpart_is_skipped()
    {
        var result = Service.DiffFeatures(
            new[] { Jira("PROJ-99") },
            new Dictionary<string, Feature>(),
            Array.Empty<string>(),
            Array.Empty<Team>());

        Assert.Empty(result.Diffs);
    }

    [Fact]
    public void Only_the_issues_present_locally_are_compared()
    {
        var result = Service.DiffFeatures(
            new[] { Jira("PROJ-1", j => j.Summary = "Changed"), Jira("PROJ-2", j => j.Summary = "Also changed") },
            new Dictionary<string, Feature> { ["PROJ-1"] = Db() },
            Array.Empty<string>(),
            Array.Empty<Team>());

        Assert.Equal(new[] { "PROJ-1" }, result.Diffs.Select(d => d.JiraKey).Distinct());
    }

    [Fact]
    public void A_changed_summary_is_reported_with_both_sides()
    {
        var result = DiffOne(Jira(j => { }), Db(f => f.Summary = "Local summary"));

        var diff = Single(result, JiraSyncProperties.Summary);

        Assert.Equal("PROJ-1", diff.JiraKey);
        Assert.Equal("Local summary", diff.DbValue);
        Assert.Equal("Shared summary", diff.JiraValue);
    }

    [Fact]
    public void A_summary_differing_only_by_case_is_still_a_diff()
    {
        var result = DiffOne(Jira(), Db(f => f.Summary = "SHARED SUMMARY"));

        Assert.NotNull(Single(result, JiraSyncProperties.Summary));
    }

    [Theory]
    [InlineData(JiraSyncProperties.Description)]
    [InlineData(JiraSyncProperties.AcceptanceCriteria)]
    [InlineData(JiraSyncProperties.NavigatorId)]
    [InlineData(JiraSyncProperties.Status)]
    public void Each_text_field_is_compared(string property)
    {
        var db = Db(f =>
        {
            switch (property)
            {
                case JiraSyncProperties.Description: f.Description = "Local"; break;
                case JiraSyncProperties.AcceptanceCriteria: f.AcceptanceCriteria = "Local"; break;
                case JiraSyncProperties.NavigatorId: f.NavigatorId = "Local"; break;
                case JiraSyncProperties.Status: f.Status = "Local"; break;
            }
        });

        var result = DiffOne(Jira(), db);

        Assert.Equal("Local", Single(result, property).DbValue);
    }

    [Fact]
    public void Labels_are_compared_against_the_joined_jira_value()
    {
        var result = DiffOne(
            Jira(j => j.Labels = new List<string> { "alpha", "beta" }),
            Db(f => f.Labels = "alpha"));

        var diff = Single(result, JiraSyncProperties.Labels);

        Assert.Equal("alpha", diff.DbValue);
        Assert.Equal("alpha,beta", diff.JiraValue);
    }

    [Fact]
    public void Matching_labels_produce_no_diff()
    {
        var result = DiffOne(
            Jira(j => j.Labels = new List<string> { "alpha", "beta" }),
            Db(f => f.Labels = "alpha,beta"));

        Assert.DoesNotContain(result.Diffs, d => d.PropertyName == JiraSyncProperties.Labels);
    }

    [Fact]
    public void Labels_removed_in_jira_are_reported_as_a_clear()
    {
        var result = DiffOne(Jira(j => j.Labels = null), Db(f => f.Labels = "alpha"));

        Assert.Null(Single(result, JiraSyncProperties.Labels).JiraValue);
    }

    [Fact]
    public void Components_are_compared_against_the_joined_jira_value()
    {
        var result = DiffOne(
            Jira(j => j.Components = new List<string> { "Cards", "Wallet" }),
            Db(f => f.Components = "Cards"));

        var diff = Single(result, JiraSyncProperties.Components);

        Assert.Equal("Cards", diff.DbValue);
        Assert.Equal("Cards,Wallet", diff.JiraValue);
    }

    [Fact]
    public void Components_not_yet_stored_locally_are_reported()
    {
        var result = DiffOne(Jira(j => j.Components = new List<string> { "Cards" }), Db());

        var diff = Single(result, JiraSyncProperties.Components);

        Assert.Null(diff.DbValue);
        Assert.Equal("Cards", diff.JiraValue);
    }

    [Fact]
    public void Matching_components_produce_no_diff()
    {
        var result = DiffOne(
            Jira(j => j.Components = new List<string> { "Cards", "Wallet" }),
            Db(f => f.Components = "Cards,Wallet"));

        Assert.DoesNotContain(result.Diffs, d => d.PropertyName == JiraSyncProperties.Components);
    }

    [Fact]
    public void Components_removed_in_jira_are_reported_as_a_clear()
    {
        var result = DiffOne(Jira(j => j.Components = null), Db(f => f.Components = "Cards"));

        Assert.Null(Single(result, JiraSyncProperties.Components).JiraValue);
    }

    [Fact]
    public void A_train_entity_is_compared_on_its_components()
    {
        var db = new BusinessOutcome
        {
            JiraId = "BO-1",
            Summary = "Shared summary",
            Description = "Shared description",
            AcceptanceCriteria = "Shared criteria",
            NavigatorId = "NAV-1",
            Status = "In Progress",
            Labels = "alpha",
            TargetStart = new DateTime(2026, 7, 1),
            TargetEnd = new DateTime(2026, 9, 30),
            StoryPoints = 8
        };

        var diffs = Service.DiffJiraIssues(
            new[] { Jira("BO-1", j => j.Components = new List<string> { "Cards" }) },
            new Dictionary<string, BusinessOutcome> { ["BO-1"] = db });

        var diff = Assert.Single(diffs);
        Assert.Equal(JiraSyncProperties.Components, diff.PropertyName);
        Assert.Equal("Cards", diff.JiraValue);
    }

    [Fact]
    public void Target_dates_are_reported_in_iso_form()
    {
        var result = DiffOne(
            Jira(j => j.TargetStart = new DateTime(2026, 8, 15)),
            Db(f => f.TargetStart = new DateTime(2026, 7, 1)));

        var diff = Single(result, JiraSyncProperties.TargetStart);

        Assert.Equal("2026-07-01", diff.DbValue);
        Assert.Equal("2026-08-15", diff.JiraValue);
    }

    [Fact]
    public void A_cleared_target_date_is_reported_as_null()
    {
        var result = DiffOne(Jira(j => j.TargetEnd = null), Db());

        var diff = Single(result, JiraSyncProperties.TargetEnd);

        Assert.Equal("2026-09-30", diff.DbValue);
        Assert.Null(diff.JiraValue);
    }

    [Fact]
    public void Changed_story_points_are_reported()
    {
        var result = DiffOne(Jira(j => j.StoryPoints = 13), Db(f => f.StoryPoints = 8));

        var diff = Single(result, JiraSyncProperties.StoryPoints);

        Assert.Equal("8", diff.DbValue);
        Assert.Equal("13", diff.JiraValue);
    }

    [Fact]
    public void The_project_key_is_derived_from_the_issue_key()
    {
        var result = DiffOne(Jira("OTHER-5"), Db(f => f.ProjectKey = "PROJ"));

        var diff = Single(result, JiraSyncProperties.Project);

        Assert.Equal("PROJ", diff.DbValue);
        Assert.Equal("OTHER", diff.JiraValue);
    }

    [Fact]
    public void A_matching_project_key_produces_no_diff_regardless_of_case()
    {
        var result = DiffOne(Jira("proj-1"), Db(f => f.ProjectKey = "PROJ"));

        Assert.DoesNotContain(result.Diffs, d => d.PropertyName == JiraSyncProperties.Project);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_feature_with_no_project_key_is_reported_as_gaining_one(string? projectKey)
    {
        var result = DiffOne(Jira("PROJ-1"), Db(f => f.ProjectKey = projectKey));

        var diff = Single(result, JiraSyncProperties.Project);

        Assert.Null(diff.DbValue);
        Assert.Equal("PROJ", diff.JiraValue);
    }

    [Fact]
    public void The_rag_explanation_is_compared_after_trimming()
    {
        var result = DiffOne(
            Jira(j => j.RagExplain = "  Amber because  "),
            Db(f => f.RagExplain = "Amber because"));

        Assert.DoesNotContain(result.Diffs, d => d.PropertyName == JiraSyncProperties.RagExplain);
    }

    [Fact]
    public void A_changed_rag_explanation_is_reported()
    {
        var result = DiffOne(Jira(j => j.RagExplain = "Red because"), Db(f => f.RagExplain = "Amber because"));

        Assert.Equal("Red because", Single(result, JiraSyncProperties.RagExplain).JiraValue);
    }

    [Fact]
    public void A_changed_planning_increment_is_reported()
    {
        var result = DiffOne(
            Jira(j => j.PlanningIncrement = "PI 2026.2"),
            Db(f => f.Pi = new Pi { Name = "PI 2026.1" }),
            piNames: new[] { "PI 2026.1", "PI 2026.2" });

        var diff = Single(result, JiraSyncProperties.Pi);

        Assert.Equal("PI 2026.1", diff.DbValue);
        Assert.Equal("PI 2026.2", diff.JiraValue);
    }

    [Fact]
    public void A_planning_increment_matching_apart_from_case_produces_no_diff()
    {
        var result = DiffOne(
            Jira(j => j.PlanningIncrement = "pi 2026.1"),
            Db(f => f.Pi = new Pi { Name = "PI 2026.1" }),
            piNames: new[] { "PI 2026.1" });

        Assert.DoesNotContain(result.Diffs, d => d.PropertyName == JiraSyncProperties.Pi);
    }

    [Fact]
    public void A_planning_increment_is_trimmed_before_comparison()
    {
        var result = DiffOne(
            Jira(j => j.PlanningIncrement = "  PI 2026.1  "),
            Db(f => f.Pi = new Pi { Name = "PI 2026.1" }),
            piNames: new[] { "PI 2026.1" });

        Assert.DoesNotContain(result.Diffs, d => d.PropertyName == JiraSyncProperties.Pi);
    }

    [Fact]
    public void A_planning_increment_new_to_the_tool_is_flagged_for_creation()
    {
        var result = DiffOne(
            Jira(j => j.PlanningIncrement = "PI 2027.1"),
            Db(),
            piNames: new[] { "PI 2026.1" });

        Assert.Equal(new[] { "PI 2027.1" }, result.PisToCreate);
    }

    [Fact]
    public void A_planning_increment_the_tool_already_knows_is_not_flagged()
    {
        var result = DiffOne(
            Jira(j => j.PlanningIncrement = "PI 2026.1"),
            Db(),
            piNames: new[] { "pi 2026.1" });

        Assert.Empty(result.PisToCreate);
    }

    [Fact]
    public void A_feature_losing_its_planning_increment_is_reported_without_flagging_a_creation()
    {
        var result = DiffOne(
            Jira(j => j.PlanningIncrement = null),
            Db(f => f.Pi = new Pi { Name = "PI 2026.1" }),
            piNames: new[] { "PI 2026.1" });

        Assert.Null(Single(result, JiraSyncProperties.Pi).JiraValue);
        Assert.Empty(result.PisToCreate);
    }

    [Fact]
    public void Planning_increments_to_create_are_deduplicated_and_sorted()
    {
        var result = Service.DiffFeatures(
            new[]
            {
                Jira("PROJ-1", j => j.PlanningIncrement = "PI 2027.2"),
                Jira("PROJ-2", j => j.PlanningIncrement = "PI 2027.1"),
                Jira("PROJ-3", j => j.PlanningIncrement = "pi 2027.1")
            },
            new Dictionary<string, Feature>
            {
                ["PROJ-1"] = Db(),
                ["PROJ-2"] = Db(f => f.JiraId = "PROJ-2"),
                ["PROJ-3"] = Db(f => f.JiraId = "PROJ-3")
            },
            Array.Empty<string>(),
            Array.Empty<Team>());

        Assert.Equal(new[] { "PI 2027.1", "PI 2027.2" }, result.PisToCreate);
    }

    [Fact]
    public void Teams_added_in_jira_are_reported()
    {
        var teams = new[] { Team(1, "Neon", "CFT-Neon") };

        var result = DiffOne(Jira(j => j.GfedTeam = "CFT-Neon"), Db(), teams: teams);

        var diff = Single(result, JiraSyncProperties.Teams);

        Assert.Null(diff.DbValue);
        Assert.Equal("CFT-Neon", diff.JiraValue);
    }

    [Fact]
    public void Teams_removed_in_jira_are_reported()
    {
        var team = Team(1, "Neon", "CFT-Neon");
        var db = Db(f => f.FeatureTeams = new List<FeatureTeam> { new() { TeamId = 1, Team = team } });

        var result = DiffOne(Jira(j => j.GfedTeam = null), db, teams: new[] { team });

        var diff = Single(result, JiraSyncProperties.Teams);

        Assert.Equal("CFT-Neon", diff.DbValue);
        Assert.Null(diff.JiraValue);
    }

    [Fact]
    public void A_matching_team_set_produces_no_diff()
    {
        var team = Team(1, "Neon", "CFT-Neon");
        var db = Db(f => f.FeatureTeams = new List<FeatureTeam> { new() { TeamId = 1, Team = team } });

        var result = DiffOne(Jira(j => j.GfedTeam = "CFT-Neon"), db, teams: new[] { team });

        Assert.DoesNotContain(result.Diffs, d => d.PropertyName == JiraSyncProperties.Teams);
    }

    [Fact]
    public void The_same_teams_listed_in_a_different_order_produce_no_diff()
    {
        var neon = Team(1, "Neon", "CFT-Neon");
        var argon = Team(2, "Argon", "CFT-Argon");
        var db = Db(f => f.FeatureTeams = new List<FeatureTeam>
        {
            new() { TeamId = 1, Team = neon },
            new() { TeamId = 2, Team = argon }
        });

        var result = DiffOne(Jira(j => j.GfedTeam = "CFT-Neon, CFT-Argon"), db, teams: new[] { neon, argon });

        Assert.DoesNotContain(result.Diffs, d => d.PropertyName == JiraSyncProperties.Teams);
    }

    [Fact]
    public void A_team_diff_lists_both_sides_alphabetically()
    {
        var neon = Team(1, "Neon", "CFT-Neon");
        var argon = Team(2, "Argon", "CFT-Argon");
        var db = Db(f => f.FeatureTeams = new List<FeatureTeam> { new() { TeamId = 1, Team = neon } });

        var result = DiffOne(Jira(j => j.GfedTeam = "CFT-Neon, CFT-Argon"), db, teams: new[] { neon, argon });

        var diff = Single(result, JiraSyncProperties.Teams);

        Assert.Equal("CFT-Neon", diff.DbValue);
        Assert.Equal("CFT-Argon, CFT-Neon", diff.JiraValue);
    }

    [Fact]
    public void A_jira_team_that_matches_nothing_locally_is_surfaced_as_a_warning()
    {
        var result = DiffOne(Jira(j => j.GfedTeam = "CFT-Mystery"), Db(), teams: new[] { Team(1, "Neon", "CFT-Neon") });

        Assert.Equal(new[] { "CFT-Mystery" }, result.TeamsNotFound);
    }

    [Fact]
    public void Unmatched_teams_are_deduplicated_and_sorted()
    {
        var result = Service.DiffFeatures(
            new[]
            {
                Jira("PROJ-1", j => j.GfedTeam = "CFT-Zeta, CFT-Alpha"),
                Jira("PROJ-2", j => j.GfedTeam = "CFT-Alpha")
            },
            new Dictionary<string, Feature> { ["PROJ-1"] = Db(), ["PROJ-2"] = Db(f => f.JiraId = "PROJ-2") },
            Array.Empty<string>(),
            Array.Empty<Team>());

        Assert.Equal(new[] { "CFT-Alpha", "CFT-Zeta" }, result.TeamsNotFound);
    }

    [Fact]
    public void An_unmatched_team_is_left_out_of_the_compared_team_set()
    {
        var neon = Team(1, "Neon", "CFT-Neon");
        var db = Db(f => f.FeatureTeams = new List<FeatureTeam> { new() { TeamId = 1, Team = neon } });

        var result = DiffOne(Jira(j => j.GfedTeam = "CFT-Neon, CFT-Mystery"), db, teams: new[] { neon });

        Assert.DoesNotContain(result.Diffs, d => d.PropertyName == JiraSyncProperties.Teams);
        Assert.Equal(new[] { "CFT-Mystery" }, result.TeamsNotFound);
    }

    [Fact]
    public void The_same_team_listed_twice_in_jira_is_counted_once()
    {
        var neon = Team(1, "Neon", "CFT-Neon");
        var db = Db(f => f.FeatureTeams = new List<FeatureTeam> { new() { TeamId = 1, Team = neon } });

        var result = DiffOne(Jira(j => j.GfedTeam = "CFT-Neon, CFT-Neon"), db, teams: new[] { neon });

        Assert.DoesNotContain(result.Diffs, d => d.PropertyName == JiraSyncProperties.Teams);
    }

    [Fact]
    public void A_team_matched_by_short_name_is_compared_under_its_canonical_name()
    {
        var neon = Team(1, "Neon", "CFT-Neon");
        var db = Db(f => f.FeatureTeams = new List<FeatureTeam> { new() { TeamId = 1, Team = neon } });

        var result = DiffOne(Jira(j => j.GfedTeam = "Neon"), db, teams: new[] { neon });

        Assert.DoesNotContain(result.Diffs, d => d.PropertyName == JiraSyncProperties.Teams);
    }

    [Fact]
    public void Several_changed_fields_are_all_reported_for_one_issue()
    {
        var result = DiffOne(
            Jira(j =>
            {
                j.Summary = "New summary";
                j.Status = "Done";
                j.StoryPoints = 21;
            }),
            Db());

        Assert.Equal(3, result.Diffs.Count);
        Assert.All(result.Diffs, d => Assert.Equal("PROJ-1", d.JiraKey));
    }

    [Fact]
    public void A_business_outcome_is_compared_on_its_shared_fields()
    {
        var db = new BusinessOutcome { JiraId = "BO-1", Summary = "Local", Status = "To Do" };

        var diffs = Service.DiffJiraIssues(
            new[] { Jira("BO-1", j => j.Summary = "From Jira") },
            new Dictionary<string, BusinessOutcome> { ["BO-1"] = db });

        Assert.Contains(diffs, d => d.PropertyName == JiraSyncProperties.Summary && d.JiraValue == "From Jira");
        Assert.Contains(diffs, d => d.PropertyName == JiraSyncProperties.Status);
    }

    [Fact]
    public void A_portfolio_epic_is_compared_on_its_shared_fields()
    {
        var db = new PortfolioEpic { JiraId = "EP-1", Summary = "Local" };

        var diffs = Service.DiffJiraIssues(
            new[] { Jira("EP-1", j => j.Summary = "From Jira") },
            new Dictionary<string, PortfolioEpic> { ["EP-1"] = db });

        Assert.Contains(diffs, d => d.PropertyName == JiraSyncProperties.Summary);
    }

    [Fact]
    public void A_strategic_objective_is_compared_on_its_shared_fields()
    {
        var db = new StrategicObjective { JiraId = "SO-1", Summary = "Local" };

        var diffs = Service.DiffJiraIssues(
            new[] { Jira("SO-1", j => j.Summary = "From Jira") },
            new Dictionary<string, StrategicObjective> { ["SO-1"] = db });

        Assert.Contains(diffs, d => d.PropertyName == JiraSyncProperties.Summary);
    }

    [Fact]
    public void A_train_entity_is_not_compared_on_feature_only_fields()
    {
        var db = new StrategicObjective
        {
            JiraId = "SO-1",
            Summary = "Shared summary",
            Description = "Shared description",
            AcceptanceCriteria = "Shared criteria",
            NavigatorId = "NAV-1",
            Status = "In Progress",
            Labels = "alpha",
            TargetStart = new DateTime(2026, 7, 1),
            TargetEnd = new DateTime(2026, 9, 30),
            StoryPoints = 8
        };

        var diffs = Service.DiffJiraIssues(
            new[] { Jira("SO-1", j => { j.PlanningIncrement = "PI 2026.9"; j.GfedTeam = "CFT-Neon"; }) },
            new Dictionary<string, StrategicObjective> { ["SO-1"] = db });

        Assert.Empty(diffs);
    }

    [Fact]
    public void A_train_entity_with_no_local_counterpart_is_skipped()
    {
        var diffs = Service.DiffJiraIssues(
            new[] { Jira("SO-9") },
            new Dictionary<string, StrategicObjective>());

        Assert.Empty(diffs);
    }
}

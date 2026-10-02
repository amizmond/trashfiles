using System.Text.Json.Nodes;
using Estimation.Core.JiraIntegration.Client;
using Xunit;

namespace Estimation.Core.Tests.JiraIntegration;

public class JiraIssueParserTests
{
    private static readonly JiraSettings Settings = new();

    private static JiraIssueResponse Parse(string json, string? fallbackKey = null) =>
        new JiraIssueParser(Settings).Parse(JsonNode.Parse(json)!, fallbackKey);

    private static JiraIssueResponse ParseFields(string fieldsJson) =>
        Parse("{\"key\":\"PROJ-1\",\"fields\":{" + fieldsJson + "}}");

    private static JiraIssueResponse ParseField(string fieldId, string valueJson) =>
        ParseFields("\"" + fieldId + "\":" + valueJson);

    private static string Quoted(string value) => "\"" + value + "\"";

    [Fact]
    public void The_issue_key_is_read_from_the_node()
    {
        Assert.Equal("PROJ-1", Parse("{\"key\":\"PROJ-1\",\"fields\":{}}").Key);
    }

    [Fact]
    public void A_missing_key_falls_back_to_the_supplied_one()
    {
        Assert.Equal("PROJ-9", Parse("{\"fields\":{}}", "PROJ-9").Key);
    }

    [Fact]
    public void With_no_key_anywhere_the_key_is_empty_rather_than_null()
    {
        Assert.Equal(string.Empty, Parse("{\"fields\":{}}").Key);
    }

    [Fact]
    public void An_issue_without_a_fields_object_parses_to_an_empty_response()
    {
        var issue = Parse("{\"key\":\"PROJ-1\"}");

        Assert.Equal("PROJ-1", issue.Key);
        Assert.Null(issue.Summary);
        Assert.Null(issue.Status);
        Assert.Null(issue.Labels);
    }

    [Fact]
    public void The_plain_text_fields_are_read_straight_across()
    {
        var issue = ParseFields("\"summary\":\"Do the thing\",\"description\":\"Longer text\"");

        Assert.Equal("Do the thing", issue.Summary);
        Assert.Equal("Longer text", issue.Description);
    }

    [Fact]
    public void The_status_and_issue_type_are_read_from_their_nested_name()
    {
        var issue = ParseFields(
            "\"status\":{\"name\":\"In Progress\",\"id\":\"3\"},\"issuetype\":{\"name\":\"Feature\",\"id\":\"10\"}");

        Assert.Equal("In Progress", issue.Status);
        Assert.Equal("Feature", issue.IssueType);
    }

    [Fact]
    public void The_custom_fields_are_read_through_the_configured_ids()
    {
        var issue = ParseFields(string.Join(",",
            Quoted(Settings.AcceptanceCriteriaCustomFieldId) + ":\"Given when then\"",
            Quoted(Settings.NavigatorIdCustomFieldId) + ":\"NAV-7\"",
            Quoted(Settings.FeatureNameCustomFieldId) + ":\"Short name\"",
            Quoted(Settings.RagExplainCustomFieldId) + ":\"Amber because\"",
            Quoted(Settings.ParentLinkCustomFieldId) + ":\"PROJ-100\"",
            Quoted(Settings.FeatureLinkCustomFieldId) + ":\"PROJ-200\""));

        Assert.Equal("Given when then", issue.AcceptanceCriteria);
        Assert.Equal("NAV-7", issue.NavigatorId);
        Assert.Equal("Short name", issue.FeatureName);
        Assert.Equal("Amber because", issue.RagExplain);
        Assert.Equal("PROJ-100", issue.ParentLink);
        Assert.Equal("PROJ-200", issue.FeatureLink);
    }

    [Fact]
    public void A_custom_field_the_settings_do_not_name_is_not_read()
    {
        var settings = new JiraSettings { AcceptanceCriteriaCustomFieldId = "" };
        var node = JsonNode.Parse("{\"key\":\"PROJ-1\",\"fields\":{\"customfield_15900\":\"text\"}}")!;

        Assert.Null(new JiraIssueParser(settings).Parse(node).AcceptanceCriteria);
    }

    [Fact]
    public void Labels_are_read_as_a_list()
    {
        Assert.Equal(new[] { "alpha", "beta" }, ParseField("labels", "[\"alpha\",\"beta\"]").Labels);
    }

    [Fact]
    public void Empty_labels_are_dropped_from_the_list()
    {
        Assert.Equal(new[] { "alpha", "beta" }, ParseField("labels", "[\"alpha\",\"\",\"beta\"]").Labels);
    }

    [Fact]
    public void An_empty_label_array_parses_to_an_empty_list()
    {
        Assert.Empty(ParseField("labels", "[]").Labels!);
    }

    [Fact]
    public void A_missing_label_field_parses_to_null_rather_than_an_empty_list()
    {
        Assert.Null(ParseField("summary", "\"x\"").Labels);
    }

    [Fact]
    public void Components_are_read_as_a_list_of_names()
    {
        var issue = ParseField("components", "[{\"id\":\"1\",\"name\":\"Backend\"},{\"id\":\"2\",\"name\":\"UI\"}]");

        Assert.Equal(new[] { "Backend", "UI" }, issue.Components);
    }

    [Fact]
    public void Components_without_a_name_are_dropped()
    {
        var issue = ParseField("components", "[{\"id\":\"1\"},{\"id\":\"2\",\"name\":\"UI\"}]");

        Assert.Equal(new[] { "UI" }, issue.Components);
    }

    [Fact]
    public void An_empty_component_array_parses_to_an_empty_list()
    {
        Assert.Empty(ParseField("components", "[]").Components!);
    }

    [Fact]
    public void A_missing_component_field_parses_to_null()
    {
        Assert.Null(ParseField("summary", "\"x\"").Components);
    }

    [Fact]
    public void Fix_versions_are_read_as_a_list_of_names()
    {
        var issue = ParseField("fixVersions",
            "[{\"id\":\"10\",\"name\":\"R26.3\",\"released\":true,\"archived\":false},{\"id\":\"11\",\"name\":\"R26.4\",\"released\":false}]");

        Assert.Equal(new[] { "R26.3", "R26.4" }, issue.FixVersions);
    }

    [Fact]
    public void Fix_versions_without_a_name_are_dropped()
    {
        var issue = ParseField("fixVersions", "[{\"id\":\"10\"},{\"id\":\"11\",\"name\":\"R26.4\"}]");

        Assert.Equal(new[] { "R26.4" }, issue.FixVersions);
    }

    [Fact]
    public void An_empty_fix_version_array_parses_to_an_empty_list()
    {
        Assert.Empty(ParseField("fixVersions", "[]").FixVersions!);
    }

    [Fact]
    public void A_missing_fix_version_field_parses_to_null()
    {
        Assert.Null(ParseField("summary", "\"x\"").FixVersions);
    }

    [Fact]
    public void The_rag_status_is_read_as_the_colour_of_its_option()
    {
        var issue = ParseField(Settings.RagStatusCustomFieldId,
            "{\"self\":\"https://jira.example.test/rest/api/2/customFieldOption/501\",\"value\":\"Green – Everything going to plan\",\"id\":\"501\"}");

        Assert.Equal("Green", issue.RagStatus);
    }

    [Fact]
    public void A_rag_status_option_worded_differently_still_yields_its_colour()
    {
        Assert.Equal("Amber", ParseField(Settings.RagStatusCustomFieldId, "{\"value\":\"amber - some issues\"}").RagStatus);
    }

    [Fact]
    public void A_rag_status_option_of_no_known_colour_keeps_its_text()
    {
        Assert.Equal("Blue – Delivered", ParseField(Settings.RagStatusCustomFieldId, "{\"value\":\"Blue – Delivered\"}").RagStatus);
    }

    [Fact]
    public void An_issue_without_a_rag_status_has_none()
    {
        Assert.Null(ParseField(Settings.RagStatusCustomFieldId, "null").RagStatus);
        Assert.Null(ParseField("summary", "\"x\"").RagStatus);
    }

    [Fact]
    public void The_updated_timestamp_is_parsed()
    {
        var issue = ParseField("updated", "\"2026-07-30T10:15:00.000+0000\"");

        Assert.NotNull(issue.Updated);
        Assert.Equal(new DateTime(2026, 7, 30), issue.Updated!.Value.Date);
    }

    [Fact]
    public void An_unparseable_updated_timestamp_is_ignored()
    {
        Assert.Null(ParseField("updated", "\"not a date\"").Updated);
    }

    [Fact]
    public void The_target_dates_are_parsed_from_their_custom_fields()
    {
        var issue = ParseFields(
            Quoted(Settings.TargetStartCustomFieldId) + ":\"2026-07-01\"," +
            Quoted(Settings.TargetEndCustomFieldId) + ":\"2026-09-30\"");

        Assert.Equal(new DateTime(2026, 7, 1), issue.TargetStart);
        Assert.Equal(new DateTime(2026, 9, 30), issue.TargetEnd);
    }

    [Fact]
    public void An_unparseable_target_date_is_ignored()
    {
        Assert.Null(ParseField(Settings.TargetStartCustomFieldId, "\"tomorrow\"").TargetStart);
    }

    [Fact]
    public void Story_points_arriving_as_a_number_are_read()
    {
        Assert.Equal(8, ParseField(Settings.StoryPointsCustomFieldId, "8").StoryPoints);
    }

    [Fact]
    public void Fractional_story_points_are_rounded()
    {
        Assert.Equal(6, ParseField(Settings.StoryPointsCustomFieldId, "5.6").StoryPoints);
    }

    [Fact]
    public void A_story_point_midpoint_rounds_to_even()
    {
        Assert.Equal(2, ParseField(Settings.StoryPointsCustomFieldId, "2.5").StoryPoints);
    }

    [Fact]
    public void Story_points_arriving_as_text_are_still_read()
    {
        Assert.Equal(13, ParseField(Settings.StoryPointsCustomFieldId, "\"13\"").StoryPoints);
    }

    [Fact]
    public void Unreadable_story_points_are_ignored()
    {
        Assert.Null(ParseField(Settings.StoryPointsCustomFieldId, "\"lots\"").StoryPoints);
    }

    [Fact]
    public void A_missing_story_point_field_is_ignored()
    {
        Assert.Null(ParseField("summary", "\"x\"").StoryPoints);
    }

    [Fact]
    public void A_single_option_planning_increment_is_read_from_its_value()
    {
        var issue = ParseField(Settings.PlanningIncrementCustomFieldId, "{\"value\":\"PI 2026.1\"}");

        Assert.Equal("PI 2026.1", issue.PlanningIncrement);
    }

    [Fact]
    public void A_single_option_falls_back_to_its_name_when_it_has_no_value()
    {
        var issue = ParseField(Settings.PlanningIncrementCustomFieldId, "{\"name\":\"PI 2026.2\"}");

        Assert.Equal("PI 2026.2", issue.PlanningIncrement);
    }

    [Fact]
    public void An_option_array_yields_its_first_usable_entry()
    {
        var issue = ParseField(
            Settings.PlanningIncrementCustomFieldId,
            "[{\"value\":\"\"},{\"value\":\"PI 2026.3\"}]");

        Assert.Equal("PI 2026.3", issue.PlanningIncrement);
    }

    [Fact]
    public void A_greenhopper_sprint_string_yields_just_the_sprint_name()
    {
        const string sprint =
            "com.atlassian.greenhopper.service.sprint.Sprint@1f2e3d" +
            "[id=42,rapidViewId=7,state=ACTIVE,name=PI 2026.1,startDate=2026-07-01]";

        var issue = ParseField(Settings.PlanningIncrementCustomFieldId, Quoted(sprint));

        Assert.Equal("PI 2026.1", issue.PlanningIncrement);
    }

    [Fact]
    public void A_sprint_name_at_the_end_of_the_string_is_still_extracted()
    {
        const string sprint = "com.atlassian.greenhopper.service.sprint.Sprint@1f2e3d[id=42,name=PI 2026.4]";

        var issue = ParseField(Settings.PlanningIncrementCustomFieldId, Quoted(sprint));

        Assert.Equal("PI 2026.4", issue.PlanningIncrement);
    }

    [Fact]
    public void A_plain_string_planning_increment_is_used_as_is()
    {
        var issue = ParseField(Settings.PlanningIncrementCustomFieldId, "\"PI 2026.5\"");

        Assert.Equal("PI 2026.5", issue.PlanningIncrement);
    }

    [Fact]
    public void A_missing_planning_increment_is_null()
    {
        Assert.Null(ParseField("summary", "\"x\"").PlanningIncrement);
    }

    [Fact]
    public void Multiple_teams_are_joined_into_one_value()
    {
        var issue = ParseField(
            Settings.GfedTeamCustomFieldId,
            "[{\"value\":\"CFT-Neon\"},{\"value\":\"CFT-Argon\"}]");

        Assert.Equal("CFT-Neon, CFT-Argon", issue.GfedTeam);
    }

    [Fact]
    public void A_single_team_object_is_read_without_joining()
    {
        var issue = ParseField(Settings.GfedTeamCustomFieldId, "{\"value\":\"CFT-Neon\"}");

        Assert.Equal("CFT-Neon", issue.GfedTeam);
    }

    [Fact]
    public void An_empty_team_array_yields_no_value()
    {
        Assert.Null(ParseField(Settings.GfedTeamCustomFieldId, "[]").GfedTeam);
    }

    [Fact]
    public void Teams_arriving_as_plain_strings_are_joined_too()
    {
        var issue = ParseField(Settings.GfedTeamCustomFieldId, "[\"CFT-Neon\",\"CFT-Argon\"]");

        Assert.Equal("CFT-Neon, CFT-Argon", issue.GfedTeam);
    }

    [Fact]
    public void The_joined_team_value_can_be_split_back_apart()
    {
        var issue = ParseField(
            Settings.GfedTeamCustomFieldId,
            "[{\"value\":\"CFT-Neon\"},{\"value\":\"CFT-Argon\"}]");

        Assert.Equal(new[] { "CFT-Neon", "CFT-Argon" }, JiraTeamMatcher.SplitJiraValue(issue.GfedTeam));
    }

    [Fact]
    public void The_assignee_is_read_from_its_nested_fields()
    {
        var issue = ParseField("assignee",
            "{\"displayName\":\"Ada Lovelace\",\"name\":\"alovelace\",\"key\":\"E12345\"," +
            "\"avatarUrls\":{\"48x48\":\"https://jira.example.test/avatar/48\"}}");

        Assert.Equal("Ada Lovelace", issue.AssigneeDisplayName);
        Assert.Equal("alovelace", issue.AssigneeUserName);
        Assert.Equal("E12345", issue.AssigneeKey);
        Assert.Equal("https://jira.example.test/avatar/48", issue.AssigneeAvatarUrl);
    }

    [Fact]
    public void An_unassigned_issue_has_no_assignee_details()
    {
        var issue = ParseField("assignee", "null");

        Assert.Null(issue.AssigneeDisplayName);
        Assert.Null(issue.AssigneeUserName);
        Assert.Null(issue.AssigneeKey);
        Assert.Null(issue.AssigneeAvatarUrl);
    }

    [Fact]
    public void The_priority_is_read_from_its_nested_fields()
    {
        var issue = ParseField("priority",
            "{\"name\":\"Major\",\"iconUrl\":\"https://jira.example.test/major.png\"}");

        Assert.Equal("Major", issue.PriorityName);
        Assert.Equal("https://jira.example.test/major.png", issue.PriorityIconUrl);
    }

    [Fact]
    public void A_numeric_field_where_text_is_expected_is_stringified_rather_than_dropped()
    {
        Assert.Equal("12345", ParseField("summary", "12345").Summary);
    }

    [Fact]
    public void An_explicitly_null_text_field_stays_null()
    {
        Assert.Null(ParseField("summary", "null").Summary);
    }

    [Fact]
    public void A_realistic_issue_parses_every_field_at_once()
    {
        var json = "{\"key\":\"PROJ-42\",\"fields\":{" + string.Join(",",
            "\"summary\":\"Ship the feature\"",
            "\"description\":\"The long description\"",
            "\"status\":{\"name\":\"In Progress\"}",
            "\"issuetype\":{\"name\":\"Feature\"}",
            "\"labels\":[\"alpha\",\"beta\"]",
            "\"updated\":\"2026-07-30T10:15:00.000+0000\"",
            "\"assignee\":{\"displayName\":\"Ada Lovelace\",\"name\":\"alovelace\",\"key\":\"E12345\"}",
            "\"priority\":{\"name\":\"Major\"}",
            Quoted(Settings.AcceptanceCriteriaCustomFieldId) + ":\"Given when then\"",
            Quoted(Settings.NavigatorIdCustomFieldId) + ":\"NAV-7\"",
            Quoted(Settings.TargetStartCustomFieldId) + ":\"2026-07-01\"",
            Quoted(Settings.TargetEndCustomFieldId) + ":\"2026-09-30\"",
            Quoted(Settings.StoryPointsCustomFieldId) + ":8",
            Quoted(Settings.GfedTeamCustomFieldId) + ":[{\"value\":\"CFT-Neon\"}]",
            Quoted(Settings.PlanningIncrementCustomFieldId) + ":{\"value\":\"PI 2026.1\"}",
            Quoted(Settings.ParentLinkCustomFieldId) + ":\"PROJ-100\"") + "}}";

        var issue = Parse(json);

        Assert.Equal("PROJ-42", issue.Key);
        Assert.Equal("Ship the feature", issue.Summary);
        Assert.Equal("In Progress", issue.Status);
        Assert.Equal("Feature", issue.IssueType);
        Assert.Equal(new[] { "alpha", "beta" }, issue.Labels);
        Assert.Equal(8, issue.StoryPoints);
        Assert.Equal("CFT-Neon", issue.GfedTeam);
        Assert.Equal("PI 2026.1", issue.PlanningIncrement);
        Assert.Equal("PROJ-100", issue.ParentLink);
        Assert.Equal(new DateTime(2026, 7, 1), issue.TargetStart);
        Assert.Equal(new DateTime(2026, 9, 30), issue.TargetEnd);
        Assert.Equal("alovelace", issue.AssigneeUserName);
        Assert.Equal("Major", issue.PriorityName);
    }

    private const string BlocksType =
        "\"type\":{\"id\":\"10000\",\"name\":\"Blocks\",\"inward\":\"is blocked by\",\"outward\":\"blocks\"}";

    private static string LinkedIssueJson(string direction, string key, string summary, string status) =>
        "\"" + direction + "\":{\"id\":\"1\",\"key\":\"" + key + "\",\"fields\":{\"summary\":\"" + summary
        + "\",\"status\":{\"name\":\"" + status + "\"}}}";

    [Fact]
    public void An_issue_without_the_linked_issues_field_has_no_link_list()
    {
        Assert.Null(ParseFields("\"summary\":\"x\"").IssueLinks);
    }

    [Fact]
    public void An_issue_with_no_linked_issues_has_an_empty_link_list()
    {
        Assert.Empty(ParseField("issuelinks", "[]").IssueLinks!);
    }

    [Fact]
    public void An_outward_link_reads_the_other_issue_and_both_phrases()
    {
        var link = Assert.Single(ParseField("issuelinks",
            "[{\"id\":\"501\"," + BlocksType + "," + LinkedIssueJson("outwardIssue", "PROJ-2", "Second", "Open") + "}]").IssueLinks!);

        Assert.Equal("501", link.Id);
        Assert.Equal("Blocks", link.TypeName);
        Assert.Equal("blocks", link.OutwardLabel);
        Assert.Equal("is blocked by", link.InwardLabel);
        Assert.True(link.IsOutward);
        Assert.Equal("PROJ-2", link.OtherKey);
        Assert.Equal("Second", link.OtherSummary);
        Assert.Equal("Open", link.OtherStatus);
    }

    [Fact]
    public void An_inward_link_is_not_outward()
    {
        var link = Assert.Single(ParseField("issuelinks",
            "[{\"id\":\"502\"," + BlocksType + "," + LinkedIssueJson("inwardIssue", "PROJ-3", "Third", "Done") + "}]").IssueLinks!);

        Assert.False(link.IsOutward);
        Assert.Equal("PROJ-3", link.OtherKey);
    }

    [Fact]
    public void A_link_without_an_id_or_another_issue_is_left_out()
    {
        var links = ParseField("issuelinks",
            "[{" + BlocksType + "," + LinkedIssueJson("outwardIssue", "PROJ-2", "Second", "Open") + "},"
            + "{\"id\":\"503\"," + BlocksType + "},"
            + "{\"id\":\"504\"," + BlocksType + "," + LinkedIssueJson("inwardIssue", "PROJ-4", "Fourth", "Open") + "}]").IssueLinks!;

        Assert.Equal("504", Assert.Single(links).Id);
    }

    [Fact]
    public void An_oddly_shaped_link_entry_does_not_fail_the_issue()
    {
        var links = ParseField("issuelinks",
            "[\"10001\",{\"id\":\"505\",\"type\":\"Blocks\",\"outwardIssue\":{\"key\":\"PROJ-5\",\"fields\":\"none\"}}]").IssueLinks!;

        var link = Assert.Single(links);
        Assert.Equal("PROJ-5", link.OtherKey);
        Assert.Equal(string.Empty, link.TypeName);
        Assert.Null(link.OtherSummary);
    }

    [Fact]
    public void The_search_asks_jira_for_the_linked_issues_only_on_request()
    {
        Assert.DoesNotContain(JiraIssueFields.IssueLinks, JiraIssueFields.BuildFieldList(Settings).Split(','));
        Assert.Contains(JiraIssueFields.IssueLinks, JiraIssueFields.BuildFieldList(Settings, includeLinks: true).Split(','));
    }
}

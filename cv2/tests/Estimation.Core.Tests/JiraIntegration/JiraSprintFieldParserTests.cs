using System.Text.Json.Nodes;
using Estimation.Core.JiraIntegration.Client;
using Xunit;

namespace Estimation.Core.Tests.JiraIntegration;

public class JiraSprintFieldParserTests
{
    private const string SprintText =
        "com.atlassian.greenhopper.service.sprint.Sprint@6f1f[id=37,rapidViewId=12,state=ACTIVE,name=ARG 26.09.4,"
        + "startDate=2026-10-26T09:12:00.000+01:00,endDate=2026-11-08T17:00:00.000+01:00,completeDate=<null>,"
        + "activatedDate=2026-10-26T09:12:00.000+01:00,sequence=37,goal=Ship payments, fix bugs,autoStartStop=false,synced=false]";

    [Fact]
    public void The_classic_text_form_is_parsed_including_a_goal_with_commas()
    {
        var sprint = JiraSprintFieldParser.ParseText(SprintText)!;

        Assert.Equal(37, sprint.Id);
        Assert.Equal(12, sprint.BoardId);
        Assert.Equal("ACTIVE", sprint.State);
        Assert.Equal("ARG 26.09.4", sprint.Name);
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 9, 12, 0, TimeSpan.FromHours(1)), sprint.StartDate);
        Assert.Equal(new DateTimeOffset(2026, 11, 8, 17, 0, 0, TimeSpan.FromHours(1)), sprint.EndDate);
        Assert.Null(sprint.CompleteDate);
        Assert.Equal("Ship payments, fix bugs", sprint.Goal);
    }

    [Fact]
    public void The_json_object_form_is_parsed_with_compact_offsets()
    {
        var node = JsonNode.Parse("""
            {"id":37,"name":"ARG 26.09.4","state":"closed","boardId":12,"goal":"x",
             "startDate":"2026-10-26T09:12:00.000+0100","endDate":"2026-11-08T17:00:00.000+0100","completeDate":"2026-11-06T16:30:00.000+0100"}
            """);

        var sprint = JiraSprintFieldParser.ParseOne(node)!;

        Assert.Equal(37, sprint.Id);
        Assert.Equal(12, sprint.BoardId);
        Assert.Equal("closed", sprint.State);
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 9, 12, 0, TimeSpan.FromHours(1)), sprint.StartDate);
        Assert.Equal(new DateTimeOffset(2026, 11, 6, 16, 30, 0, TimeSpan.FromHours(1)), sprint.CompleteDate);
    }

    [Fact]
    public void An_array_may_mix_both_forms_and_skips_what_is_not_a_sprint()
    {
        var node = JsonNode.Parse("[\"" + SprintText + "\",{\"id\":38,\"name\":\"ARG 26.09.5\",\"state\":\"future\"},\"garbage\",5]");

        var sprints = JiraSprintFieldParser.Parse(node)!;

        Assert.Equal([37, 38], sprints.Select(s => s.Id));
        Assert.Null(JiraSprintFieldParser.Parse(null));
    }

    [Theory]
    [InlineData("2026-10-27T09:15:32.000+0100", 1)]
    [InlineData("2026-10-27T09:15:32.000+01:00", 1)]
    [InlineData("2026-10-27T09:15:32.000-0530", -5.5)]
    [InlineData("2026-10-27T09:15:32.000Z", 0)]
    public void Jira_timestamps_keep_their_offset(string text, double offsetHours)
    {
        var parsed = JiraTimestamps.Parse(text)!.Value;

        Assert.Equal(TimeSpan.FromHours(offsetHours), parsed.Offset);
        Assert.Equal(new DateTime(2026, 10, 27, 9, 15, 32), parsed.DateTime);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<null>")]
    [InlineData("not a date")]
    public void Empty_or_unreadable_timestamps_are_null(string? text)
    {
        Assert.Null(JiraTimestamps.Parse(text));
    }
}

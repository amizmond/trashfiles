using Estimation.Core.JiraIntegration.Client;
using Estimation.Core.Poker.Services;

using Xunit;

namespace Estimation.Core.Tests.Poker;

public class PokerIssueFilterTests
{
    private static readonly List<JiraIssueResponse> Issues =
    [
        new()
        {
            Key = "ABC-1", Summary = "Login form", IssueType = "Story", Status = "In Progress",
            PriorityName = "Major", AssigneeDisplayName = "Alice", StoryPoints = 5,
            Components = ["UI", "Backend"],
        },
        new()
        {
            Key = "ABC-2", Summary = "Crash on save", IssueType = "Bug", Status = "Open",
            PriorityName = "Blocker", AssigneeDisplayName = "Bob",
            Components = ["Backend"],
        },
        new()
        {
            Key = "XYZ-9", Summary = "Login rate limit", IssueType = "Task", Status = "Open",
            PriorityName = "Major",
        },
    ];

    private static List<string> Apply(PokerIssueFilter filter)
    {
        return filter.Apply(Issues).Select(i => i.Key).ToList();
    }

    [Fact]
    public void EmptyFilter_KeepsEveryIssue_AndReportsItselfInactive()
    {
        var filter = new PokerIssueFilter();

        Assert.False(filter.IsActive);
        Assert.Equal(["ABC-1", "ABC-2", "XYZ-9"], Apply(filter));
    }

    [Fact]
    public void Search_MatchesTheJiraId_AndTheSummary_CaseInsensitively()
    {
        Assert.Equal(["ABC-1", "ABC-2"], Apply(new PokerIssueFilter { Search = "abc-" }));
        Assert.Equal(["ABC-1", "XYZ-9"], Apply(new PokerIssueFilter { Search = "LOGIN" }));
        Assert.Empty(Apply(new PokerIssueFilter { Search = "nothing here" }));

        Assert.False(new PokerIssueFilter { Search = "   " }.IsActive);
        Assert.Equal(3, Apply(new PokerIssueFilter { Search = "   " }).Count);
    }

    [Fact]
    public void SelectionLists_AreOrWithinThemselves_AndAndAcrossFields()
    {
        Assert.Equal(["ABC-2", "XYZ-9"], Apply(new PokerIssueFilter { Types = ["Bug", "Task"] }));

        var combined = new PokerIssueFilter { Statuses = ["Open"], Priorities = ["Major"] };
        Assert.Equal(["XYZ-9"], Apply(combined));
    }

    [Fact]
    public void UnassignedIssues_FilterUnderTheirOwnLabel()
    {
        Assert.Equal(["XYZ-9"], Apply(new PokerIssueFilter { Assignees = [PokerIssueFilter.UnassignedLabel] }));
        Assert.Equal(["ABC-1"], Apply(new PokerIssueFilter { Assignees = ["alice"] }));
        Assert.Equal(PokerIssueFilter.UnassignedLabel, PokerIssueFilter.AssigneeLabel(Issues[2]));
    }

    [Fact]
    public void Components_MatchIssuesCarryingAnyOfTheSelectedOnes()
    {
        Assert.Equal(["ABC-1", "ABC-2"], Apply(new PokerIssueFilter { Components = ["backend"] }));
        Assert.Equal(["ABC-1"], Apply(new PokerIssueFilter { Components = ["UI"] }));
        Assert.Equal(["ABC-1", "ABC-2"], Apply(new PokerIssueFilter { Components = ["UI", "Backend"] }));
        Assert.Empty(Apply(new PokerIssueFilter { Components = ["Infra"] }));
    }

    [Fact]
    public void Components_AreCombinedWithTheOtherFields()
    {
        var filter = new PokerIssueFilter { Components = ["Backend"], Types = ["Bug"] };

        Assert.True(filter.IsActive);
        Assert.Equal(["ABC-2"], Apply(filter));
    }

    [Fact]
    public void ComponentsLabel_JoinsTheNames_AndIsEmptyWithoutAny()
    {
        Assert.Equal("UI, Backend", PokerIssueFilter.ComponentsLabel(Issues[0]));
        Assert.Equal(string.Empty, PokerIssueFilter.ComponentsLabel(Issues[2]));
    }

    [Fact]
    public void OnlyWithoutStoryPoints_KeepsTheIssuesStillToEstimate()
    {
        var filter = new PokerIssueFilter { OnlyWithoutStoryPoints = true };

        Assert.True(filter.IsActive);
        Assert.Equal(["ABC-2", "XYZ-9"], Apply(filter));
    }

    [Fact]
    public void Clear_ResetsEveryField()
    {
        var filter = new PokerIssueFilter
        {
            Search = "abc",
            Types = ["Bug"],
            Statuses = ["Open"],
            Priorities = ["Major"],
            Assignees = ["Bob"],
            Components = ["Backend"],
            OnlyWithoutStoryPoints = true,
        };

        filter.Clear();

        Assert.False(filter.IsActive);
        Assert.Equal(3, Apply(filter).Count);
    }
}

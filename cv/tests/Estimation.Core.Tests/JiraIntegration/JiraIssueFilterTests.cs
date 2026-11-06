using Estimation.Core.JiraIntegration.Client;

using Xunit;

namespace Estimation.Core.Tests.JiraIntegration;

public class JiraIssueFilterTests
{
    private static readonly List<JiraIssueResponse> Issues =
    [
        new()
        {
            Key = "ABC-1", Summary = "Login form", IssueType = "Story", Status = "In Progress",
            PriorityName = "Major", AssigneeDisplayName = "Alice", StoryPoints = 5,
            Components = ["UI", "Backend"], Labels = ["tech-debt", "ui"], FixVersions = ["R1"],
            FeatureLink = "FEAT-1",
        },
        new()
        {
            Key = "ABC-2", Summary = "Crash on save", IssueType = "Bug", Status = "Open",
            PriorityName = "Blocker", AssigneeDisplayName = "Bob",
            Components = ["Backend"], Labels = ["tech-debt"], FeatureLink = "FEAT-2",
        },
        new()
        {
            Key = "XYZ-9", Summary = "Login rate limit", IssueType = "Task", Status = "Open",
            PriorityName = "Major", FixVersions = ["R2"],
        },
    ];

    private static List<string> Apply(JiraIssueFilter filter)
    {
        return filter.Apply(Issues).Select(i => i.Key).ToList();
    }

    [Fact]
    public void EmptyFilter_KeepsEveryIssue_AndReportsItselfInactive()
    {
        var filter = new JiraIssueFilter();

        Assert.False(filter.IsActive);
        Assert.Equal(0, filter.ActiveCount);
        Assert.Equal(["ABC-1", "ABC-2", "XYZ-9"], Apply(filter));
    }

    [Fact]
    public void Search_MatchesTheJiraId_AndTheSummary_CaseInsensitively()
    {
        Assert.Equal(["ABC-1", "ABC-2"], Apply(new JiraIssueFilter { Search = "abc-" }));
        Assert.Equal(["ABC-1", "XYZ-9"], Apply(new JiraIssueFilter { Search = "LOGIN" }));
        Assert.Empty(Apply(new JiraIssueFilter { Search = "nothing here" }));

        Assert.False(new JiraIssueFilter { Search = "   " }.IsActive);
        Assert.Equal(3, Apply(new JiraIssueFilter { Search = "   " }).Count);
    }

    [Fact]
    public void SelectionLists_AreOrWithinThemselves_AndAndAcrossFields()
    {
        Assert.Equal(["ABC-2", "XYZ-9"], Apply(new JiraIssueFilter { Types = ["Bug", "Task"] }));

        var combined = new JiraIssueFilter { Statuses = ["Open"], Priorities = ["Major"] };
        Assert.Equal(["XYZ-9"], Apply(combined));
    }

    [Fact]
    public void UnassignedIssues_FilterUnderTheirOwnLabel()
    {
        Assert.Equal(["XYZ-9"], Apply(new JiraIssueFilter { Assignees = [JiraIssueFilter.UnassignedLabel] }));
        Assert.Equal(["ABC-1"], Apply(new JiraIssueFilter { Assignees = ["alice"] }));
        Assert.Equal(JiraIssueFilter.UnassignedLabel, JiraIssueFilter.AssigneeLabel(Issues[2]));
    }

    [Fact]
    public void Components_MatchIssuesCarryingAnyOfTheSelectedOnes()
    {
        Assert.Equal(["ABC-1", "ABC-2"], Apply(new JiraIssueFilter { Components = ["backend"] }));
        Assert.Equal(["ABC-1"], Apply(new JiraIssueFilter { Components = ["UI"] }));
        Assert.Equal(["ABC-1", "ABC-2"], Apply(new JiraIssueFilter { Components = ["UI", "Backend"] }));
        Assert.Empty(Apply(new JiraIssueFilter { Components = ["Infra"] }));
    }

    [Fact]
    public void Components_AreCombinedWithTheOtherFields()
    {
        var filter = new JiraIssueFilter { Components = ["Backend"], Types = ["Bug"] };

        Assert.True(filter.IsActive);
        Assert.Equal(["ABC-2"], Apply(filter));
    }

    [Fact]
    public void TheNoValueOptions_FindIssuesWithoutComponentsLabelsOrFixVersions()
    {
        Assert.Equal(["XYZ-9"], Apply(new JiraIssueFilter { Components = [JiraIssueFilter.NoComponentLabel] }));
        Assert.Equal(["XYZ-9"], Apply(new JiraIssueFilter { Labels = [JiraIssueFilter.NoLabelLabel] }));
        Assert.Equal(["ABC-2"], Apply(new JiraIssueFilter { FixVersions = [JiraIssueFilter.NoFixVersionLabel] }));
        Assert.Equal(["ABC-1", "XYZ-9"], Apply(new JiraIssueFilter { Labels = ["UI", JiraIssueFilter.NoLabelLabel] }));
    }

    [Fact]
    public void LabelsAndFixVersions_MatchIssuesCarryingAnyOfTheSelectedOnes()
    {
        Assert.Equal(["ABC-1", "ABC-2"], Apply(new JiraIssueFilter { Labels = ["TECH-DEBT"] }));
        Assert.Equal(["ABC-1"], Apply(new JiraIssueFilter { Labels = ["ui"] }));
        Assert.Equal(["XYZ-9"], Apply(new JiraIssueFilter { FixVersions = ["r2"] }));
        Assert.Equal(["ABC-1", "XYZ-9"], Apply(new JiraIssueFilter { FixVersions = ["R1", "R2"] }));
    }

    [Fact]
    public void FeatureLinks_MatchTheLinkedFeature_AndIssuesWithoutOne()
    {
        Assert.Equal(["ABC-1"], Apply(new JiraIssueFilter { FeatureLinks = ["feat-1"] }));
        Assert.Equal(["XYZ-9"], Apply(new JiraIssueFilter { FeatureLinks = [JiraIssueFilter.NoFeatureLinkLabel] }));
        Assert.Equal(JiraIssueFilter.NoFeatureLinkLabel, JiraIssueFilter.FeatureLinkLabel(Issues[2]));
    }

    [Fact]
    public void ComponentsLabel_JoinsTheNames_AndIsEmptyWithoutAny()
    {
        Assert.Equal("UI, Backend", JiraIssueFilter.ComponentsLabel(Issues[0]));
        Assert.Equal(string.Empty, JiraIssueFilter.ComponentsLabel(Issues[2]));
    }

    [Fact]
    public void OnlyWithoutStoryPoints_KeepsTheIssuesStillToEstimate()
    {
        var filter = new JiraIssueFilter { OnlyWithoutStoryPoints = true };

        Assert.True(filter.IsActive);
        Assert.Equal(["ABC-2", "XYZ-9"], Apply(filter));
    }

    [Fact]
    public void ActiveCount_CountsEachFieldInUse()
    {
        var filter = new JiraIssueFilter
        {
            Search = "login",
            Labels = ["ui"],
            FeatureLinks = ["FEAT-1"],
            OnlyWithoutStoryPoints = true,
        };

        Assert.Equal(4, filter.ActiveCount);
    }

    [Fact]
    public void Options_AreDistinctAndSorted_IgnoringCase()
    {
        Assert.Equal(["Blocker", "Major"], JiraIssueFilter.Options(Issues, i => i.PriorityName));
        Assert.Equal(["Alice", "Bob", "Unassigned"], JiraIssueFilter.Options(Issues, JiraIssueFilter.AssigneeLabel));
        Assert.Equal(["(no feature)", "FEAT-1", "FEAT-2"], JiraIssueFilter.Options(Issues, JiraIssueFilter.FeatureLinkLabel));
    }

    [Fact]
    public void ListOptions_StartWithTheNoValueOption_OnlyWhenAnIssueHasNone()
    {
        Assert.Equal(
            [JiraIssueFilter.NoComponentLabel, "Backend", "UI"],
            JiraIssueFilter.Options(Issues, i => i.Components, JiraIssueFilter.NoComponentLabel));
        Assert.Equal(
            ["Backend", "UI"],
            JiraIssueFilter.Options(Issues.Take(2), i => i.Components, JiraIssueFilter.NoComponentLabel));
        Assert.Equal(
            ["tech-debt", "ui"],
            JiraIssueFilter.Options(Issues.Take(2), i => i.Labels, JiraIssueFilter.NoLabelLabel));
    }

    [Fact]
    public void Clear_ResetsEveryField()
    {
        var filter = new JiraIssueFilter
        {
            Search = "abc",
            Types = ["Bug"],
            Statuses = ["Open"],
            Priorities = ["Major"],
            Assignees = ["Bob"],
            Components = ["Backend"],
            Labels = ["tech-debt"],
            FixVersions = ["R1"],
            FeatureLinks = ["FEAT-2"],
            OnlyWithoutStoryPoints = true,
        };

        filter.Clear();

        Assert.False(filter.IsActive);
        Assert.Equal(3, Apply(filter).Count);
    }
}

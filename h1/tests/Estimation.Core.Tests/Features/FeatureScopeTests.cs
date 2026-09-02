using Estimation.Core.Features.Models;
using Estimation.Core.Features.Services;
using Estimation.Core.PlanningIncrement.Models;
using Xunit;

namespace Estimation.Core.Tests.Features;

public class FeatureScopeTests
{
    [Theory]
    [InlineData("PAY", "PAY-1", "PAY")]
    [InlineData(" pay ", "CORE-1", "pay")]
    [InlineData(null, "PAY-12", "PAY")]
    [InlineData("", " CORE-3 ", "CORE")]
    [InlineData(null, "PAY7", "PAY7")]
    [InlineData(null, null, null)]
    [InlineData(null, "  ", null)]
    public void ProjectKeyOf_prefers_the_project_key_and_falls_back_to_the_jira_id_prefix(string? projectKey, string? jiraId, string? expected)
    {
        Assert.Equal(expected, FeatureScope.ProjectKeyOf(projectKey, jiraId));
    }

    [Fact]
    public void BelongsToArt_compares_keys_ignoring_case_and_whitespace()
    {
        var feature = new Feature { JiraId = "pay-4", Summary = "x" };

        Assert.True(FeatureScope.BelongsToArt(feature, " PAY "));
        Assert.False(FeatureScope.BelongsToArt(feature, "PAYX"));
        Assert.False(FeatureScope.BelongsToArt(feature, null));
        Assert.False(FeatureScope.BelongsToArt(feature, ""));
    }

    [Fact]
    public void LikelyOnArt_keeps_every_feature_that_can_belong_to_the_art()
    {
        var features = new List<Feature>
        {
            new() { Id = 1, ProjectKey = "PAY", JiraId = "PAY-1", Summary = "a" },
            new() { Id = 2, ProjectKey = " pay", JiraId = "OTHER-1", Summary = "b" },
            new() { Id = 3, ProjectKey = null, JiraId = "pay-3", Summary = "c" },
            new() { Id = 4, ProjectKey = "", JiraId = "PAY-4", Summary = "d" },
            new() { Id = 5, ProjectKey = "CORE", JiraId = "PAY-5", Summary = "e" },
            new() { Id = 6, ProjectKey = null, JiraId = "PAYX-6", Summary = "f" },
            new() { Id = 7, ProjectKey = null, JiraId = null, Summary = "g" }
        }.AsQueryable();

        var kept = features.LikelyOnArt("pay").Select(f => f.Id).ToList();

        Assert.Equal([1, 2, 3, 4], kept);
        Assert.All(features.Where(f => kept.Contains(f.Id)), f => Assert.True(FeatureScope.BelongsToArt(f, "PAY")));
    }

    [Fact]
    public void MatchesPi_by_explicit_pi_or_by_label_rules()
    {
        var rules = new List<FeatureScope.PiLabelRule>
        {
            new(new HashSet<string>(["pi-26-2", "q3"], StringComparer.OrdinalIgnoreCase), PiLabelMatchMode.All)
        };

        var byPi = new Feature { Summary = "a", Pi = new Pi { Id = 2, Name = "PI 26.2" } };
        var byLabels = new Feature { Summary = "b", Labels = "Q3, PI-26-2" };
        var partialLabels = new Feature { Summary = "c", Labels = "q3" };
        var otherPi = new Feature { Summary = "d", Pi = new Pi { Id = 1, Name = "PI 26.1" } };

        Assert.True(FeatureScope.MatchesPi(byPi, "pi 26.2", rules));
        Assert.True(FeatureScope.MatchesPi(byLabels, "PI 26.2", rules));
        Assert.False(FeatureScope.MatchesPi(partialLabels, "PI 26.2", rules));
        Assert.False(FeatureScope.MatchesPi(otherPi, "PI 26.2", rules));
        Assert.False(FeatureScope.MatchesPi(byLabels, "PI 26.2", []));
    }
}

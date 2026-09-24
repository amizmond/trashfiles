using Estimation.Core.Features.Models;
using Estimation.Core.Features.Services;
using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Train.Services;
using Xunit;

namespace Estimation.Core.Tests.Features;

public class FeatureScopeTests
{
    private static ArtMatcher Matcher(params (int ArtId, string Key)[] keys) => new(keys);

    [Theory]
    [InlineData("PAY", "PAY-1", "PAY")]
    [InlineData(" pay ", "CORE-1", "pay")]
    [InlineData(null, "PAY-12", "PAY")]
    [InlineData("", " CORE-3 ", "CORE")]
    [InlineData(null, "PAY7", "PAY7")]
    [InlineData(null, null, null)]
    [InlineData(null, "  ", null)]
    public void The_project_key_is_preferred_and_the_jira_id_prefix_is_the_fallback(string? projectKey, string? jiraId, string? expected)
    {
        Assert.Equal(expected, JiraProjectKeys.Of(projectKey, jiraId));
        Assert.Equal(expected, JiraProjectKeys.Of(new Feature { ProjectKey = projectKey, JiraId = jiraId, Summary = "x" }));
    }

    [Fact]
    public void Art_membership_compares_keys_ignoring_case_and_whitespace()
    {
        var feature = new Feature { JiraId = "pay-4", Summary = "x" };
        var matcher = Matcher((1, " PAY "), (2, "PAYX"));

        Assert.True(matcher.BelongsTo(feature, 1));
        Assert.False(matcher.BelongsTo(feature, 2));
        Assert.False(matcher.BelongsTo(feature, 3));
        Assert.False(ArtMatcher.Empty.BelongsTo(feature, 1));
    }

    [Fact]
    public void A_feature_belongs_to_a_multi_key_art_through_any_of_its_keys()
    {
        var matcher = Matcher((1, "PAY"), (1, "CARD"));

        Assert.True(matcher.BelongsTo(new Feature { ProjectKey = "PAY", JiraId = "PAY-1", Summary = "a" }, 1));
        Assert.True(matcher.BelongsTo(new Feature { JiraId = "card-2", Summary = "b" }, 1));
        Assert.False(matcher.BelongsTo(new Feature { ProjectKey = "CORE", JiraId = "CORE-3", Summary = "c" }, 1));
    }

    [Fact]
    public void A_feature_on_a_shared_key_belongs_to_neither_art()
    {
        var feature = new Feature { ProjectKey = "PAY", JiraId = "PAY-1", Summary = "a" };
        var matcher = Matcher((1, "PAY"), (2, "PAY"), (2, "CORE"));

        Assert.False(matcher.BelongsTo(feature, 1));
        Assert.False(matcher.BelongsTo(feature, 2));
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

        var kept = features.LikelyOnArt(["pay"]).Select(f => f.Id).ToList();
        var matcher = Matcher((1, "PAY"));

        Assert.Equal([1, 2, 3, 4], kept);
        Assert.All(features.Where(f => kept.Contains(f.Id)), f => Assert.True(matcher.BelongsTo(f, 1)));
    }

    [Fact]
    public void LikelyOnArt_keeps_the_features_of_every_key_of_the_art()
    {
        var features = new List<Feature>
        {
            new() { Id = 1, ProjectKey = "PAY", JiraId = "PAY-1", Summary = "a" },
            new() { Id = 2, ProjectKey = null, JiraId = "card-2", Summary = "b" },
            new() { Id = 3, ProjectKey = " CARD ", JiraId = "OTHER-3", Summary = "c" },
            new() { Id = 4, ProjectKey = "CORE", JiraId = "CORE-4", Summary = "d" },
            new() { Id = 5, ProjectKey = null, JiraId = "CARDX-5", Summary = "e" },
            new() { Id = 6, ProjectKey = "", JiraId = "PAY-6", Summary = "f" }
        }.AsQueryable();

        var kept = features.LikelyOnArt(["pay", " Card ", "PAY", ""]).Select(f => f.Id).ToList();

        Assert.Equal([1, 2, 3, 6], kept);
    }

    [Fact]
    public void LikelyOnArt_keeps_nothing_for_an_art_without_keys()
    {
        var features = new List<Feature>
        {
            new() { Id = 1, ProjectKey = "PAY", JiraId = "PAY-1", Summary = "a" },
            new() { Id = 2, ProjectKey = null, JiraId = null, Summary = "b" }
        }.AsQueryable();

        Assert.Empty(features.LikelyOnArt([]));
        Assert.Empty(features.LikelyOnArt(["", "  "]));
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

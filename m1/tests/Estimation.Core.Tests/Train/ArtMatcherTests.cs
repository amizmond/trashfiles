using Estimation.Core.Features.Models;
using Estimation.Core.Train.Models;
using Estimation.Core.Train.Services;
using Xunit;

namespace Estimation.Core.Tests.Train;

public class JiraProjectKeysTests
{
    [Fact]
    public void An_explicit_project_key_wins_over_the_jira_id()
    {
        Assert.Equal("ATLAS", JiraProjectKeys.Of("ATLAS", "OTHER-123"));
    }

    [Fact]
    public void An_explicit_project_key_is_trimmed()
    {
        Assert.Equal("ATLAS", JiraProjectKeys.Of("  ATLAS  ", null));
    }

    [Fact]
    public void Without_a_project_key_the_prefix_of_the_jira_id_is_used()
    {
        Assert.Equal("ATLAS", JiraProjectKeys.Of(null, "ATLAS-123"));
    }

    [Fact]
    public void A_jira_id_without_a_dash_is_used_whole()
    {
        Assert.Equal("ATLAS", JiraProjectKeys.Of(null, " ATLAS "));
    }

    [Fact]
    public void A_jira_id_starting_with_a_dash_is_used_whole()
    {
        Assert.Equal("-123", JiraProjectKeys.Of(null, "-123"));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    public void With_neither_a_project_key_nor_a_jira_id_there_is_no_key(string? projectKey, string? jiraId)
    {
        Assert.Null(JiraProjectKeys.Of(projectKey, jiraId));
    }

    [Fact]
    public void A_blank_project_key_falls_through_to_the_jira_id()
    {
        Assert.Equal("ATLAS", JiraProjectKeys.Of("   ", "ATLAS-1"));
    }

    [Theory]
    [InlineData(" pay ", "PAY")]
    [InlineData("Pay_2", "PAY_2")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Normalizing_trims_and_upper_cases(string? key, string? expected)
    {
        Assert.Equal(expected, JiraProjectKeys.Normalize(key));
    }
}

public class ArtMatcherTests
{
    private static Feature Issue(string? projectKey, string? jiraId = null) =>
        new() { ProjectKey = projectKey, JiraId = jiraId, Summary = "Feature" };

    [Fact]
    public void An_issue_on_a_key_of_one_art_matches_that_art()
    {
        var matcher = new ArtMatcher([(1, "PAY")]);

        var match = matcher.Match(Issue("PAY"));

        Assert.Equal(ArtMatchKind.Matched, match.Kind);
        Assert.Equal(1, match.ArtId);
        Assert.Equal("PAY", match.ProjectKey);
    }

    [Fact]
    public void Every_key_of_a_multi_key_art_matches_it()
    {
        var matcher = new ArtMatcher([(1, "PAY"), (1, "CARD"), (2, "LOAN")]);

        Assert.Equal(1, matcher.ArtIdOf(Issue(null, "PAY-10")));
        Assert.Equal(1, matcher.ArtIdOf(Issue(null, "CARD-3")));
        Assert.Equal(2, matcher.ArtIdOf(Issue("LOAN")));
    }

    [Fact]
    public void Keys_are_matched_without_regard_to_case_or_padding()
    {
        var matcher = new ArtMatcher([(1, " pay ")]);

        Assert.True(matcher.BelongsTo(Issue("Pay"), 1));
        Assert.True(matcher.BelongsTo(Issue(null, "pay-7"), 1));
    }

    [Fact]
    public void An_issue_on_an_unknown_key_is_unassigned()
    {
        var match = new ArtMatcher([(1, "PAY")]).Match(Issue("LOAN"));

        Assert.Equal(ArtMatchKind.Unassigned, match.Kind);
        Assert.Null(match.ArtId);
        Assert.Empty(match.CandidateArtIds);
    }

    [Fact]
    public void An_issue_without_a_key_has_no_key()
    {
        var match = new ArtMatcher([(1, "PAY")]).Match(Issue(null));

        Assert.Same(ArtMatch.NoKey, match);
    }

    [Fact]
    public void A_key_listed_by_two_arts_is_ambiguous_and_matches_neither()
    {
        var matcher = new ArtMatcher([(2, "PAY"), (1, "PAY")]);

        var match = matcher.Match(Issue("PAY"));

        Assert.Equal(ArtMatchKind.Ambiguous, match.Kind);
        Assert.Null(match.ArtId);
        Assert.Equal(new[] { 1, 2 }, match.CandidateArtIds);
        Assert.False(matcher.BelongsTo(Issue("PAY"), 1));
        Assert.False(matcher.BelongsTo(Issue("PAY"), 2));
        Assert.True(matcher.IsSharedKey("pay"));
    }

    [Fact]
    public void A_key_listed_twice_by_the_same_art_is_not_ambiguous()
    {
        var matcher = new ArtMatcher([(1, "PAY"), (1, "pay")]);

        Assert.Equal(1, matcher.ArtIdOf(Issue("PAY")));
        Assert.Equal(new[] { "PAY" }, matcher.KeysOf(1));
    }

    [Fact]
    public void Keys_of_an_art_keep_their_order()
    {
        var matcher = new ArtMatcher([(1, "PAY"), (1, "CARD"), (1, "ATM")]);

        Assert.Equal(new[] { "PAY", "CARD", "ATM" }, matcher.KeysOf(1));
        Assert.Empty(matcher.KeysOf(99));
    }

    [Fact]
    public void Arts_for_a_key_lists_every_art_that_uses_it()
    {
        var matcher = new ArtMatcher([(1, "PAY"), (2, "PAY"), (3, "LOAN")]);

        Assert.Equal(new[] { 1, 2 }, matcher.ArtsForKey("pay"));
        Assert.Equal(new[] { 3 }, matcher.ArtsForKey("LOAN"));
        Assert.Empty(matcher.ArtsForKey("NONE"));
        Assert.Empty(matcher.ArtsForKey(null));
        Assert.True(matcher.IsConfiguredKey("loan"));
        Assert.False(matcher.IsConfiguredKey("NONE"));
    }

    [Fact]
    public void A_matcher_built_from_arts_uses_their_keys_in_insertion_order()
    {
        var arts = new[]
        {
            new Art
            {
                Id = 1, Name = "Payments",
                JiraKeys = [new ArtJiraKey { Id = 2, JiraKey = "CARD" }, new ArtJiraKey { Id = 1, JiraKey = "PAY" }]
            },
            new Art { Id = 2, Name = "No keys" }
        };

        var matcher = ArtMatcher.FromArts(arts);

        Assert.Equal(new[] { "PAY", "CARD" }, matcher.KeysOf(1));
        Assert.Equal(1, matcher.ArtIdOf(Issue("CARD")));
        Assert.Equal(new[] { "PAY", "CARD" }, arts[0].Keys);
        Assert.Equal("PAY, CARD", arts[0].KeysText);
        Assert.False(arts[1].HasKeys);
    }

    [Fact]
    public void The_empty_matcher_assigns_nothing()
    {
        Assert.Equal(ArtMatchKind.Unassigned, ArtMatcher.Empty.Match(Issue("PAY")).Kind);
        Assert.Empty(ArtMatcher.Empty.AllKeys);
    }
}

public class ArtMatcherFilterTests
{
    private static Feature Issue(string key, string? labels = null, string? components = null) =>
        new() { ProjectKey = key, JiraId = key + "-1", Labels = labels, Components = components, Summary = "Feature" };

    private static readonly ArtMatcher Shared = new(
    [
        ArtKeyScope.Create(1, "PAY"),
        ArtKeyScope.Create(2, "PAY", components: "Cards"),
        ArtKeyScope.Create(3, "PAY", components: "Web", labels: "web,portal")
    ]);

    [Fact]
    public void A_filtered_art_beats_the_art_that_takes_the_key_without_filters()
    {
        var match = Shared.Match(Issue("PAY", components: "Cards"));

        Assert.Equal(ArtMatchKind.Matched, match.Kind);
        Assert.Equal(2, match.ArtId);
        Assert.Equal("PAY [components: Cards]", match.Scope!.ToString());
    }

    [Fact]
    public void An_issue_no_filter_accepts_goes_to_the_art_without_filters()
    {
        Assert.Equal(1, Shared.ArtIdOf(Issue("PAY", labels: "web", components: "Batch")));
        Assert.Equal(1, Shared.ArtIdOf(Issue("PAY")));
    }

    [Fact]
    public void Components_and_labels_must_both_match_when_both_are_set()
    {
        Assert.Equal(1, Shared.ArtIdOf(Issue("PAY", components: "Web")));
        Assert.Equal(1, Shared.ArtIdOf(Issue("PAY", labels: "portal")));
        Assert.Equal(3, Shared.ArtIdOf(Issue("PAY", labels: "x,portal", components: "Web")));
    }

    [Fact]
    public void Any_listed_value_is_enough_and_case_and_spacing_are_ignored()
    {
        var matcher = new ArtMatcher([ArtKeyScope.Create(2, "PAY", components: "Cards, Debit Cards")]);

        Assert.Equal(2, matcher.ArtIdOf(Issue("PAY", components: "Batch, debit cards ")));
    }

    [Fact]
    public void Two_filtered_arts_accepting_the_issue_make_it_ambiguous()
    {
        var match = Shared.Match(Issue("PAY", labels: "web", components: "Cards,Web"));

        Assert.Equal(ArtMatchKind.Ambiguous, match.Kind);
        Assert.Null(match.ArtId);
        Assert.Equal(new[] { 2, 3 }, match.CandidateArtIds);
    }

    [Fact]
    public void Without_an_art_taking_the_whole_key_an_unaccepted_issue_is_unmatched()
    {
        var matcher = new ArtMatcher([ArtKeyScope.Create(2, "PAY", components: "Cards"), ArtKeyScope.Create(3, "PAY", labels: "web")]);

        var match = matcher.Match(Issue("PAY", components: "Batch"));

        Assert.Equal(ArtMatchKind.Unmatched, match.Kind);
        Assert.Null(match.ArtId);
        Assert.Equal(new[] { 2, 3 }, match.CandidateArtIds);
    }

    [Fact]
    public void Filters_apply_per_key_so_another_key_of_the_art_stays_whole()
    {
        var matcher = new ArtMatcher([ArtKeyScope.Create(2, "PAY", components: "Cards"), ArtKeyScope.Create(2, "CARD")]);

        Assert.Equal(2, matcher.ArtIdOf(Issue("CARD")));
        Assert.Equal(ArtMatchKind.Unmatched, matcher.Match(Issue("PAY")).Kind);
        Assert.True(matcher.OwnsWholeKey(2, "card"));
        Assert.False(matcher.OwnsWholeKey(2, "PAY"));
    }

    [Fact]
    public void Arts_with_the_same_filters_on_a_key_are_reported_as_conflicts()
    {
        var matcher = new ArtMatcher(
        [
            ArtKeyScope.Create(1, "PAY"),
            ArtKeyScope.Create(4, "PAY"),
            ArtKeyScope.Create(2, "PAY", components: "Cards", labels: "x"),
            ArtKeyScope.Create(5, "PAY", components: "cards", labels: "X"),
            ArtKeyScope.Create(3, "PAY", components: "Web")
        ]);

        var conflicts = matcher.Conflicts.OrderBy(c => c.ArtIds[0]).ToList();

        Assert.Equal(2, conflicts.Count);
        Assert.True(conflicts[0].Unfiltered);
        Assert.Equal(new[] { 1, 4 }, conflicts[0].ArtIds);
        Assert.False(conflicts[1].Unfiltered);
        Assert.Equal(new[] { 2, 5 }, conflicts[1].ArtIds);
        Assert.Empty(matcher.ConflictsOf(3));
        Assert.Empty(Shared.Conflicts);
    }

    [Fact]
    public void A_matcher_built_from_arts_reads_their_filters()
    {
        var art = new Art
        {
            Id = 7, Name = "Cards",
            JiraKeys = [new ArtJiraKey { Id = 1, JiraKey = "PAY", Components = "Cards", Labels = "retail" }]
        };

        var matcher = ArtMatcher.FromArts([art]);

        Assert.Equal(7, matcher.ArtIdOf(Issue("PAY", labels: "retail", components: "Cards")));
        Assert.Equal(ArtMatchKind.Unmatched, matcher.Match(Issue("PAY", components: "Cards")).Kind);
        Assert.Equal("PAY [components: Cards; labels: retail]", art.JiraKeys[0].Description);
    }

    [Fact]
    public void List_values_are_joined_trimmed_and_distinct()
    {
        Assert.Equal("Cards,web", JiraListValues.Join([" Cards", "web", "Web ", "", null]));
        Assert.Null(JiraListValues.Join([" ", null]));
        Assert.Equal(new[] { "a", "b" }, JiraListValues.Parse(" a, ,b ,A").Order());
    }
}

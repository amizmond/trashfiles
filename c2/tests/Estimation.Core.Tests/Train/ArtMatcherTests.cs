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

public class ArtMatcherNotAndEmptyTests
{
    private static Feature Issue(string key, string? labels = null, string? components = null) =>
        new() { ProjectKey = key, JiraId = key + "-1", Labels = labels, Components = components, Summary = "Feature" };

    [Fact]
    public void A_not_only_filter_beats_the_art_that_takes_the_whole_key()
    {
        var matcher = new ArtMatcher([ArtKeyScope.Create(1, "PAY"), ArtKeyScope.Create(2, "PAY", components: "!Cards")]);

        var match = matcher.Match(Issue("PAY", components: "Web"));

        Assert.Equal(ArtMatchKind.Matched, match.Kind);
        Assert.Equal(2, match.ArtId);
        Assert.Equal("PAY [components: not Cards]", match.Scope!.ToString());
        Assert.Equal(2, matcher.ArtIdOf(Issue("PAY")));
        Assert.Equal(1, matcher.ArtIdOf(Issue("PAY", components: "cards")));
        Assert.Equal(1, matcher.ArtIdOf(Issue("PAY", components: "Web,Cards")));
        Assert.False(matcher.OwnsWholeKey(2, "PAY"));
    }

    [Theory]
    [InlineData(null, 2)]
    [InlineData("A", 1)]
    [InlineData("a", 1)]
    [InlineData("B", 2)]
    [InlineData("A,B", 1)]
    public void A_value_and_its_not_on_two_arts_split_the_key_without_gaps_or_overlaps(string? components, int artId)
    {
        var matcher = new ArtMatcher([ArtKeyScope.Create(1, "PAY", components: "A"), ArtKeyScope.Create(2, "PAY", components: "!A")]);

        var match = matcher.Match(Issue("PAY", components: components));

        Assert.Equal(ArtMatchKind.Matched, match.Kind);
        Assert.Equal(artId, match.ArtId);
        Assert.Empty(matcher.Conflicts);
        Assert.Empty(matcher.OverlapsForKey("PAY"));
    }

    [Fact]
    public void An_empty_only_filter_is_a_filter()
    {
        var scope = ArtKeyScope.Create(1, "PAY", components: "(empty)");
        var alone = new ArtMatcher([scope]);
        var withWholeKey = new ArtMatcher([ArtKeyScope.Create(1, "PAY"), ArtKeyScope.Create(2, "PAY", components: "(EMPTY)")]);

        Assert.True(scope.IsFiltered);
        Assert.False(alone.OwnsWholeKey(1, "PAY"));
        Assert.Equal(1, alone.ArtIdOf(Issue("PAY")));
        Assert.Equal(ArtMatchKind.Unmatched, alone.Match(Issue("PAY", components: "Cards")).Kind);
        Assert.Equal(2, withWholeKey.ArtIdOf(Issue("PAY", labels: "web")));
        Assert.Equal(1, withWholeKey.ArtIdOf(Issue("PAY", components: "Cards")));
    }

    [Fact]
    public void A_not_empty_only_filter_takes_every_issue_with_a_value()
    {
        var matcher = new ArtMatcher([ArtKeyScope.Create(1, "PAY"), ArtKeyScope.Create(2, "PAY", labels: "!(empty)")]);

        Assert.True(matcher.ScopesForKey("PAY")[1].IsFiltered);
        Assert.Equal(2, matcher.ArtIdOf(Issue("PAY", labels: "anything")));
        Assert.Equal(1, matcher.ArtIdOf(Issue("PAY", components: "Cards")));
    }

    [Theory]
    [InlineData(null, "Cards")]
    [InlineData("TestComment", "Cards")]
    [InlineData("testcomment, Other", "Cards")]
    [InlineData("Other", "Payments")]
    public void An_art_can_take_a_component_or_no_component_next_to_the_art_taking_the_whole_key(string? components, string art)
    {
        var arts = new[]
        {
            new Art { Id = 1, Name = "Payments", JiraKeys = [new ArtJiraKey { Id = 1, JiraKey = "PAY" }] },
            new Art { Id = 2, Name = "Cards", JiraKeys = [new ArtJiraKey { Id = 2, JiraKey = "PAY", Components = "TestComment,(empty)" }] }
        };
        var matcher = ArtMatcher.FromArts(arts);

        var match = matcher.Match(Issue("PAY", components: components));

        Assert.Equal(ArtMatchKind.Matched, match.Kind);
        Assert.Equal(art, arts.Single(a => a.Id == match.ArtId).Name);
        Assert.Equal("PAY [components: TestComment or empty]", arts[1].JiraKeys[0].Description);
    }

    [Fact]
    public void Components_and_labels_with_not_and_empty_must_both_accept()
    {
        var matcher = new ArtMatcher([ArtKeyScope.Create(1, "PAY"), ArtKeyScope.Create(2, "PAY", components: "!Cards", labels: "(empty)")]);

        Assert.Equal(2, matcher.ArtIdOf(Issue("PAY", components: "Web")));
        Assert.Equal(2, matcher.ArtIdOf(Issue("PAY")));
        Assert.Equal(1, matcher.ArtIdOf(Issue("PAY", labels: "retail", components: "Web")));
        Assert.Equal(1, matcher.ArtIdOf(Issue("PAY", components: "Cards")));
    }

    [Fact]
    public void Issue_values_that_look_like_tokens_are_read_literally()
    {
        var empty = new ArtMatcher([ArtKeyScope.Create(1, "PAY"), ArtKeyScope.Create(2, "PAY", components: "(empty)")]);
        var not = new ArtMatcher([ArtKeyScope.Create(1, "PAY"), ArtKeyScope.Create(2, "PAY", components: "!x")]);

        Assert.Equal(1, empty.ArtIdOf(Issue("PAY", components: "(empty)")));
        Assert.Equal(2, not.ArtIdOf(Issue("PAY", components: "!x")));
        Assert.Equal(1, not.ArtIdOf(Issue("PAY", components: "x")));
    }

    [Fact]
    public void A_value_and_its_not_are_not_a_conflict_but_the_same_rule_written_differently_is()
    {
        var matcher = new ArtMatcher(
        [
            ArtKeyScope.Create(1, "PAY", components: "!a"),
            ArtKeyScope.Create(2, "PAY", components: "! A"),
            ArtKeyScope.Create(3, "PAY", components: "a,(empty)"),
            ArtKeyScope.Create(4, "PAY", components: "(EMPTY),A"),
            ArtKeyScope.Create(5, "PAY", components: "A"),
            ArtKeyScope.Create(6, "PAY", components: "(empty)"),
            ArtKeyScope.Create(7, "PAY", components: "!(empty)")
        ]);

        var conflicts = matcher.Conflicts.OrderBy(c => c.ArtIds[0]).ToList();

        Assert.Equal(2, conflicts.Count);
        Assert.Equal(new[] { 1, 2 }, conflicts[0].ArtIds);
        Assert.False(conflicts[0].Unfiltered);
        Assert.Equal(new[] { 3, 4 }, conflicts[1].ArtIds);
        Assert.Empty(matcher.ConflictsOf(5));
        Assert.Empty(matcher.ConflictsOf(6));
        Assert.Empty(matcher.ConflictsOf(7));
        Assert.Empty(new ArtMatcher([ArtKeyScope.Create(1, "PAY", "A"), ArtKeyScope.Create(2, "PAY", "!A")]).Conflicts);
    }

    [Fact]
    public void Scopes_keep_their_tokens_canonical()
    {
        var scope = ArtKeyScope.Create(1, "pay", components: " ! TestComment , !(EMPTY)", labels: "(Empty), web");

        Assert.Equal(new[] { "!(empty)", "!TestComment" }, scope.Components.Order(StringComparer.Ordinal));
        Assert.Equal(new[] { "(empty)", "web" }, scope.Labels.Order(StringComparer.Ordinal));
        Assert.True(scope.ComponentFilter.IsNot);
        Assert.True(scope.LabelFilter.IncludesEmpty);
    }
}

public class ArtMatcherOverlapTests
{
    [Fact]
    public void Two_arts_that_both_take_issues_without_a_component_overlap_there()
    {
        var matcher = new ArtMatcher([ArtKeyScope.Create(1, "PAY", "X,(empty)"), ArtKeyScope.Create(2, "PAY", "Y,(empty)")]);

        var overlaps = matcher.OverlapsForKey("pay");

        Assert.Equal(
            new[] { "an issue with no component", "an issue with components X and Y" },
            overlaps.Select(o => o.IssueText));
        Assert.All(overlaps, o =>
        {
            Assert.Equal("PAY", o.JiraKey);
            Assert.Equal(new[] { 1, 2 }, o.ArtIds);
            Assert.Null(o.Labels);
        });
    }

    [Fact]
    public void Two_not_filters_overlap_on_the_values_neither_excludes()
    {
        var matcher = new ArtMatcher([ArtKeyScope.Create(1, "PAY", "!X"), ArtKeyScope.Create(2, "PAY", "!Y")]);

        var overlaps = matcher.OverlapsForKey("PAY");

        Assert.Equal(
            new[] { "an issue with no component", "an issue with a component no ART lists" },
            overlaps.Select(o => o.IssueText));
    }

    [Fact]
    public void Two_not_filters_that_also_exclude_empty_overlap_only_on_other_values()
    {
        var matcher = new ArtMatcher([ArtKeyScope.Create(1, "PAY", "!X,!(empty)"), ArtKeyScope.Create(2, "PAY", "!Y,!(empty)")]);

        var overlap = Assert.Single(matcher.OverlapsForKey("PAY"));

        Assert.True(overlap.Components!.Other);
        Assert.Equal("an issue with a component no ART lists", overlap.IssueText);
    }

    [Fact]
    public void A_dimension_no_art_filters_is_left_out_of_the_overlap()
    {
        var matcher = new ArtMatcher([ArtKeyScope.Create(1, "PAY", "Cards"), ArtKeyScope.Create(2, "PAY", "!TestComment")]);

        var overlap = Assert.Single(matcher.OverlapsForKey("PAY"));

        Assert.Equal("an issue with component Cards", overlap.IssueText);
        Assert.Null(overlap.Labels);
        Assert.Equal(new[] { 1, 2 }, overlap.ArtIds);
    }

    [Fact]
    public void An_overlap_on_labels_alone_leaves_out_the_components()
    {
        var matcher = new ArtMatcher([ArtKeyScope.Create(1, "PAY", labels: "web"), ArtKeyScope.Create(2, "PAY", labels: "!mobile")]);

        var overlap = Assert.Single(matcher.OverlapsForKey("PAY"));

        Assert.Equal("an issue with label web", overlap.IssueText);
        Assert.Null(overlap.Components);
    }

    [Fact]
    public void Arts_that_take_the_whole_key_overlap_on_any_issue()
    {
        var matcher = new ArtMatcher([ArtKeyScope.Create(1, "PAY"), ArtKeyScope.Create(2, "PAY")]);

        var overlap = Assert.Single(matcher.OverlapsForKey("PAY"));

        Assert.Equal("any issue", overlap.IssueText);
        Assert.Equal(new[] { 1, 2 }, overlap.ArtIds);
    }

    [Fact]
    public void Arts_split_by_many_components_still_overlap_on_an_issue_with_one_of_each()
    {
        var matcher = new ArtMatcher(
        [
            ArtKeyScope.Create(1, "PAY", string.Join(",", Enumerable.Range(1, 8).Select(i => $"C{i}"))),
            ArtKeyScope.Create(2, "PAY", string.Join(",", Enumerable.Range(9, 8).Select(i => $"C{i}")))
        ]);

        var overlap = Assert.Single(matcher.OverlapsForKey("PAY"));

        Assert.Equal("an issue with components C1 and C9", overlap.IssueText);
        Assert.Equal(new[] { 1, 2 }, overlap.ArtIds);
        Assert.Equal(ArtMatchKind.Ambiguous, matcher.Match(new IssueMatchFacts("PAY", "PAY-1", null, "C8,C16")).Kind);
    }

    [Fact]
    public void Overlaps_that_were_there_before_are_left_out()
    {
        var saved = new ArtMatcher([ArtKeyScope.Create(1, "PAY", "Cards"), ArtKeyScope.Create(2, "PAY", "Loans")]);
        var same = new ArtMatcher([ArtKeyScope.Create(1, "PAY", "Cards"), ArtKeyScope.Create(2, "PAY", "Loans")]);
        var edited = new ArtMatcher([ArtKeyScope.Create(1, "PAY", "Cards,Web"), ArtKeyScope.Create(2, "PAY", "Loans")]);

        Assert.Equal("an issue with components Cards and Loans", Assert.Single(saved.OverlapsForKey("PAY")).IssueText);
        Assert.Empty(same.OverlapsForKey("PAY", saved));
        Assert.Equal("an issue with components Web and Loans", Assert.Single(edited.OverlapsForKey("PAY", saved)).IssueText);
    }

    [Fact]
    public void An_overlap_with_a_new_art_is_new()
    {
        var saved = new ArtMatcher([ArtKeyScope.Create(1, "PAY", "Cards"), ArtKeyScope.Create(2, "PAY", "Loans")]);
        var edited = new ArtMatcher(
        [
            ArtKeyScope.Create(1, "PAY", "Cards"),
            ArtKeyScope.Create(2, "PAY", "Loans"),
            ArtKeyScope.Create(3, "PAY", "!(empty)")
        ]);

        var overlaps = edited.OverlapsForKey("PAY", saved);

        Assert.All(overlaps, o => Assert.Contains(3, o.ArtIds));
        Assert.Contains(overlaps, o => o.IssueText == "an issue with component Cards" && o.ArtIds.SequenceEqual([1, 3]));
        Assert.Contains(overlaps, o => o.IssueText == "an issue with components Cards and Loans" && o.ArtIds.SequenceEqual([1, 2, 3]));
    }

    [Fact]
    public void Overlaps_combine_components_and_labels()
    {
        var matcher = new ArtMatcher([ArtKeyScope.Create(1, "PAY", labels: "web"), ArtKeyScope.Create(2, "PAY", components: "Cards")]);

        var overlap = Assert.Single(matcher.OverlapsForKey("PAY"));

        Assert.Equal("an issue with component Cards and label web", overlap.IssueText);
        Assert.Equal(new[] { 1, 2 }, overlap.ArtIds);
    }

    [Fact]
    public void A_value_and_its_not_never_overlap()
    {
        var matcher = new ArtMatcher(
        [
            ArtKeyScope.Create(1, "PAY", "TestComment,(empty)"),
            ArtKeyScope.Create(2, "PAY", "!TestComment,!(empty)"),
            ArtKeyScope.Create(3, "PAY")
        ]);

        Assert.Empty(matcher.OverlapsForKey("PAY"));
    }

    [Fact]
    public void A_filtered_art_never_overlaps_the_art_taking_the_whole_key()
    {
        var matcher = new ArtMatcher([ArtKeyScope.Create(1, "PAY"), ArtKeyScope.Create(2, "PAY", "!X")]);

        Assert.Empty(matcher.OverlapsForKey("PAY"));
    }

    [Fact]
    public void A_key_used_by_one_art_or_by_none_has_no_overlaps()
    {
        var matcher = new ArtMatcher([ArtKeyScope.Create(1, "PAY", "X"), ArtKeyScope.Create(1, "CARD"), ArtKeyScope.Create(2, "LOAN", "(empty)")]);

        Assert.Empty(matcher.OverlapsForKey("PAY"));
        Assert.Empty(matcher.OverlapsForKey("LOAN"));
        Assert.Empty(matcher.OverlapsForKey("NONE"));
        Assert.Empty(matcher.OverlapsForKey(null));
    }

    private static List<JiraValueFilter> Filters(params string?[] csvs) => csvs.Select(c => JiraValueFilter.Parse(c)).ToList();

    [Fact]
    public void Samples_cover_no_value_each_value_another_value_and_pairs()
    {
        var samples = SampleValues.For(Filters("A", "b", "!a"));

        Assert.Equal(
            new[] { "", "A", "b", "other", "A+b" },
            samples.Select(s => string.Join("+", s.Values)));
        Assert.Equal(new[] { false, false, false, true, false }, samples.Select(s => s.Other));
    }

    [Fact]
    public void Samples_without_mentioned_values_are_no_value_and_another_value()
    {
        Assert.Equal(new[] { "", "other" }, SampleValues.For([]).Select(s => string.Join("+", s.Values)));
        Assert.Equal(new[] { "", "other" }, SampleValues.For(Filters(null, "(empty)")).Select(s => string.Join("+", s.Values)));
    }

    [Fact]
    public void The_other_sample_does_not_collide_with_a_real_value()
    {
        var other = SampleValues.For(Filters("Other,other1")).Single(s => s.Other);

        Assert.Equal(new[] { "other2" }, other.Values);
    }

    [Fact]
    public void Pairs_take_one_value_from_each_of_two_filters_with_no_limit_on_values()
    {
        var first = string.Join(",", Enumerable.Range(1, 20).Select(i => $"a{i}"));
        var second = string.Join(",", Enumerable.Range(1, 20).Select(i => $"b{i}"));

        var samples = SampleValues.For(Filters(first, second));

        var pair = Assert.Single(samples, s => s.Values.Count > 1);
        Assert.Equal(new[] { "a1", "b1" }, pair.Values);
        Assert.Equal(1 + 40 + 1 + 1, samples.Count);
    }

    [Fact]
    public void Pairs_that_behave_like_one_of_their_values_are_left_out()
    {
        var samples = SampleValues.For(Filters("A", "A,B"));

        Assert.DoesNotContain(samples, s => s.Values.Count > 1);
    }

    [Fact]
    public void Pairs_that_behave_differently_are_all_kept()
    {
        var samples = SampleValues.For(Filters("A", "B", "C"));

        Assert.Equal(
            new[] { "A+B", "A+C", "B+C" },
            samples.Where(s => s.Values.Count > 1).Select(s => string.Join("+", s.Values)));
    }

    [Fact]
    public void Samples_describe_the_issue_they_stand_for()
    {
        Assert.Equal("no component", new SampleValues([], false).Describe("component", "components"));
        Assert.Equal("component A", new SampleValues(["A"], false).Describe("component", "components"));
        Assert.Equal("components A and B", new SampleValues(["A", "B"], false).Describe("component", "components"));
        Assert.Equal("a label no ART lists", new SampleValues(["other"], true).Describe("label", "labels"));
        Assert.True(new SampleValues(["A"], false).Set.Contains("a"));
    }
}

using Estimation.Core.JiraIntegration.Models;
using Estimation.Core.Train.Models;
using Estimation.Core.Train.Services;
using Xunit;

namespace Estimation.Core.Tests.Train;

public class JiraValueFilterTests
{
    private static readonly string[] IssueValues = ["", "TestComment", "Other", "TestComment,Other"];

    private static JiraValueFilter F(string? csv) => JiraValueFilter.Parse(csv);

    [Theory]
    [InlineData(null, true, true, true, true)]
    [InlineData("", true, true, true, true)]
    [InlineData("TestComment", false, true, false, true)]
    [InlineData("TestComment,(empty)", true, true, false, true)]
    [InlineData("(empty)", true, false, false, false)]
    [InlineData("!TestComment", true, false, true, false)]
    [InlineData("!TestComment,!(empty)", false, false, true, false)]
    [InlineData("!(empty)", false, true, true, true)]
    public void Each_filter_accepts_the_issues_its_rule_describes(string? csv, bool none, bool testComment, bool other, bool both)
    {
        var filter = F(csv);

        Assert.Equal(new[] { none, testComment, other, both }, IssueValues.Select(v => filter.Accepts(JiraListValues.Parse(v))));
        Assert.Equal(none, filter.AcceptsEmpty);
    }

    [Theory]
    [InlineData("A,!B", false, true, false, false)]
    [InlineData("(empty),!(empty)", false, false, false, false)]
    [InlineData("(empty),!A", true, false, false, false)]
    public void A_mixed_filter_needs_its_includes_and_none_of_its_excludes(string csv, bool none, bool a, bool b, bool both)
    {
        var filter = F(csv);

        Assert.Equal(new[] { none, a, b, both }, new[] { "", "A", "B", "A,B" }.Select(v => filter.Accepts(JiraListValues.Parse(v))));
    }

    [Fact]
    public void Values_and_tokens_are_compared_without_regard_to_case()
    {
        Assert.True(F("TestComment").Accepts(new HashSet<string> { "testcomment" }));
        Assert.False(F("!TESTCOMMENT").Accepts(new HashSet<string> { "Other", "testComment" }));
        Assert.True(F("(EMPTY)").IncludesEmpty);
        Assert.True(F("!(Empty)").ExcludesEmpty);
        Assert.Equal("(empty)", F(" (EMPTY) ").ToCsv());
        Assert.Equal("!(empty)", F("! (eMPTY)").ToCsv());
    }

    [Fact]
    public void Issue_values_that_look_like_tokens_are_read_literally()
    {
        var emptyLooking = JiraListValues.Parse("(empty)");
        var notLooking = JiraListValues.Parse("!x");

        Assert.False(F("(empty)").Accepts(emptyLooking));
        Assert.True(F("!(empty)").Accepts(emptyLooking));
        Assert.True(F("!x").Accepts(notLooking));
        Assert.False(F("x").Accepts(notLooking));
        Assert.True(F("!x,!(empty)").Accepts(notLooking));
    }

    [Theory]
    [InlineData(" B , (EMPTY), a, b", "B,a,(empty)")]
    [InlineData("!(empty), ! x, !X", "!x,!(empty)")]
    [InlineData("! a , (EMPTY), a", "a,(empty),!a")]
    [InlineData("(empty),!(empty),!b,a", "a,(empty),!b,!(empty)")]
    [InlineData("a,A, a ", "a")]
    [InlineData("!, a,,  ", "a")]
    [InlineData("(empty),(Empty)", "(empty)")]
    [InlineData("!!x", "!!x")]
    [InlineData("!", null)]
    [InlineData(" ! , ", null)]
    [InlineData(" , ", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Parsing_writes_the_tokens_back_canonically(string? csv, string? expected)
    {
        Assert.Equal(expected, F(csv).ToCsv());
    }

    [Fact]
    public void Tokens_can_be_parsed_one_by_one()
    {
        var filter = JiraValueFilter.Parse(new[] { " !a ", "", "  ", "!", "!(EMPTY)", "!A" });

        Assert.Equal("!a,!(empty)", filter.ToCsv());
        Assert.Equal(new[] { "a" }, filter.ExcludedValues);
    }

    [Fact]
    public void Nothing_to_filter_by_is_the_any_filter()
    {
        Assert.Same(JiraValueFilter.Any, F(null));
        Assert.Same(JiraValueFilter.Any, F(" , ! "));
        Assert.Same(JiraValueFilter.Any, JiraValueFilter.Parse(Array.Empty<string>()));
        Assert.Empty(JiraValueFilter.Any.Tokens);
        Assert.Null(JiraValueFilter.Any.ToCsv());
    }

    [Fact]
    public void The_same_rule_written_differently_has_the_same_tokens()
    {
        Assert.True(F("!a, !(empty)").Tokens.SetEquals(F("!(EMPTY),! A,!a").Tokens));
        Assert.True(F("a,b").Tokens.SetEquals(F("B, A").Tokens));
        Assert.True(F("a,(empty)").Tokens.SetEquals(F("(EMPTY),A").Tokens));
        Assert.False(F("a").Tokens.SetEquals(F("!a").Tokens));
        Assert.False(F("(empty)").Tokens.SetEquals(F("!(empty)").Tokens));
    }

    [Fact]
    public void Duplicate_values_are_kept_once_with_their_first_spelling()
    {
        Assert.Equal(new[] { "Cards", "Debit" }, F("Cards, cards,Debit,CARDS").IncludedValues);
        Assert.Equal(new[] { "Cards" }, F("!Cards,! cards").ExcludedValues);
    }

    [Theory]
    [InlineData(null, false, false, false, false, "")]
    [InlineData("a,b", true, false, false, false, "a,b")]
    [InlineData("a,(empty)", true, false, false, true, "a")]
    [InlineData("(empty)", true, false, false, true, "")]
    [InlineData("!a,!b", true, true, false, false, "a,b")]
    [InlineData("!a,!(empty)", true, true, false, true, "a")]
    [InlineData("!(empty)", true, true, false, true, "")]
    [InlineData("a,!b", true, false, true, false, "a")]
    [InlineData("(empty),!(empty)", true, false, true, true, "")]
    public void The_shape_of_a_filter_is_read_from_its_tokens(string? csv, bool active, bool not, bool mixed, bool hasEmpty, string values)
    {
        var filter = F(csv);

        Assert.Equal(active, filter.IsActive);
        Assert.Equal(not, filter.IsNot);
        Assert.Equal(mixed, filter.IsMixed);
        Assert.Equal(hasEmpty, filter.HasEmpty);
        Assert.Equal(values, string.Join(",", filter.Values));
    }

    [Fact]
    public void Mentioned_lists_the_values_of_both_polarities_without_the_empty_token()
    {
        Assert.Equal(new[] { "a", "b" }, F("(empty),!b,a,!(empty)").Mentioned);
        Assert.Empty(F("(empty)").Mentioned);
    }

    [Fact]
    public void Creating_a_filter_applies_not_to_every_value_and_to_empty()
    {
        Assert.Equal("!a,!b,!(empty)", JiraValueFilter.Create([" a ", "", "b", "A"], withEmpty: true, not: true).ToCsv());
        Assert.Equal("a,(empty)", JiraValueFilter.Create(["a"], withEmpty: true, not: false).ToCsv());
        Assert.Equal("(empty)", JiraValueFilter.Create([], withEmpty: true, not: false).ToCsv());
        Assert.Same(JiraValueFilter.Any, JiraValueFilter.Create([], withEmpty: false, not: true));
    }

    [Fact]
    public void Adding_values_keeps_the_polarity()
    {
        var not = F("!a").WithValues(["b", "A"]);
        Assert.Equal("!a,!b", not.ToCsv());
        Assert.True(not.IsNot);

        Assert.Equal("a,b,(empty)", F("a,(empty)").WithValues(["b"]).ToCsv());
        Assert.Equal("!a,!b,!(empty)", F("!a,!(empty)").WithValues(["b"]).ToCsv());
        Assert.Equal("a", JiraValueFilter.Any.WithValues(["a"]).ToCsv());
    }

    [Fact]
    public void Removing_a_value_keeps_the_polarity_and_the_empty_token()
    {
        var onlyEmpty = F("!a,!(empty)").WithoutValue("A");
        Assert.Equal("!(empty)", onlyEmpty.ToCsv());
        Assert.True(onlyEmpty.IsNot);

        Assert.Equal("!b", F("!a,!b").WithoutValue("a").ToCsv());
        Assert.Equal("(empty)", F("a,(empty)").WithoutValue("a").ToCsv());
        Assert.Equal("a", F("a").WithoutValue("x").ToCsv());
    }

    [Fact]
    public void Removing_the_last_not_value_without_the_empty_token_leaves_no_filter()
    {
        var filter = F("!a").WithoutValue("a");

        Assert.Same(JiraValueFilter.Any, filter);
        Assert.False(filter.IsActive);
        Assert.False(filter.IsNot);
    }

    [Fact]
    public void Toggling_empty_keeps_the_polarity()
    {
        Assert.Equal("a,(empty)", F("a").WithEmpty(true).ToCsv());
        Assert.Equal("!a,!(empty)", F("!a").WithEmpty(true).ToCsv());
        Assert.Equal("!a", F("!a,!(empty)").WithEmpty(false).ToCsv());
        Assert.Same(JiraValueFilter.Any, F("!(empty)").WithEmpty(false));
        Assert.Equal("(empty)", JiraValueFilter.Any.WithEmpty(true).ToCsv());
    }

    [Fact]
    public void Toggling_not_flips_every_value_and_the_empty_token()
    {
        var not = F("a,b,(empty)").WithNot(true);

        Assert.Equal("!a,!b,!(empty)", not.ToCsv());
        Assert.Equal("a,b,(empty)", not.WithNot(false).ToCsv());
        Assert.Equal("a", F("a").WithNot(false).ToCsv());
        Assert.Equal("!(empty)", F("(empty)").WithNot(true).ToCsv());
    }

    [Fact]
    public void Toggling_not_without_values_changes_nothing()
    {
        Assert.Same(JiraValueFilter.Any, JiraValueFilter.Any.WithNot(true));
        Assert.Same(JiraValueFilter.Any, JiraValueFilter.Any.WithNot(false));
    }

    [Fact]
    public void A_mixed_filter_is_left_alone_by_the_editing_methods()
    {
        var mixed = F("a,!b");

        Assert.Same(mixed, mixed.WithValues(["c"]));
        Assert.Same(mixed, mixed.WithoutValue("a"));
        Assert.Same(mixed, mixed.WithEmpty(true));
        Assert.Same(mixed, mixed.WithNot(true));
    }

    [Theory]
    [InlineData("!x", true)]
    [InlineData("  !x", true)]
    [InlineData("!", true)]
    [InlineData("(empty)", true)]
    [InlineData(" (EMPTY) ", true)]
    [InlineData("x", false)]
    [InlineData("x!", false)]
    [InlineData("(empty) x", false)]
    [InlineData("empty", false)]
    [InlineData("  ", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Typed_values_that_would_read_as_tokens_are_reserved(string? value, bool reserved)
    {
        Assert.Equal(reserved, JiraValueFilter.IsReserved(value));
    }

    [Fact]
    public void Mixing_values_with_and_without_not_is_a_problem()
    {
        Assert.Equal("The components mix values with and without Not. Use Not for all of them or for none.",
            F("A,!B").Problem("components"));
        Assert.Equal("The labels mix values with and without Not. Use Not for all of them or for none.",
            F("(empty),!(empty)").Problem("labels"));
    }

    [Fact]
    public void A_value_that_still_starts_with_not_is_a_problem()
    {
        Assert.Equal("The labels value \"!x\" starts with '!', which is reserved for Not.", F("!!x").Problem("labels"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("A,B")]
    [InlineData("A,(empty)")]
    [InlineData("(empty)")]
    [InlineData("!A,!(empty)")]
    [InlineData("!(empty)")]
    public void A_filter_with_one_polarity_has_no_problem(string? csv)
    {
        Assert.Null(F(csv).Problem("components"));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("A, B", "A, B")]
    [InlineData("A,(empty)", "A or empty")]
    [InlineData("A,B,(empty)", "A, B or empty")]
    [InlineData("(empty)", "empty")]
    [InlineData("!A", "not A")]
    [InlineData("!A,!B", "none of A, B")]
    [InlineData("!A,!(empty)", "not A and not empty")]
    [InlineData("!A,!B,!(empty)", "none of A, B and not empty")]
    [InlineData("!(empty)", "not empty")]
    [InlineData("A,!B", "A, not B")]
    public void The_short_description_names_the_rule(string? csv, string expected)
    {
        Assert.Equal(expected, F(csv).Describe());
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("TestComment", "component TestComment")]
    [InlineData("A,B", "one of the components A, B")]
    [InlineData("TestComment,(empty)", "component TestComment or no component")]
    [InlineData("A,B,(empty)", "one of the components A, B or no component")]
    [InlineData("(empty)", "no component")]
    [InlineData("!TestComment", "no component TestComment")]
    [InlineData("!A,!B", "none of the components A, B")]
    [InlineData("!TestComment,!(empty)", "a component, but not TestComment")]
    [InlineData("!A,!B,!(empty)", "a component, but none of A, B")]
    [InlineData("!(empty)", "a component")]
    [InlineData("A,!B", "component A and no component B")]
    public void The_requirement_reads_after_issues_with(string? csv, string expected)
    {
        Assert.Equal(expected, F(csv).Requirement("component", "components"));
    }

    [Fact]
    public void The_requirement_uses_the_names_it_is_given()
    {
        Assert.Equal("label web or no label", F("web,(empty)").Requirement("label", "labels"));
        Assert.Equal("a label", F("!(empty)").Requirement("label", "labels"));
        Assert.Equal("none of the labels web, portal", F("!web,!portal").Requirement("label", "labels"));
    }
}

public class JiraKeyValuesTests
{
    [Fact]
    public void Values_are_tallied_once_per_issue_most_used_first()
    {
        var tally = JiraValueCount.Tally(["A, b", "a", null, "", "B,C", "c,C"]);

        Assert.Equal(new[] { "A", "b", "C" }, tally.Select(c => c.Value));
        Assert.Equal(new[] { 2, 2, 2 }, tally.Select(c => c.Count));
        Assert.Empty(JiraValueCount.Tally([null, " , "]));
    }

    [Fact]
    public void Ties_are_ordered_by_value()
    {
        var tally = JiraValueCount.Tally(["zeta", "Alpha,zeta", "beta"]);

        Assert.Equal(new[] { new JiraValueCount("zeta", 2), new JiraValueCount("Alpha", 1), new JiraValueCount("beta", 1) }, tally);
    }

    [Fact]
    public void Key_values_count_the_features_with_and_without_components_and_labels()
    {
        var values = JiraKeyValues.From(
        [
            new IssueMatchFacts("PAY", "PAY-1", "web", "Cards"),
            new IssueMatchFacts("PAY", "PAY-2", null, "cards,Debit"),
            new IssueMatchFacts("PAY", "PAY-3", " , ", null),
            new IssueMatchFacts("PAY", "PAY-4", "web,retail", "")
        ]);

        Assert.Equal(4, values.Features);
        Assert.Equal(new[] { new JiraValueCount("Cards", 2), new JiraValueCount("Debit", 1) }, values.Components);
        Assert.Equal(2, values.WithoutComponents);
        Assert.Equal(new[] { new JiraValueCount("web", 2), new JiraValueCount("retail", 1) }, values.Labels);
        Assert.Equal(2, values.WithoutLabels);
    }

    [Fact]
    public void Key_values_of_no_features_are_empty()
    {
        var values = JiraKeyValues.From([]);

        Assert.Equal(0, values.Features);
        Assert.Empty(values.Components);
        Assert.Equal(0, values.WithoutComponents);
        Assert.Empty(values.Labels);
        Assert.Equal(0, values.WithoutLabels);
    }
}

public class ArtJiraKeyDescriptionTests
{
    [Theory]
    [InlineData("Cards", "retail", "PAY [components: Cards; labels: retail]")]
    [InlineData("TestComment,(empty)", null, "PAY [components: TestComment or empty]")]
    [InlineData("!TestComment", "!(empty)", "PAY [components: not TestComment; labels: not empty]")]
    [InlineData(null, "(empty)", "PAY [labels: empty]")]
    [InlineData("!a,!b,!(empty)", null, "PAY [components: none of a, b and not empty]")]
    [InlineData("!", " , ", "PAY")]
    [InlineData(null, null, "PAY")]
    public void A_key_row_describes_its_not_and_empty_rules(string? components, string? labels, string expected)
    {
        Assert.Equal(expected, new ArtJiraKey { JiraKey = "PAY", Components = components, Labels = labels }.Description);
    }

    [Fact]
    public void Filters_are_described_without_the_key()
    {
        Assert.Equal("components: not TestComment and not empty; labels: web", ArtJiraKey.DescribeFilters(["!TestComment", "!(empty)"], ["web"]));
        Assert.Equal("", ArtJiraKey.DescribeFilters([], []));
    }

    [Fact]
    public void A_scope_describes_itself_like_its_key_row()
    {
        Assert.Equal("PAY [components: not TestComment]", ArtKeyScope.Create(1, "pay", "! TestComment").ToString());
        Assert.Equal("PAY [labels: empty]", ArtKeyScope.Create(1, "PAY", labels: "(EMPTY)").ToString());
        Assert.Equal("PAY", ArtKeyScope.Create(1, "PAY", "!", "").ToString());
    }

    [Fact]
    public void A_sync_key_art_reads_not_and_empty_rules_as_filters()
    {
        var filtered = new JiraSyncKeyArt(1, "Cards", "!TestComment,!(empty)", null);
        var emptyOnly = new JiraSyncKeyArt(2, "Web", null, "(empty)");
        var whole = new JiraSyncKeyArt(3, "Payments", " , ", "!");

        Assert.True(filtered.IsFiltered);
        Assert.Equal("components: not TestComment and not empty", filtered.FilterDescription);
        Assert.True(emptyOnly.IsFiltered);
        Assert.Equal("labels: empty", emptyOnly.FilterDescription);
        Assert.False(whole.IsFiltered);
        Assert.Equal("", whole.FilterDescription);
    }
}

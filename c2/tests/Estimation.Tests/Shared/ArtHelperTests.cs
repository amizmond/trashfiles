using Estimation.Components.Shared;
using Estimation.Core.Features.Models;
using Estimation.Core.Train.Models;
using Estimation.Core.Train.Services;
using Xunit;

namespace Estimation.Tests.Shared;

public class ArtHelperTests
{
    private static readonly ArtLookup Lookup = ArtHelper.BuildLookup(
    [
        new Art { Id = 1, Name = "Payments", JiraKeys = [new ArtJiraKey { Id = 1, JiraKey = "PAY" }] },
        new Art { Id = 2, Name = "Cards", JiraKeys = [new ArtJiraKey { Id = 2, JiraKey = "PAY", Components = "Cards" }] },
        new Art { Id = 3, Name = "Loans", JiraKeys = [new ArtJiraKey { Id = 3, JiraKey = "LOAN" }] },
        new Art { Id = 4, Name = "Web", JiraKeys = [new ArtJiraKey { Id = 4, JiraKey = "WEB", Labels = "web" }] },
        new Art { Id = 5, Name = "Portal", JiraKeys = [new ArtJiraKey { Id = 5, JiraKey = "WEB", Labels = "portal" }] }
    ]);

    private static Feature Issue(string key, string? labels = null, string? components = null) =>
        new() { ProjectKey = key, JiraId = key + "-1", Labels = labels, Components = components, Summary = "Issue" };

    private static List<string> Names(IEnumerable<ArtRef> arts) => arts.Select(a => a.Name).ToList();

    [Fact]
    public void A_parent_takes_the_arts_of_its_features_sorted_and_distinct()
    {
        var features = new[] { Issue("LOAN"), Issue("PAY", components: "Cards"), Issue("PAY", components: "cards"), Issue("PAY") };

        var arts = ArtHelper.ResolveParentArts(Issue("OTHER"), features, Lookup);

        Assert.Equal(new[] { "Cards", "Loans", "Payments" }, Names(arts));
    }

    [Fact]
    public void A_parent_without_features_falls_back_to_its_own_key()
    {
        Assert.Equal(new[] { "Loans" }, Names(ArtHelper.ResolveParentArts(Issue("LOAN"), [], Lookup)));
        Assert.Empty(ArtHelper.ResolveParentArts(Issue("OTHER"), [], Lookup));
        Assert.Empty(ArtHelper.ResolveParentArts(Issue("WEB"), [], Lookup));
    }

    [Fact]
    public void Features_without_an_art_add_nothing_and_do_not_bring_back_the_parents_key()
    {
        var features = new[] { Issue("WEB"), Issue("WEB", labels: "web,portal") };

        Assert.Empty(ArtHelper.ResolveParentArts(Issue("LOAN"), features, Lookup));
    }

    [Fact]
    public void The_art_column_names_unmatched_and_ambiguous_issues()
    {
        Assert.Equal("Cards", ArtHelper.DisplayName(Lookup.Matcher.Match(Issue("PAY", components: "Cards")), Lookup));
        Assert.Equal(ArtHelper.UnassignedLabel, ArtHelper.DisplayName(Lookup.Matcher.Match(Issue("WEB")), Lookup));
        Assert.Equal(ArtHelper.AmbiguousLabel, ArtHelper.DisplayName(Lookup.Matcher.Match(Issue("WEB", labels: "web,portal")), Lookup));
        Assert.Null(ArtHelper.DisplayName(Lookup.Matcher.Match(Issue("OTHER")), Lookup));
    }

    [Fact]
    public void The_explanation_names_the_rule_or_the_competing_arts()
    {
        Assert.Equal("Cards via PAY [components: Cards]",
            ArtHelper.Explain(Lookup.Matcher.Match(Issue("PAY", components: "Cards")), Lookup));
        Assert.Equal("Claimed by Web, Portal",
            ArtHelper.Explain(Lookup.Matcher.Match(Issue("WEB", labels: "portal,web")), Lookup));
        Assert.Equal("Key WEB is used by Web, Portal, but none of their filters accepts the issue (Web needs label web; Portal needs label portal)",
            ArtHelper.Explain(Lookup.Matcher.Match(Issue("WEB")), Lookup));
    }

    [Fact]
    public void An_unmatched_issue_is_told_what_the_filter_of_each_art_needs()
    {
        var lookup = ArtHelper.BuildLookup(
        [
            new Art { Id = 1, Name = "Cards", JiraKeys = [new ArtJiraKey { Id = 1, JiraKey = "CRD", Components = "Cards, Debit", Labels = "retail" }] },
            new Art { Id = 2, Name = "Lending", JiraKeys = [new ArtJiraKey { Id = 2, JiraKey = "CRD", Labels = "loan, credit" }] }
        ]);

        Assert.Equal(
            "Key CRD is used by Cards, Lending, but none of their filters accepts the issue "
            + "(Cards needs one of the components Cards, Debit and label retail; Lending needs one of the labels loan, credit)",
            ArtHelper.Explain(lookup.Matcher.Match(Issue("CRD", labels: "retail", components: "Credit")), lookup));
    }

    [Fact]
    public void A_move_is_described_only_when_the_art_changes()
    {
        var payments = Lookup.Matcher.Match(Issue("PAY"));
        var cards = Lookup.Matcher.Match(Issue("PAY", components: "Cards"));
        var unmatched = Lookup.Matcher.Match(Issue("WEB"));

        Assert.Null(ArtHelper.DescribeMove(payments, Lookup.Matcher.Match(Issue("PAY", labels: "x")), Lookup));
        Assert.Equal("Moves from Payments to Cards via PAY [components: Cards].", ArtHelper.DescribeMove(payments, cards, Lookup));
        Assert.Equal(
            "Moves from Cards to (Unassigned): Key WEB is used by Web, Portal, but none of their filters accepts the issue "
            + "(Web needs label web; Portal needs label portal).",
            ArtHelper.DescribeMove(cards, unmatched, Lookup));
    }

    [Fact]
    public void An_unmatched_issue_is_told_what_not_and_empty_filters_need()
    {
        var lookup = ArtHelper.BuildLookup(
        [
            new Art { Id = 1, Name = "Cards", JiraKeys = [new ArtJiraKey { Id = 1, JiraKey = "CRD", Components = "!TestComment" }] },
            new Art { Id = 2, Name = "Lending", JiraKeys = [new ArtJiraKey { Id = 2, JiraKey = "CRD", Components = "Loans,(empty)" }] },
            new Art { Id = 3, Name = "Savings", JiraKeys = [new ArtJiraKey { Id = 3, JiraKey = "CRD", Components = "!TestComment,!(empty)" }] },
            new Art { Id = 4, Name = "Deposits", JiraKeys = [new ArtJiraKey { Id = 4, JiraKey = "CRD", Labels = "!(empty)" }] }
        ]);

        Assert.Equal(
            "Key CRD is used by Cards, Lending, Savings, Deposits, but none of their filters accepts the issue "
            + "(Cards needs no component TestComment; Lending needs component Loans or no component; "
            + "Savings needs a component, but not TestComment; Deposits needs a label)",
            ArtHelper.Explain(lookup.Matcher.Match(Issue("CRD", components: "TestComment")), lookup));
    }

    [Fact]
    public void An_unmatched_issue_is_told_when_an_art_also_takes_issues_without_a_component()
    {
        var lookup = ArtHelper.BuildLookup(
        [
            new Art { Id = 1, Name = "Cards", JiraKeys = [new ArtJiraKey { Id = 1, JiraKey = "CRD", Components = "TestComment,(empty)" }] },
            new Art { Id = 2, Name = "Web", JiraKeys = [new ArtJiraKey { Id = 2, JiraKey = "CRD", Components = "!TestComment", Labels = "!(empty)" }] }
        ]);

        Assert.Equal(
            "Key CRD is used by Cards, Web, but none of their filters accepts the issue "
            + "(Cards needs component TestComment or no component; Web needs no component TestComment and a label)",
            ArtHelper.Explain(lookup.Matcher.Match(Issue("CRD", components: "Other")), lookup));
    }

    [Fact]
    public void The_explanation_of_a_match_names_not_and_empty_rules()
    {
        var lookup = ArtHelper.BuildLookup(
        [
            new Art { Id = 1, Name = "Payments", JiraKeys = [new ArtJiraKey { Id = 1, JiraKey = "PAY" }] },
            new Art { Id = 2, Name = "Cards", JiraKeys = [new ArtJiraKey { Id = 2, JiraKey = "PAY", Components = "!TestComment" }] },
            new Art { Id = 3, Name = "Loans", JiraKeys = [new ArtJiraKey { Id = 3, JiraKey = "LOAN", Components = "TestComment,(empty)", Labels = "!(empty)" }] }
        ]);
        var payments = lookup.Matcher.Match(Issue("PAY", components: "TestComment"));
        var cards = lookup.Matcher.Match(Issue("PAY", components: "Other"));

        Assert.Equal("Cards via PAY [components: not TestComment]", ArtHelper.Explain(cards, lookup));
        Assert.Equal("Cards via PAY [components: not TestComment]", ArtHelper.Explain(lookup.Matcher.Match(Issue("PAY")), lookup));
        Assert.Equal("Payments via PAY", ArtHelper.Explain(payments, lookup));
        Assert.Equal("Loans via LOAN [components: TestComment or empty; labels: not empty]",
            ArtHelper.Explain(lookup.Matcher.Match(Issue("LOAN", labels: "retail")), lookup));
        Assert.Equal("Moves from Payments to Cards via PAY [components: not TestComment].", ArtHelper.DescribeMove(payments, cards, lookup));
    }
}

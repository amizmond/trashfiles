using Estimation.Core.JiraIntegration.Client;
using Estimation.Core.JiraIntegration.Models;
using Xunit;

namespace Estimation.Core.Tests.JiraIntegration;

public class LinkedIssueEditsTests
{
    private static readonly LinkRelation Blocks = new("Blocks", true, "blocks", false);
    private static readonly LinkRelation BlockedBy = new("Blocks", false, "is blocked by", false);
    private static readonly LinkRelation Relates = new("Relates", true, "relates to", true);

    private readonly LinkedIssueEdits _edits = new();

    private static LinkTarget Target(string key) => new(1, key, $"Name of {key}", "To Do");

    private static LinkedIssue Live(string id, string key, bool outward = true, string type = "Blocks") =>
        new(id, type, outward ? "blocks" : "is blocked by", outward, key);

    private string? Add(LinkRelation relation, string key, params LinkedIssue[] current) =>
        _edits.TryAdd("PAY-1", relation, Target(key), current);

    [Fact]
    public void Every_link_type_offers_both_phrases_in_alphabetical_order()
    {
        var relations = LinkRelation.From(
        [
            new JiraIssueLinkType { Name = "Blocks", Outward = "blocks", Inward = "is blocked by" },
            new JiraIssueLinkType { Name = "Cloners", Outward = "clones", Inward = "is cloned by" },
        ]);

        Assert.Equal(new[] { "blocks", "clones", "is blocked by", "is cloned by" }, relations.Select(r => r.Phrase));
        Assert.Equal(new[] { true, true, false, false }, relations.Select(r => r.IsOutward));
    }

    [Fact]
    public void A_link_type_that_reads_the_same_both_ways_offers_one_phrase()
    {
        var relation = Assert.Single(LinkRelation.From(
            [new JiraIssueLinkType { Name = "Relates", Outward = "relates to", Inward = "Relates To" }]));

        Assert.True(relation.Symmetric);
        Assert.True(relation.IsOutward);
    }

    [Fact]
    public void A_link_type_without_phrases_falls_back_to_its_name()
    {
        var relation = Assert.Single(LinkRelation.From([new JiraIssueLinkType { Name = "Depends" }]));

        Assert.Equal("Depends", relation.Phrase);
    }

    [Fact]
    public void An_added_link_waits_with_the_picked_feature()
    {
        Assert.Null(_edits.TryAdd("PAY-1", Blocks, new LinkTarget(7, " PAY-2 ", "Fees", "To Do"), []));

        var add = Assert.Single(_edits.Adds);
        Assert.Equal("PAY-2", add.Key);
        Assert.Equal("blocks", add.Relation);
        Assert.Equal(7, add.FeatureId);
        Assert.Equal("Fees", add.Name);
        Assert.True(_edits.HasChanges);
    }

    [Fact]
    public void A_feature_cannot_be_linked_to_itself()
    {
        Assert.NotNull(Add(Blocks, "pay-1"));
        Assert.False(_edits.HasChanges);
    }

    [Fact]
    public void A_link_jira_already_has_cannot_be_added_again()
    {
        Assert.NotNull(Add(Blocks, "PAY-2", Live("501", "PAY-2")));
        Assert.Empty(_edits.Adds);
    }

    [Fact]
    public void The_same_link_cannot_be_added_twice()
    {
        Add(Blocks, "PAY-2");

        Assert.NotNull(Add(Blocks, "pay-2"));
        Assert.Single(_edits.Adds);
    }

    [Fact]
    public void The_opposite_direction_of_an_existing_link_is_a_different_link()
    {
        Assert.Null(Add(BlockedBy, "PAY-2", Live("501", "PAY-2")));
    }

    [Fact]
    public void A_symmetric_link_counts_as_present_in_either_direction()
    {
        Assert.NotNull(Add(Relates, "PAY-2", Live("501", "PAY-2", outward: false, type: "Relates")));
    }

    [Fact]
    public void A_removal_can_be_taken_back()
    {
        _edits.Remove("501");
        Assert.True(_edits.IsRemoved("501"));

        _edits.Restore("501");

        Assert.False(_edits.HasChanges);
    }

    [Fact]
    public void The_changes_to_send_list_removals_and_additions()
    {
        _edits.Remove("501");
        Add(Blocks, "PAY-2");

        var changes = _edits.Changes();

        Assert.True(changes.Any);
        Assert.Equal(new[] { "501" }, changes.Removals);
        Assert.Equal("PAY-2", Assert.Single(changes.Adds).Key);
    }

    [Fact]
    public void A_fresh_read_drops_additions_jira_now_has_and_removals_that_are_gone()
    {
        Add(Blocks, "PAY-2");
        Add(Blocks, "PAY-3");
        _edits.Remove("501");
        _edits.Remove("502");

        var gone = _edits.Rebase([Live("600", "PAY-2"), Live("502", "PAY-9")]);

        Assert.Equal(new[] { "501" }, gone);
        Assert.Equal("PAY-3", Assert.Single(_edits.Adds).Key);
        Assert.False(_edits.IsRemoved("501"));
        Assert.True(_edits.IsRemoved("502"));
    }

    [Fact]
    public void Changes_jira_did_not_take_keep_their_reason()
    {
        Add(Blocks, "PAY-2");
        Add(Blocks, "PAY-3");
        _edits.Remove("501");
        var push = new LinkPushResult(
            [],
            [_edits.Adds[1]],
            [new LinkPushFailure(null, _edits.Adds[0], "No permission"), new LinkPushFailure("501", null, "Not found")]);

        _edits.MarkFailures(push);

        Assert.Equal("No permission", _edits.Adds[0].Error);
        Assert.Equal(LinkedIssueEdits.NotShownByJira, _edits.Adds[1].Error);
        Assert.Equal("Not found", _edits.RemovalError("501"));
    }

    [Fact]
    public void Without_a_fresh_read_the_sent_changes_are_settled_by_what_jira_answered()
    {
        Add(Blocks, "PAY-2");
        Add(Blocks, "PAY-3");
        _edits.Remove("501");
        _edits.Remove("502");
        var push = new LinkPushResult(
            ["501"],
            [_edits.Adds[0]],
            [new LinkPushFailure(null, _edits.Adds[1], "No permission"), new LinkPushFailure("502", null, "Not found")]);

        _edits.Settle(push);

        var add = Assert.Single(_edits.Adds);
        Assert.Equal("PAY-3", add.Key);
        Assert.Equal("No permission", add.Error);
        Assert.False(_edits.IsRemoved("501"));
        Assert.Equal("Not found", _edits.RemovalError("502"));
    }

    [Fact]
    public void A_failed_addition_can_be_sent_again_or_dropped()
    {
        Add(Blocks, "PAY-2");
        _edits.MarkFailures(new LinkPushResult([], [], [new LinkPushFailure(null, _edits.Adds[0], "No permission")]));

        Assert.Equal("PAY-2", Assert.Single(_edits.Changes().Adds).Key);

        _edits.CancelAdd(_edits.Adds[0]);
        Assert.False(_edits.HasChanges);
    }

    [Fact]
    public void A_symmetric_addition_jira_shows_from_the_other_side_is_dropped_by_a_fresh_read()
    {
        Add(Relates, "PAY-2");

        _edits.Rebase([Live("600", "PAY-2", outward: false, type: "Relates")]);

        Assert.False(_edits.HasChanges);
    }

    [Fact]
    public void A_removal_jira_still_shows_is_marked_as_not_shown_yet()
    {
        _edits.Remove("501");

        _edits.MarkFailures(new LinkPushResult(["501"], [], []));

        Assert.Equal(LinkedIssueEdits.NotShownByJira, _edits.RemovalError("501"));
    }

    [Fact]
    public void After_a_save_only_removals_this_user_sent_and_jira_no_longer_shows_are_forgotten()
    {
        _edits.Remove("501");
        _edits.Remove("502");
        var sent = _edits.Changes();

        var forget = _edits.Confirm(
            [Live("502", "PAY-9"), Live("700", "PAY-8")],
            sent,
            new LinkPushResult(["501"], [], [new LinkPushFailure("502", null, "No permission")]));

        Assert.Equal(new[] { "501" }, forget);
        Assert.False(_edits.IsRemoved("501"));
        Assert.Equal("No permission", _edits.RemovalError("502"));
    }

    [Fact]
    public void A_sent_removal_is_forgotten_even_when_an_earlier_read_already_dropped_it()
    {
        _edits.Remove("501");
        var sent = _edits.Changes();
        _edits.Rebase([]);

        var forget = _edits.Confirm([], sent, new LinkPushResult(["501"], [], []));

        Assert.Equal(new[] { "501" }, forget);
        Assert.False(_edits.HasChanges);
    }

    [Fact]
    public void After_a_save_additions_jira_shows_are_done_and_the_others_keep_their_reason()
    {
        Add(Blocks, "PAY-2");
        Add(Blocks, "PAY-3");
        var sent = _edits.Changes();

        var forget = _edits.Confirm(
            [Live("600", "PAY-2")],
            sent,
            new LinkPushResult([], [sent.Adds[0]], [new LinkPushFailure(null, sent.Adds[1], "No permission")]));

        Assert.Empty(forget);
        var left = Assert.Single(_edits.Adds);
        Assert.Equal("PAY-3", left.Key);
        Assert.Equal("No permission", left.Error);
        Assert.Equal("No permission", _edits.FirstError);
    }

    [Fact]
    public void The_first_error_is_of_a_change_that_is_still_pending()
    {
        Assert.Null(_edits.FirstError);

        Add(Blocks, "PAY-2");
        _edits.Remove("501");
        _edits.MarkFailures(new LinkPushResult([], [], [new LinkPushFailure("501", null, "Not found")]));
        _edits.Restore("501");

        Assert.Equal(LinkedIssueEdits.NotShownByJira, _edits.FirstError);
    }

    [Fact]
    public void Clearing_forgets_everything()
    {
        Add(Blocks, "PAY-2");
        _edits.Remove("501");

        _edits.Clear();

        Assert.False(_edits.HasChanges);
        Assert.Equal(0, _edits.Count);
    }
}

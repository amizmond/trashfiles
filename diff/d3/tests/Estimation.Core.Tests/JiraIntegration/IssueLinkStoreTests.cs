using Estimation.Core.JiraIntegration.Client;
using Estimation.Core.JiraIntegration.Models;
using Estimation.Core.JiraIntegration.Services;
using Estimation.Core.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Estimation.Core.Tests.JiraIntegration;

public class IssueLinkStoreTests
{
    private readonly InMemoryDatabase _db = new();

    private static JiraIssueLink Outward(string id, string otherKey, string type = "Blocks") => new()
    {
        Id = id,
        TypeName = type,
        OutwardLabel = "blocks",
        InwardLabel = "is blocked by",
        IsOutward = true,
        OtherKey = otherKey,
    };

    private static JiraIssueLink Inward(string id, string otherKey, string type = "Blocks") => new()
    {
        Id = id,
        TypeName = type,
        OutwardLabel = "blocks",
        InwardLabel = "is blocked by",
        IsOutward = false,
        OtherKey = otherKey,
    };

    private static IssueLinkSnapshot Issue(string key, params JiraIssueLink[] links) => new(key, links);

    private Task<bool> ApplyAsync(bool prune, params IssueLinkSnapshot[] snapshots) =>
        IssueLinkStore.ApplyAsync(_db, snapshots, prune);

    private Task<List<string>> RowsAsync() =>
        _db.ReadAsync(async db => (await db.IssueLinks.AsNoTracking().OrderBy(l => l.JiraLinkId).ToListAsync())
            .Select(l => $"{l.JiraLinkId}: {l.FromKey} > {l.ToKey}")
            .ToList());

    [Fact]
    public async Task An_outward_link_is_stored_from_the_issue_to_the_other_one()
    {
        Assert.True(await ApplyAsync(true, Issue("PAY-1", Outward("501", "PAY-2"))));

        Assert.Equal(new[] { "501: PAY-1 > PAY-2" }, await RowsAsync());
    }

    [Fact]
    public async Task An_inward_link_is_stored_from_the_other_issue_to_this_one()
    {
        await ApplyAsync(true, Issue("PAY-2", Inward("501", "PAY-1")));

        Assert.Equal(new[] { "501: PAY-1 > PAY-2" }, await RowsAsync());
    }

    [Fact]
    public async Task Both_ends_of_a_link_make_one_row()
    {
        await ApplyAsync(true, Issue("PAY-1", Outward("501", "PAY-2")), Issue("PAY-2", Inward("501", "PAY-1")));

        Assert.Equal(new[] { "501: PAY-1 > PAY-2" }, await RowsAsync());
    }

    [Fact]
    public async Task The_type_and_both_phrases_are_stored()
    {
        await ApplyAsync(true, Issue("PAY-1", Outward("501", "PAY-2")));

        var row = await _db.ReadAsync(db => db.IssueLinks.AsNoTracking().SingleAsync());
        Assert.Equal("Blocks", row.TypeName);
        Assert.Equal("blocks", row.OutwardLabel);
        Assert.Equal("is blocked by", row.InwardLabel);
    }

    [Fact]
    public async Task A_link_jira_no_longer_returns_is_removed_when_pruning()
    {
        await ApplyAsync(true, Issue("PAY-1", Outward("501", "PAY-2"), Outward("502", "PAY-3")));

        await ApplyAsync(true, Issue("PAY-1", Outward("502", "PAY-3")));

        Assert.Equal(new[] { "502: PAY-1 > PAY-3" }, await RowsAsync());
    }

    [Fact]
    public async Task A_removed_link_is_dropped_when_the_other_end_is_read()
    {
        await ApplyAsync(true, Issue("PAY-1", Outward("501", "PAY-2")));

        await ApplyAsync(true, Issue("PAY-2"));

        Assert.Empty(await RowsAsync());
    }

    [Fact]
    public async Task Without_pruning_nothing_is_removed()
    {
        await ApplyAsync(true, Issue("PAY-1", Outward("501", "PAY-2")));

        await ApplyAsync(false, Issue("PAY-1", Outward("502", "PAY-3")));

        Assert.Equal(new[] { "501: PAY-1 > PAY-2", "502: PAY-1 > PAY-3" }, await RowsAsync());
    }

    [Fact]
    public async Task An_issue_jira_returned_without_the_field_keeps_its_links()
    {
        await ApplyAsync(true, Issue("PAY-1", Outward("501", "PAY-2")));

        Assert.True(await ApplyAsync(true, new IssueLinkSnapshot("PAY-1", null)));

        Assert.Equal(new[] { "501: PAY-1 > PAY-2" }, await RowsAsync());
    }

    [Fact]
    public async Task Links_of_other_issues_are_left_alone()
    {
        await ApplyAsync(true, Issue("PAY-7", Outward("700", "PAY-8")));

        await ApplyAsync(true, Issue("PAY-1"));

        Assert.Equal(new[] { "700: PAY-7 > PAY-8" }, await RowsAsync());
    }

    [Fact]
    public async Task A_link_one_end_no_longer_lists_stays_while_the_other_end_still_does()
    {
        await ApplyAsync(true, Issue("PAY-1", Outward("501", "PAY-2")));

        await ApplyAsync(true, Issue("PAY-1"), Issue("PAY-2", Inward("501", "PAY-1")));

        Assert.Equal(new[] { "501: PAY-1 > PAY-2" }, await RowsAsync());
    }

    [Fact]
    public async Task A_renamed_link_type_updates_the_stored_row()
    {
        await ApplyAsync(true, Issue("PAY-1", Outward("501", "PAY-2")));

        await ApplyAsync(true, Issue("PAY-1", Outward("501", "PAY-2", type: "Dependency")));

        var row = await _db.ReadAsync(db => db.IssueLinks.AsNoTracking().SingleAsync());
        Assert.Equal("Dependency", row.TypeName);
    }

    [Fact]
    public async Task A_padded_issue_key_still_matches_its_rows()
    {
        await ApplyAsync(true, Issue("PAY-1", Outward("501", "PAY-2")));

        await ApplyAsync(true, Issue(" PAY-1 "));

        Assert.Empty(await RowsAsync());
    }

    [Fact]
    public async Task An_over_long_phrase_is_cut_to_fit()
    {
        var link = Outward("501", "PAY-2");
        link.OutwardLabel = new string('x', IssueLink.MaxTextLength + 40);

        await ApplyAsync(true, Issue("PAY-1", link));

        var row = await _db.ReadAsync(db => db.IssueLinks.AsNoTracking().SingleAsync());
        Assert.Equal(IssueLink.MaxTextLength, row.OutwardLabel!.Length);
    }

    [Fact]
    public async Task A_link_whose_id_or_key_cannot_be_stored_is_skipped()
    {
        await ApplyAsync(true, Issue("PAY-1",
            Outward(new string('9', IssueLink.MaxLinkIdLength + 1), "PAY-2"),
            Outward("502", new string('K', IssueLink.MaxKeyLength + 1)),
            Outward("503", "PAY-3")));

        Assert.Equal(new[] { "503: PAY-1 > PAY-3" }, await RowsAsync());
    }

    [Fact]
    public async Task Nothing_to_store_is_a_success()
    {
        Assert.True(await ApplyAsync(true));
        Assert.Empty(await RowsAsync());
    }

    [Fact]
    public async Task A_link_already_stored_under_other_keys_is_rewritten_not_added_again()
    {
        await ApplyAsync(false, Issue("OLD-1", Outward("501", "PAY-2")));

        await ApplyAsync(true, Issue("PAY-1", Outward("501", "PAY-3")));

        Assert.Equal(new[] { "501: PAY-1 > PAY-3" }, await RowsAsync());
    }

    private sealed class FailingFactory : IDbContextFactory<EstimationDbContext>
    {
        private readonly InMemoryDatabase _inner;
        private readonly Exception _failure;
        private int _failuresLeft;

        public FailingFactory(InMemoryDatabase inner, Exception failure, int failures)
        {
            _inner = inner;
            _failure = failure;
            _failuresLeft = failures;
        }

        public int Calls { get; private set; }

        public EstimationDbContext CreateDbContext()
        {
            Calls++;
            if (_failuresLeft-- > 0)
            {
                throw _failure;
            }
            return _inner.CreateDbContext();
        }
    }

    [Fact]
    public async Task A_store_that_fails_once_is_tried_again()
    {
        var factory = new FailingFactory(_db, new InvalidOperationException("deadlock victim"), failures: 1);

        Assert.True(await IssueLinkStore.ApplyAsync(factory, [Issue("PAY-1", Outward("501", "PAY-2"))], prune: true));

        Assert.Equal(2, factory.Calls);
        Assert.Equal(new[] { "501: PAY-1 > PAY-2" }, await RowsAsync());
    }

    [Fact]
    public async Task A_store_that_fails_twice_reports_failure_instead_of_throwing()
    {
        var factory = new FailingFactory(_db, new DbUpdateException("unique index"), failures: 5);

        Assert.False(await IssueLinkStore.ApplyAsync(factory, [Issue("PAY-1", Outward("501", "PAY-2"))], prune: true));

        Assert.Equal(2, factory.Calls);
        Assert.Empty(await RowsAsync());
    }

    [Fact]
    public async Task A_cancelled_store_is_not_swallowed()
    {
        var factory = new FailingFactory(_db, new OperationCanceledException(), failures: 1);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            IssueLinkStore.ApplyAsync(factory, [Issue("PAY-1", Outward("501", "PAY-2"))], prune: true));

        Assert.Equal(1, factory.Calls);
    }
}

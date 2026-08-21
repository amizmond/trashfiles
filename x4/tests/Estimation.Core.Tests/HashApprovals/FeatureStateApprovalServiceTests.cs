using Estimation.Core.Administration.Audit;
using Estimation.Core.HashApprovals.Data;
using Estimation.Core.HashApprovals.Services;
using Estimation.Core.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Estimation.Core.Tests.HashApprovals;

public class FeatureStateApprovalServiceTests
{
    private const string Approver = "DOMAIN\\tester";
    private const string Art = "Payments ART";
    private const string Pi = "PI 26.2";

    private readonly InMemoryDatabase _db = new();
    private readonly FeatureStateApprovalService _service;

    public FeatureStateApprovalServiceTests()
    {
        _service = new FeatureStateApprovalService(_db, new StubAuditUser(Approver));
    }

    private sealed class StubAuditUser : IAuditUserProvider
    {
        private readonly string? _userName;

        public StubAuditUser(string? userName) => _userName = userName;

        public string? GetCurrentUserName() => _userName;
    }

    private static FeatureStateApprovalRequest Request(string featureKey = "PAY-1", string hash = "v1:aaa") =>
        new(featureKey, hash, "[{\"f\":\"Story points\",\"v\":\"8\"}]", null, featureKey, "Checkout");

    [Fact]
    public async Task Approving_records_who_approved_which_state()
    {
        var added = await _service.ApproveAsync(Art, Pi, baselineSnapshotId: 7, [Request()], "  looks fine  ");

        Assert.Equal(1, added);

        var active = await _service.GetActiveAsync(Art, Pi);
        var info = Assert.Single(active).Value;
        Assert.Equal("PAY-1", info.FeatureKey);
        Assert.Equal("v1:aaa", info.StateHash);
        Assert.Equal(Approver, info.ApprovedBy);
        Assert.Equal("looks fine", info.Comment);

        var stored = await _db.ReadAsync(db => db.FeatureStateApprovals().SingleAsync());
        Assert.Equal(7, stored.BaselineSnapshotId);
        Assert.Equal("Checkout", stored.FeatureName);
        Assert.Null(stored.WithdrawnAt);
    }

    [Fact]
    public async Task An_already_approved_state_is_not_approved_twice()
    {
        await _service.ApproveAsync(Art, Pi, null, [Request()], null);

        var added = await _service.ApproveAsync(Art, Pi, null, [Request(), Request()], null);

        Assert.Equal(0, added);
        Assert.Equal(1, await _db.ReadAsync(db => db.FeatureStateApprovals().CountAsync()));
    }

    [Fact]
    public async Task Duplicates_inside_one_request_collapse_into_one_approval()
    {
        var added = await _service.ApproveAsync(Art, Pi, null, [Request(), Request("pay-1"), Request(hash: "v1:bbb")], null);

        Assert.Equal(2, added);
    }

    [Fact]
    public async Task Lookups_ignore_the_casing_of_the_feature_key()
    {
        await _service.ApproveAsync(Art, Pi, null, [Request("PAY-1")], null);

        var active = await _service.GetActiveAsync(Art, Pi);

        Assert.True(active.ContainsKey(FeatureStateKey.Of("pay-1", "v1:aaa")));
        Assert.False(active.ContainsKey(FeatureStateKey.Of("pay-1", "v1:bbb")));
    }

    [Fact]
    public async Task Approvals_are_scoped_to_the_art_and_pi()
    {
        await _service.ApproveAsync(Art, Pi, null, [Request()], null);

        Assert.Empty(await _service.GetActiveAsync(Art, "PI 26.3"));
        Assert.Empty(await _service.GetActiveAsync("Core ART", Pi));
    }

    [Fact]
    public async Task Withdrawing_keeps_the_row_but_hides_it_from_lookups()
    {
        await _service.ApproveAsync(Art, Pi, null, [Request()], null);
        var id = Assert.Single(await _service.GetActiveAsync(Art, Pi)).Value.Id;

        Assert.True(await _service.WithdrawAsync(id));
        Assert.False(await _service.WithdrawAsync(id));

        Assert.Empty(await _service.GetActiveAsync(Art, Pi));

        var stored = await _db.ReadAsync(db => db.FeatureStateApprovals().SingleAsync());
        Assert.Equal(Approver, stored.WithdrawnBy);
        Assert.NotNull(stored.WithdrawnAt);
    }

    [Fact]
    public async Task A_withdrawn_state_can_be_approved_again_and_the_history_keeps_both()
    {
        await _service.ApproveAsync(Art, Pi, null, [Request()], null);
        await _service.WithdrawAsync(Assert.Single(await _service.GetActiveAsync(Art, Pi)).Value.Id);

        Assert.Equal(1, await _service.ApproveAsync(Art, Pi, null, [Request()], "second time"));

        var history = await _service.GetHistoryAsync(Art, Pi, "PAY-1");
        Assert.Equal(2, history.Count);
        Assert.Null(history[0].WithdrawnAt);
        Assert.Equal("second time", history[0].Comment);
        Assert.NotNull(history[1].WithdrawnAt);
    }

    [Fact]
    public async Task Approving_nothing_is_a_no_op()
    {
        Assert.Equal(0, await _service.ApproveAsync(Art, Pi, null, [], null));
        Assert.False(await _service.WithdrawAsync(123));
    }
}

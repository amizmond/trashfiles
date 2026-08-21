using System.Text.Json;
using Estimation.Core.Features.Models;
using Estimation.Core.Features.Services;
using Estimation.Core.HashApprovals.Services;
using Xunit;

namespace Estimation.Core.Tests.HashApprovals;

public class FeatureStateHasherTests
{
    private static readonly FeatureSnapshotDeltaService Delta = new();

    private static FeatureSnapshotItem Item(
        string? labels = "pi26.2, payments",
        string? teams = "Alpha, Beta",
        DateTime? targetStart = null,
        int? storyPoints = 5,
        string? summary = "Checkout redesign",
        string? acceptanceCriteria = "Given a cart") =>
        new()
        {
            FeatureId = 1,
            JiraId = "PAY-1",
            ArtName = "Payments ART",
            PiName = "PI 26.2",
            Labels = labels,
            Teams = teams,
            TargetStart = targetStart ?? new DateTime(2026, 9, 1),
            StoryPoints = storyPoints,
            Summary = summary,
            Name = "Checkout",
            AcceptanceCriteria = acceptanceCriteria,
            BusinessOutcomeJiraId = "BO-1",
            BusinessOutcomeName = "Faster checkout"
        };

    [Fact]
    public void The_same_state_always_hashes_the_same()
    {
        Assert.Equal(FeatureStateHasher.HashOf(Item()), FeatureStateHasher.HashOf(Item()));
    }

    [Fact]
    public void The_hash_carries_a_version_prefix()
    {
        Assert.StartsWith("v1:", FeatureStateHasher.HashOf(Item()));
        Assert.Equal("v1:REMOVED", FeatureStateHasher.RemovedHash);
    }

    [Theory]
    [InlineData("payments, pi26.2")]
    [InlineData("PAYMENTS,Pi26.2")]
    [InlineData("  pi26.2 ,  payments , payments ")]
    public void Label_order_casing_whitespace_and_duplicates_do_not_change_the_hash(string labels)
    {
        Assert.Equal(FeatureStateHasher.HashOf(Item()), FeatureStateHasher.HashOf(Item(labels: labels)));
    }

    [Fact]
    public void The_time_of_day_of_a_date_does_not_change_the_hash()
    {
        var morning = Item(targetStart: new DateTime(2026, 9, 1, 8, 0, 0));
        var evening = Item(targetStart: new DateTime(2026, 9, 1, 20, 30, 0));

        Assert.Equal(FeatureStateHasher.HashOf(morning), FeatureStateHasher.HashOf(evening));
    }

    [Fact]
    public void Surrounding_whitespace_of_text_does_not_change_the_hash()
    {
        Assert.Equal(FeatureStateHasher.HashOf(Item()), FeatureStateHasher.HashOf(Item(summary: "  Checkout redesign ")));
    }

    [Fact]
    public void A_changed_value_changes_the_hash()
    {
        Assert.NotEqual(FeatureStateHasher.HashOf(Item()), FeatureStateHasher.HashOf(Item(storyPoints: 8)));
        Assert.NotEqual(FeatureStateHasher.HashOf(Item()), FeatureStateHasher.HashOf(Item(acceptanceCriteria: "Given an empty cart")));
        Assert.NotEqual(FeatureStateHasher.HashOf(Item()), FeatureStateHasher.HashOf(Item(teams: "Alpha")));
    }

    [Fact]
    public void Text_comparison_is_case_sensitive_like_the_delta()
    {
        Assert.NotEqual(FeatureStateHasher.HashOf(Item()), FeatureStateHasher.HashOf(Item(summary: "checkout redesign")));
    }

    public static IEnumerable<object[]> Pairs()
    {
        yield return [Item(), Item()];
        yield return [Item(), Item(labels: "Payments,PI26.2")];
        yield return [Item(), Item(labels: "payments")];
        yield return [Item(), Item(teams: "beta , alpha")];
        yield return [Item(), Item(targetStart: new DateTime(2026, 9, 1, 23, 59, 0))];
        yield return [Item(), Item(targetStart: new DateTime(2026, 9, 2))];
        yield return [Item(), Item(storyPoints: null)];
        yield return [Item(), Item(summary: "Checkout redesign ")];
        yield return [Item(), Item(summary: "Checkout redesign!")];
        yield return [Item(), Item(acceptanceCriteria: null)];
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Hashes_are_equal_exactly_when_the_delta_reports_no_change(FeatureSnapshotItem a, FeatureSnapshotItem b)
    {
        var row = Assert.Single(Delta.Compare([a], [b]).Rows);
        var unchanged = row.Kind == FeatureDeltaChangeKind.Unchanged;

        Assert.Equal(unchanged, FeatureStateHasher.HashOf(a) == FeatureStateHasher.HashOf(b));
    }

    [Fact]
    public void A_removed_row_hashes_to_the_removal_marker_and_has_no_state()
    {
        var row = Assert.Single(Delta.Compare([Item()], []).Rows);

        Assert.Equal(FeatureDeltaChangeKind.Removed, row.Kind);
        Assert.Equal(FeatureStateHasher.RemovedHash, FeatureStateHasher.HashForRow(row));
        Assert.Null(FeatureStateHasher.StateJsonForRow(row));
    }

    [Fact]
    public void The_state_json_lists_every_compared_field_in_order()
    {
        using var json = JsonDocument.Parse(FeatureStateHasher.StateJsonOf(Item()));
        var fields = json.RootElement.EnumerateArray().Select(e => e.GetProperty("f").GetString()).ToArray();

        Assert.Equal(FeatureDeltaFields.All, fields);
        Assert.Equal(
            "PAYMENTS,PI26.2",
            json.RootElement.EnumerateArray().Single(e => e.GetProperty("f").GetString() == FeatureDeltaFields.Labels).GetProperty("v").GetString());
    }

    [Fact]
    public void The_hash_is_the_hash_of_the_state_json()
    {
        var item = Item();

        Assert.Equal(FeatureStateHasher.HashOfStateJson(FeatureStateHasher.StateJsonOf(item)), FeatureStateHasher.HashOf(item));
    }
}

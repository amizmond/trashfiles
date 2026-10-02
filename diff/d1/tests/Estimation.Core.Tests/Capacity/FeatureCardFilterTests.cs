using Estimation.Core.Features.Models;
using Xunit;

namespace Estimation.Core.Tests.Capacity;

public class FeatureCardFilterTests
{
    private sealed record Row : IFeatureFilterRow
    {
        public string? JiraId { get; init; }
        public string? Name { get; init; }
        public string? Summary { get; init; }
        public string? BusinessOutcomeJiraId { get; init; }
        public string? BusinessOutcomeSummary { get; init; }
        public string? PortfolioEpicJiraId { get; init; }
        public string? PortfolioEpicSummary { get; init; }
        public IReadOnlyList<FeatureFilterRef> StrategicObjectives { get; init; } = Array.Empty<FeatureFilterRef>();
        public string? Status { get; init; }
        public string? RagStatus { get; init; }
        public string? Labels { get; init; }
        public string? RequirementStatus { get; init; }
        public string? TechnicalApproval { get; init; }
        public string? FundingStatus { get; init; }
        public int? HygieneFailureCount { get; init; }
    }

    private static Row Full => new()
    {
        JiraId = "FEA-1",
        Name = "Payments rewrite",
        Summary = "Replace the legacy gateway",
        BusinessOutcomeJiraId = "BO-9",
        BusinessOutcomeSummary = "Faster checkout",
        PortfolioEpicJiraId = "PE-4",
        PortfolioEpicSummary = "Commerce platform",
        StrategicObjectives = new[] { new FeatureFilterRef("SO-2", "Grow revenue") },
        Status = "In Progress",
        RagStatus = "Amber",
        Labels = "pi-2026-1, payments",
        RequirementStatus = "Ready",
        TechnicalApproval = "Approved",
        FundingStatus = "Funded",
    };

    private static Row Empty => new() { JiraId = "FEA-2", Name = "Bare feature" };

    [Fact]
    public void Empty_filter_keeps_everything()
    {
        var filter = new FeatureCardFilter();

        Assert.True(filter.Matches(Full));
        Assert.True(filter.Matches(Empty));
        Assert.False(filter.IsActive);
        Assert.Equal(0, filter.ActiveCount);
    }

    [Fact]
    public void Selection_keeps_only_matching_rows()
    {
        var filter = new FeatureCardFilter { Statuses = new[] { "In Progress" } };

        Assert.True(filter.Matches(Full));
        Assert.False(filter.Matches(Empty));
        Assert.False(filter.Matches(Full with { Status = "Done" }));
    }

    [Fact]
    public void Not_with_a_selection_drops_matching_rows_but_keeps_the_rest()
    {
        var filter = new FeatureCardFilter { Statuses = new[] { "In Progress" }, StatusNot = true };

        Assert.False(filter.Matches(Full));
        Assert.True(filter.Matches(Full with { Status = "Done" }));

        Assert.True(filter.Matches(Empty));
    }

    [Fact]
    public void Not_with_no_selection_keeps_only_rows_missing_that_value()
    {
        var filter = new FeatureCardFilter { StatusNot = true };

        Assert.True(filter.Matches(Empty));
        Assert.False(filter.Matches(Full));
        Assert.True(filter.Matches(Full with { Status = "   " }));
    }

    [Fact]
    public void Matching_is_case_insensitive()
    {
        var filter = new FeatureCardFilter { RequirementStatuses = new[] { "READY" } };

        Assert.True(filter.Matches(Full));
    }

    [Fact]
    public void Technical_approval_filters_like_the_other_single_value_dimensions()
    {
        Assert.True(new FeatureCardFilter { TechnicalApprovals = new[] { "approved" } }.Matches(Full));
        Assert.False(new FeatureCardFilter { TechnicalApprovals = new[] { "Approved" } }.Matches(Empty));
        Assert.False(new FeatureCardFilter { TechnicalApprovals = new[] { "Required approve" } }.Matches(Full));
        Assert.True(new FeatureCardFilter { TechnicalApprovalNot = true }.Matches(Empty));
        Assert.False(new FeatureCardFilter { TechnicalApprovalNot = true }.Matches(Full));
    }

    [Fact]
    public void Rag_status_filters_like_the_other_single_value_dimensions()
    {
        Assert.True(new FeatureCardFilter { RagStatuses = new[] { "Amber", "Red" } }.Matches(Full));
        Assert.False(new FeatureCardFilter { RagStatuses = new[] { "Green" } }.Matches(Full));
        Assert.False(new FeatureCardFilter { RagStatuses = new[] { "Amber" } }.Matches(Empty));
        Assert.False(new FeatureCardFilter { RagStatuses = new[] { "Amber" }, RagStatusNot = true }.Matches(Full));
        Assert.True(new FeatureCardFilter { RagStatusNot = true }.Matches(Empty));
        Assert.False(new FeatureCardFilter { RagStatusNot = true }.Matches(Full));
        Assert.Equal(1, new FeatureCardFilter { RagStatuses = new[] { "Amber" } }.ActiveCount);
    }

    [Fact]
    public void Labels_match_any_single_label_of_the_comma_separated_list()
    {
        Assert.True(new FeatureCardFilter { Labels = new[] { "payments" } }.Matches(Full));
        Assert.True(new FeatureCardFilter { Labels = new[] { "pi-2026-1" } }.Matches(Full));
        Assert.False(new FeatureCardFilter { Labels = new[] { "pi-2026" } }.Matches(Full));
    }

    [Fact]
    public void Not_labels_with_no_selection_keeps_only_unlabelled_rows()
    {
        var filter = new FeatureCardFilter { LabelsNot = true };

        Assert.False(filter.Matches(Full));
        Assert.True(filter.Matches(Empty));
        Assert.True(filter.Matches(Full with { Labels = "" }));
    }

    [Fact]
    public void Strategic_objectives_match_any_of_a_features_several_objectives()
    {
        var many = Full with
        {
            StrategicObjectives = new[]
            {
                new FeatureFilterRef("SO-2", "Grow revenue"),
                new FeatureFilterRef("SO-7", "Cut cost"),
            },
        };

        Assert.True(new FeatureCardFilter { StrategicObjectives = new[] { "SO-7" } }.Matches(many));
        Assert.False(new FeatureCardFilter { StrategicObjectives = new[] { "SO-7" }, StrategicObjectiveNot = true }.Matches(many));
        Assert.True(new FeatureCardFilter { StrategicObjectiveNot = true }.Matches(Empty));
        Assert.False(new FeatureCardFilter { StrategicObjectiveNot = true }.Matches(many));
    }

    [Fact]
    public void Dimensions_combine_with_and()
    {
        var filter = new FeatureCardFilter
        {
            Statuses = new[] { "In Progress" },
            FundingStatuses = new[] { "Unfunded" },
        };

        Assert.False(filter.Matches(Full));
        Assert.True(filter.Matches(Full with { FundingStatus = "Unfunded" }));
    }

    [Fact]
    public void Search_spans_jira_id_name_summary_and_the_hierarchy()
    {
        Assert.True(new FeatureCardFilter { Search = "fea-1" }.Matches(Full));
        Assert.True(new FeatureCardFilter { Search = "payments rewrite" }.Matches(Full));
        Assert.True(new FeatureCardFilter { Search = "legacy" }.Matches(Full));
        Assert.True(new FeatureCardFilter { Search = "BO-9" }.Matches(Full));
        Assert.True(new FeatureCardFilter { Search = "Commerce" }.Matches(Full));
        Assert.False(new FeatureCardFilter { Search = "nothing here" }.Matches(Full));
    }

    [Fact]
    public void Active_count_counts_each_narrowing_dimension_once()
    {
        var filter = new FeatureCardFilter
        {
            Search = "pay",
            Statuses = new[] { "In Progress", "Done" },
            LabelsNot = true,
        };

        Assert.Equal(3, filter.ActiveCount);
        Assert.True(filter.IsActive);
    }

    [Fact]
    public void Clear_resets_every_dimension()
    {
        var filter = new FeatureCardFilter
        {
            Search = "pay",
            Statuses = new[] { "In Progress" },
            StatusNot = true,
            RagStatuses = new[] { "Red" },
            RagStatusNot = true,
            Labels = new[] { "payments" },
            LabelsNot = true,
            BusinessOutcomes = new[] { "BO-9" },
            PortfolioEpics = new[] { "PE-4" },
            StrategicObjectives = new[] { "SO-2" },
            RequirementStatuses = new[] { "Ready" },
            TechnicalApprovals = new[] { "Approved" },
            TechnicalApprovalNot = true,
            FundingStatuses = new[] { "Funded" },
        };

        filter.Clear();

        Assert.False(filter.IsActive);
        Assert.True(filter.Matches(Full));
        Assert.True(filter.Matches(Empty));
    }

    [Fact]
    public void Hygiene_tells_healthy_unhealthy_and_never_checked_apart()
    {
        var healthy = Full with { JiraId = "FEA-5", HygieneFailureCount = 0 };
        var unhealthy = Full with { JiraId = "FEA-6", HygieneFailureCount = 2 };
        var unchecked_ = Full with { JiraId = "FEA-7" };

        var rows = new List<Row> { healthy, unhealthy, unchecked_ };

        Assert.Equal(["FEA-5"], Filtered(rows, FeatureCardFilter.HygieneHealthy));
        Assert.Equal(["FEA-6"], Filtered(rows, FeatureCardFilter.HygieneUnhealthy));
        Assert.Equal(["FEA-7"], Filtered(rows, FeatureCardFilter.HygieneUnchecked));

        var none = new FeatureCardFilter();
        Assert.Equal(3, none.Apply(rows).Count);
        Assert.Equal(0, none.ActiveCount);
        Assert.Equal(1, new FeatureCardFilter { Hygiene = FeatureCardFilter.HygieneHealthy }.ActiveCount);
    }

    private static List<string?> Filtered(List<Row> rows, string hygiene) =>
        new FeatureCardFilter { Hygiene = hygiene }.Apply(rows).Select(r => r.JiraId).ToList();

    [Fact]
    public void Apply_preserves_the_incoming_order()
    {
        var rows = new List<Row>
        {
            Full with { JiraId = "FEA-3" },
            Empty,
            Full with { JiraId = "FEA-4" },
        };

        var result = new FeatureCardFilter { Statuses = new[] { "In Progress" } }.Apply(rows);

        Assert.Equal(new[] { "FEA-3", "FEA-4" }, result.Select(r => r.JiraId));
    }
}

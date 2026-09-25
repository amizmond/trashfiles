using Estimation.Core.Features.Models;
using Estimation.Core.JiraIntegration.Client;
using Estimation.Core.JiraIntegration.Client.JiraSync;
using Estimation.Core.JiraIntegration.Models;
using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Resources.Models;
using Estimation.Core.Tests.Infrastructure;
using Estimation.Core.Train.Models;
using Estimation.Core.Train.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Estimation.Core.Tests.JiraIntegration;

public class FeaturePiConverterTests
{
    private readonly InMemoryDatabase _database = new();
    private readonly FeaturePiConverter _converter = new();
    private readonly List<string> _warnings = new();
    private readonly Pi _knownPi = new() { Id = 7, Name = "PI 25.3" };
    private readonly Dictionary<string, Pi> _pisByName = new(StringComparer.OrdinalIgnoreCase);

    public FeaturePiConverterTests()
    {
        _pisByName[_knownPi.Name] = _knownPi;
    }

    private async Task<(Feature Feature, EstimationDbContext Db)> ApplyAsync(
        string? planningIncrement, Action<Feature>? configure = null)
    {
        var feature = new Feature { JiraId = "ATL-1" };
        configure?.Invoke(feature);

        var db = _database.CreateDbContext();
        await _converter.ApplyAsync(
            feature,
            new JiraIssueResponse { Key = "ATL-1", PlanningIncrement = planningIncrement },
            Context(db),
            CancellationToken.None);

        return (feature, db);
    }

    private JiraSyncConvertContext Context(EstimationDbContext db) => new()
    {
        Db = db,
        Matcher = ArtMatcher.Empty,
        Teams = Array.Empty<Team>(),
        PisByName = _pisByName,
        Warnings = _warnings,
    };

    [Fact]
    public async Task A_pi_the_tool_knows_by_name_is_linked_to_the_feature()
    {
        var (feature, db) = await ApplyAsync("PI 25.3");
        await using var _ = db;

        Assert.Equal(7, feature.PiId);
        Assert.Same(_knownPi, feature.Pi);
        Assert.Empty(_warnings);
    }

    [Theory]
    [InlineData("pi 25.3")]
    [InlineData("  PI 25.3  ")]
    public async Task The_name_is_matched_ignoring_case_and_surrounding_space(string jiraValue)
    {
        var (feature, db) = await ApplyAsync(jiraValue);
        await using var _ = db;

        Assert.Equal(7, feature.PiId);
    }

    [Fact]
    public async Task A_pi_name_the_tool_does_not_have_yet_is_created_and_linked()
    {
        var (feature, db) = await ApplyAsync("PI 99.9");
        await using var _ = db;

        var added = Assert.Single(db.ChangeTracker.Entries<Pi>().Where(e => e.State == EntityState.Added));
        Assert.Equal("PI 99.9", added.Entity.Name);
        Assert.Same(added.Entity, feature.Pi);
        Assert.Empty(_warnings);
    }

    [Fact]
    public async Task A_pi_created_for_one_feature_is_reused_by_the_next_one_in_the_batch()
    {
        var (first, db) = await ApplyAsync("PI 99.9");
        await using var _ = db;
        var (second, secondDb) = await ApplyAsync("pi 99.9");
        await using var __ = secondDb;

        Assert.Same(first.Pi, second.Pi);
        Assert.DoesNotContain(secondDb.ChangeTracker.Entries<Pi>(), e => e.State == EntityState.Added);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_planning_increment_clears_the_pi(string? jiraValue)
    {
        var (feature, db) = await ApplyAsync(jiraValue, f =>
        {
            f.PiId = 7;
            f.Pi = _knownPi;
        });
        await using var _ = db;

        Assert.Null(feature.PiId);
        Assert.Null(feature.Pi);
        Assert.Empty(_warnings);
    }

    [Fact]
    public async Task An_entity_that_is_not_a_feature_is_left_alone()
    {
        await using var db = _database.CreateDbContext();
        var businessOutcome = new BusinessOutcome { JiraId = "ATL-2" };

        await _converter.ApplyAsync(
            businessOutcome,
            new JiraIssueResponse { Key = "ATL-2", PlanningIncrement = "PI 99.9" },
            Context(db),
            CancellationToken.None);

        Assert.Empty(_warnings);
    }
}

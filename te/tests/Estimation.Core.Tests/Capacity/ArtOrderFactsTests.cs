using Estimation.Core.Capacity.Services;
using Estimation.Core.Tests.Infrastructure;
using Estimation.Core.Train.Models;
using Xunit;
using static Estimation.Core.Tests.Capacity.ArtOrderTestData;

namespace Estimation.Core.Tests.Capacity;

public class ArtOrderFactsTests
{
    private const ArtPrioritization Pe = ArtPrioritization.PortfolioEpic;
    private const ArtPrioritization Bo = ArtPrioritization.BusinessOutcome;

    private readonly InMemoryDatabase _db = new();

    private Task<ArtOrderFacts> LoadAsync(ArtPrioritization level, params int?[] scopes) =>
        _db.ReadAsync(async db => (await ArtOrderLoader.LoadAsync(db, CorePayments, level, scopes))!);

    private static IReadOnlyList<int> RelevantIds(ArtOrderFacts facts, int? piId) =>
        facts.Relevant(piId).Select(i => i.ItemId).Order().ToList();

    [Fact]
    public async Task A_pi_holds_the_features_planned_on_the_art_teams_and_the_ones_saved_on_their_boards()
    {
        await SeedAsync(_db);

        var facts = await LoadAsync(Pe, CurrentPi);

        var features = facts.FeaturesByItem(CurrentPi);
        Assert.Equal([ArtBoardItems.None, 1, 2, PastOnly], features.Keys.Order());
        Assert.Equal([1], features[1]);
        Assert.Equal([2], features[2]);
        Assert.Equal([4, 5], features[ArtBoardItems.None].Order());
        Assert.Equal([9], features[PastOnly]);
        Assert.DoesNotContain(OtherArtOnly, features.Keys);
    }

    [Fact]
    public async Task General_holds_features_of_pis_that_have_not_ended_and_features_without_a_pi()
    {
        await SeedAsync(_db);

        var facts = await LoadAsync(Pe, [null]);

        var features = facts.FeaturesByItem(null);
        Assert.Equal([ArtBoardItems.None, 1, 2, 3], features.Keys.Order());
        Assert.Equal([1, 8], features[1].Order());
        Assert.Equal([ArtBoardItems.None, 1, 2, 3], RelevantIds(facts, null));
    }

    [Fact]
    public async Task Business_outcome_level_groups_the_features_by_their_outcome()
    {
        await SeedAsync(_db, corePayments: Bo);

        var facts = await LoadAsync(Bo, CurrentPi);

        Assert.Equal([ArtBoardItems.None, 1, 2, 4, 7], facts.FeaturesByItem(CurrentPi).Keys.Order());
        Assert.Equal([4], facts.FeaturesByItem(CurrentPi)[ArtBoardItems.None]);
    }

    [Fact]
    public async Task Saved_items_without_any_feature_on_the_art_teams_are_relevant_in_every_pi()
    {
        await SeedAsync(_db);
        await SaveRowsAsync(_db, CorePayments, null, Pe, (1, 1, true), (Featureless, 2, true), (OtherArtOnly, 3, true), (PastOnly, 4, true));

        var facts = await LoadAsync(Pe, null, CurrentPi, NextPi);

        Assert.Equal([Featureless, OtherArtOnly], facts.FeaturelessItems.Order());
        Assert.Equal([ArtBoardItems.None, 1, 2, Featureless, OtherArtOnly, PastOnly], RelevantIds(facts, CurrentPi));
        Assert.Equal([3, Featureless, OtherArtOnly], RelevantIds(facts, NextPi));
        Assert.Equal([ArtBoardItems.None, 1, 2, 3, Featureless, OtherArtOnly, PastOnly], RelevantIds(facts, null));
    }

    [Fact]
    public async Task A_featureless_item_saved_only_in_one_pi_is_relevant_only_there()
    {
        await SeedAsync(_db);
        await SaveRowsAsync(_db, CorePayments, NextPi, Pe, (Featureless, 1, true));

        var facts = await LoadAsync(Pe, CurrentPi, NextPi);

        Assert.DoesNotContain(Featureless, RelevantIds(facts, CurrentPi));
        Assert.Contains(Featureless, RelevantIds(facts, NextPi));
    }

    [Fact]
    public async Task Rows_of_another_level_are_ignored()
    {
        await SeedAsync(_db);
        await SaveRowsAsync(_db, CorePayments, null, Bo, (1, 1, true));
        await SaveRowsAsync(_db, CorePayments, CurrentPi, Bo, (2, 1, true));

        var facts = await LoadAsync(Pe, CurrentPi);

        Assert.False(facts.HasGeneralRows);
        Assert.False(facts.HasOwnRows(CurrentPi));
        Assert.Equal(ArtOrderMode.Empty, facts.Resolve(CurrentPi).Mode);
    }

    [Fact]
    public async Task New_items_are_ordered_by_jira_key_with_the_bucket_last()
    {
        await SeedAsync(_db);
        await SaveRowsAsync(_db, CorePayments, null, Pe, (3, 1, true));

        var facts = await LoadAsync(Pe, CurrentPi);
        var resolution = facts.Resolve(CurrentPi);

        Assert.Equal(ArtOrderMode.FollowsGeneral, resolution.Mode);
        Assert.Equal([1, 2, PastOnly, ArtBoardItems.None], resolution.Entries.Select(e => e.ItemId));
        Assert.All(resolution.Entries, e => Assert.Equal(ArtOrderOrigin.New, e.Origin));
    }

    [Fact]
    public async Task An_art_without_teams_has_only_its_saved_items()
    {
        await SeedAsync(_db, mobile: Pe);
        await SaveRowsAsync(_db, Mobile, null, Pe, (2, 1, true), (Featureless, 2, false));

        var facts = await _db.ReadAsync(async db => (await ArtOrderLoader.LoadAsync(db, Mobile, Pe, [null, CurrentPi]))!);

        Assert.Equal([2, Featureless], facts.Resolve(null).Entries.Select(e => e.ItemId));
        Assert.Equal([2, Featureless], facts.Resolve(CurrentPi).Entries.Select(e => e.ItemId));
        Assert.Equal([true, false], facts.Resolve(CurrentPi).Entries.Select(e => e.IsIncluded));
    }

    [Fact]
    public async Task Freeze_copies_the_follows_general_order_of_locked_pis_with_art_features()
    {
        await SeedAsync(_db);
        await SaveRowsAsync(_db, CorePayments, null, Pe, (3, 1, true), (1, 2, true), (PastOnly, 3, false), (Featureless, 4, true));
        await SaveRowsAsync(_db, CorePayments, OwnPi, Pe, (2, 1, true));

        var facts = await LoadAsync(Pe, PastPi, CurrentPi, NextPi, QuietPi, OwnPi);
        var frozen = facts.LockedPiFreeze([PastPi, NextPi, QuietPi, OwnPi]).ToDictionary(f => f.PiId, f => f.Resolution);

        Assert.Equal([PastPi, NextPi], frozen.Keys);
        Assert.Equal(
            [(Featureless, 1, true), (PastOnly, 2, false)],
            frozen[PastPi].Entries.Select(e => (e.ItemId, e.Rank, e.IsIncluded)));
        Assert.Equal(
            [(3, 1, true), (Featureless, 2, true)],
            frozen[NextPi].Entries.Select(e => (e.ItemId, e.Rank, e.IsIncluded)));
    }

    [Fact]
    public async Task Without_a_general_order_locked_pis_freeze_their_items_above_the_line_in_jira_key_order()
    {
        await SeedAsync(_db);

        var facts = await LoadAsync(Pe, PastPi, CurrentPi, NextPi, QuietPi);
        var frozen = facts.LockedPiFreeze([PastPi, NextPi, QuietPi]).ToDictionary(f => f.PiId, f => f.Resolution);

        Assert.Equal([PastPi, NextPi], frozen.Keys);
        Assert.Equal([(PastOnly, 1, true)], frozen[PastPi].Entries.Select(e => (e.ItemId, e.Rank, e.IsIncluded)));
        Assert.Equal([(3, 1, true)], frozen[NextPi].Entries.Select(e => (e.ItemId, e.Rank, e.IsIncluded)));
    }

    [Fact]
    public async Task A_pi_also_holds_the_features_its_label_rule_links_to_it()
    {
        await SeedAsync(_db);
        await _db.SeedAsync(db =>
        {
            var pi = db.Pis.Single(p => p.Id == NextPi);
            pi.FeatureLabels = "carry-over";
            db.Features.Single(f => f.Id == 2).Labels = "Carry-Over, other";
            db.Features.Single(f => f.Id == 6).Labels = "carry-over";
        });

        var facts = await LoadAsync(Pe, CurrentPi, NextPi);

        Assert.Equal([2, 3], facts.FeaturesByItem(NextPi).Keys.Order());
        Assert.Equal([2], facts.FeaturesByItem(NextPi)[2]);
        Assert.DoesNotContain(OtherArtOnly, facts.FeaturesByItem(NextPi).Keys);
        Assert.Equal([ArtBoardItems.None, 1, 2, PastOnly], facts.FeaturesByItem(CurrentPi).Keys.Order());
    }
}

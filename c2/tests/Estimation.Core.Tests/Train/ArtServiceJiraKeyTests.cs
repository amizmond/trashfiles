using Estimation.Core.Features.Models;
using Estimation.Core.JiraIntegration.Models;
using Estimation.Core.Resources.Models;
using Estimation.Core.Tests.Infrastructure;
using Estimation.Core.Train.Models;
using Estimation.Core.Train.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Estimation.Core.Tests.Train;

public class ArtServiceJiraKeyTests
{
    private readonly InMemoryDatabase _db = new();
    private readonly ArtService _service;

    public ArtServiceJiraKeyTests()
    {
        _service = new ArtService(_db);
    }

    private static List<ArtJiraKey> Keys(params string[] keys) =>
        keys.Select(k => new ArtJiraKey { JiraKey = k }).ToList();

    private Task<List<string>> StoredKeysAsync(int artId) =>
        _db.ReadAsync(db => db.CapitalProjectJiraKeys
            .Where(k => k.CapitalProjectId == artId)
            .OrderBy(k => k.Id)
            .Select(k => k.JiraKey)
            .ToListAsync());

    [Fact]
    public async Task Creating_an_art_stores_its_keys_normalized_and_distinct()
    {
        var art = await _service.CreateAsync(new Art { Name = "Payments", JiraKeys = Keys(" pay ", "CARD", "Pay", "") });

        Assert.Equal(new[] { "PAY", "CARD" }, await StoredKeysAsync(art.Id));
    }

    [Fact]
    public async Task An_invalid_key_is_rejected()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CreateAsync(new Art { Name = "Payments", JiraKeys = Keys("PAY-1") }));

        Assert.Contains("PAY-1", ex.Message);
        Assert.Empty(await _db.ReadAsync(db => db.CapitalProjects.ToListAsync()));
    }

    [Fact]
    public async Task A_key_longer_than_the_column_is_rejected()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CreateAsync(new Art { Name = "Payments", JiraKeys = Keys("ABCDEFGHIJK") }));
    }

    [Fact]
    public async Task A_key_another_art_takes_without_filters_cannot_be_added_without_filters()
    {
        await _service.CreateAsync(new Art { Name = "Payments", JiraKeys = Keys("PAY") });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CreateAsync(new Art { Name = "Cards", JiraKeys = Keys("CARD", "pay") }));

        Assert.Contains("Jira key PAY is already used without components or labels by ART 'Payments'", ex.Message);
    }

    [Fact]
    public async Task Updating_adds_and_removes_keys()
    {
        var art = await _service.CreateAsync(new Art { Name = "Payments", JiraKeys = Keys("PAY", "CARD") });

        var loaded = (await _service.GetByIdAsync(art.Id))!;
        loaded.JiraKeys = [loaded.JiraKeys.Single(k => k.JiraKey == "PAY"), new ArtJiraKey { JiraKey = "atm" }];
        await _service.UpdateAsync(loaded);

        Assert.Equal(new[] { "PAY", "ATM" }, await StoredKeysAsync(art.Id));
    }

    [Fact]
    public async Task A_key_shared_before_multi_key_support_does_not_block_saving_the_art()
    {
        await _db.SeedAsync(db =>
        {
            db.CapitalProjects.Add(new Art { Id = 1, Name = "Basel", JiraKeys = Keys("BSL") });
            db.CapitalProjects.Add(new Art { Id = 2, Name = "Basel copy", JiraKeys = Keys("BSL") });
        });

        var loaded = (await _service.GetByIdAsync(2))!;
        loaded.Name = "Basel renamed";
        await _service.UpdateAsync(loaded);

        Assert.Equal("Basel renamed", (await _service.GetByIdAsync(2))!.Name);
        Assert.Equal(new[] { "BSL" }, await StoredKeysAsync(2));
    }

    [Fact]
    public async Task Updating_saves_the_department()
    {
        await _db.SeedAsync(db =>
        {
            db.Departments.Add(new Department { Id = 5, Name = "Retail" });
            db.CapitalProjects.Add(new Art { Id = 1, Name = "Payments" });
        });

        var loaded = (await _service.GetByIdAsync(1))!;
        loaded.DepartmentId = 5;
        await _service.UpdateAsync(loaded);

        Assert.Equal(5, (await _service.GetByIdAsync(1))!.DepartmentId);
    }

    private static readonly DateTime Watermark = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    private static JiraSyncKey SyncKey(string key) =>
        new() { JiraKey = key, CreateNotExisted = true, UpdateExisted = true, LastSyncedWatermarkUtc = Watermark };

    private Task<Dictionary<string, DateTime?>> WatermarksAsync() =>
        _db.ReadAsync(db => db.JiraSyncKeys.ToDictionaryAsync(k => k.JiraKey, k => k.LastSyncedWatermarkUtc));

    private Task<List<string>> SyncKeyNamesAsync() =>
        _db.ReadAsync(db => db.JiraSyncKeys.Select(k => k.JiraKey).OrderBy(k => k).ToListAsync());

    [Fact]
    public async Task Adding_a_key_to_an_art_restarts_only_that_keys_jira_sync()
    {
        await _db.SeedAsync(db =>
        {
            db.CapitalProjects.Add(new Art { Id = 1, Name = "Payments", JiraKeys = Keys("PAY") });
            db.JiraSyncKeys.Add(SyncKey("PAY"));
            db.JiraSyncKeys.Add(SyncKey("CARD"));
        });

        var loaded = (await _service.GetByIdAsync(1))!;
        loaded.Name = "Payments & Cards";
        await _service.UpdateAsync(loaded);
        Assert.All((await WatermarksAsync()).Values, w => Assert.Equal(Watermark, w));

        loaded = (await _service.GetByIdAsync(1))!;
        loaded.JiraKeys.Add(new ArtJiraKey { JiraKey = "card" });
        await _service.UpdateAsync(loaded);

        var watermarks = await WatermarksAsync();
        Assert.Equal(Watermark, watermarks["PAY"]);
        Assert.Null(watermarks["CARD"]);
    }

    private static ArtJiraKey Row(string key, string? components = null, string? labels = null) =>
        new() { JiraKey = key, Components = components, Labels = labels };

    private Task<List<ArtJiraKey>> StoredRowsAsync(int artId) =>
        _db.ReadAsync(db => db.CapitalProjectJiraKeys
            .Where(k => k.CapitalProjectId == artId)
            .OrderBy(k => k.Id)
            .ToListAsync());

    [Fact]
    public async Task Filters_are_stored_trimmed_distinct_and_comma_joined()
    {
        var art = await _service.CreateAsync(new Art { Name = "Cards", JiraKeys = [Row("pay", " Cards , cards,Debit ", "")] });

        var row = Assert.Single(await StoredRowsAsync(art.Id));
        Assert.Equal("PAY", row.JiraKey);
        Assert.Equal("Cards,Debit", row.Components);
        Assert.Null(row.Labels);
    }

    [Fact]
    public async Task A_shared_key_is_allowed_when_filters_tell_the_arts_apart()
    {
        await _service.CreateAsync(new Art { Name = "Payments", JiraKeys = [Row("PAY")] });
        await _service.CreateAsync(new Art { Name = "Cards", JiraKeys = [Row("PAY", components: "Cards")] });
        await _service.CreateAsync(new Art { Name = "Web", JiraKeys = [Row("PAY", labels: "web")] });

        Assert.Equal(3, await _db.ReadAsync(db => db.CapitalProjectJiraKeys.CountAsync(k => k.JiraKey == "PAY")));
    }

    [Fact]
    public async Task Two_arts_cannot_use_the_same_filters_on_a_key()
    {
        await _service.CreateAsync(new Art { Name = "Cards", JiraKeys = [Row("PAY", "Cards", "retail")] });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CreateAsync(new Art { Name = "Cards 2", JiraKeys = [Row("PAY", "cards", "Retail")] }));

        Assert.Contains("Jira key PAY already has the same components and labels on ART 'Cards'", ex.Message);
    }

    [Fact]
    public async Task Removing_the_filters_is_refused_when_another_art_already_takes_the_whole_key()
    {
        await _service.CreateAsync(new Art { Name = "Payments", JiraKeys = [Row("PAY")] });
        var cards = await _service.CreateAsync(new Art { Name = "Cards", JiraKeys = [Row("PAY", "Cards")] });

        var loaded = (await _service.GetByIdAsync(cards.Id))!;
        loaded.JiraKeys[0].Components = null;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UpdateAsync(loaded));
        Assert.Equal("Cards", Assert.Single(await StoredRowsAsync(cards.Id)).Components);
    }

    [Fact]
    public async Task Updating_the_filters_of_a_kept_key_keeps_its_jira_sync_watermark()
    {
        await _db.SeedAsync(db =>
        {
            db.CapitalProjects.Add(new Art { Id = 1, Name = "Cards", JiraKeys = [Row("PAY", "Cards")] });
            db.JiraSyncKeys.Add(SyncKey("PAY"));
        });

        var loaded = (await _service.GetByIdAsync(1))!;
        loaded.JiraKeys[0].Components = "Cards,Debit";
        loaded.JiraKeys[0].Labels = "retail";
        await _service.UpdateAsync(loaded);

        var row = Assert.Single(await StoredRowsAsync(1));
        Assert.Equal("Cards,Debit", row.Components);
        Assert.Equal("retail", row.Labels);
        Assert.Equal(Watermark, (await WatermarksAsync())["PAY"]);
    }

    private Task SeedSharedKeyArtsAsync() =>
        _db.SeedAsync(db =>
        {
            db.CapitalProjects.Add(new Art { Id = 1, Name = "Payments", JiraKeys = [Row("PAY")] });
            db.CapitalProjects.Add(new Art { Id = 2, Name = "Cards", JiraKeys = [Row("PAY", "Cards"), Row("CARD")] });
            db.CapitalProjects.Add(new Art { Id = 3, Name = "Loans", JiraKeys = [Row("LOAN")] });
            foreach (var key in new[] { "PAY", "CARD", "LOAN" })
            {
                db.JiraSyncKeys.Add(SyncKey(key));
            }
        });

    [Fact]
    public async Task Removing_a_shared_key_from_an_art_restarts_only_that_keys_jira_sync()
    {
        await SeedSharedKeyArtsAsync();

        var loaded = (await _service.GetByIdAsync(2))!;
        loaded.JiraKeys = loaded.JiraKeys.Where(k => k.JiraKey == "CARD").ToList();
        await _service.UpdateAsync(loaded);

        var watermarks = await WatermarksAsync();
        Assert.Null(watermarks["PAY"]);
        Assert.Equal(Watermark, watermarks["CARD"]);
        Assert.Equal(Watermark, watermarks["LOAN"]);
    }

    [Fact]
    public async Task Refiltering_a_shared_key_keeps_every_jira_sync_watermark()
    {
        await SeedSharedKeyArtsAsync();

        var loaded = (await _service.GetByIdAsync(2))!;
        loaded.JiraKeys.Single(k => k.JiraKey == "PAY").Components = "Debit";
        await _service.UpdateAsync(loaded);

        Assert.Equal("Debit", (await StoredRowsAsync(2)).Single(r => r.JiraKey == "PAY").Components);
        Assert.All((await WatermarksAsync()).Values, w => Assert.Equal(Watermark, w));
    }

    [Fact]
    public async Task Deleting_an_art_restarts_the_jira_sync_of_all_its_keys_and_keeps_their_rows()
    {
        await SeedSharedKeyArtsAsync();

        Assert.True(await _service.DeleteAsync(2));

        var watermarks = await WatermarksAsync();
        Assert.Null(watermarks["PAY"]);
        Assert.Null(watermarks["CARD"]);
        Assert.Equal(Watermark, watermarks["LOAN"]);
        Assert.Equal(new[] { "CARD", "LOAN", "PAY" }, await SyncKeyNamesAsync());
        Assert.Empty(await StoredRowsAsync(2));
    }

    [Fact]
    public async Task Deleting_an_art_leaves_the_jira_sync_of_other_keys_alone()
    {
        await SeedSharedKeyArtsAsync();

        Assert.True(await _service.DeleteAsync(3));

        var watermarks = await WatermarksAsync();
        Assert.Null(watermarks["LOAN"]);
        Assert.Equal(Watermark, watermarks["PAY"]);
        Assert.Equal(Watermark, watermarks["CARD"]);
        Assert.Equal(new[] { "CARD", "LOAN", "PAY" }, await SyncKeyNamesAsync());
    }

    [Fact]
    public async Task Renaming_an_art_that_changes_no_key_restarts_no_jira_sync()
    {
        await SeedSharedKeyArtsAsync();

        var loaded = (await _service.GetByIdAsync(3))!;
        loaded.Name = "Loans & Mortgages";
        await _service.UpdateAsync(loaded);

        loaded = (await _service.GetByIdAsync(1))!;
        loaded.Description = "Whole PAY key";
        await _service.UpdateAsync(loaded);

        Assert.All((await WatermarksAsync()).Values, w => Assert.Equal(Watermark, w));
    }

    [Fact]
    public async Task Updating_without_keys_keeps_the_stored_rows_even_when_the_page_had_older_ones()
    {
        await SeedSharedKeyArtsAsync();
        var stale = (await _service.GetByIdAsync(2))!;

        var admin = (await _service.GetByIdAsync(2))!;
        admin.JiraKeys.Single(k => k.JiraKey == "PAY").Components = "Debit";
        await _service.UpdateAsync(admin);

        stale.Name = "Cards & Debit";
        stale.JiraKeys.Add(Row("ATM"));
        stale.JiraKeys.Remove(stale.JiraKeys.Single(k => k.JiraKey == "CARD"));
        await _service.UpdateAsync(stale, updateKeys: false);

        var rows = await StoredRowsAsync(2);
        Assert.Equal(new[] { "CARD", "PAY" }, rows.Select(r => r.JiraKey).Order());
        Assert.Equal("Debit", rows.Single(r => r.JiraKey == "PAY").Components);
        Assert.Equal("Cards & Debit", (await _service.GetByIdAsync(2))!.Name);
        Assert.All((await WatermarksAsync()).Values, w => Assert.Equal(Watermark, w));
    }

    [Fact]
    public async Task Feature_match_facts_cover_every_key_asked_for()
    {
        await _db.SeedAsync(db =>
        {
            db.Features.Add(new Feature { Id = 1, ProjectKey = "PAY", JiraId = "PAY-1", Labels = "web", Components = "Cards", Summary = "a" });
            db.Features.Add(new Feature { Id = 2, JiraId = "CARD-2", Summary = "b" });
            db.Features.Add(new Feature { Id = 3, ProjectKey = "LOAN", JiraId = "LOAN-3", Summary = "c" });
        });

        var facts = await _service.GetFeatureMatchFactsAsync(["pay", "CARD"]);

        Assert.Equal(new[] { "CARD-2", "PAY-1" }, facts.Select(f => f.JiraId).Order());
        Assert.Contains(facts, f => f.Components == "Cards" && f.Labels == "web");
    }

    [Fact]
    public async Task Adding_a_key_row_keeps_the_arts_other_rows()
    {
        await SeedSharedKeyArtsAsync();

        var rows = await _service.SaveJiraKeyAsync(2, Row(" atm ", "Cash, cash", ""), isNew: true);

        Assert.Equal(new[] { "PAY", "CARD", "ATM" }, rows.Select(r => r.JiraKey));
        var atm = rows.Single(r => r.JiraKey == "ATM");
        Assert.True(atm.Id > 0);
        Assert.Equal("Cash", atm.Components);
        Assert.Null(atm.Labels);
        Assert.Equal("Cards", (await StoredRowsAsync(2)).Single(r => r.JiraKey == "PAY").Components);
    }

    [Fact]
    public async Task Adding_a_key_the_art_already_has_is_refused()
    {
        await SeedSharedKeyArtsAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.SaveJiraKeyAsync(2, Row("card", "Debit"), isNew: true));

        Assert.Contains("already has Jira key CARD", ex.Message);
        Assert.Null((await StoredRowsAsync(2)).Single(r => r.JiraKey == "CARD").Components);
    }

    [Fact]
    public async Task Adding_a_key_row_restarts_that_keys_jira_sync()
    {
        await SeedSharedKeyArtsAsync();

        await _service.SaveJiraKeyAsync(3, Row("PAY", "Loans"), isNew: true);

        var watermarks = await WatermarksAsync();
        Assert.Null(watermarks["PAY"]);
        Assert.Equal(Watermark, watermarks["CARD"]);
        Assert.Equal(Watermark, watermarks["LOAN"]);
    }

    [Fact]
    public async Task Adding_a_manual_sync_key_to_an_art_restarts_that_keys_jira_sync()
    {
        await SeedSharedKeyArtsAsync();
        await _db.SeedAsync(db => db.JiraSyncKeys.Add(SyncKey("ATM")));

        await _service.SaveJiraKeyAsync(3, Row("atm"), isNew: true);

        var watermarks = await WatermarksAsync();
        Assert.Null(watermarks["ATM"]);
        Assert.Equal(Watermark, watermarks["PAY"]);
        Assert.Equal(Watermark, watermarks["CARD"]);
        Assert.Equal(Watermark, watermarks["LOAN"]);
    }

    [Fact]
    public async Task Adding_a_key_without_a_sync_row_creates_none()
    {
        await SeedSharedKeyArtsAsync();

        await _service.SaveJiraKeyAsync(3, Row("ATM"), isNew: true);
        var loaded = (await _service.GetByIdAsync(1))!;
        loaded.JiraKeys.Add(Row("MORT"));
        await _service.UpdateAsync(loaded);

        Assert.Equal(new[] { "LOAN", "ATM" }, await StoredKeysAsync(3));
        Assert.Equal(new[] { "PAY", "MORT" }, await StoredKeysAsync(1));
        Assert.Equal(new[] { "CARD", "LOAN", "PAY" }, await SyncKeyNamesAsync());
        Assert.All((await WatermarksAsync()).Values, w => Assert.Equal(Watermark, w));
    }

    [Fact]
    public async Task Creating_an_art_creates_no_jira_sync_row()
    {
        await _service.CreateAsync(new Art { Name = "Payments", JiraKeys = Keys("PAY", "CARD") });

        Assert.Empty(await SyncKeyNamesAsync());
    }

    [Fact]
    public async Task Creating_an_art_restarts_the_jira_sync_of_its_keys_only()
    {
        await _db.SeedAsync(db =>
        {
            db.JiraSyncKeys.Add(SyncKey("PORT"));
            db.JiraSyncKeys.Add(SyncKey("CARD"));
        });

        await _service.CreateAsync(new Art { Name = "Portfolio", JiraKeys = Keys("port") });

        var watermarks = await WatermarksAsync();
        Assert.Null(watermarks["PORT"]);
        Assert.Equal(Watermark, watermarks["CARD"]);
    }

    [Fact]
    public async Task Adding_a_key_row_is_checked_like_a_full_save()
    {
        await SeedSharedKeyArtsAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveJiraKeyAsync(3, Row("PAY"), isNew: true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveJiraKeyAsync(3, Row("PAY", "cards"), isNew: true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveJiraKeyAsync(3, Row("PAY-1"), isNew: true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveJiraKeyAsync(3, Row("  "), isNew: true));

        Assert.Equal(new[] { "LOAN" }, await StoredKeysAsync(3));
    }

    [Fact]
    public async Task Editing_a_key_row_changes_only_its_filters_and_keeps_the_keys_jira_sync()
    {
        await SeedSharedKeyArtsAsync();

        var rows = await _service.SaveJiraKeyAsync(2, Row("pay", "Cards,Debit", "retail"), isNew: false);

        var pay = rows.Single(r => r.JiraKey == "PAY");
        Assert.Equal("Cards,Debit", pay.Components);
        Assert.Equal("retail", pay.Labels);
        Assert.Equal(new[] { "PAY", "CARD" }, rows.Select(r => r.JiraKey));
        Assert.All((await WatermarksAsync()).Values, w => Assert.Equal(Watermark, w));
    }

    [Fact]
    public async Task Editing_a_key_row_the_art_no_longer_has_is_refused()
    {
        await SeedSharedKeyArtsAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.SaveJiraKeyAsync(3, Row("PAY", "Loans"), isNew: false));

        Assert.Contains("no longer has Jira key PAY", ex.Message);
        Assert.Equal(new[] { "LOAN" }, await StoredKeysAsync(3));
    }

    [Fact]
    public async Task Removing_a_key_row_keeps_the_others_and_restarts_that_keys_jira_sync()
    {
        await SeedSharedKeyArtsAsync();

        var rows = await _service.RemoveJiraKeyAsync(2, "pay");

        Assert.Equal(new[] { "CARD" }, rows.Select(r => r.JiraKey));
        Assert.Equal(new[] { "CARD" }, await StoredKeysAsync(2));
        var watermarks = await WatermarksAsync();
        Assert.Null(watermarks["PAY"]);
        Assert.Equal(Watermark, watermarks["CARD"]);
        Assert.Equal(Watermark, watermarks["LOAN"]);
    }

    [Fact]
    public async Task Removing_the_last_art_from_a_key_keeps_its_sync_row()
    {
        await SeedSharedKeyArtsAsync();

        await _service.RemoveJiraKeyAsync(3, "LOAN");

        Assert.Empty(await StoredKeysAsync(3));
        Assert.Equal(new[] { "CARD", "LOAN", "PAY" }, await SyncKeyNamesAsync());
        var watermarks = await WatermarksAsync();
        Assert.Null(watermarks["LOAN"]);
        Assert.Equal(Watermark, watermarks["PAY"]);
        Assert.Equal(Watermark, watermarks["CARD"]);
    }

    [Fact]
    public async Task Creating_an_art_stores_not_and_empty_tokens_canonically()
    {
        var art = await _service.CreateAsync(new Art
        {
            Name = "Cards",
            JiraKeys = [Row("pay", "! TestComment, !(EMPTY), !testcomment", " (EMPTY) , retail,!")]
        });

        var row = Assert.Single(await StoredRowsAsync(art.Id));
        Assert.Equal("!TestComment,!(empty)", row.Components);
        Assert.Equal("retail,(empty)", row.Labels);
    }

    [Fact]
    public async Task Adding_a_key_row_stores_its_tokens_canonically()
    {
        await SeedSharedKeyArtsAsync();

        var rows = await _service.SaveJiraKeyAsync(3, Row("pay", "! TestComment", "(EMPTY)"), isNew: true);

        var pay = rows.Single(r => r.JiraKey == "PAY");
        Assert.Equal("!TestComment", pay.Components);
        Assert.Equal("(empty)", pay.Labels);
        var stored = (await StoredRowsAsync(3)).Single(r => r.JiraKey == "PAY");
        Assert.Equal("!TestComment", stored.Components);
        Assert.Equal("(empty)", stored.Labels);
    }

    [Fact]
    public async Task Updating_an_art_stores_its_tokens_canonically()
    {
        await SeedSharedKeyArtsAsync();

        var loaded = (await _service.GetByIdAsync(2))!;
        loaded.JiraKeys.Single(k => k.JiraKey == "PAY").Components = "(EMPTY), Debit";
        loaded.JiraKeys.Single(k => k.JiraKey == "CARD").Labels = "!(Empty)";
        await _service.UpdateAsync(loaded);

        var rows = await StoredRowsAsync(2);
        Assert.Equal("Debit,(empty)", rows.Single(r => r.JiraKey == "PAY").Components);
        Assert.Equal("!(empty)", rows.Single(r => r.JiraKey == "CARD").Labels);
    }

    [Fact]
    public async Task A_lone_not_sign_is_dropped()
    {
        await _service.CreateAsync(new Art { Name = "Payments", JiraKeys = [Row("PAY")] });

        var cards = await _service.CreateAsync(new Art { Name = "Cards", JiraKeys = [Row("PAY", "!", "(empty)")] });
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CreateAsync(new Art { Name = "Cards 2", JiraKeys = [Row("PAY", " ! ")] }));

        var row = Assert.Single(await StoredRowsAsync(cards.Id));
        Assert.Null(row.Components);
        Assert.Equal("(empty)", row.Labels);
        Assert.Contains("Jira key PAY is already used without components or labels by ART 'Payments'", ex.Message);
    }

    [Fact]
    public async Task Toggling_only_not_on_a_key_row_is_saved_and_keeps_the_keys_jira_sync()
    {
        await SeedSharedKeyArtsAsync();

        var rows = await _service.SaveJiraKeyAsync(2, Row("PAY", "!Cards"), isNew: false);

        Assert.Equal("!Cards", rows.Single(r => r.JiraKey == "PAY").Components);
        Assert.Equal("!Cards", (await StoredRowsAsync(2)).Single(r => r.JiraKey == "PAY").Components);
        Assert.All((await WatermarksAsync()).Values, w => Assert.Equal(Watermark, w));

        var loaded = (await _service.GetByIdAsync(2))!;
        loaded.JiraKeys.Single(k => k.JiraKey == "PAY").Components = "Cards";
        await _service.UpdateAsync(loaded);

        Assert.Equal("Cards", (await StoredRowsAsync(2)).Single(r => r.JiraKey == "PAY").Components);
        Assert.All((await WatermarksAsync()).Values, w => Assert.Equal(Watermark, w));
    }

    [Fact]
    public async Task Toggling_empty_on_a_key_row_is_saved_and_keeps_the_keys_jira_sync()
    {
        await SeedSharedKeyArtsAsync();

        await _service.SaveJiraKeyAsync(2, Row("PAY", "Cards,(empty)"), isNew: false);

        Assert.Equal("Cards,(empty)", (await StoredRowsAsync(2)).Single(r => r.JiraKey == "PAY").Components);
        Assert.All((await WatermarksAsync()).Values, w => Assert.Equal(Watermark, w));
    }

    [Fact]
    public async Task Mixing_values_with_and_without_not_is_refused()
    {
        await SeedSharedKeyArtsAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.SaveJiraKeyAsync(2, Row("PAY", "Debit,!Cards"), isNew: false));

        Assert.Equal("Jira key PAY: The components mix values with and without Not. Use Not for all of them or for none.", ex.Message);
        Assert.Equal("Cards", (await StoredRowsAsync(2)).Single(r => r.JiraKey == "PAY").Components);
    }

    [Fact]
    public async Task Mixing_empty_with_and_without_not_is_refused_for_components_and_labels()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CreateAsync(new Art { Name = "Cards", JiraKeys = [Row("ATM", "A,!(empty)", "(empty),!(empty)")] }));

        Assert.Equal(
            "Jira key ATM: The components mix values with and without Not. Use Not for all of them or for none. "
            + "Jira key ATM: The labels mix values with and without Not. Use Not for all of them or for none.",
            ex.Message);
        Assert.Empty(await _db.ReadAsync(db => db.CapitalProjects.ToListAsync()));
    }

    [Fact]
    public async Task A_value_that_still_starts_with_not_is_refused()
    {
        await SeedSharedKeyArtsAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.SaveJiraKeyAsync(3, Row("ATM", labels: "!!x"), isNew: true));

        Assert.Equal("Jira key ATM: The labels value \"!x\" starts with '!', which is reserved for Not.", ex.Message);
        Assert.Equal(new[] { "LOAN" }, await StoredKeysAsync(3));
    }

    [Fact]
    public async Task A_stored_mixed_row_that_is_not_changed_does_not_block_saving()
    {
        await _db.SeedAsync(db =>
            db.CapitalProjects.Add(new Art { Id = 1, Name = "Cards", JiraKeys = [Row("PAY", "A,!B"), Row("CARD")] }));

        var loaded = (await _service.GetByIdAsync(1))!;
        loaded.Name = "Cards & Debit";
        loaded.JiraKeys.Single(k => k.JiraKey == "CARD").Components = "!Debit";
        await _service.UpdateAsync(loaded);
        await _service.SaveJiraKeyAsync(1, Row("ATM", "!Cash"), isNew: true);

        var rows = await StoredRowsAsync(1);
        Assert.Equal("A,!B", rows.Single(r => r.JiraKey == "PAY").Components);
        Assert.Equal("!Debit", rows.Single(r => r.JiraKey == "CARD").Components);
        Assert.Equal("!Cash", rows.Single(r => r.JiraKey == "ATM").Components);
    }

    [Fact]
    public async Task A_value_and_its_not_can_share_a_key_but_the_same_not_cannot()
    {
        await _service.CreateAsync(new Art { Name = "Payments", JiraKeys = [Row("PAY")] });
        await _service.CreateAsync(new Art { Name = "Cards", JiraKeys = [Row("PAY", "TestComment")] });
        await _service.CreateAsync(new Art { Name = "Other", JiraKeys = [Row("PAY", "!TestComment")] });
        await _service.CreateAsync(new Art { Name = "Empty", JiraKeys = [Row("PAY", "(empty)")] });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CreateAsync(new Art { Name = "Other 2", JiraKeys = [Row("PAY", "! testcomment")] }));

        Assert.Contains("Jira key PAY already has the same components and labels on ART 'Other'", ex.Message);
        Assert.Equal(4, await _db.ReadAsync(db => db.CapitalProjectJiraKeys.CountAsync(k => k.JiraKey == "PAY")));
    }

    [Fact]
    public async Task Rewriting_the_same_rule_differently_changes_nothing()
    {
        await _db.SeedAsync(db =>
        {
            db.CapitalProjects.Add(new Art { Id = 1, Name = "Cards", JiraKeys = [Row("PAY", "!TestComment,!(empty)")] });
            db.JiraSyncKeys.Add(SyncKey("PAY"));
        });

        await _service.SaveJiraKeyAsync(1, Row("PAY", "!(EMPTY), ! testcomment"), isNew: false);

        Assert.Equal("!TestComment,!(empty)", Assert.Single(await StoredRowsAsync(1)).Components);
        Assert.Equal(Watermark, (await WatermarksAsync())["PAY"]);
    }
}

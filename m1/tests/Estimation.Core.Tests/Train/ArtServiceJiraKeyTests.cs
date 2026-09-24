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

    [Fact]
    public async Task Adding_a_key_restarts_the_arts_jira_sync_from_scratch()
    {
        var watermark = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        await _db.SeedAsync(db =>
        {
            db.CapitalProjects.Add(new Art { Id = 1, Name = "Payments", JiraKeys = Keys("PAY") });
            db.JiraSyncProjectSettings.Add(new JiraSyncProjectSettings { CapitalProjectId = 1, LastSyncedWatermarkUtc = watermark });
        });

        var loaded = (await _service.GetByIdAsync(1))!;
        loaded.Name = "Payments & Cards";
        await _service.UpdateAsync(loaded);
        Assert.Equal(watermark, await _db.ReadAsync(db => db.JiraSyncProjectSettings.Select(s => s.LastSyncedWatermarkUtc).SingleAsync()));

        loaded = (await _service.GetByIdAsync(1))!;
        loaded.JiraKeys.Add(new ArtJiraKey { JiraKey = "CARD" });
        await _service.UpdateAsync(loaded);
        Assert.Null(await _db.ReadAsync(db => db.JiraSyncProjectSettings.Select(s => s.LastSyncedWatermarkUtc).SingleAsync()));
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
    public async Task Updating_the_filters_of_a_kept_key_restarts_the_arts_jira_sync()
    {
        var watermark = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        await _db.SeedAsync(db =>
        {
            db.CapitalProjects.Add(new Art { Id = 1, Name = "Cards", JiraKeys = [Row("PAY", "Cards")] });
            db.JiraSyncProjectSettings.Add(new JiraSyncProjectSettings { CapitalProjectId = 1, LastSyncedWatermarkUtc = watermark });
        });

        var loaded = (await _service.GetByIdAsync(1))!;
        loaded.JiraKeys[0].Components = "Cards,Debit";
        loaded.JiraKeys[0].Labels = "retail";
        await _service.UpdateAsync(loaded);

        var row = Assert.Single(await StoredRowsAsync(1));
        Assert.Equal("Cards,Debit", row.Components);
        Assert.Equal("retail", row.Labels);
        Assert.Null(await _db.ReadAsync(db => db.JiraSyncProjectSettings.Select(s => s.LastSyncedWatermarkUtc).SingleAsync()));
    }

    private static readonly DateTime Watermark = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    private Task SeedSharedKeyArtsAsync() =>
        _db.SeedAsync(db =>
        {
            db.CapitalProjects.Add(new Art { Id = 1, Name = "Payments", JiraKeys = [Row("PAY")] });
            db.CapitalProjects.Add(new Art { Id = 2, Name = "Cards", JiraKeys = [Row("PAY", "Cards"), Row("CARD")] });
            db.CapitalProjects.Add(new Art { Id = 3, Name = "Loans", JiraKeys = [Row("LOAN")] });
            foreach (var artId in new[] { 1, 2, 3 })
            {
                db.JiraSyncProjectSettings.Add(new JiraSyncProjectSettings { CapitalProjectId = artId, LastSyncedWatermarkUtc = Watermark });
            }
        });

    private Task<Dictionary<int, DateTime?>> WatermarksAsync() =>
        _db.ReadAsync(db => db.JiraSyncProjectSettings.ToDictionaryAsync(s => s.CapitalProjectId, s => s.LastSyncedWatermarkUtc));

    [Fact]
    public async Task Removing_a_shared_key_restarts_the_jira_sync_of_every_art_on_it()
    {
        await SeedSharedKeyArtsAsync();

        var loaded = (await _service.GetByIdAsync(2))!;
        loaded.JiraKeys = loaded.JiraKeys.Where(k => k.JiraKey == "CARD").ToList();
        await _service.UpdateAsync(loaded);

        var watermarks = await WatermarksAsync();
        Assert.Null(watermarks[1]);
        Assert.Null(watermarks[2]);
        Assert.Equal(Watermark, watermarks[3]);
    }

    [Fact]
    public async Task Refiltering_a_shared_key_restarts_the_jira_sync_of_the_other_arts_on_it()
    {
        await SeedSharedKeyArtsAsync();

        var loaded = (await _service.GetByIdAsync(2))!;
        loaded.JiraKeys.Single(k => k.JiraKey == "PAY").Components = "Debit";
        await _service.UpdateAsync(loaded);

        var watermarks = await WatermarksAsync();
        Assert.Null(watermarks[1]);
        Assert.Null(watermarks[2]);
        Assert.Equal(Watermark, watermarks[3]);
    }

    [Fact]
    public async Task Deleting_an_art_restarts_the_jira_sync_of_the_arts_sharing_its_keys()
    {
        await SeedSharedKeyArtsAsync();

        Assert.True(await _service.DeleteAsync(2));

        var watermarks = await WatermarksAsync();
        Assert.Null(watermarks[1]);
        Assert.Equal(Watermark, watermarks[3]);
        Assert.Empty(await StoredRowsAsync(2));
    }

    [Fact]
    public async Task Deleting_an_art_whose_keys_no_other_art_lists_restarts_no_other_jira_sync()
    {
        await SeedSharedKeyArtsAsync();

        Assert.True(await _service.DeleteAsync(3));

        var watermarks = await WatermarksAsync();
        Assert.Equal(Watermark, watermarks[1]);
        Assert.Equal(Watermark, watermarks[2]);
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
        await _db.SeedAsync(db =>
        {
            foreach (var s in db.JiraSyncProjectSettings)
            {
                s.LastSyncedWatermarkUtc = Watermark;
            }
        });

        stale.Name = "Cards & Debit";
        stale.JiraKeys.Add(Row("ATM"));
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
    public async Task Editing_a_key_row_changes_only_its_filters_and_restarts_the_arts_on_the_key()
    {
        await SeedSharedKeyArtsAsync();

        var rows = await _service.SaveJiraKeyAsync(2, Row("pay", "Cards,Debit", "retail"), isNew: false);

        var pay = rows.Single(r => r.JiraKey == "PAY");
        Assert.Equal("Cards,Debit", pay.Components);
        Assert.Equal("retail", pay.Labels);
        Assert.Equal(new[] { "PAY", "CARD" }, rows.Select(r => r.JiraKey));
        var watermarks = await WatermarksAsync();
        Assert.Null(watermarks[1]);
        Assert.Null(watermarks[2]);
        Assert.Equal(Watermark, watermarks[3]);
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
    public async Task Removing_a_key_row_keeps_the_others_and_restarts_the_arts_on_the_key()
    {
        await SeedSharedKeyArtsAsync();

        var rows = await _service.RemoveJiraKeyAsync(2, "pay");

        Assert.Equal(new[] { "CARD" }, rows.Select(r => r.JiraKey));
        Assert.Equal(new[] { "CARD" }, await StoredKeysAsync(2));
        var watermarks = await WatermarksAsync();
        Assert.Null(watermarks[1]);
        Assert.Null(watermarks[2]);
        Assert.Equal(Watermark, watermarks[3]);
    }
}

using Estimation.Core.Administration.Audit;
using Estimation.Core.Features.Models;
using Estimation.Core.Train.Services;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.Features.Services;

public record FeatureSnapshotTarget(
    string ArtName,
    string? ArtJiraKeys,
    int? CapitalProjectId,
    string PiName,
    string? Name = null);

public interface IFeatureSnapshotService
{
    Task<List<FeatureSnapshot>> GetAllAsync();

    Task<FeatureSnapshot?> GetByIdAsync(int id);

    Task<List<FeatureSnapshotItem>> GetItemsAsync(int snapshotId);

    Task<List<FeatureSnapshotItem>> CaptureCurrentAsync(FeatureSnapshotTarget target, bool includePiLabelMatches);

    Task<FeatureSnapshot> CreateAsync(FeatureSnapshotTarget target, bool includePiLabelMatches);

    Task<IReadOnlyList<FeatureSnapshot>> CreateForLockedPiAsync(string piName);

    Task<bool> DeleteAsync(int id);
}

public class FeatureSnapshotService : IFeatureSnapshotService
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;
    private readonly IAuditUserProvider _auditUser;

    public FeatureSnapshotService(IDbContextFactory<EstimationDbContext> ctx, IAuditUserProvider auditUser)
    {
        _ctx = ctx;
        _auditUser = auditUser;
    }

    public async Task<List<FeatureSnapshot>> GetAllAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.FeatureSnapshots
            .AsNoTracking()
            .OrderByDescending(s => s.CreatedAt)
            .ThenByDescending(s => s.Id)
            .ToListAsync();
    }

    public async Task<FeatureSnapshot?> GetByIdAsync(int id)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.FeatureSnapshots.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<List<FeatureSnapshotItem>> GetItemsAsync(int snapshotId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.FeatureSnapshotItems
            .AsNoTracking()
            .Where(i => i.FeatureSnapshotId == snapshotId)
            .OrderBy(i => i.JiraId)
            .ThenBy(i => i.FeatureId)
            .ToListAsync();
    }

    public async Task<List<FeatureSnapshotItem>> CaptureCurrentAsync(FeatureSnapshotTarget target, bool includePiLabelMatches)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await BuildItemsAsync(db, target, includePiLabelMatches);
    }

    public async Task<FeatureSnapshot> CreateAsync(FeatureSnapshotTarget target, bool includePiLabelMatches)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var items = await BuildItemsAsync(db, target, includePiLabelMatches);

        var snapshot = new FeatureSnapshot
        {
            CapitalProjectId = target.CapitalProjectId,
            ArtName = target.ArtName,
            ArtJiraKeys = target.ArtJiraKeys,
            PiName = target.PiName,
            Name = Normalize(target.Name),
            IncludedPiLabelMatches = includePiLabelMatches,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _auditUser.GetCurrentUserName(),
            FeatureCount = items.Count,
            Items = items
        };

        db.FeatureSnapshots.Add(snapshot);
        await db.SaveChangesAsync();

        return snapshot;
    }

    public async Task<IReadOnlyList<FeatureSnapshot>> CreateForLockedPiAsync(string piName)
    {
        if (string.IsNullOrWhiteSpace(piName))
        {
            return [];
        }

        await using var db = await _ctx.CreateDbContextAsync();

        var arts = await db.CapitalProjects
            .AsNoTracking()
            .Where(cp => cp.JiraKeys.Any())
            .OrderBy(cp => cp.Id)
            .Select(cp => new { cp.Id, cp.Name })
            .ToListAsync();

        if (arts.Count == 0)
        {
            return [];
        }

        var alreadyCaptured = (await db.FeatureSnapshots
                .AsNoTracking()
                .Where(s => s.IsAutomatic && s.PiName == piName)
                .Select(s => s.ArtName)
                .ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var labelRules = await FeatureScope.LoadPiLabelRulesAsync(db, piName);
        var features = await LoadFeaturesAsync(db);

        var matcher = await ArtMatcher.LoadAsync(db);

        var featuresByArtId = features
            .Where(f => FeatureScope.MatchesPi(f, piName, labelRules))
            .Select(f => new { ArtId = matcher.ArtIdOf(f), Feature = f })
            .Where(x => x.ArtId is not null)
            .GroupBy(x => x.ArtId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Feature).ToList());

        var createdBy = _auditUser.GetCurrentUserName();
        var createdAt = DateTime.UtcNow;
        var created = new List<FeatureSnapshot>();

        foreach (var art in arts)
        {
            if (alreadyCaptured.Contains(art.Name))
            {
                continue;
            }

            if (!featuresByArtId.TryGetValue(art.Id, out var artFeatures) || artFeatures.Count == 0)
            {
                continue;
            }

            var items = artFeatures
                .Select(f => ToItem(f, art.Name))
                .OrderBy(i => i.JiraId)
                .ThenBy(i => i.FeatureId)
                .ToList();

            var snapshot = new FeatureSnapshot
            {
                CapitalProjectId = art.Id,
                ArtName = art.Name,
                ArtJiraKeys = JoinKeys(matcher.KeysOf(art.Id)),
                PiName = piName,
                IncludedPiLabelMatches = true,
                IsAutomatic = true,
                CreatedAt = createdAt,
                CreatedBy = createdBy,
                FeatureCount = items.Count,
                Items = items
            };

            db.FeatureSnapshots.Add(snapshot);
            created.Add(snapshot);
        }

        if (created.Count > 0)
        {
            await db.SaveChangesAsync();
        }

        return created;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var snapshot = await db.FeatureSnapshots
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (snapshot is null)
        {
            return false;
        }

        db.FeatureSnapshotItems.RemoveRange(snapshot.Items);
        db.FeatureSnapshots.Remove(snapshot);
        await db.SaveChangesAsync();
        return true;
    }

    private static string? Normalize(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : name.Trim();

    public static string? JoinKeys(IEnumerable<string> keys)
    {
        var joined = "";
        foreach (var key in keys)
        {
            var next = joined.Length == 0 ? key : joined + "," + key;
            if (next.Length > FeatureSnapshot.ArtJiraKeysMaxLength)
            {
                break;
            }

            joined = next;
        }

        return joined.Length == 0 ? null : joined;
    }

    public static IReadOnlyList<string> SplitKeys(string? keys) =>
        string.IsNullOrWhiteSpace(keys)
            ? []
            : keys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static async Task<List<FeatureSnapshotItem>> BuildItemsAsync(
        EstimationDbContext db,
        FeatureSnapshotTarget target,
        bool includePiLabelMatches)
    {
        var belongs = await MembershipAsync(db, target);
        if (belongs is null)
        {
            return [];
        }

        var labelRules = includePiLabelMatches
            ? await FeatureScope.LoadPiLabelRulesAsync(db, target.PiName)
            : [];

        var features = await LoadFeaturesAsync(db);

        return features
            .Where(belongs)
            .Where(f => FeatureScope.MatchesPi(f, target.PiName, labelRules))
            .Select(f => ToItem(f, target.ArtName))
            .OrderBy(i => i.JiraId)
            .ThenBy(i => i.FeatureId)
            .ToList();
    }

    private static async Task<Func<Feature, bool>?> MembershipAsync(EstimationDbContext db, FeatureSnapshotTarget target)
    {
        if (target.CapitalProjectId is { } artId && await db.CapitalProjects.AnyAsync(a => a.Id == artId))
        {
            var matcher = await ArtMatcher.LoadAsync(db);
            return f => matcher.BelongsTo(f, artId);
        }

        var keys = SplitKeys(target.ArtJiraKeys).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (keys.Count == 0)
        {
            return null;
        }

        var current = await ArtMatcher.LoadAsync(db);
        return f => JiraProjectKeys.Of(f) is { } key && keys.Contains(key) && !current.Match(f).IsMatched;
    }

    private static Task<List<Feature>> LoadFeaturesAsync(EstimationDbContext db) =>
        db.Features
            .WithPlanningLookups()
            .AsNoTracking()
            .ToListAsync();

    private static FeatureSnapshotItem ToItem(Feature feature, string artName) =>
        new()
        {
            FeatureId = feature.Id,
            JiraId = feature.JiraId,
            ArtName = artName,
            PiName = feature.Pi?.Name,
            Labels = feature.Labels,
            BusinessOutcomeJiraId = feature.BusinessOutcome?.JiraId,
            BusinessOutcomeName = feature.BusinessOutcome?.Summary,
            TargetStart = feature.TargetStart,
            TargetEnd = feature.TargetEnd,
            StoryPoints = feature.StoryPoints,
            Teams = JoinTeams(feature),
            RequirementStatus = feature.RequirementStatus?.Name,
            TechnicalApproval = feature.TechnicalApproval?.Name,
            FundingStatus = feature.UnfundedOption?.Name,
            Summary = feature.Summary,
            Name = feature.Name,
            AcceptanceCriteria = feature.AcceptanceCriteria,
            PiObjective = feature.PiObjective?.Name,
            RagExplain = feature.RagExplain
        };

    private static string? JoinTeams(Feature feature)
    {
        var names = feature.FeatureTeams
            .Select(ft => ft.Team?.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return names.Count == 0 ? null : string.Join(", ", names);
    }
}

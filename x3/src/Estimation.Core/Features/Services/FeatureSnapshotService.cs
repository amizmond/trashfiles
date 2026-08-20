using Estimation.Core.Administration.Audit;
using Estimation.Core.Features.Models;
using Estimation.Core.PlanningIncrement.Models;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.Features.Services;

public record FeatureSnapshotTarget(
    string ArtName,
    string? ArtJiraKey,
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
            ArtJiraKey = target.ArtJiraKey,
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
            .Where(cp => cp.JiraKey != null && cp.JiraKey != "")
            .Select(cp => new { cp.Id, cp.Name, cp.JiraKey })
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

        var labelRules = await LoadPiLabelRulesAsync(db, piName);
        var features = await LoadFeaturesAsync(db);

        var featuresByArtKey = features
            .Where(f => MatchesPi(f, piName, labelRules))
            .Select(f => new { Key = ProjectKeyOf(f), Feature = f })
            .Where(x => !string.IsNullOrEmpty(x.Key))
            .GroupBy(x => x.Key!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Feature).ToList(), StringComparer.OrdinalIgnoreCase);

        var createdBy = _auditUser.GetCurrentUserName();
        var createdAt = DateTime.UtcNow;
        var created = new List<FeatureSnapshot>();

        foreach (var art in arts)
        {
            if (alreadyCaptured.Contains(art.Name))
            {
                continue;
            }

            if (!featuresByArtKey.TryGetValue(art.JiraKey!.Trim(), out var artFeatures) || artFeatures.Count == 0)
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
                ArtJiraKey = art.JiraKey,
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

        var usedByReviews = await db.FeatureChangeReviews
            .AsNoTracking()
            .Where(r => r.BaselineSnapshotId == id || r.ReviewSnapshotId == id)
            .Select(r => r.Name ?? r.PiName)
            .ToListAsync();

        if (usedByReviews.Count > 0)
        {
            throw new InvalidOperationException(
                $"The snapshot is used by change review(s): {string.Join(", ", usedByReviews)}. Delete the review(s) first.");
        }

        db.FeatureSnapshotItems.RemoveRange(snapshot.Items);
        db.FeatureSnapshots.Remove(snapshot);
        await db.SaveChangesAsync();
        return true;
    }

    private static string? Normalize(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : name.Trim();

    private static async Task<List<FeatureSnapshotItem>> BuildItemsAsync(
        EstimationDbContext db,
        FeatureSnapshotTarget target,
        bool includePiLabelMatches)
    {
        var artKey = target.ArtJiraKey?.Trim();

        if (string.IsNullOrEmpty(artKey))
        {
            return [];
        }

        var labelRules = includePiLabelMatches
            ? await LoadPiLabelRulesAsync(db, target.PiName)
            : [];

        var features = await LoadFeaturesAsync(db);

        return features
            .Where(f => string.Equals(ProjectKeyOf(f), artKey, StringComparison.OrdinalIgnoreCase))
            .Where(f => MatchesPi(f, target.PiName, labelRules))
            .Select(f => ToItem(f, target.ArtName))
            .OrderBy(i => i.JiraId)
            .ThenBy(i => i.FeatureId)
            .ToList();
    }

    private static Task<List<Feature>> LoadFeaturesAsync(EstimationDbContext db) =>
        db.Features
            .Include(f => f.Pi)
            .Include(f => f.PiObjective)
            .Include(f => f.BusinessOutcome)
            .Include(f => f.RequirementStatus)
            .Include(f => f.UnfundedOption)
            .Include(f => f.FeatureTeams).ThenInclude(ft => ft.Team)
            .AsSplitQuery()
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
            FundingStatus = feature.UnfundedOption?.Name,
            Summary = feature.Summary,
            Name = feature.Name,
            AcceptanceCriteria = feature.AcceptanceCriteria,
            PiObjective = feature.PiObjective?.Name,
            RagExplain = feature.RagExplain
        };

    private static async Task<List<PiLabelRule>> LoadPiLabelRulesAsync(EstimationDbContext db, string piName)
    {
        var pis = await db.Pis
            .AsNoTracking()
            .Where(p => p.Name == piName && p.FeatureLabels != null && p.FeatureLabels != "")
            .Select(p => new { p.FeatureLabels, p.LabelMatchMode })
            .ToListAsync();

        return pis
            .Select(p => new PiLabelRule(
                new HashSet<string>(
                    p.FeatureLabels!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    StringComparer.OrdinalIgnoreCase),
                p.LabelMatchMode))
            .Where(rule => rule.Labels.Count > 0)
            .ToList();
    }

    private static bool MatchesPi(Feature feature, string piName, IReadOnlyList<PiLabelRule> labelRules)
    {
        if (feature.Pi is not null && string.Equals(feature.Pi.Name, piName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (labelRules.Count == 0 || string.IsNullOrEmpty(feature.Labels))
        {
            return false;
        }

        var featureLabels = new HashSet<string>(
            feature.Labels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);

        if (featureLabels.Count == 0)
        {
            return false;
        }

        return labelRules.Any(rule => rule.Mode == PiLabelMatchMode.All
            ? rule.Labels.All(featureLabels.Contains)
            : rule.Labels.Any(featureLabels.Contains));
    }

    private static string? ProjectKeyOf(Feature feature)
    {
        if (!string.IsNullOrWhiteSpace(feature.ProjectKey))
        {
            return feature.ProjectKey.Trim();
        }

        if (string.IsNullOrWhiteSpace(feature.JiraId))
        {
            return null;
        }

        var dash = feature.JiraId.IndexOf('-');
        return dash > 0 ? feature.JiraId[..dash].Trim() : feature.JiraId.Trim();
    }

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

    private record PiLabelRule(HashSet<string> Labels, PiLabelMatchMode Mode);
}

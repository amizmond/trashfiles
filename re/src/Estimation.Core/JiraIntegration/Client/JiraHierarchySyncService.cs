using Estimation.Core.Administration.Audit;
using Estimation.Core.Features.Models;
using Estimation.Core.JiraIntegration.Models;
using Estimation.Core.Train.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Estimation.Core.JiraIntegration.Client;

public class JiraHierarchySyncService : IJiraHierarchySyncService
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;
    private readonly IAuditUserProvider _auditUser;

    public JiraHierarchySyncService(IDbContextFactory<EstimationDbContext> ctx, IAuditUserProvider auditUser)
    {
        _ctx = ctx;
        _auditUser = auditUser;
    }

    public async Task<HierarchySyncResult> SyncSingleAsync(
        JiraIssueResponse jiraIssue,
        string? featureProjectKey,
        JiraIssueResponse? childInChain)
    {
        var entityType = IJiraHierarchySyncService.MapEntityType(jiraIssue.IssueType)
            ?? throw new InvalidOperationException(
                $"Issue type '{jiraIssue.IssueType}' is not supported for synchronization.");

        await using var db = await _ctx.CreateDbContextAsync();

        var result = entityType switch
        {
            HierarchyEntityType.Feature => await SyncFeatureAsync(db, jiraIssue, featureProjectKey),
            HierarchyEntityType.BusinessOutcome => await SyncBusinessOutcomeAsync(db, jiraIssue, featureProjectKey),
            HierarchyEntityType.PortfolioEpic => await SyncPortfolioEpicAsync(db, jiraIssue, featureProjectKey),
            HierarchyEntityType.StrategicObjective => await SyncStrategicObjectiveAsync(db, jiraIssue, featureProjectKey),
            _ => throw new InvalidOperationException($"Unsupported entity type {entityType}"),
        };

        var childRelinked = false;
        if (childInChain is not null)
        {
            childRelinked = await RelinkChildAsync(db, entityType, result.EntityId, childInChain);
            if (childRelinked)
            {
                await db.SaveChangesAsync();
            }
        }

        Log.Information(
            "Hierarchy sync: {EntityType} Id={EntityId} for Jira {JiraKey}; parentLinked={ParentLinked}; childRelinked={ChildRelinked}",
            entityType, result.EntityId, jiraIssue.Key, result.ParentLinked, childRelinked);

        return result with { ChildRelinked = childRelinked };
    }

    private async Task<HierarchySyncResult> SyncFeatureAsync(EstimationDbContext db, JiraIssueResponse j, string? projectKey)
    {
        var f = await db.Features.FirstOrDefaultAsync(x => x.JiraId == j.Key);
        var isNew = f is null;
        f ??= new Feature();

        f.JiraId = j.Key;
        f.ProjectKey = projectKey;
        f.IssueType = j.IssueType;
        f.Summary = j.Summary ?? j.Key;
        f.Description = j.Description;
        f.Labels = JiraSyncItemMapping.JoinLabels(j.Labels);
        f.Name = j.FeatureName;
        f.Status = j.Status;
        f.IsLinkedToTheJira = true;
        f.JiraUpdated = j.Updated;
        f.TargetStart = j.TargetStart;
        f.TargetEnd = j.TargetEnd;
        f.StoryPoints = j.StoryPoints;
        f.ModifiedBy = _auditUser.GetCurrentUserName();
        f.ModifiedAt = DateTime.UtcNow;

        var parentLinked = false;
        if (!string.IsNullOrEmpty(j.ParentLink))
        {
            var parentBo = await db.BusinessOutcomes.FirstOrDefaultAsync(b => b.JiraId == j.ParentLink);
            if (parentBo is not null && f.BusinessOutcomeId != parentBo.Id)
            {
                f.BusinessOutcomeId = parentBo.Id;
                parentLinked = true;
            }
        }

        if (isNew)
        {
            db.Features.Add(f);
        }
        await db.SaveChangesAsync();

        return new HierarchySyncResult(HierarchyEntityType.Feature, f.Id, parentLinked, false);
    }

    private static async Task<HierarchySyncResult> SyncBusinessOutcomeAsync(EstimationDbContext db, JiraIssueResponse j, string? projectKey)
    {
        var bo = await db.BusinessOutcomes.FirstOrDefaultAsync(x => x.JiraId == j.Key);
        var isNew = bo is null;
        bo ??= new BusinessOutcome();

        ApplyCommonFields(bo, j, projectKey);

        var parentLinked = false;
        if (!string.IsNullOrEmpty(j.ParentLink))
        {
            var parentPe = await db.PortfolioEpics.FirstOrDefaultAsync(p => p.JiraId == j.ParentLink);
            if (parentPe is not null && bo.PortfolioEpicId != parentPe.Id)
            {
                bo.PortfolioEpicId = parentPe.Id;
                parentLinked = true;
            }
        }

        if (isNew)
        {
            db.BusinessOutcomes.Add(bo);
        }
        await db.SaveChangesAsync();

        return new HierarchySyncResult(HierarchyEntityType.BusinessOutcome, bo.Id, parentLinked, false);
    }

    private static async Task<HierarchySyncResult> SyncPortfolioEpicAsync(EstimationDbContext db, JiraIssueResponse j, string? projectKey)
    {
        var pe = await db.PortfolioEpics.FirstOrDefaultAsync(x => x.JiraId == j.Key);
        var isNew = pe is null;
        pe ??= new PortfolioEpic();

        ApplyCommonFields(pe, j, projectKey);

        if (isNew)
        {
            db.PortfolioEpics.Add(pe);
        }
        await db.SaveChangesAsync();

        var parentLinked = false;
        if (!string.IsNullOrEmpty(j.ParentLink))
        {
            var parentSo = await db.StrategicObjectives.FirstOrDefaultAsync(s => s.JiraId == j.ParentLink);
            if (parentSo is not null)
            {
                var alreadyLinked = await db.StrategicObjectivePortfolioEpics
                    .AnyAsync(l => l.StrategicObjectiveId == parentSo.Id && l.PortfolioEpicId == pe.Id);
                if (!alreadyLinked)
                {
                    db.StrategicObjectivePortfolioEpics.Add(new StrategicObjectivePortfolioEpic
                    {
                        StrategicObjectiveId = parentSo.Id,
                        PortfolioEpicId = pe.Id,
                    });
                    await db.SaveChangesAsync();
                    parentLinked = true;
                }
            }
        }

        return new HierarchySyncResult(HierarchyEntityType.PortfolioEpic, pe.Id, parentLinked, false);
    }

    private static async Task<HierarchySyncResult> SyncStrategicObjectiveAsync(EstimationDbContext db, JiraIssueResponse j, string? projectKey)
    {
        var so = await db.StrategicObjectives.FirstOrDefaultAsync(x => x.JiraId == j.Key);
        var isNew = so is null;
        so ??= new StrategicObjective();

        ApplyCommonFields(so, j, projectKey);

        if (isNew)
        {
            db.StrategicObjectives.Add(so);
        }
        await db.SaveChangesAsync();

        var parentLinked = false;
        if (!string.IsNullOrWhiteSpace(projectKey))
        {
            var cp = await db.CapitalProjects.FirstOrDefaultAsync(c => c.JiraKey == projectKey);
            if (cp is not null)
            {
                var alreadyLinked = await db.CapitalProjectStrategicObjectives
                    .AnyAsync(l => l.CapitalProjectId == cp.Id && l.StrategicObjectiveId == so.Id);
                if (!alreadyLinked)
                {
                    db.CapitalProjectStrategicObjectives.Add(new ArtStrategicObjective
                    {
                        CapitalProjectId = cp.Id,
                        StrategicObjectiveId = so.Id,
                    });
                    await db.SaveChangesAsync();
                    parentLinked = true;
                }
            }
        }

        return new HierarchySyncResult(HierarchyEntityType.StrategicObjective, so.Id, parentLinked, false);
    }

    private static async Task<bool> RelinkChildAsync(
        EstimationDbContext db,
        HierarchyEntityType parentType,
        int parentId,
        JiraIssueResponse child)
    {
        var childType = IJiraHierarchySyncService.MapEntityType(child.IssueType);
        if (childType is null || string.IsNullOrWhiteSpace(child.Key))
        {
            return false;
        }

        return (parentType, childType.Value) switch
        {
            (HierarchyEntityType.BusinessOutcome, HierarchyEntityType.Feature)
                => await RelinkFeatureToBoAsync(db, child.Key, parentId),
            (HierarchyEntityType.PortfolioEpic, HierarchyEntityType.BusinessOutcome)
                => await RelinkBoToPeAsync(db, child.Key, parentId),
            (HierarchyEntityType.StrategicObjective, HierarchyEntityType.PortfolioEpic)
                => await RelinkPeToSoAsync(db, child.Key, parentId),
            _ => false,
        };
    }

    private static async Task<bool> RelinkFeatureToBoAsync(EstimationDbContext db, string childJiraKey, int boId)
    {
        var feature = await db.Features.FirstOrDefaultAsync(f => f.JiraId == childJiraKey);
        if (feature is null || feature.BusinessOutcomeId is not null)
        {
            return false;
        }
        feature.BusinessOutcomeId = boId;
        return true;
    }

    private static async Task<bool> RelinkBoToPeAsync(EstimationDbContext db, string childJiraKey, int peId)
    {
        var bo = await db.BusinessOutcomes.FirstOrDefaultAsync(b => b.JiraId == childJiraKey);
        if (bo is null || bo.PortfolioEpicId is not null)
        {
            return false;
        }
        bo.PortfolioEpicId = peId;
        return true;
    }

    private static async Task<bool> RelinkPeToSoAsync(EstimationDbContext db, string childJiraKey, int soId)
    {
        var pe = await db.PortfolioEpics.FirstOrDefaultAsync(p => p.JiraId == childJiraKey);
        if (pe is null)
        {
            return false;
        }
        var alreadyLinked = await db.StrategicObjectivePortfolioEpics
            .AnyAsync(l => l.StrategicObjectiveId == soId && l.PortfolioEpicId == pe.Id);
        if (alreadyLinked)
        {
            return false;
        }
        db.StrategicObjectivePortfolioEpics.Add(new StrategicObjectivePortfolioEpic
        {
            StrategicObjectiveId = soId,
            PortfolioEpicId = pe.Id,
        });
        return true;
    }

    public async Task<IReadOnlyList<HierarchyParentInfo>> GetParentInfoAsync(
        IReadOnlyCollection<(HierarchyEntityType ChildType, string ChildJiraKey)> children)
    {
        if (children.Count == 0)
        {
            return Array.Empty<HierarchyParentInfo>();
        }

        await using var db = await _ctx.CreateDbContextAsync();
        var results = new List<HierarchyParentInfo>(children.Count);

        var featureKeys = children
            .Where(c => c.ChildType == HierarchyEntityType.Feature)
            .Select(c => c.ChildJiraKey).Distinct().ToList();
        var boKeys = children
            .Where(c => c.ChildType == HierarchyEntityType.BusinessOutcome)
            .Select(c => c.ChildJiraKey).Distinct().ToList();
        var peKeys = children
            .Where(c => c.ChildType == HierarchyEntityType.PortfolioEpic)
            .Select(c => c.ChildJiraKey).Distinct().ToList();

        if (featureKeys.Count > 0)
        {
            var rows = await db.Features
                .Where(f => f.JiraId != null && featureKeys.Contains(f.JiraId))
                .Select(f => new { f.JiraId, ParentJiraId = f.BusinessOutcome != null ? f.BusinessOutcome.JiraId : null })
                .ToListAsync();
            foreach (var row in rows)
            {
                var parents = string.IsNullOrEmpty(row.ParentJiraId)
                    ? (IReadOnlyList<string>)Array.Empty<string>()
                    : new[] { row.ParentJiraId };
                results.Add(new HierarchyParentInfo(HierarchyEntityType.Feature, row.JiraId!, parents));
            }
        }

        if (boKeys.Count > 0)
        {
            var rows = await db.BusinessOutcomes
                .Where(b => b.JiraId != null && boKeys.Contains(b.JiraId))
                .Select(b => new { b.JiraId, ParentJiraId = b.PortfolioEpic != null ? b.PortfolioEpic.JiraId : null })
                .ToListAsync();
            foreach (var row in rows)
            {
                var parents = string.IsNullOrEmpty(row.ParentJiraId)
                    ? (IReadOnlyList<string>)Array.Empty<string>()
                    : new[] { row.ParentJiraId };
                results.Add(new HierarchyParentInfo(HierarchyEntityType.BusinessOutcome, row.JiraId!, parents));
            }
        }

        if (peKeys.Count > 0)
        {
            var rows = await db.PortfolioEpics
                .Where(p => p.JiraId != null && peKeys.Contains(p.JiraId))
                .Select(p => new
                {
                    p.JiraId,
                    SoKeys = p.StrategicObjectivePortfolioEpics
                        .Where(l => l.StrategicObjective.JiraId != null)
                        .Select(l => l.StrategicObjective.JiraId!)
                        .ToList(),
                })
                .ToListAsync();
            foreach (var row in rows)
            {
                results.Add(new HierarchyParentInfo(
                    HierarchyEntityType.PortfolioEpic,
                    row.JiraId!,
                    row.SoKeys));
            }
        }

        return results;
    }

    public async Task<HierarchyParentFixResult> FixParentLinkAsync(
        HierarchyEntityType childType,
        string childJiraKey,
        string? expectedParentJiraKey)
    {
        if (string.IsNullOrWhiteSpace(childJiraKey))
        {
            throw new ArgumentException("Child Jira key is required.", nameof(childJiraKey));
        }

        await using var db = await _ctx.CreateDbContextAsync();

        var changed = childType switch
        {
            HierarchyEntityType.Feature
                => await FixFeatureParentAsync(db, childJiraKey, expectedParentJiraKey),
            HierarchyEntityType.BusinessOutcome
                => await FixBusinessOutcomeParentAsync(db, childJiraKey, expectedParentJiraKey),
            HierarchyEntityType.PortfolioEpic
                => await FixPortfolioEpicParentAsync(db, childJiraKey, expectedParentJiraKey),
            HierarchyEntityType.StrategicObjective => false,
            _ => false,
        };

        if (changed)
        {
            await db.SaveChangesAsync();
            Log.Information(
                "Hierarchy parent fix: {ChildType} {ChildKey} parent set to {ParentKey}",
                childType, childJiraKey, expectedParentJiraKey ?? "(none)");
        }

        return new HierarchyParentFixResult(childType, childJiraKey, changed);
    }

    private static async Task<bool> FixFeatureParentAsync(EstimationDbContext db, string childJiraKey, string? expectedParentJiraKey)
    {
        var feature = await db.Features.FirstOrDefaultAsync(f => f.JiraId == childJiraKey);
        if (feature is null)
        {
            return false;
        }

        int? newParentId = null;
        if (!string.IsNullOrWhiteSpace(expectedParentJiraKey))
        {
            var parentBo = await db.BusinessOutcomes.FirstOrDefaultAsync(b => b.JiraId == expectedParentJiraKey);
            if (parentBo is null)
            {
                return false;
            }
            newParentId = parentBo.Id;
        }

        if (feature.BusinessOutcomeId == newParentId)
        {
            return false;
        }
        feature.BusinessOutcomeId = newParentId;
        return true;
    }

    private static async Task<bool> FixBusinessOutcomeParentAsync(EstimationDbContext db, string childJiraKey, string? expectedParentJiraKey)
    {
        var bo = await db.BusinessOutcomes.FirstOrDefaultAsync(b => b.JiraId == childJiraKey);
        if (bo is null)
        {
            return false;
        }

        int? newParentId = null;
        if (!string.IsNullOrWhiteSpace(expectedParentJiraKey))
        {
            var parentPe = await db.PortfolioEpics.FirstOrDefaultAsync(p => p.JiraId == expectedParentJiraKey);
            if (parentPe is null)
            {
                return false;
            }
            newParentId = parentPe.Id;
        }

        if (bo.PortfolioEpicId == newParentId)
        {
            return false;
        }
        bo.PortfolioEpicId = newParentId;
        return true;
    }

    private static async Task<bool> FixPortfolioEpicParentAsync(EstimationDbContext db, string childJiraKey, string? expectedParentJiraKey)
    {
        var pe = await db.PortfolioEpics
            .Include(p => p.StrategicObjectivePortfolioEpics)
            .ThenInclude(l => l.StrategicObjective)
            .FirstOrDefaultAsync(p => p.JiraId == childJiraKey);
        if (pe is null)
        {
            return false;
        }

        int? newSoId = null;
        if (!string.IsNullOrWhiteSpace(expectedParentJiraKey))
        {
            var parentSo = await db.StrategicObjectives.FirstOrDefaultAsync(s => s.JiraId == expectedParentJiraKey);
            if (parentSo is null)
            {
                return false;
            }
            newSoId = parentSo.Id;
        }

        var changed = false;

        var toRemove = pe.StrategicObjectivePortfolioEpics
            .Where(l => l.StrategicObjectiveId != newSoId)
            .ToList();
        if (toRemove.Count > 0)
        {
            db.StrategicObjectivePortfolioEpics.RemoveRange(toRemove);
            changed = true;
        }

        if (newSoId.HasValue
            && pe.StrategicObjectivePortfolioEpics.All(l => l.StrategicObjectiveId != newSoId.Value))
        {
            db.StrategicObjectivePortfolioEpics.Add(new StrategicObjectivePortfolioEpic
            {
                StrategicObjectiveId = newSoId.Value,
                PortfolioEpicId = pe.Id,
            });
            changed = true;
        }

        return changed;
    }

    private static void ApplyCommonFields(JiraIssue target, JiraIssueResponse j, string? projectKey)
    {
        target.JiraId = j.Key;
        target.ProjectKey = projectKey;
        target.IssueType = j.IssueType;
        target.Summary = j.Summary ?? j.Key;
        target.Description = j.Description;
        target.Labels = JiraSyncItemMapping.JoinLabels(j.Labels);
        target.Status = j.Status;
        target.JiraUpdated = j.Updated;
        target.TargetStart = j.TargetStart;
        target.TargetEnd = j.TargetEnd;
        target.StoryPoints = j.StoryPoints;
    }
}

using Estimation.Core.Audit;
using Estimation.Core.JiraLogic;
using Estimation.Core.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Estimation.Core.Services;

public record JiraFeatureSyncItem(string JiraKey, string? Summary, string? Description, string? IssueType, string? Labels, string? FeatureName, string? ParentLink, string? Status, DateTime? JiraUpdated, DateTime? TargetStart, DateTime? TargetEnd, int? StoryPoints, string? GfedTeam, string? PlanningIncrement)
{
    /// <summary>
    /// Optional opt-in mask for partial updates. When null, every property is written
    /// (current behavior). When set, only properties whose <see cref="Estimation.Core.JiraLogic.JiraSyncProperties"/>
    /// key is present in the set are written to existing records.
    /// </summary>
    public HashSet<string>? PropertyMask { get; init; }
}

public interface IFeatureService
{
    Task<List<Feature>> GetAllAsync();
    Task<List<Feature>> GetAllWithHierarchyAsync();
    Task<Feature?> GetByIdAsync(int id);
    Task<Feature> CreateAsync(Feature feature);
    Task<Feature> UpdateAsync(Feature feature);
    Task<bool> DeleteAsync(int id);
    Task AddTeamAsync(int featureId, int teamId);
    Task RemoveTeamAsync(int featureId, int teamId);
    Task<FeatureTechnologyStack> AddFeatureTechnologyStackAsync(int featureId, int technologyStackId, int? estimatedEffort);
    Task SetFeatureTechnologyStackEffortAsync(int featureTechnologyStackId, int? estimatedEffort);
    Task RemoveFeatureTechnologyStackAsync(int featureTechnologyStackId);
    Task<JiraSyncResult> SyncFromJiraAsync(string projectKey, List<JiraFeatureSyncItem> items);
    Task MapFeaturesToPiAsync(List<int> featureIds, int? piId);
    Task<Feature> UpsertFromUploadAsync(FeatureUploadData data);
}

public class FeatureUploadData
{
    public int? ExistingFeatureId { get; set; }
    public string? JiraId { get; set; }
    public string? ProjectKey { get; set; }

    public string? FeatureName { get; set; }
    public string? Summary { get; set; }
    public int? Ranking { get; set; }
    public string? Description { get; set; }
    public int? BusinessOutcomeId { get; set; }
    public int? PiId { get; set; }
    public string? Labels { get; set; }
    public int? TeamId { get; set; }
    public string? Comments { get; set; }
    public bool ConnectToJira { get; set; }

    /// <summary>TechnologyStackId -> EstimatedEffort (null means remove)</summary>
    public Dictionary<int, int?> TechStackEfforts { get; set; } = new();
}

public class FeatureService : IFeatureService
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;
    private readonly IAuditUserProvider _auditUser;

    public FeatureService(IDbContextFactory<EstimationDbContext> ctx, IAuditUserProvider auditUser)
    {
        _ctx = ctx;
        _auditUser = auditUser;
    }

    public async Task<List<Feature>> GetAllAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.Features
            .Include(f => f.Pi)
            .Include(f => f.BusinessOutcome)
            .Include(f => f.UnfundedOption)
            .Include(f => f.FeatureTeams).ThenInclude(ft => ft.Team)
            .AsSplitQuery()
            .AsNoTracking()
            .OrderBy(f => f.Ranking).ThenBy(f => f.Name)
            .ToListAsync();
    }

    public async Task<List<Feature>> GetAllWithHierarchyAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.Features
            .Include(f => f.Pi)
            .Include(f => f.BusinessOutcome)
                .ThenInclude(bo => bo!.PortfolioEpic)
                    .ThenInclude(pe => pe!.StrategicObjectivePortfolioEpics)
                        .ThenInclude(ppe => ppe.StrategicObjective)
                            .ThenInclude(pp => pp.CapitalProjectStrategicObjectives)
                                .ThenInclude(cpp => cpp.CapitalProject)
            .Include(f => f.FeatureTeams).ThenInclude(ft => ft.Team)
            .Include(f => f.FeatureTechnologyStacks).ThenInclude(fts => fts.TechnologyStack)
            .AsSplitQuery()
            .AsNoTracking()
            .OrderBy(f => f.Ranking).ThenBy(f => f.Name)
            .ToListAsync();
    }

    public async Task<Feature?> GetByIdAsync(int id)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.Features
            .Include(f => f.Pi)
            .Include(f => f.BusinessOutcome)
            .Include(f => f.FeatureTeams).ThenInclude(ft => ft.Team)
            .Include(f => f.FeatureTechnologyStacks).ThenInclude(fts => fts.TechnologyStack)
            .AsSplitQuery()
            .AsNoTracking().FirstOrDefaultAsync(f => f.Id == id);
    }

    public async Task<Feature> CreateAsync(Feature feature)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        feature.ModifiedBy = _auditUser.GetCurrentUserName();
        feature.ModifiedAt = DateTime.UtcNow;
        db.Features.Add(feature);
        await db.SaveChangesAsync();
        return feature;
    }

    public async Task<Feature> UpdateAsync(Feature feature)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var existing = await db.Features
            .FirstOrDefaultAsync(f => f.Id == feature.Id);

        if (existing is null)
        {
            Log.Warning("Feature {FeatureId} not found", feature.Id);
            throw new KeyNotFoundException($"Feature {feature.Id} not found.");
        }

        existing.JiraId = feature.JiraId;
        existing.ProjectKey = feature.ProjectKey;
        existing.IssueType = feature.IssueType;
        existing.Summary = feature.Summary;
        existing.Name = feature.Name;
        existing.Description = feature.Description;
        existing.Labels = feature.Labels;
        existing.Comments = feature.Comments;
        existing.Ranking = feature.Ranking;
        existing.UnfundedOptionId = feature.UnfundedOptionId;
        existing.BusinessOutcomeId = feature.BusinessOutcomeId;
        existing.PiId = feature.PiId;
        existing.DateExpected = feature.DateExpected;
        existing.IsLinkedToTheJira = feature.IsLinkedToTheJira;
        existing.TargetStart = feature.TargetStart;
        existing.TargetEnd = feature.TargetEnd;
        existing.StoryPoints = feature.StoryPoints;
        existing.ModifiedBy = _auditUser.GetCurrentUserName();
        existing.ModifiedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var f = await db.Features.FindAsync(id);
        if (f is null)
        {
            return false;
        }
        db.Features.Remove(f);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task AddTeamAsync(int featureId, int teamId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        if (!await db.FeatureTeams.AnyAsync(ft => ft.FeatureId == featureId && ft.TeamId == teamId))
        {
            db.FeatureTeams.Add(new FeatureTeam { FeatureId = featureId, TeamId = teamId });
            await db.SaveChangesAsync();
        }
    }

    public async Task RemoveTeamAsync(int featureId, int teamId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var e = await db.FeatureTeams.FirstOrDefaultAsync(
            ft => ft.FeatureId == featureId && ft.TeamId == teamId);
        if (e is not null)
        { db.FeatureTeams.Remove(e); await db.SaveChangesAsync(); }
    }

    public async Task<FeatureTechnologyStack> AddFeatureTechnologyStackAsync(int featureId, int technologyStackId, int? estimatedEffort)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var fts = new FeatureTechnologyStack
        {
            FeatureId = featureId,
            TechnologyStackId = technologyStackId,
            EstimatedEffort = estimatedEffort
        };
        db.FeatureTechnologyStacks.Add(fts);
        await db.SaveChangesAsync();
        return fts;
    }

    public async Task SetFeatureTechnologyStackEffortAsync(int featureTechnologyStackId, int? estimatedEffort)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var e = await db.FeatureTechnologyStacks.FindAsync(featureTechnologyStackId);
        if (e is not null)
        { e.EstimatedEffort = estimatedEffort; await db.SaveChangesAsync(); }
    }

    public async Task RemoveFeatureTechnologyStackAsync(int featureTechnologyStackId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var e = await db.FeatureTechnologyStacks.FindAsync(featureTechnologyStackId);
        if (e is not null)
        { db.FeatureTechnologyStacks.Remove(e); await db.SaveChangesAsync(); }
    }

    public async Task MapFeaturesToPiAsync(List<int> featureIds, int? piId)
    {
        if (featureIds.Count == 0)
        {
            return;
        }

        await using var db = await _ctx.CreateDbContextAsync();

        var features = await db.Features
            .Where(f => featureIds.Contains(f.Id))
            .ToListAsync();

        foreach (var feature in features)
        {
            feature.PiId = piId;
            feature.ModifiedBy = _auditUser.GetCurrentUserName();
            feature.ModifiedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
    }

    public async Task<Feature> UpsertFromUploadAsync(FeatureUploadData data)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var userName = _auditUser.GetCurrentUserName();

        Feature feature;
        if (data.ExistingFeatureId.HasValue)
        {
            feature = await db.Features
                .Include(f => f.FeatureTeams)
                .Include(f => f.FeatureTechnologyStacks)
                .FirstOrDefaultAsync(f => f.Id == data.ExistingFeatureId.Value)
                ?? throw new KeyNotFoundException($"Feature {data.ExistingFeatureId.Value} not found.");

            feature.JiraId = data.JiraId?.Trim();
            feature.ProjectKey = data.ProjectKey?.Trim();
            feature.Summary = data.Summary?.Trim();
            feature.Name = data.FeatureName?.Trim();
            feature.Ranking = data.Ranking;
            feature.Description = data.Description?.Trim();
            feature.BusinessOutcomeId = data.BusinessOutcomeId;
            feature.PiId = data.PiId;
            feature.Labels = data.Labels?.Trim();
            feature.Comments = data.Comments?.Trim();
            if (data.ConnectToJira)
            {
                feature.IsLinkedToTheJira = true;
            }
            feature.ModifiedBy = userName;
            feature.ModifiedAt = DateTime.UtcNow;
        }
        else
        {
            feature = new Feature
            {
                JiraId = data.JiraId?.Trim(),
                ProjectKey = data.ProjectKey?.Trim(),
                Name = data.FeatureName?.Trim(),
                Summary = data.Summary?.Trim(),
                Ranking = data.Ranking,
                Description = data.Description?.Trim(),
                BusinessOutcomeId = data.BusinessOutcomeId,
                PiId = data.PiId,
                Labels = data.Labels?.Trim(),
                Comments = data.Comments?.Trim(),
                IsLinkedToTheJira = data.ConnectToJira ? true : null,
                ModifiedBy = userName,
                ModifiedAt = DateTime.UtcNow
            };
            db.Features.Add(feature);
            await db.SaveChangesAsync(); // get feature.Id

            feature = await db.Features
                .Include(f => f.FeatureTeams)
                .Include(f => f.FeatureTechnologyStacks)
                .FirstAsync(f => f.Id == feature.Id);
        }

        // --- Team: replace all with the single uploaded team ---
        db.FeatureTeams.RemoveRange(feature.FeatureTeams);
        if (data.TeamId.HasValue)
        {
            db.FeatureTeams.Add(new FeatureTeam { FeatureId = feature.Id, TeamId = data.TeamId.Value });
        }

        // --- Tech stacks ---
        var existingTs = feature.FeatureTechnologyStacks.ToDictionary(fts => fts.TechnologyStackId);

        foreach (var (tsId, effort) in data.TechStackEfforts)
        {
            if (effort.HasValue)
            {
                if (existingTs.TryGetValue(tsId, out var existing))
                {
                    existing.EstimatedEffort = effort.Value;
                }
                else
                {
                    db.FeatureTechnologyStacks.Add(new FeatureTechnologyStack
                    {
                        FeatureId = feature.Id,
                        TechnologyStackId = tsId,
                        EstimatedEffort = effort.Value
                    });
                }
            }
            else
            {
                // Empty cell: remove if it existed
                if (existingTs.TryGetValue(tsId, out var toRemove))
                {
                    db.FeatureTechnologyStacks.Remove(toRemove);
                }
            }
        }

        await db.SaveChangesAsync();
        return feature;
    }

    public async Task<JiraSyncResult> SyncFromJiraAsync(string projectKey, List<JiraFeatureSyncItem> items)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var jiraKeys = items.Select(i => i.JiraKey).ToList();
        var existing = await db.Features
            .Include(f => f.FeatureTeams)
            .Where(f => f.JiraId != null && jiraKeys.Contains(f.JiraId))
            .ToListAsync();
        var existingByJiraId = existing.ToDictionary(f => f.JiraId!);

        // Load BusinessOutcomes by JiraId for parent linking
        var parentLinks = items.Where(i => !string.IsNullOrEmpty(i.ParentLink)).Select(i => i.ParentLink!).Distinct().ToList();
        var boByJiraId = parentLinks.Count > 0
            ? await db.BusinessOutcomes
                .Where(bo => bo.JiraId != null && parentLinks.Contains(bo.JiraId))
                .ToDictionaryAsync(bo => bo.JiraId!)
            : new Dictionary<string, BusinessOutcome>();

        var allTeams = await db.Teams.AsNoTracking().ToListAsync();
        var allPis = await db.Pis.ToListAsync();
        var piByName = allPis.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        // Pre-create any PIs referenced by Jira that don't exist yet.
        var newPiNames = items
            .Select(i => i.PlanningIncrement?.Trim())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(n => !piByName.ContainsKey(n!))
            .ToList();

        foreach (var name in newPiNames)
        {
            var newPi = new Pi { Name = name! };
            db.Pis.Add(newPi);
            piByName[name!] = newPi;
        }

        if (newPiNames.Count > 0)
        {
            await db.SaveChangesAsync();
        }

        int created = 0, updated = 0, linked = 0;

        foreach (var item in items)
        {
            var resolvedTeamIds = ResolveTeamIds(item.GfedTeam, allTeams);
            var resolvedPiId = ResolvePiId(item.PlanningIncrement, piByName);

            if (existingByJiraId.TryGetValue(item.JiraKey, out var existingF))
            {
                var mask = item.PropertyMask;
                if (ShouldWrite(mask, JiraSyncProperties.Summary))
                {
                    existingF.Summary = item.Summary ?? existingF.Summary;
                }
                if (ShouldWrite(mask, JiraSyncProperties.Description))
                {
                    existingF.Description = item.Description;
                }
                if (ShouldWrite(mask, JiraSyncProperties.Labels))
                {
                    existingF.Labels = item.Labels;
                }
                if (ShouldWrite(mask, JiraSyncProperties.Status))
                {
                    existingF.Status = item.Status;
                }
                existingF.JiraUpdated = item.JiraUpdated;
                if (ShouldWrite(mask, JiraSyncProperties.TargetStart))
                {
                    existingF.TargetStart = item.TargetStart;
                }
                if (ShouldWrite(mask, JiraSyncProperties.TargetEnd))
                {
                    existingF.TargetEnd = item.TargetEnd;
                }
                if (ShouldWrite(mask, JiraSyncProperties.StoryPoints))
                {
                    existingF.StoryPoints = item.StoryPoints;
                }
                if (ShouldWrite(mask, JiraSyncProperties.Pi))
                {
                    existingF.PiId = resolvedPiId;
                }
                if (mask is null && !string.IsNullOrWhiteSpace(item.FeatureName))
                {
                    existingF.Name = item.FeatureName;
                }
                updated++;

                if (!string.IsNullOrEmpty(item.ParentLink) && boByJiraId.TryGetValue(item.ParentLink, out var parentBo)
                    && existingF.BusinessOutcomeId != parentBo.Id)
                {
                    existingF.BusinessOutcomeId = parentBo.Id;
                    linked++;
                }

                if (ShouldWrite(mask, JiraSyncProperties.Teams))
                {
                    db.FeatureTeams.RemoveRange(existingF.FeatureTeams);
                    foreach (var teamId in resolvedTeamIds)
                    {
                        db.FeatureTeams.Add(new FeatureTeam { FeatureId = existingF.Id, TeamId = teamId });
                    }
                }
            }
            else
            {
                var f = new Feature
                {
                    JiraId = item.JiraKey,
                    ProjectKey = projectKey,
                    IssueType = item.IssueType,
                    Summary = item.Summary ?? item.JiraKey,
                    Description = item.Description,
                    Labels = item.Labels,
                    Name = item.FeatureName,
                    Status = item.Status,
                    IsLinkedToTheJira = true,
                    JiraUpdated = item.JiraUpdated,
                    TargetStart = item.TargetStart,
                    TargetEnd = item.TargetEnd,
                    StoryPoints = item.StoryPoints,
                    PiId = resolvedPiId,
                };

                if (!string.IsNullOrEmpty(item.ParentLink) && boByJiraId.TryGetValue(item.ParentLink, out var parentBo))
                {
                    f.BusinessOutcomeId = parentBo.Id;
                    linked++;
                }

                foreach (var teamId in resolvedTeamIds)
                {
                    f.FeatureTeams.Add(new FeatureTeam { TeamId = teamId });
                }

                db.Features.Add(f);
                created++;
            }
        }

        await db.SaveChangesAsync();

        Log.Information("Jira Feature sync: created={Created}, updated={Updated}, linked={Linked}",
            created, updated, linked);

        return new JiraSyncResult(created, updated, linked);
    }

    private static List<int> ResolveTeamIds(string? gfedTeam, List<Team> allTeams)
    {
        var ids = new List<int>();
        if (string.IsNullOrWhiteSpace(gfedTeam))
        {
            return ids;
        }

        var jiraTeams = gfedTeam.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var jt in jiraTeams)
        {
            // DB team Name is a substring of the Jira value (e.g. DB "Neon" matches Jira "CFT-Neon").
            // Prefer the longest match to disambiguate.
            var match = allTeams
                .Where(t => !string.IsNullOrEmpty(t.Name) && jt.Contains(t.Name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(t => t.Name.Length)
                .FirstOrDefault();

            if (match is not null && !ids.Contains(match.Id))
            {
                ids.Add(match.Id);
            }
        }

        return ids;
    }

    private static int? ResolvePiId(string? planningIncrement, Dictionary<string, Pi> piByName)
    {
        if (string.IsNullOrWhiteSpace(planningIncrement))
        {
            return null;
        }

        return piByName.TryGetValue(planningIncrement.Trim(), out var pi) ? pi.Id : null;
    }

    private static bool ShouldWrite(HashSet<string>? mask, string property)
    {
        return mask is null || mask.Contains(property);
    }
}

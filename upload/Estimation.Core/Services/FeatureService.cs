using Estimation.Core.Audit;
using Estimation.Core.JiraLogic;
using Estimation.Core.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Estimation.Core.Services;

public record JiraFeatureSyncItem(string JiraKey, string? Summary, string? Description, string? AcceptanceCriteria, string? NavigatorId, string? IssueType, string? Labels, string? FeatureName, string? RagExplain, string? ParentLink, string? Status, DateTime? JiraUpdated, DateTime? TargetStart, DateTime? TargetEnd, int? StoryPoints, string? GfedTeam, string? PlanningIncrement)
    : IJiraScalarSyncItem
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
    Task<HashSet<string>> GetExistingJiraIdsAsync();
    Task<bool> JiraIdExistsAsync(string jiraId);
    Task<Feature?> GetByIdAsync(int id);
    Task<Feature> CreateAsync(Feature feature);
    Task<Feature> UpdateAsync(Feature feature);
    Task<bool> DeleteAsync(int id);
    Task AddTeamAsync(int featureId, int teamId);
    Task RemoveTeamAsync(int featureId, int teamId);
    Task SetFeatureTeamStoryPointsAsync(int featureId, int teamId, int? storyPoints);
    Task SetPrimaryTeamAsync(int featureId, int teamId, bool isPrimary);
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
    public string? AcceptanceCriteria { get; set; }
    public int? BusinessOutcomeId { get; set; }
    public string? Labels { get; set; }
    public List<int> TeamIds { get; set; } = new();
    public string? Status { get; set; }

    /// <summary>
    /// Requirement Status name. Resolved to an existing RequirementStatus (or a newly-created one)
    /// on save; a blank value clears the assignment. Only applied when its column was included.
    /// </summary>
    public string? RequirementStatus { get; set; }
    public string? Comments { get; set; }
    public DateTime? TargetStart { get; set; }
    public DateTime? TargetEnd { get; set; }
    public DateTime? DateExpected { get; set; }
    public int? StoryPoints { get; set; }
    public string? RagExplain { get; set; }
    public string? Dependencies { get; set; }
    public bool ConnectToJira { get; set; }

    /// <summary>
    /// Columns the upload applied. Only applied columns are written; columns the user did not
    /// include keep their existing values. Defaults to "all" for callers that set every field.
    /// </summary>
    public FeatureUploadColumnSelection AppliedColumns { get; set; } = FeatureUploadColumnSelection.All();

    /// <summary>TechnologyStackId -> EstimatedEffort (null means remove). Only applied when the
    /// TechStack column was included in the upload.</summary>
    public Dictionary<int, int?> TechStackEfforts { get; set; } = new();

    /// <summary>Default status assigned to a newly-created feature when none is supplied.</summary>
    public const string DefaultStatus = "Backlog";
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
            .Include(f => f.PiObjective)
            .Include(f => f.RequirementStatus)
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

    public async Task<HashSet<string>> GetExistingJiraIdsAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var ids = await db.Features.AsNoTracking()
            .Where(f => f.JiraId != null && f.JiraId != "")
            .Select(f => f.JiraId!)
            .ToListAsync();
        return new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<bool> JiraIdExistsAsync(string jiraId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.Features.AnyAsync(f => f.JiraId == jiraId);
    }

    public async Task<Feature?> GetByIdAsync(int id)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.Features
            .Include(f => f.Pi)
            .Include(f => f.PiObjective)
            .Include(f => f.RequirementStatus)
            .Include(f => f.BusinessOutcome)
            .Include(f => f.FeatureTeams).ThenInclude(ft => ft.Team)
            .Include(f => f.FeatureTechnologyStacks).ThenInclude(fts => fts.TechnologyStack)
            .AsSplitQuery()
            .AsNoTracking().FirstOrDefaultAsync(f => f.Id == id);
    }

    public async Task<Feature> CreateAsync(Feature feature)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        if (!string.IsNullOrWhiteSpace(feature.JiraId))
        {
            var jiraId = feature.JiraId.Trim();
            if (await db.Features.AnyAsync(f => f.JiraId == jiraId))
            {
                throw new InvalidOperationException(
                    $"A Feature with Jira ID '{jiraId}' already exists in the database.");
            }
        }

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
        existing.RagExplain = feature.RagExplain;
        existing.Description = feature.Description;
        existing.AcceptanceCriteria = feature.AcceptanceCriteria;
        existing.NavigatorId = feature.NavigatorId;
        existing.Labels = feature.Labels;
        existing.Status = feature.Status;
        existing.Comments = feature.Comments;
        existing.Dependencies = feature.Dependencies;
        existing.RequirementStatusId = feature.RequirementStatusId;
        existing.Ranking = feature.Ranking;
        existing.UnfundedOptionId = feature.UnfundedOptionId;
        existing.BusinessOutcomeId = feature.BusinessOutcomeId;
        existing.PiId = feature.PiId;
        existing.PiObjectiveId = feature.PiObjectiveId;
        existing.DateExpected = feature.DateExpected;
        existing.IsLinkedToTheJira = feature.IsLinkedToTheJira;
        existing.JiraUpdated = feature.JiraUpdated;
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

    public async Task SetFeatureTeamStoryPointsAsync(int featureId, int teamId, int? storyPoints)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var e = await db.FeatureTeams.FirstOrDefaultAsync(
            ft => ft.FeatureId == featureId && ft.TeamId == teamId);
        if (e is not null)
        {
            e.StoryPoints = storyPoints;
            await db.SaveChangesAsync();
        }
    }

    public async Task SetPrimaryTeamAsync(int featureId, int teamId, bool isPrimary)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var teams = await db.FeatureTeams
            .Where(ft => ft.FeatureId == featureId)
            .ToListAsync();

        foreach (var ft in teams)
        {
            if (ft.TeamId == teamId)
            {
                ft.IsPrimary = isPrimary ? true : null;
            }
            else if (isPrimary)
            {
                // Only one team can be primary per feature.
                ft.IsPrimary = null;
            }
        }

        await db.SaveChangesAsync();
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

        bool Has(FeatureUploadColumn column) => data.AppliedColumns.Includes(column);

        // Resolve the Requirement Status name to an existing (or newly-created) lookup id once, so both
        // the create and update branches share it. Null when the column was not applied or the cell is blank.
        int? requirementStatusId = Has(FeatureUploadColumn.RequirementStatus)
            ? await ResolveRequirementStatusIdAsync(db, data.RequirementStatus)
            : null;

        Feature feature;
        if (data.ExistingFeatureId.HasValue)
        {
            feature = await db.Features
                .Include(f => f.FeatureTeams)
                .Include(f => f.FeatureTechnologyStacks)
                .FirstOrDefaultAsync(f => f.Id == data.ExistingFeatureId.Value)
                ?? throw new KeyNotFoundException($"Feature {data.ExistingFeatureId.Value} not found.");

            feature.JiraId = data.JiraId?.Trim();

            // An existing record whose Project is already populated cannot be changed from Excel.
            if (Has(FeatureUploadColumn.ProjectKey) && string.IsNullOrWhiteSpace(feature.ProjectKey))
            {
                feature.ProjectKey = data.ProjectKey?.Trim();
            }
            // Summary is required, so a blank uploaded value keeps the existing summary.
            if (Has(FeatureUploadColumn.Summary) && !string.IsNullOrWhiteSpace(data.Summary))
            {
                feature.Summary = data.Summary.Trim();
            }
            if (Has(FeatureUploadColumn.FeatureName))
            {
                feature.Name = data.FeatureName?.Trim();
            }
            if (Has(FeatureUploadColumn.Ranking))
            {
                feature.Ranking = data.Ranking;
            }
            if (Has(FeatureUploadColumn.Description))
            {
                feature.Description = data.Description?.Trim();
            }
            if (Has(FeatureUploadColumn.AcceptanceCriteria))
            {
                feature.AcceptanceCriteria = data.AcceptanceCriteria?.Trim();
            }
            if (Has(FeatureUploadColumn.BusinessOutcome))
            {
                feature.BusinessOutcomeId = data.BusinessOutcomeId;
            }
            // PI mapping is managed exclusively via "Map to PI" on the feature list, so the
            // upload leaves any existing PiId untouched.
            if (Has(FeatureUploadColumn.Labels))
            {
                feature.Labels = data.Labels?.Trim();
            }
            // Status: the parse layer resolves the effective status (uploaded value, the existing
            // status when the cell is blank, or "Backlog" when neither is set), so it is never blanked.
            if (Has(FeatureUploadColumn.Status) && !string.IsNullOrWhiteSpace(data.Status))
            {
                feature.Status = data.Status.Trim();
            }
            // Requirement Status: applied column writes the resolved id (a blank cell clears it).
            if (Has(FeatureUploadColumn.RequirementStatus))
            {
                feature.RequirementStatusId = requirementStatusId;
            }
            if (Has(FeatureUploadColumn.Comments))
            {
                feature.Comments = data.Comments?.Trim();
            }
            if (Has(FeatureUploadColumn.TargetStart))
            {
                feature.TargetStart = data.TargetStart;
            }
            if (Has(FeatureUploadColumn.TargetEnd))
            {
                feature.TargetEnd = data.TargetEnd;
            }
            if (Has(FeatureUploadColumn.DateExpected))
            {
                feature.DateExpected = data.DateExpected;
            }
            if (Has(FeatureUploadColumn.StoryPoints))
            {
                feature.StoryPoints = data.StoryPoints;
            }
            if (Has(FeatureUploadColumn.RagExplain))
            {
                feature.RagExplain = data.RagExplain?.Trim();
            }
            if (Has(FeatureUploadColumn.Dependencies))
            {
                feature.Dependencies = data.Dependencies?.Trim();
            }
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
                ProjectKey = Has(FeatureUploadColumn.ProjectKey) ? data.ProjectKey?.Trim() : null,
                IssueType = JiraIssueTypes.Feature, // uploaded issues default to the Feature issue type
                Name = Has(FeatureUploadColumn.FeatureName) ? data.FeatureName?.Trim() : null,
                Summary = data.Summary?.Trim(),
                Ranking = Has(FeatureUploadColumn.Ranking) ? data.Ranking : null,
                Description = Has(FeatureUploadColumn.Description) ? data.Description?.Trim() : null,
                AcceptanceCriteria = Has(FeatureUploadColumn.AcceptanceCriteria) ? data.AcceptanceCriteria?.Trim() : null,
                BusinessOutcomeId = Has(FeatureUploadColumn.BusinessOutcome) ? data.BusinessOutcomeId : null,
                Labels = Has(FeatureUploadColumn.Labels) ? data.Labels?.Trim() : null,
                // Default the status to "Backlog" when none is supplied on a new feature.
                Status = Has(FeatureUploadColumn.Status) && !string.IsNullOrWhiteSpace(data.Status)
                    ? data.Status.Trim()
                    : FeatureUploadData.DefaultStatus,
                RequirementStatusId = requirementStatusId,
                Comments = Has(FeatureUploadColumn.Comments) ? data.Comments?.Trim() : null,
                TargetStart = Has(FeatureUploadColumn.TargetStart) ? data.TargetStart : null,
                TargetEnd = Has(FeatureUploadColumn.TargetEnd) ? data.TargetEnd : null,
                DateExpected = Has(FeatureUploadColumn.DateExpected) ? data.DateExpected : null,
                StoryPoints = Has(FeatureUploadColumn.StoryPoints) ? data.StoryPoints : null,
                RagExplain = Has(FeatureUploadColumn.RagExplain) ? data.RagExplain?.Trim() : null,
                Dependencies = Has(FeatureUploadColumn.Dependencies) ? data.Dependencies?.Trim() : null,
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

        // --- Teams: replace all with the uploaded set (only when the Team column was applied) ---
        if (Has(FeatureUploadColumn.Team))
        {
            db.FeatureTeams.RemoveRange(feature.FeatureTeams);
            foreach (var teamId in data.TeamIds.Distinct())
            {
                db.FeatureTeams.Add(new FeatureTeam { FeatureId = feature.Id, TeamId = teamId });
            }
        }

        // --- Tech stacks (only when the TechStack column was applied) ---
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

    // Find-or-create a RequirementStatus by name within the upload's db context, mirroring the
    // feature edit form's autocomplete (RequirementStatusService.GetOrCreateByNameAsync). Returns
    // null for blank input; truncates to the column's 30-char limit.
    private static async Task<int?> ResolveRequirementStatusIdAsync(EstimationDbContext db, string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > 30)
        {
            trimmed = trimmed[..30];
        }

        var existing = await db.RequirementStatuses.FirstOrDefaultAsync(rs => rs.Name == trimmed);
        if (existing is not null)
        {
            return existing.Id;
        }

        var created = new RequirementStatus { Name = trimmed };
        db.RequirementStatuses.Add(created);
        await db.SaveChangesAsync();
        return created.Id;
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

        var parentLinks = items.Where(i => !string.IsNullOrEmpty(i.ParentLink)).Select(i => i.ParentLink!).Distinct().ToList();
        var boByJiraId = parentLinks.Count > 0
            ? await db.BusinessOutcomes
                .Where(bo => bo.JiraId != null && parentLinks.Contains(bo.JiraId))
                .ToDictionaryAsync(bo => bo.JiraId!)
            : new Dictionary<string, BusinessOutcome>();

        var allTeams = await db.Teams.AsNoTracking().ToListAsync();
        var piByName = (await db.Pis.ToListAsync())
            .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

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
                JiraScalarApply.ApplyToExisting(existingF, item);

                var mask = item.PropertyMask;
                if (JiraScalarApply.ShouldWrite(mask, JiraSyncProperties.Project))
                {
                    existingF.ProjectKey = JiraSyncItemMapping.ProjectKeyFromJiraKey(item.JiraKey);
                }
                if (JiraScalarApply.ShouldWrite(mask, JiraSyncProperties.Pi))
                {
                    existingF.PiId = resolvedPiId;
                }
                if (mask is null && !string.IsNullOrWhiteSpace(item.FeatureName))
                {
                    existingF.Name = item.FeatureName;
                }
                if (JiraScalarApply.ShouldWrite(mask, JiraSyncProperties.RagExplain))
                {
                    existingF.RagExplain = item.RagExplain;
                }
                updated++;

                if (!string.IsNullOrEmpty(item.ParentLink) && boByJiraId.TryGetValue(item.ParentLink, out var parentBo)
                    && existingF.BusinessOutcomeId != parentBo.Id)
                {
                    existingF.BusinessOutcomeId = parentBo.Id;
                    linked++;
                }

                if (JiraScalarApply.ShouldWrite(mask, JiraSyncProperties.Teams))
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
                    Name = item.FeatureName,
                    RagExplain = item.RagExplain,
                    IsLinkedToTheJira = true,
                    PiId = resolvedPiId,
                };
                JiraScalarApply.ApplyToNew(f, item, projectKey);

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
        foreach (var jt in JiraTeamMatcher.SplitJiraValue(gfedTeam))
        {
            var match = JiraTeamMatcher.Match(jt, allTeams);
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
}

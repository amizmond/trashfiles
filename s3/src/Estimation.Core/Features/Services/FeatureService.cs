using Estimation.Core.Administration.Audit;
using Estimation.Core.Features.Models;
using Estimation.Core.JiraIntegration.Client;
using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Train.Models;
using Estimation.Core.Train.Services;
using Estimation.Core.Resources.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Estimation.Core.Features.Services;

public record JiraFeatureSyncItem(string JiraKey, string? Summary, string? Description, string? AcceptanceCriteria, string? NavigatorId, string? IssueType, string? Labels, string? FeatureName, string? RagExplain, string? ParentLink, string? Status, DateTime? JiraUpdated, DateTime? TargetStart, DateTime? TargetEnd, int? StoryPoints, string? GfedTeam, string? PlanningIncrement)
    : IJiraScalarSyncItem
{
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
    Task SyncFeatureTeamsAsync(int featureId, IReadOnlyCollection<int> teamIds);
    Task<Dictionary<int, HashSet<int>>> GetTeamSprintSelectionsAsync(int featureId);
    Task SyncTeamSprintsAsync(int featureId, IReadOnlyDictionary<int, HashSet<int>> selections);
    Task SetFeatureTeamStoryPointsAsync(int featureId, int teamId, int? storyPoints);
    Task SetPrimaryTeamAsync(int featureId, int teamId, bool isPrimary);
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

    public string? RequirementStatus { get; set; }

    public int? TechnicalApprovalId { get; set; }

    public int? UnfundedOptionId { get; set; }

    public string? PiObjective { get; set; }
    public DateTime? TargetStart { get; set; }
    public DateTime? TargetEnd { get; set; }
    public DateTime? DateExpected { get; set; }
    public int? StoryPoints { get; set; }
    public string? RagExplain { get; set; }
    public string? Dependencies { get; set; }
    public bool ExternalDependencies { get; set; }
    public bool ConnectToJira { get; set; }

    public FeatureUploadColumnSelection AppliedColumns { get; set; } = FeatureUploadColumnSelection.All();

    public Dictionary<int, int?> TechStackEfforts { get; set; } = new();

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
            .Include(f => f.TechnicalApproval)
            .Include(f => f.UnfundedOption)
            .Include(f => f.BusinessOutcome)
                .ThenInclude(bo => bo!.PortfolioEpic)
                    .ThenInclude(pe => pe!.StrategicObjectivePortfolioEpics)
                        .ThenInclude(ppe => ppe.StrategicObjective)
                            .ThenInclude(pp => pp.CapitalProjectStrategicObjectives)
                                .ThenInclude(cpp => cpp.CapitalProject)
            .Include(f => f.FeatureTeams).ThenInclude(ft => ft.Team)
            .Include(f => f.FeatureTeams)
                .ThenInclude(ft => ft.TechnologyStacks)
                    .ThenInclude(ftts => ftts.SkillValues)
                        .ThenInclude(sv => sv.Skill)
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
            .Include(f => f.TechnicalApproval)
            .Include(f => f.BusinessOutcome)
            .Include(f => f.FeatureTeams).ThenInclude(ft => ft.Team)
            .Include(f => f.FeatureSkills).ThenInclude(fs => fs.Skill)
            .Include(f => f.FeatureTeams).ThenInclude(ft => ft.TechnologyStacks).ThenInclude(ftts => ftts.TechnologyStack)
                .ThenInclude(ts => ts.TechnologyStackSkills).ThenInclude(tss => tss.Skill)
            .Include(f => f.FeatureTeams).ThenInclude(ft => ft.TechnologyStacks).ThenInclude(ftts => ftts.SkillValues)
                .ThenInclude(sv => sv.Skill)
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
        existing.Dependencies = feature.Dependencies;
        existing.ExternalDependencies = feature.ExternalDependencies;
        existing.RequirementStatusId = feature.RequirementStatusId;
        existing.TechnicalApprovalId = feature.TechnicalApprovalId ?? TechnicalApproval.DefaultId;
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

    public async Task SyncFeatureTeamsAsync(int featureId, IReadOnlyCollection<int> teamIds)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var existing = await db.FeatureTeams.Where(ft => ft.FeatureId == featureId).ToListAsync();
        var existingIds = existing.Select(ft => ft.TeamId).ToHashSet();
        var target = teamIds.Distinct().ToHashSet();

        var toRemove = existing.Where(ft => !target.Contains(ft.TeamId)).ToList();
        if (toRemove.Count > 0)
        {
            db.FeatureTeams.RemoveRange(toRemove);
        }

        foreach (var teamId in target.Where(id => !existingIds.Contains(id)))
        {
            db.FeatureTeams.Add(new FeatureTeam { FeatureId = featureId, TeamId = teamId });
        }

        await db.SaveChangesAsync();
    }

    public async Task<Dictionary<int, HashSet<int>>> GetTeamSprintSelectionsAsync(int featureId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var rows = await db.FeatureTeamSprints
            .Where(x => x.FeatureId == featureId)
            .Select(x => new { x.TeamId, x.SprintId })
            .ToListAsync();

        return rows
            .GroupBy(r => r.TeamId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.SprintId).ToHashSet());
    }

    public async Task SyncTeamSprintsAsync(int featureId, IReadOnlyDictionary<int, HashSet<int>> selections)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var teamIds = await db.FeatureTeams
            .Where(ft => ft.FeatureId == featureId)
            .Select(ft => ft.TeamId)
            .ToListAsync();

        var requested = selections
            .Where(kv => teamIds.Contains(kv.Key))
            .SelectMany(kv => kv.Value.Select(sprintId => (TeamId: kv.Key, SprintId: sprintId)))
            .ToList();
        var requestedSprintIds = requested.Select(r => r.SprintId).Distinct().ToList();

        var sprintTeams = await db.Sprints
            .Where(s => requestedSprintIds.Contains(s.Id))
            .Select(s => new { s.Id, s.TeamId })
            .ToDictionaryAsync(s => s.Id, s => s.TeamId);

        var desired = requested
            .Where(r => sprintTeams.TryGetValue(r.SprintId, out var ownerTeam) && ownerTeam == r.TeamId)
            .ToHashSet();

        var existing = await db.FeatureTeamSprints.Where(x => x.FeatureId == featureId).ToListAsync();
        var toRemove = existing.Where(x => !desired.Contains((x.TeamId, x.SprintId))).ToList();
        if (toRemove.Count > 0)
        {
            db.FeatureTeamSprints.RemoveRange(toRemove);
        }

        var existingKeys = existing.Select(x => (x.TeamId, x.SprintId)).ToHashSet();
        foreach (var (teamId, sprintId) in desired.Where(d => !existingKeys.Contains(d)))
        {
            db.FeatureTeamSprints.Add(new FeatureTeamSprint { FeatureId = featureId, TeamId = teamId, SprintId = sprintId });
        }

        await db.SaveChangesAsync();
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
                ft.IsPrimary = null;
            }
        }

        await db.SaveChangesAsync();
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

        int? requirementStatusId = Has(FeatureUploadColumn.RequirementStatus)
            ? await ResolveRequirementStatusIdAsync(db, data.RequirementStatus)
            : null;

        int? piObjectiveId = Has(FeatureUploadColumn.PiObjective)
            ? await ResolvePiObjectiveIdAsync(db, data.PiObjective)
            : null;

        Feature feature;
        if (data.ExistingFeatureId.HasValue)
        {
            feature = await db.Features
                .Include(f => f.FeatureTeams)
                .Include(f => f.FeatureSkills)
                .FirstOrDefaultAsync(f => f.Id == data.ExistingFeatureId.Value)
                ?? throw new KeyNotFoundException($"Feature {data.ExistingFeatureId.Value} not found.");

            feature.JiraId = data.JiraId?.Trim();

            if (Has(FeatureUploadColumn.ProjectKey) && string.IsNullOrWhiteSpace(feature.ProjectKey))
            {
                feature.ProjectKey = data.ProjectKey?.Trim();
            }
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
            if (Has(FeatureUploadColumn.Labels))
            {
                feature.Labels = data.Labels?.Trim();
            }
            if (Has(FeatureUploadColumn.Status) && !string.IsNullOrWhiteSpace(data.Status))
            {
                feature.Status = data.Status.Trim();
            }
            if (Has(FeatureUploadColumn.RequirementStatus))
            {
                feature.RequirementStatusId = requirementStatusId;
            }
            if (Has(FeatureUploadColumn.TechnicalApproval))
            {
                feature.TechnicalApprovalId = data.TechnicalApprovalId ?? TechnicalApproval.DefaultId;
            }
            if (Has(FeatureUploadColumn.FundingStatus))
            {
                feature.UnfundedOptionId = data.UnfundedOptionId;
            }
            if (Has(FeatureUploadColumn.PiObjective))
            {
                feature.PiObjectiveId = piObjectiveId;
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
            if (Has(FeatureUploadColumn.ExternalDependencies))
            {
                feature.ExternalDependencies = data.ExternalDependencies;
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
                IssueType = JiraIssueTypes.Feature,
                Name = Has(FeatureUploadColumn.FeatureName) ? data.FeatureName?.Trim() : null,
                Summary = data.Summary?.Trim(),
                Ranking = Has(FeatureUploadColumn.Ranking) ? data.Ranking : null,
                Description = Has(FeatureUploadColumn.Description) ? data.Description?.Trim() : null,
                AcceptanceCriteria = Has(FeatureUploadColumn.AcceptanceCriteria) ? data.AcceptanceCriteria?.Trim() : null,
                BusinessOutcomeId = Has(FeatureUploadColumn.BusinessOutcome) ? data.BusinessOutcomeId : null,
                Labels = Has(FeatureUploadColumn.Labels) ? data.Labels?.Trim() : null,
                Status = Has(FeatureUploadColumn.Status) && !string.IsNullOrWhiteSpace(data.Status)
                    ? data.Status.Trim()
                    : FeatureUploadData.DefaultStatus,
                RequirementStatusId = requirementStatusId,
                TechnicalApprovalId = (Has(FeatureUploadColumn.TechnicalApproval) ? data.TechnicalApprovalId : null)
                    ?? TechnicalApproval.DefaultId,
                UnfundedOptionId = Has(FeatureUploadColumn.FundingStatus) ? data.UnfundedOptionId : null,
                PiObjectiveId = piObjectiveId,
                TargetStart = Has(FeatureUploadColumn.TargetStart) ? data.TargetStart : null,
                TargetEnd = Has(FeatureUploadColumn.TargetEnd) ? data.TargetEnd : null,
                DateExpected = Has(FeatureUploadColumn.DateExpected) ? data.DateExpected : null,
                StoryPoints = Has(FeatureUploadColumn.StoryPoints) ? data.StoryPoints : null,
                RagExplain = Has(FeatureUploadColumn.RagExplain) ? data.RagExplain?.Trim() : null,
                Dependencies = Has(FeatureUploadColumn.Dependencies) ? data.Dependencies?.Trim() : null,
                ExternalDependencies = Has(FeatureUploadColumn.ExternalDependencies) && data.ExternalDependencies,
                IsLinkedToTheJira = data.ConnectToJira ? true : null,
                ModifiedBy = userName,
                ModifiedAt = DateTime.UtcNow
            };
            db.Features.Add(feature);
            await db.SaveChangesAsync();

            feature = await db.Features
                .Include(f => f.FeatureTeams)
                .Include(f => f.FeatureSkills)
                .FirstAsync(f => f.Id == feature.Id);
        }

        if (Has(FeatureUploadColumn.Team))
        {
            var desiredTeamIds = data.TeamIds.ToHashSet();
            var currentTeamIds = feature.FeatureTeams.Select(ft => ft.TeamId).ToHashSet();

            foreach (var link in feature.FeatureTeams.Where(ft => !desiredTeamIds.Contains(ft.TeamId)).ToList())
            {
                db.FeatureTeams.Remove(link);
            }

            foreach (var teamId in desiredTeamIds.Where(id => !currentTeamIds.Contains(id)))
            {
                db.FeatureTeams.Add(new FeatureTeam { FeatureId = feature.Id, TeamId = teamId });
            }
        }

        var existingTs = feature.FeatureSkills.ToDictionary(fs => fs.SkillId);

        foreach (var (skillId, value) in data.TechStackEfforts)
        {
            if (value.HasValue)
            {
                if (existingTs.TryGetValue(skillId, out var existing))
                {
                    existing.Value = value.Value;
                }
                else
                {
                    db.FeatureSkills.Add(new FeatureSkill
                    {
                        FeatureId = feature.Id,
                        SkillId = skillId,
                        Value = value.Value
                    });
                }
            }
            else
            {
                if (existingTs.TryGetValue(skillId, out var toRemove))
                {
                    db.FeatureSkills.Remove(toRemove);
                }
            }
        }

        await db.SaveChangesAsync();
        return feature;
    }

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

    private static async Task<int?> ResolvePiObjectiveIdAsync(EstimationDbContext db, string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > 255)
        {
            trimmed = trimmed[..255];
        }

        var existing = await db.PiObjectives.FirstOrDefaultAsync(po => po.Name == trimmed);
        if (existing is not null)
        {
            return existing.Id;
        }

        var created = new PiObjective { Name = trimmed };
        db.PiObjectives.Add(created);
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
                    var desired = new HashSet<int>(resolvedTeamIds);
                    var existingTeamIds = new HashSet<int>();
                    foreach (var link in existingF.FeatureTeams.ToList())
                    {
                        if (!desired.Contains(link.TeamId))
                        {
                            db.FeatureTeams.Remove(link);
                        }
                        else
                        {
                            existingTeamIds.Add(link.TeamId);
                        }
                    }
                    foreach (var teamId in resolvedTeamIds)
                    {
                        if (existingTeamIds.Add(teamId))
                        {
                            db.FeatureTeams.Add(new FeatureTeam { FeatureId = existingF.Id, TeamId = teamId });
                        }
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

using Estimation.Core.Calendar.Services;
using Estimation.Core.Capacity.Models;
using Estimation.Core.Features.Models;
using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Resources.Models;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.Capacity.Services;

public record TeamCapacityMemberRow(
    int HumanResourceId,
    string FullName,
    string? RoleName,
    double BusinessDays,
    double HolidayDays,
    double PublicHolidayDays,
    decimal Coefficient,
    double EffectiveDays);

public record TeamCapacityFeatureTeamRef(
    int TeamId,
    string Name,
    bool IsPrimary);

public record TeamCapacityFeatureRow(
    int FeatureId,
    string? JiraId,
    string? Name,
    string? BusinessOutcomeJiraId,
    string? BusinessOutcomeSummary,
    int Effort,
    bool IsIncluded,
    int SortOrder,
    bool IsManuallyAdded = false,
    int? FeaturePiId = null,
    string? FeaturePiName = null,
    bool MatchedByLabel = false,
    bool IsSaved = false,
    string? Status = null,
    string? Dependencies = null,
    bool ExternalDependencies = false,
    string? RagExplain = null,
    string? Labels = null,
    string? RequirementStatus = null,
    string? TechnicalApproval = null,
    string? PiObjective = null,
    string? Summary = null,
    DateTime? TargetStart = null,
    DateTime? TargetEnd = null,
    string? AcceptanceCriteria = null,
    IReadOnlyList<TeamCapacityFeatureTeamRef>? Teams = null) : IFeatureFilterRow
{
    public string? PortfolioEpicJiraId { get; init; }
    public string? PortfolioEpicSummary { get; init; }
    public IReadOnlyList<FeatureFilterRef> StrategicObjectives { get; init; } = Array.Empty<FeatureFilterRef>();
    public string? FundingStatus { get; init; }
    public string? RagStatus { get; init; }

    public int? HygieneFailureCount { get; init; }
}

public record FeaturePiDriftInfo(
    int FeatureId,
    string? JiraId,
    string? Name,
    int? AddedPiId,
    string? AddedPiName,
    int? CurrentPiId,
    string? CurrentPiName);

public record TeamMemberMeta(
    int HumanResourceId,
    string FullName,
    double EffectiveDays,
    Dictionary<int, int> SkillLevels,
    Dictionary<int, string?> SkillLevelNames,
    HashSet<int> StackIds);

public record TeamFeatureSkillDemand(int SkillId, string SkillName, decimal Value);

public record TeamFeatureStackDemand(int TechnologyStackId, string StackName, int? StoryPoints, List<TeamFeatureSkillDemand> Skills);

public record TeamFeatureDemand(
    int FeatureId,
    int PlainEffort,
    List<TeamFeatureStackDemand> Stacks,
    List<TeamFeatureSkillDemand> FeatureSkills);

public record TeamStackCapacityRow(int TechnologyStackId, string StackName, int MemberCount, double Capacity);

public record TeamCapacityResult(
    double TotalCapacity,
    string WindowMode,
    DateTime? WindowStart,
    DateTime? WindowEnd,
    List<TeamCapacityMemberRow> Members,
    List<TeamCapacityFeatureRow> Features,
    List<FeaturePiDriftInfo> Drifts,
    List<TeamMemberMeta> MemberMeta,
    Dictionary<int, TeamFeatureDemand> Demands,
    List<TeamStackCapacityRow> StackCapacities);

public record TeamCapacityOrderRow(int FeatureId, int SortOrder, bool IsIncluded, bool IsManuallyAdded = false);

public record FeatureOtherTeamLine(int TeamId, string TeamName, bool IsIncluded);

public record FeatureFindCriteria(
    string? ProjectKey,
    IReadOnlyList<string> JiraIds,
    IReadOnlyList<string> Labels,
    bool LabelMatchAll,
    IReadOnlyList<string> Statuses,
    bool ExcludeStatuses,
    IReadOnlyList<int> TeamIds,
    bool TeamMatchAll);

public record FeatureFindRow(
    int FeatureId,
    string? JiraId,
    string? Name,
    string? Status,
    string? Labels,
    string? ProjectKey,
    int? PiId,
    string? PiName,
    int Effort,
    string TeamNames,
    bool LinkedToTeam,
    bool AlreadyInPi,
    bool AlreadyAdded);

public record FeatureFindTeamOption(int Id, string Name);

public record FeatureFindMetadata(
    List<string> ProjectKeys,
    List<string> Statuses,
    List<string> Labels,
    List<FeatureFindTeamOption> Teams);

public interface ITeamCapacityService
{
    Task<TeamCapacityResult> GetAsync(int teamId, int piId, bool includeIpSprints = false);
    Task SaveOrderAsync(int teamId, int piId, IList<TeamCapacityOrderRow> rows,
        IReadOnlyCollection<int>? refreshSnapshotFeatureIds = null);
    Task<FeatureFindMetadata> GetFeatureFindMetadataAsync();
    Task<List<FeatureFindRow>> FindFeaturesAsync(int teamId, int piId, FeatureFindCriteria criteria);
    Task<Dictionary<int, List<FeatureOtherTeamLine>>> GetFeatureOtherTeamLinesAsync(
        int teamId, int piId, IReadOnlyCollection<int> featureIds);
    Task PropagateInclusionToOtherTeamsAsync(
        int sourceTeamId, int piId, IReadOnlyDictionary<int, bool> featureInclusion);
}

public class TeamCapacityService : ITeamCapacityService
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;

    private static readonly HashSet<DateTime> EmptyDates = new();

    public TeamCapacityService(IDbContextFactory<EstimationDbContext> ctx)
        => _ctx = ctx;

    public async Task<TeamCapacityResult> GetAsync(int teamId, int piId, bool includeIpSprints = false)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var pi = await db.Pis.AsNoTracking().FirstOrDefaultAsync(p => p.Id == piId);

        var allSprints = await db.Sprints
            .Where(s => s.TeamId == teamId && s.PiId == piId)
            .AsNoTracking()
            .ToListAsync();
        var sprints = includeIpSprints
            ? allSprints
            : allSprints.Where(s => s.IsIpSprint != true).ToList();

        DateTime? windowStart;
        DateTime? windowEnd;
        string windowMode;
        List<(DateTime Start, DateTime End)> windows;
        if (allSprints.Count > 0)
        {
            windows = sprints
                .Select(s => (s.StartDate.Date, s.EndDate.Date))
                .ToList();
            windowStart = windows.Count > 0 ? windows.Min(w => w.Start) : null;
            windowEnd = windows.Count > 0 ? windows.Max(w => w.End) : null;
            windowMode = "Sprints";
        }
        else if (pi?.StartDate is not null && pi.EndDate is not null)
        {
            windows = new List<(DateTime, DateTime)> { (pi.StartDate.Value.Date, pi.EndDate.Value.Date) };
            windowStart = pi.StartDate;
            windowEnd = pi.EndDate;
            windowMode = "PI";
        }
        else
        {
            windows = new List<(DateTime, DateTime)>();
            windowStart = null;
            windowEnd = null;
            windowMode = "None";
        }

        var members = await db.TeamMembers
            .Where(tm => tm.TeamId == teamId && tm.HumanResource.IsActive)
            .Include(tm => tm.HumanResource)
                .ThenInclude(hr => hr.HumanResourceSkills).ThenInclude(hrs => hrs.SkillLevel)
            .Include(tm => tm.TeamRole)
            .Include(tm => tm.MemberTechnologyStacks)
            .AsSplitQuery()
            .AsNoTracking()
            .ToListAsync();
        var memberIds = members.Select(m => m.HumanResourceId).ToList();

        var coefficients = memberIds.Count == 0
            ? new Dictionary<int, decimal>()
            : await db.TeamMemberCoefficients
                .Where(c => c.TeamId == teamId && c.PiId == piId && memberIds.Contains(c.HumanResourceId))
                .AsNoTracking()
                .ToDictionaryAsync(c => c.HumanResourceId, c => c.Value);

        var defaultCoefficients = memberIds.Count == 0
            ? new Dictionary<int, decimal>()
            : await db.TeamMemberCoefficients
                .Where(c => c.TeamId == teamId && c.PiId == null && memberIds.Contains(c.HumanResourceId))
                .AsNoTracking()
                .ToDictionaryAsync(c => c.HumanResourceId, c => c.Value);

        var businessDays = CapacityWindowMath.TotalBusinessDays(windows);
        var merged = CapacityWindowMath.MergeWindows(windows);
        var phByMember = windows.Count == 0
            ? new Dictionary<int, HashSet<DateTime>>()
            : await PublicHolidayResolver.ResolveByMemberAsync(
                db,
                members.Select(m => (m.HumanResourceId, m.HumanResource.CityId)).ToList(),
                windows.Min(w => w.Start),
                windows.Max(w => w.End));
        var holidaysByHr = await LoadHolidayDaysAsync(db, members, windows, phByMember);

        var memberRows = members
            .OrderBy(m => m.HumanResource.FullName)
            .Select(m =>
            {
                var phSet = phByMember.GetValueOrDefault(m.HumanResourceId, EmptyDates);
                var publicHolidayDays = CapacityWindowMath.PublicHolidayDays(merged, phSet);
                var holidayDays = holidaysByHr.GetValueOrDefault(m.HumanResourceId, 0d);
                var available = Math.Max(0d, businessDays - holidayDays - publicHolidayDays);
                var coefficient = coefficients.TryGetValue(m.HumanResourceId, out var c)
                    ? c
                    : defaultCoefficients.TryGetValue(m.HumanResourceId, out var d) ? d : 1.0m;
                var effective = available * (double)coefficient;
                return new TeamCapacityMemberRow(
                    m.HumanResourceId,
                    m.HumanResource.FullName,
                    m.TeamRole?.Name,
                    businessDays,
                    holidayDays,
                    publicHolidayDays,
                    coefficient,
                    effective);
            })
            .ToList();

        var totalCapacity = memberRows.Sum(r => r.EffectiveDays);

        var (featureRows, drifts) = await BuildFeatureRowsAsync(
            db, teamId, piId, pi?.FeatureLabels, pi?.LabelMatchMode ?? PiLabelMatchMode.Any);

        var effectiveByMember = memberRows.ToDictionary(r => r.HumanResourceId, r => r.EffectiveDays);
        var memberMeta = members
            .Select(m => new TeamMemberMeta(
                m.HumanResourceId,
                m.HumanResource.FullName,
                effectiveByMember.GetValueOrDefault(m.HumanResourceId, 0d),
                m.HumanResource.HumanResourceSkills.ToDictionary(h => h.SkillId, h => h.SkillLevel?.Value ?? 0),
                m.HumanResource.HumanResourceSkills.ToDictionary(h => h.SkillId, h => h.SkillLevel?.Name),
                m.MemberTechnologyStacks.Select(s => s.TechnologyStackId).ToHashSet()))
            .ToList();

        var demands = await BuildFeatureDemandsAsync(db, teamId, featureRows);

        var teamStacks = await db.TeamTechnologyStacks
            .Where(tts => tts.TeamId == teamId)
            .Include(tts => tts.TechnologyStack)
            .AsNoTracking()
            .ToListAsync();
        var stackCapacities = teamStacks
            .Select(tts =>
            {
                var membersInStack = memberMeta.Where(m => m.StackIds.Contains(tts.TechnologyStackId)).ToList();
                return new TeamStackCapacityRow(
                    tts.TechnologyStackId,
                    tts.TechnologyStack.Name,
                    membersInStack.Count,
                    membersInStack.Sum(m => m.EffectiveDays));
            })
            .OrderBy(s => s.StackName)
            .ToList();

        return new TeamCapacityResult(
            totalCapacity,
            windowMode,
            windowStart,
            windowEnd,
            memberRows,
            featureRows,
            drifts,
            memberMeta,
            demands,
            stackCapacities);
    }

    private static async Task<Dictionary<int, TeamFeatureDemand>> BuildFeatureDemandsAsync(
        EstimationDbContext db, int teamId, List<TeamCapacityFeatureRow> featureRows)
    {
        var featureIds = featureRows.Select(r => r.FeatureId).ToList();
        var effortById = featureRows.ToDictionary(r => r.FeatureId, r => r.Effort);
        var demands = new Dictionary<int, TeamFeatureDemand>();
        if (featureIds.Count == 0)
        {
            return demands;
        }

        var stacks = await db.FeatureTeamTechnologyStacks
            .Where(x => x.TeamId == teamId && featureIds.Contains(x.FeatureId))
            .Include(x => x.TechnologyStack)
            .Include(x => x.SkillValues).ThenInclude(sv => sv.Skill)
            .AsSplitQuery()
            .AsNoTracking()
            .ToListAsync();

        var featureSkills = await db.FeatureSkills
            .Where(x => featureIds.Contains(x.FeatureId))
            .Include(x => x.Skill)
            .AsNoTracking()
            .ToListAsync();

        foreach (var fid in featureIds)
        {
            var stackDemands = stacks
                .Where(x => x.FeatureId == fid)
                .Select(x => new TeamFeatureStackDemand(
                    x.TechnologyStackId,
                    x.TechnologyStack.Name,
                    x.StoryPoints,
                    x.SkillValues
                        .Select(sv => new TeamFeatureSkillDemand(sv.SkillId, sv.Skill.Name, sv.Value))
                        .ToList()))
                .ToList();

            var skillDemands = featureSkills
                .Where(x => x.FeatureId == fid)
                .Select(x => new TeamFeatureSkillDemand(x.SkillId, x.Skill.Name, x.Value))
                .ToList();

            demands[fid] = new TeamFeatureDemand(fid, effortById.GetValueOrDefault(fid), stackDemands, skillDemands);
        }

        return demands;
    }

    private static async Task<(List<TeamCapacityFeatureRow> Rows, List<FeaturePiDriftInfo> Drifts)> BuildFeatureRowsAsync(
        EstimationDbContext db, int teamId, int piId, string? piFeatureLabels, PiLabelMatchMode labelMode)
    {
        var existingOrder = await db.TeamCapacityFeatureOrders
            .Where(o => o.TeamId == teamId && o.PiId == piId)
            .AsNoTracking()
            .ToDictionaryAsync(o => o.FeatureId);

        var manualFeatureIds = existingOrder.Values
            .Where(o => o.IsManuallyAdded)
            .Select(o => o.FeatureId)
            .ToHashSet();

        var savedFeatureIds = existingOrder.Keys.ToHashSet();

        var ruleLabels = PiLabelMatching.ParseLabels(piFeatureLabels);
        var includeLabelLinked = ruleLabels.Count > 0;

        var baseQuery = db.FeatureTeams.Where(ft => ft.TeamId == teamId);
        baseQuery = includeLabelLinked
            ? baseQuery.Where(ft => ft.Feature.PiId == piId
                || savedFeatureIds.Contains(ft.FeatureId)
                || (ft.Feature.Labels != null && ft.Feature.Labels != ""))
            : baseQuery.Where(ft => ft.Feature.PiId == piId || savedFeatureIds.Contains(ft.FeatureId));

        var rawRows = await baseQuery
            .Include(ft => ft.Feature).ThenInclude(f => f.BusinessOutcome)
                .ThenInclude(bo => bo!.PortfolioEpic)
            .Include(ft => ft.Feature).ThenInclude(f => f.BusinessOutcome)
                .ThenInclude(bo => bo!.PortfolioEpic)
                    .ThenInclude(pe => pe!.StrategicObjectivePortfolioEpics)
                        .ThenInclude(spe => spe.StrategicObjective)
            .Include(ft => ft.Feature).ThenInclude(f => f.Pi)
            .Include(ft => ft.Feature).ThenInclude(f => f.PiObjective)
            .Include(ft => ft.Feature).ThenInclude(f => f.RequirementStatus)
            .Include(ft => ft.Feature).ThenInclude(f => f.TechnicalApproval)
            .Include(ft => ft.Feature).ThenInclude(f => f.UnfundedOption)
            .AsSplitQuery()
            .AsNoTracking()
            .ToListAsync();

        if (includeLabelLinked)
        {
            rawRows = rawRows
                .Where(ft => ft.Feature.PiId == piId
                    || savedFeatureIds.Contains(ft.Feature.Id)
                    || (ft.Feature.PiId != piId && PiLabelMatching.Matches(ruleLabels, labelMode, ft.Feature.Labels)))
                .ToList();
        }

        if (rawRows.Count == 0)
        {
            return (new List<TeamCapacityFeatureRow>(), new List<FeaturePiDriftInfo>());
        }

        var featureIds = rawRows.Select(ft => ft.FeatureId).Distinct().ToList();
        var teamsByFeature = (await db.FeatureTeams
                .Where(ft => featureIds.Contains(ft.FeatureId))
                .Include(ft => ft.Team)
                .AsNoTracking()
                .ToListAsync())
            .GroupBy(ft => ft.FeatureId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<TeamCapacityFeatureTeamRef>)g
                    .Select(ft => new TeamCapacityFeatureTeamRef(ft.TeamId, ft.Team.Name, ft.IsPrimary == true))
                    .OrderByDescending(t => t.IsPrimary)
                    .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList());

        var rows = rawRows
            .Select(ft =>
            {
                var feature = ft.Feature;
                int effort = ft.StoryPoints ?? feature.StoryPoints ?? 0;
                var isManual = manualFeatureIds.Contains(feature.Id) && feature.PiId != piId;
                var matchedByLabel = includeLabelLinked
                    && feature.PiId != piId
                    && PiLabelMatching.Matches(ruleLabels, labelMode, feature.Labels);
                var epic = feature.BusinessOutcome?.PortfolioEpic;

                if (existingOrder.TryGetValue(feature.Id, out var saved))
                {
                    return new TeamCapacityFeatureRow(
                        feature.Id,
                        feature.JiraId,
                        feature.Name,
                        feature.BusinessOutcome?.JiraId,
                        feature.BusinessOutcome?.Summary,
                        effort,
                        saved.IsIncluded,
                        saved.SortOrder,
                        isManual,
                        feature.PiId,
                        feature.Pi?.Name,
                        matchedByLabel,
                        true,
                        feature.Status,
                        feature.Dependencies,
                        feature.ExternalDependencies,
                        feature.RagExplain,
                        feature.Labels,
                        feature.RequirementStatus?.Name,
                        feature.TechnicalApproval?.Name,
                        feature.PiObjective?.Name,
                        feature.Summary,
                        feature.TargetStart,
                        feature.TargetEnd,
                        feature.AcceptanceCriteria,
                        teamsByFeature.GetValueOrDefault(feature.Id))
                    {
                        PortfolioEpicJiraId = epic?.JiraId,
                        PortfolioEpicSummary = epic?.Summary,
                        StrategicObjectives = StrategicObjectivesOf(feature),
                        FundingStatus = feature.UnfundedOption?.Name,
                        RagStatus = feature.RagStatus,
                    };
                }

                return new TeamCapacityFeatureRow(
                    feature.Id,
                    feature.JiraId,
                    feature.Name,
                    feature.BusinessOutcome?.JiraId,
                    feature.BusinessOutcome?.Summary,
                    effort,
                    true,
                    feature.Ranking ?? int.MaxValue,
                    isManual,
                    feature.PiId,
                    feature.Pi?.Name,
                    matchedByLabel,
                    false,
                    feature.Status,
                    feature.Dependencies,
                    feature.ExternalDependencies,
                    feature.RagExplain,
                    feature.Labels,
                    feature.RequirementStatus?.Name,
                    feature.TechnicalApproval?.Name,
                    feature.PiObjective?.Name,
                    feature.Summary,
                    feature.TargetStart,
                    feature.TargetEnd,
                    feature.AcceptanceCriteria,
                    teamsByFeature.GetValueOrDefault(feature.Id))
                {
                    PortfolioEpicJiraId = epic?.JiraId,
                    PortfolioEpicSummary = epic?.Summary,
                    StrategicObjectives = StrategicObjectivesOf(feature),
                    FundingStatus = feature.UnfundedOption?.Name,
                    RagStatus = feature.RagStatus,
                };
            })
            .OrderBy(r => r.SortOrder)
            .ThenBy(r => r.FeatureId)
            .ToList();

        var drifts = await BuildDriftAsync(db, existingOrder.Values, rawRows);
        return (rows, drifts);
    }

    private static async Task<List<FeaturePiDriftInfo>> BuildDriftAsync(
        EstimationDbContext db,
        IEnumerable<TeamCapacityFeatureOrder> orders,
        List<FeatureTeam> rawRows)
    {
        var featureById = rawRows
            .GroupBy(ft => ft.FeatureId)
            .ToDictionary(g => g.Key, g => g.First().Feature);

        var drifted = orders
            .Where(o => o.IsManuallyAdded
                && featureById.TryGetValue(o.FeatureId, out var f)
                && o.AddedFeaturePiId != f.PiId)
            .ToList();

        if (drifted.Count == 0)
        {
            return new List<FeaturePiDriftInfo>();
        }

        var snapshotPiIds = drifted
            .Where(o => o.AddedFeaturePiId.HasValue)
            .Select(o => o.AddedFeaturePiId!.Value)
            .Distinct()
            .ToList();
        var piNames = snapshotPiIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.Pis.Where(p => snapshotPiIds.Contains(p.Id))
                .AsNoTracking()
                .ToDictionaryAsync(p => p.Id, p => p.Name);

        return drifted
            .Select(o =>
            {
                var feature = featureById[o.FeatureId];
                return new FeaturePiDriftInfo(
                    feature.Id,
                    feature.JiraId,
                    feature.Name,
                    o.AddedFeaturePiId,
                    o.AddedFeaturePiId.HasValue ? piNames.GetValueOrDefault(o.AddedFeaturePiId.Value) : null,
                    feature.PiId,
                    feature.Pi?.Name);
            })
            .ToList();
    }

    public async Task SaveOrderAsync(int teamId, int piId, IList<TeamCapacityOrderRow> rows,
        IReadOnlyCollection<int>? refreshSnapshotFeatureIds = null)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var existing = await db.TeamCapacityFeatureOrders
            .Where(o => o.TeamId == teamId && o.PiId == piId)
            .ToListAsync();
        var existingByFeature = existing.ToDictionary(o => o.FeatureId);

        var keepFeatureIds = rows.Select(r => r.FeatureId).ToHashSet();

        var toRemove = existing.Where(o => !keepFeatureIds.Contains(o.FeatureId)).ToList();
        if (toRemove.Count > 0)
        {
            db.TeamCapacityFeatureOrders.RemoveRange(toRemove);
        }

        var newManualIds = rows
            .Where(r => r.IsManuallyAdded && !existingByFeature.ContainsKey(r.FeatureId))
            .Select(r => r.FeatureId)
            .Distinct()
            .ToList();
        var snapshotPiByFeature = newManualIds.Count == 0
            ? new Dictionary<int, int?>()
            : await db.Features
                .Where(f => newManualIds.Contains(f.Id))
                .Select(f => new { f.Id, f.PiId })
                .ToDictionaryAsync(x => x.Id, x => x.PiId);

        var refreshIds = refreshSnapshotFeatureIds is { Count: > 0 }
            ? refreshSnapshotFeatureIds.ToHashSet()
            : new HashSet<int>();
        var refreshPiByFeature = refreshIds.Count == 0
            ? new Dictionary<int, int?>()
            : await db.Features
                .Where(f => refreshIds.Contains(f.Id))
                .Select(f => new { f.Id, f.PiId })
                .ToDictionaryAsync(x => x.Id, x => x.PiId);

        foreach (var row in rows)
        {
            if (existingByFeature.TryGetValue(row.FeatureId, out var entity))
            {
                entity.SortOrder = row.SortOrder;
                entity.IsIncluded = row.IsIncluded;
                if (entity.IsManuallyAdded && refreshIds.Contains(row.FeatureId))
                {
                    entity.AddedFeaturePiId = refreshPiByFeature.GetValueOrDefault(row.FeatureId);
                }
            }
            else
            {
                db.TeamCapacityFeatureOrders.Add(new TeamCapacityFeatureOrder
                {
                    TeamId = teamId,
                    PiId = piId,
                    FeatureId = row.FeatureId,
                    SortOrder = row.SortOrder,
                    IsIncluded = row.IsIncluded,
                    IsManuallyAdded = row.IsManuallyAdded,
                    AddedFeaturePiId = row.IsManuallyAdded
                        ? snapshotPiByFeature.GetValueOrDefault(row.FeatureId)
                        : null,
                });
            }
        }

        await db.SaveChangesAsync();
    }

    public async Task<FeatureFindMetadata> GetFeatureFindMetadataAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var projectKeys = await db.Features
            .Where(f => f.ProjectKey != null && f.ProjectKey != "")
            .Select(f => f.ProjectKey!)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();

        var statuses = await db.Features
            .Where(f => f.Status != null && f.Status != "")
            .Select(f => f.Status!)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();

        var labelStrings = await db.Features
            .Where(f => f.Labels != null && f.Labels != "")
            .Select(f => f.Labels!)
            .ToListAsync();
        var labels = labelStrings
            .SelectMany(SplitLabels)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var teams = await db.Teams
            .OrderBy(t => t.Name)
            .Select(t => new FeatureFindTeamOption(t.Id, t.Name))
            .ToListAsync();

        return new FeatureFindMetadata(projectKeys, statuses, labels, teams);
    }

    public async Task<List<FeatureFindRow>> FindFeaturesAsync(int teamId, int piId, FeatureFindCriteria criteria)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var query = db.Features
            .Include(f => f.Pi)
            .Include(f => f.FeatureTeams).ThenInclude(ft => ft.Team)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(criteria.ProjectKey))
        {
            query = query.Where(f => f.ProjectKey == criteria.ProjectKey);
        }

        var jiraIds = criteria.JiraIds.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        if (jiraIds.Count > 0)
        {
            query = query.Where(f => f.JiraId != null && jiraIds.Contains(f.JiraId));
        }
        else
        {
            var statuses = criteria.Statuses.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            if (statuses.Count > 0)
            {
                if (criteria.ExcludeStatuses)
                {
                    query = query.Where(f => f.Status == null || !statuses.Contains(f.Status));
                }
                else
                {
                    query = query.Where(f => f.Status != null && statuses.Contains(f.Status));
                }
            }

            var teamIds = criteria.TeamIds.Distinct().ToList();
            if (teamIds.Count > 0)
            {
                if (criteria.TeamMatchAll)
                {
                    foreach (var id in teamIds)
                    {
                        var tid = id;
                        query = query.Where(f => f.FeatureTeams.Any(ft => ft.TeamId == tid));
                    }
                }
                else
                {
                    query = query.Where(f => f.FeatureTeams.Any(ft => teamIds.Contains(ft.TeamId)));
                }
            }
        }

        var features = await query.ToListAsync();

        var labelFilter = criteria.Labels.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        if (jiraIds.Count == 0 && labelFilter.Count > 0)
        {
            features = features
                .Where(f =>
                {
                    var fLabels = SplitLabels(f.Labels);
                    if (fLabels.Count == 0)
                    {
                        return false;
                    }
                    return criteria.LabelMatchAll
                        ? labelFilter.All(l => fLabels.Contains(l, StringComparer.OrdinalIgnoreCase))
                        : labelFilter.Any(l => fLabels.Contains(l, StringComparer.OrdinalIgnoreCase));
                })
                .ToList();
        }

        var manualFeatureIds = await db.TeamCapacityFeatureOrders
            .Where(o => o.TeamId == teamId && o.PiId == piId && o.IsManuallyAdded)
            .Select(o => o.FeatureId)
            .ToListAsync();
        var manualSet = manualFeatureIds.ToHashSet();

        return features
            .Select(f =>
            {
                var teamLink = f.FeatureTeams.FirstOrDefault(x => x.TeamId == teamId);
                var linkedToTeam = teamLink is not null;
                var effort = teamLink?.StoryPoints ?? f.StoryPoints ?? 0;
                var teamNames = string.Join(", ", f.FeatureTeams
                    .Where(x => x.Team is not null)
                    .Select(x => x.Team.Name)
                    .OrderBy(n => n));
                var alreadyInPi = f.PiId == piId && linkedToTeam;
                return new FeatureFindRow(
                    f.Id,
                    f.JiraId,
                    f.Name ?? f.Summary,
                    f.Status,
                    f.Labels,
                    f.ProjectKey,
                    f.PiId,
                    f.Pi?.Name,
                    effort,
                    teamNames,
                    linkedToTeam,
                    alreadyInPi,
                    manualSet.Contains(f.Id));
            })
            .OrderBy(r => r.JiraId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<Dictionary<int, List<FeatureOtherTeamLine>>> GetFeatureOtherTeamLinesAsync(
        int teamId, int piId, IReadOnlyCollection<int> featureIds)
    {
        var result = new Dictionary<int, List<FeatureOtherTeamLine>>();
        if (featureIds.Count == 0)
        {
            return result;
        }

        await using var db = await _ctx.CreateDbContextAsync();

        var pi = await db.Pis.AsNoTracking().FirstOrDefaultAsync(p => p.Id == piId);
        var ruleLabels = PiLabelMatching.ParseLabels(pi?.FeatureLabels);
        var labelMode = pi?.LabelMatchMode ?? PiLabelMatchMode.Any;
        var includeLabelLinked = ruleLabels.Count > 0;

        var ids = featureIds.ToList();

        var featureTeams = await db.FeatureTeams
            .Where(ft => ft.TeamId != teamId && ids.Contains(ft.FeatureId))
            .Include(ft => ft.Team)
            .Include(ft => ft.Feature)
            .AsNoTracking()
            .ToListAsync();

        var orders = await db.TeamCapacityFeatureOrders
            .Where(o => o.TeamId != teamId && o.PiId == piId && ids.Contains(o.FeatureId))
            .AsNoTracking()
            .ToListAsync();
        var includedByTeamFeature = orders.ToDictionary(o => (o.TeamId, o.FeatureId), o => o.IsIncluded);

        foreach (var ft in featureTeams)
        {
            var hasOrder = includedByTeamFeature.TryGetValue((ft.TeamId, ft.FeatureId), out var savedIncluded);
            if (!IsTrackingThisPi(ft.Feature, piId, hasOrder, includeLabelLinked, ruleLabels, labelMode))
            {
                continue;
            }

            var included = !hasOrder || savedIncluded;
            if (!result.TryGetValue(ft.FeatureId, out var list))
            {
                list = new List<FeatureOtherTeamLine>();
                result[ft.FeatureId] = list;
            }
            list.Add(new FeatureOtherTeamLine(ft.TeamId, ft.Team.Name, included));
        }

        foreach (var list in result.Values)
        {
            list.Sort((a, b) => string.Compare(a.TeamName, b.TeamName, StringComparison.OrdinalIgnoreCase));
        }

        return result;
    }

    public async Task PropagateInclusionToOtherTeamsAsync(
        int sourceTeamId, int piId, IReadOnlyDictionary<int, bool> featureInclusion)
    {
        if (featureInclusion.Count == 0)
        {
            return;
        }

        await using var db = await _ctx.CreateDbContextAsync();

        var pi = await db.Pis.AsNoTracking().FirstOrDefaultAsync(p => p.Id == piId);
        var ruleLabels = PiLabelMatching.ParseLabels(pi?.FeatureLabels);
        var labelMode = pi?.LabelMatchMode ?? PiLabelMatchMode.Any;
        var includeLabelLinked = ruleLabels.Count > 0;

        var ids = featureInclusion.Keys.ToList();

        var featureTeams = await db.FeatureTeams
            .Where(ft => ft.TeamId != sourceTeamId && ids.Contains(ft.FeatureId))
            .Include(ft => ft.Feature)
            .AsNoTracking()
            .ToListAsync();

        var orders = await db.TeamCapacityFeatureOrders
            .Where(o => o.TeamId != sourceTeamId && o.PiId == piId && ids.Contains(o.FeatureId))
            .ToListAsync();
        var orderByTeamFeature = orders.ToDictionary(o => (o.TeamId, o.FeatureId));

        var maxSortByTeam = (await db.TeamCapacityFeatureOrders
                .Where(o => o.TeamId != sourceTeamId && o.PiId == piId)
                .GroupBy(o => o.TeamId)
                .Select(g => new { TeamId = g.Key, Max = g.Max(o => o.SortOrder) })
                .ToListAsync())
            .ToDictionary(x => x.TeamId, x => x.Max);

        foreach (var ft in featureTeams)
        {
            var desiredIncluded = featureInclusion[ft.FeatureId];
            var hasOrder = orderByTeamFeature.TryGetValue((ft.TeamId, ft.FeatureId), out var entity);
            if (!IsTrackingThisPi(ft.Feature, piId, hasOrder, includeLabelLinked, ruleLabels, labelMode))
            {
                continue;
            }

            if (hasOrder)
            {
                entity!.IsIncluded = desiredIncluded;
            }
            else if (!desiredIncluded)
            {
                var nextSort = maxSortByTeam.TryGetValue(ft.TeamId, out var max)
                    ? max + 1
                    : ft.Feature.Ranking ?? 1;
                maxSortByTeam[ft.TeamId] = nextSort;
                db.TeamCapacityFeatureOrders.Add(new TeamCapacityFeatureOrder
                {
                    TeamId = ft.TeamId,
                    PiId = piId,
                    FeatureId = ft.FeatureId,
                    SortOrder = nextSort,
                    IsIncluded = false,
                    IsManuallyAdded = false,
                    AddedFeaturePiId = null,
                });
            }
        }

        await db.SaveChangesAsync();
    }

    private static IReadOnlyList<FeatureFilterRef> StrategicObjectivesOf(Feature feature) =>
        feature.BusinessOutcome?.PortfolioEpic?.StrategicObjectivePortfolioEpics
            .Select(spe => spe.StrategicObjective)
            .Where(so => !string.IsNullOrEmpty(so.JiraId))
            .Select(so => new FeatureFilterRef(so.JiraId!, so.Summary))
            .DistinctBy(so => so.JiraId, StringComparer.OrdinalIgnoreCase)
            .ToList()
        ?? (IReadOnlyList<FeatureFilterRef>)Array.Empty<FeatureFilterRef>();

    private static bool IsTrackingThisPi(
        Feature feature, int piId, bool hasOrder,
        bool includeLabelLinked, HashSet<string> ruleLabels, PiLabelMatchMode labelMode) =>
        hasOrder
        || feature.PiId == piId
        || (includeLabelLinked && feature.PiId != piId
            && PiLabelMatching.Matches(ruleLabels, labelMode, feature.Labels));

    private static List<string> SplitLabels(string? labels)
    {
        if (string.IsNullOrWhiteSpace(labels))
        {
            return new List<string>();
        }

        var separators = new[] { ',', ';', ' ', '\t', '\n', '\r' };
        return labels
            .Split(separators, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<Dictionary<int, double>> LoadHolidayDaysAsync(
        EstimationDbContext db, List<TeamMember> members, List<(DateTime Start, DateTime End)> windows,
        Dictionary<int, HashSet<DateTime>> phByMember)
    {
        var memberIds = members.Select(m => m.HumanResourceId).ToList();
        var result = memberIds.ToDictionary(id => id, _ => 0d);
        if (memberIds.Count == 0 || windows.Count == 0)
        {
            return result;
        }

        var minStart = windows.Min(w => w.Start);
        var maxEnd = windows.Max(w => w.End);

        var holidays = await db.Holidays
            .Where(h => memberIds.Contains(h.HumanResourceId)
                && h.StartDate.Date <= maxEnd
                && h.EndDate.Date >= minStart)
            .AsNoTracking()
            .ToListAsync();

        var merged = CapacityWindowMath.MergeWindows(windows);

        foreach (var h in holidays)
        {
            var phSet = phByMember.GetValueOrDefault(h.HumanResourceId, EmptyDates);
            var days = CapacityWindowMath.HolidayDaysInWindows(h, merged, phSet);

            if (result.ContainsKey(h.HumanResourceId))
            {
                result[h.HumanResourceId] += days;
            }
        }

        return result;
    }
}

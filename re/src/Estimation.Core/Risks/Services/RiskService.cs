using Estimation.Core.Administration.Audit;
using Estimation.Core.Risks.Models;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.Risks.Services;

public record RiskStrategicObjectiveVm(string? JiraId, string? Name);

public record RiskFeatureVm(
    int FeatureId,
    string? JiraId,
    string? Summary,
    string? BoJiraId,
    string? BoName,
    string? EpicJiraId,
    string? EpicName,
    List<RiskStrategicObjectiveVm> StrategicObjectives,
    string? Labels,
    int? StoryPoints)
{
    public string DisplayText => string.IsNullOrWhiteSpace(JiraId)
        ? Summary ?? $"Feature {FeatureId}"
        : string.IsNullOrWhiteSpace(Summary) ? JiraId : $"{JiraId} — {Summary}";

    public string SoNamesText => string.Join(", ", StrategicObjectives
        .Select(so => so.Name)
        .Where(n => !string.IsNullOrWhiteSpace(n)));
}

public record RiskArtVm(int CapitalProjectId, string Name);

public record RiskVm(
    int Id,
    int PiId,
    List<RiskArtVm> Arts,
    RiskCategory Category,
    RiskSeverity Severity,
    RiskStatus Status,
    string? Summary,
    string? Owner,
    DateTime DateRaised,
    DateTime? DueBy,
    string? CreatedBy,
    string? CreatedByDisplayName,
    DateTime? DateUpdated,
    string? UpdatedBy,
    string? UpdatedByDisplayName,
    List<RiskFeatureVm> Features)
{
    public string? DisplayCreatedBy =>
        string.IsNullOrWhiteSpace(CreatedByDisplayName) ? CreatedBy : CreatedByDisplayName;

    public string? DisplayUpdatedBy =>
        string.IsNullOrWhiteSpace(UpdatedByDisplayName) ? UpdatedBy : UpdatedByDisplayName;

    public string ArtNamesText => string.Join(", ", Arts.Select(a => a.Name));

    public string FeatureJiraIdsText => string.Join(", ", Features
        .Select(f => f.JiraId)
        .Where(id => !string.IsNullOrWhiteSpace(id)));

    public string FeatureSummariesText => Join(Features.Select(f => f.Summary));

    public string BoJiraIdsText => Join(Features.Select(f => f.BoJiraId));

    public string BoNamesText => Join(Features.Select(f => f.BoName));

    public string EpicJiraIdsText => Join(Features.Select(f => f.EpicJiraId));

    public string EpicNamesText => Join(Features.Select(f => f.EpicName));

    public string SoJiraIdsText => Join(Features.SelectMany(f => f.StrategicObjectives).Select(so => so.JiraId));

    public string SoNamesText => Join(Features.SelectMany(f => f.StrategicObjectives).Select(so => so.Name));

    private static string Join(IEnumerable<string?> values) => string.Join(", ", values
        .Where(v => !string.IsNullOrWhiteSpace(v))
        .Select(v => v!.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase));
}

public class RiskEditDto
{
    public List<int> CapitalProjectIds { get; set; } = [];

    public RiskCategory Category { get; set; }

    public RiskSeverity Severity { get; set; }

    public RiskStatus Status { get; set; }

    public string? Summary { get; set; }

    public string? Owner { get; set; }

    public DateTime? DueBy { get; set; }

    public List<int> FeatureIds { get; set; } = [];
}

public interface IRiskService
{
    Task<List<RiskVm>> GetForPiAsync(int piId);

    Task<RiskVm?> GetByIdAsync(int riskId);

    Task<List<RiskFeatureVm>> GetPiFeaturesAsync(int piId);

    Task<int> CreateAsync(int piId, RiskEditDto dto, string? user = null);

    Task<bool> UpdateAsync(int riskId, RiskEditDto dto, string? user = null);

    Task<bool> DeleteAsync(int riskId);
}

public class RiskService : IRiskService
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;
    private readonly IAuditUserProvider _auditUser;

    public RiskService(IDbContextFactory<EstimationDbContext> ctx, IAuditUserProvider auditUser)
    {
        _ctx = ctx;
        _auditUser = auditUser;
    }

    public async Task<List<RiskVm>> GetForPiAsync(int piId)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var risks = await db.Risks
            .AsNoTracking()
            .Where(r => r.PiId == piId)
            .Include(r => r.RiskCapitalProjects)
                .ThenInclude(rc => rc.Art)
            .Include(r => r.RiskFeatures)
                .ThenInclude(rf => rf.Feature)
                .ThenInclude(f => f.BusinessOutcome)
                .ThenInclude(bo => bo!.PortfolioEpic)
                .ThenInclude(pe => pe!.StrategicObjectivePortfolioEpics)
                .ThenInclude(spe => spe.StrategicObjective)
            .OrderBy(r => r.DateRaised)
            .ThenBy(r => r.Id)
            .AsSplitQuery()
            .ToListAsync();

        var displayNames = await ResolveDisplayNamesAsync(
            db,
            risks.SelectMany(r => new[] { r.CreatedBy, r.UpdatedBy }));

        return risks.Select(r => ToVm(r, displayNames)).ToList();
    }

    public async Task<RiskVm?> GetByIdAsync(int riskId)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var risk = await db.Risks
            .AsNoTracking()
            .Where(r => r.Id == riskId)
            .Include(r => r.RiskCapitalProjects)
                .ThenInclude(rc => rc.Art)
            .Include(r => r.RiskFeatures)
                .ThenInclude(rf => rf.Feature)
                .ThenInclude(f => f.BusinessOutcome)
                .ThenInclude(bo => bo!.PortfolioEpic)
                .ThenInclude(pe => pe!.StrategicObjectivePortfolioEpics)
                .ThenInclude(spe => spe.StrategicObjective)
            .AsSplitQuery()
            .FirstOrDefaultAsync();

        if (risk is null)
        {
            return null;
        }

        var displayNames = await ResolveDisplayNamesAsync(db, new[] { risk.CreatedBy, risk.UpdatedBy });
        return ToVm(risk, displayNames);
    }

    public async Task<List<RiskFeatureVm>> GetPiFeaturesAsync(int piId)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        return await db.Features
            .AsNoTracking()
            .Where(f => f.PiId == piId && f.JiraId != null && f.JiraId != "")
            .OrderBy(f => f.JiraId)
            .ThenBy(f => f.Summary)
            .Select(f => new RiskFeatureVm(
                f.Id,
                f.JiraId,
                f.Summary,
                f.BusinessOutcome != null ? f.BusinessOutcome.JiraId : null,
                f.BusinessOutcome != null ? f.BusinessOutcome.Summary : null,
                f.BusinessOutcome != null && f.BusinessOutcome.PortfolioEpic != null
                    ? f.BusinessOutcome.PortfolioEpic.JiraId
                    : null,
                f.BusinessOutcome != null && f.BusinessOutcome.PortfolioEpic != null
                    ? f.BusinessOutcome.PortfolioEpic.Summary
                    : null,
                f.BusinessOutcome!.PortfolioEpic!.StrategicObjectivePortfolioEpics
                    .Select(spe => new RiskStrategicObjectiveVm(
                        spe.StrategicObjective.JiraId,
                        spe.StrategicObjective.Summary))
                    .ToList(),
                f.Labels,
                f.StoryPoints))
            .ToListAsync();
    }

    public async Task<int> CreateAsync(int piId, RiskEditDto dto, string? user = null)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var risk = new Risk
        {
            PiId = piId,
            Category = dto.Category,
            Severity = dto.Severity,
            Status = dto.Status,
            Summary = Trim(dto.Summary, Risk.MaxSummaryLength),
            Owner = Trim(dto.Owner, Risk.MaxOwnerLength),
            DateRaised = DateTime.UtcNow,
            DueBy = dto.DueBy?.Date,
            CreatedBy = ResolveUser(user)
        };

        foreach (var artId in dto.CapitalProjectIds.Distinct())
        {
            risk.RiskCapitalProjects.Add(new RiskArt { CapitalProjectId = artId });
        }

        foreach (var featureId in dto.FeatureIds.Distinct())
        {
            risk.RiskFeatures.Add(new RiskFeature { FeatureId = featureId });
        }

        db.Risks.Add(risk);
        await db.SaveChangesAsync();
        return risk.Id;
    }

    public async Task<bool> UpdateAsync(int riskId, RiskEditDto dto, string? user = null)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var risk = await db.Risks
            .Include(r => r.RiskCapitalProjects)
            .Include(r => r.RiskFeatures)
            .FirstOrDefaultAsync(r => r.Id == riskId);

        if (risk is null)
        {
            return false;
        }

        risk.Category = dto.Category;
        risk.Severity = dto.Severity;
        risk.Status = dto.Status;
        risk.Summary = Trim(dto.Summary, Risk.MaxSummaryLength);
        risk.Owner = Trim(dto.Owner, Risk.MaxOwnerLength);
        risk.DueBy = dto.DueBy?.Date;
        risk.DateUpdated = DateTime.UtcNow;
        risk.UpdatedBy = ResolveUser(user);

        var wantedArts = dto.CapitalProjectIds.Distinct().ToHashSet();
        var currentArts = risk.RiskCapitalProjects.Select(rc => rc.CapitalProjectId).ToHashSet();

        foreach (var removed in risk.RiskCapitalProjects.Where(rc => !wantedArts.Contains(rc.CapitalProjectId)).ToList())
        {
            risk.RiskCapitalProjects.Remove(removed);
        }

        foreach (var added in wantedArts.Where(id => !currentArts.Contains(id)))
        {
            risk.RiskCapitalProjects.Add(new RiskArt { RiskId = riskId, CapitalProjectId = added });
        }

        var wanted = dto.FeatureIds.Distinct().ToHashSet();
        var current = risk.RiskFeatures.Select(rf => rf.FeatureId).ToHashSet();

        foreach (var removed in risk.RiskFeatures.Where(rf => !wanted.Contains(rf.FeatureId)).ToList())
        {
            risk.RiskFeatures.Remove(removed);
        }

        foreach (var added in wanted.Where(id => !current.Contains(id)))
        {
            risk.RiskFeatures.Add(new RiskFeature { RiskId = riskId, FeatureId = added });
        }

        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int riskId)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var risk = await db.Risks.FirstOrDefaultAsync(r => r.Id == riskId);
        if (risk is null)
        {
            return false;
        }

        db.Risks.Remove(risk);
        await db.SaveChangesAsync();
        return true;
    }

    private string? ResolveUser(string? user)
    {
        var resolved = string.IsNullOrWhiteSpace(user) ? _auditUser.GetCurrentUserName() : user.Trim();
        return Trim(resolved, 256);
    }

    private static string? Trim(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }

    private static async Task<Dictionary<string, string?>> ResolveDisplayNamesAsync(
        EstimationDbContext db, IEnumerable<string?> userNames)
    {
        var names = userNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (names.Count == 0)
        {
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }

        var rows = await db.AppUsers
            .AsNoTracking()
            .Where(u => names.Contains(u.WindowsUserName))
            .Select(u => new { u.WindowsUserName, u.DisplayName })
            .ToListAsync();

        return rows
            .GroupBy(u => u.WindowsUserName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().DisplayName, StringComparer.OrdinalIgnoreCase);
    }

    private static RiskVm ToVm(Risk risk, Dictionary<string, string?> displayNames)
    {
        return new RiskVm(
            risk.Id,
            risk.PiId,
            risk.RiskCapitalProjects
                .Where(rc => rc.Art is not null)
                .Select(rc => new RiskArtVm(rc.CapitalProjectId, rc.Art.Name))
                .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            risk.Category,
            risk.Severity,
            risk.Status,
            risk.Summary,
            risk.Owner,
            risk.DateRaised,
            risk.DueBy,
            risk.CreatedBy,
            Lookup(displayNames, risk.CreatedBy),
            risk.DateUpdated,
            risk.UpdatedBy,
            Lookup(displayNames, risk.UpdatedBy),
            risk.RiskFeatures
                .Where(rf => rf.Feature is not null)
                .Select(rf => new RiskFeatureVm(
                    rf.FeatureId,
                    rf.Feature.JiraId,
                    rf.Feature.Summary,
                    rf.Feature.BusinessOutcome?.JiraId,
                    rf.Feature.BusinessOutcome?.Summary,
                    rf.Feature.BusinessOutcome?.PortfolioEpic?.JiraId,
                    rf.Feature.BusinessOutcome?.PortfolioEpic?.Summary,
                    rf.Feature.BusinessOutcome?.PortfolioEpic?.StrategicObjectivePortfolioEpics
                        .Where(spe => spe.StrategicObjective is not null)
                        .Select(spe => new RiskStrategicObjectiveVm(
                            spe.StrategicObjective.JiraId,
                            spe.StrategicObjective.Summary))
                        .DistinctBy(so => so.JiraId)
                        .ToList() ?? new List<RiskStrategicObjectiveVm>(),
                    rf.Feature.Labels,
                    rf.Feature.StoryPoints))
                .OrderBy(f => f.JiraId, StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    private static string? Lookup(Dictionary<string, string?> displayNames, string? userName) =>
        string.IsNullOrWhiteSpace(userName) ? null : displayNames.GetValueOrDefault(userName);
}

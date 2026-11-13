using Estimation.HrReporting.Data;
using Estimation.HrReporting.Models;
using Microsoft.EntityFrameworkCore;

namespace Estimation.HrReporting.Services;

public interface ITeamsAnalysisExclusionService
{
    Task<List<TeamsAnalysisExclusion>> GetAllAsync(CancellationToken ct = default);

    Task<TeamsAnalysisExclusion> AddAsync(
        string domain,
        TeamsAnalysisExclusionKind kind,
        string value,
        string? createdBy,
        CancellationToken ct = default);

    Task<bool> RemoveAsync(int id, CancellationToken ct = default);
}

public sealed class TeamsAnalysisExclusionService : ITeamsAnalysisExclusionService
{
    private readonly IDbContextFactory<HrReportingDbContext> _factory;
    private readonly TimeProvider _clock;

    public TeamsAnalysisExclusionService(IDbContextFactory<HrReportingDbContext> factory, TimeProvider clock)
    {
        _factory = factory;
        _clock = clock;
    }

    public async Task<List<TeamsAnalysisExclusion>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.TeamsAnalysisExclusions
            .AsNoTracking()
            .OrderBy(e => e.Domain)
            .ThenBy(e => e.Kind)
            .ThenBy(e => e.Value)
            .ToListAsync(ct);
    }

    public async Task<TeamsAnalysisExclusion> AddAsync(
        string domain,
        TeamsAnalysisExclusionKind kind,
        string value,
        string? createdBy,
        CancellationToken ct = default)
    {
        var cleanDomain = Clean(domain, TeamsAnalysisExclusion.DomainMaxLength);
        var cleanValue = Clean(value, TeamsAnalysisExclusion.ValueMaxLength);

        await using var db = await _factory.CreateDbContextAsync(ct);
        var existing = await FindAsync(db, cleanDomain, kind, cleanValue, ct);
        if (existing is not null)
        {
            return existing;
        }

        var exclusion = new TeamsAnalysisExclusion
        {
            Domain = cleanDomain,
            Kind = kind,
            Value = cleanValue,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
            CreatedBy = string.IsNullOrWhiteSpace(createdBy) ? null : createdBy.Trim()
        };

        db.TeamsAnalysisExclusions.Add(exclusion);
        try
        {
            await db.SaveChangesAsync(ct);
            return exclusion;
        }
        catch (DbUpdateException)
        {
            await using var retry = await _factory.CreateDbContextAsync(ct);
            return await FindAsync(retry, cleanDomain, kind, cleanValue, ct) ?? throw new InvalidOperationException(
                "The hide entry could not be saved.");
        }
    }

    public async Task<bool> RemoveAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var existing = await db.TeamsAnalysisExclusions.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (existing is null)
        {
            return false;
        }

        db.TeamsAnalysisExclusions.Remove(existing);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static async Task<TeamsAnalysisExclusion?> FindAsync(
        HrReportingDbContext db, string domain, TeamsAnalysisExclusionKind kind, string value, CancellationToken ct)
    {
        var candidates = await db.TeamsAnalysisExclusions
            .AsNoTracking()
            .Where(e => e.Kind == kind)
            .ToListAsync(ct);

        return candidates.FirstOrDefault(e =>
            string.Equals(e.Domain, domain, StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.Value, value, StringComparison.OrdinalIgnoreCase));
    }

    private static string Clean(string? value, int maxLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}

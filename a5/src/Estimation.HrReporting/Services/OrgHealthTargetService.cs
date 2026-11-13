using Estimation.HrReporting.Data;
using Estimation.HrReporting.Models;
using Microsoft.EntityFrameworkCore;

namespace Estimation.HrReporting.Services;

public interface IOrgHealthTargetService
{
    Task<List<OrgHealthTarget>> GetAllAsync(CancellationToken ct = default);

    Task<int> SaveAsync(
        IReadOnlyDictionary<string, OrgHealthTargetRule> targets, string? user, CancellationToken ct = default);
}

public sealed class OrgHealthTargetService : IOrgHealthTargetService
{
    private readonly IDbContextFactory<HrReportingDbContext> _factory;
    private readonly TimeProvider _clock;

    public OrgHealthTargetService(IDbContextFactory<HrReportingDbContext> factory, TimeProvider clock)
    {
        _factory = factory;
        _clock = clock;
    }

    public async Task<List<OrgHealthTarget>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.OrgHealthTargets
            .AsNoTracking()
            .OrderBy(t => t.KpiKey)
            .ToListAsync(ct);
    }

    public async Task<int> SaveAsync(
        IReadOnlyDictionary<string, OrgHealthTargetRule> targets, string? user, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var stored = await db.OrgHealthTargets.ToListAsync(ct);
        var now = _clock.GetUtcNow().UtcDateTime;
        var by = string.IsNullOrWhiteSpace(user) ? null : user.Trim();
        var changes = 0;

        foreach (var (rawKey, rule) in targets)
        {
            var key = rawKey.Trim();
            if (key.Length == 0 || key.Length > OrgHealthTarget.KpiKeyMaxLength)
            {
                continue;
            }

            var existing = stored.Where(t => string.Equals(t.KpiKey, key, StringComparison.OrdinalIgnoreCase)).ToList();
            if (rule == OrgHealthKpis.DefaultTarget(key))
            {
                if (existing.Count > 0)
                {
                    db.OrgHealthTargets.RemoveRange(existing);
                    changes++;
                }

                continue;
            }

            var row = existing.FirstOrDefault();
            if (row is not null && OrgHealthTargetRule.From(row) == rule)
            {
                continue;
            }

            if (row is null)
            {
                row = new OrgHealthTarget { KpiKey = key };
                db.OrgHealthTargets.Add(row);
            }

            row.Text = rule.Text;
            row.Comparison = rule.Comparison;
            row.Value = rule.Value;
            row.UpdatedAt = now;
            row.UpdatedBy = by;
            db.OrgHealthTargets.RemoveRange(existing.Skip(1));
            changes++;
        }

        if (changes > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return changes;
    }
}

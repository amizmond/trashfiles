using Estimation.Core.Administration.Audit;
using Estimation.Core.Features.Hygiene.Data;
using Estimation.Core.Features.Hygiene.Models;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.Features.Hygiene.Services;

/// <summary>The values a choice field can take, for the rule editor's value pickers.</summary>
public sealed record HygieneChoiceValues(
    IReadOnlyList<string> RequirementStatuses,
    IReadOnlyList<string> TechnicalApprovals,
    IReadOnlyList<string> FundingStatuses,
    IReadOnlyList<string> JiraStatuses)
{
    public static readonly HygieneChoiceValues Empty = new([], [], [], []);

    public IReadOnlyList<string> For(HygieneField field) => field switch
    {
        HygieneField.RequirementStatus => RequirementStatuses,
        HygieneField.TechnicalApproval => TechnicalApprovals,
        HygieneField.FundingStatus => FundingStatuses,
        HygieneField.Status => JiraStatuses,
        _ => []
    };
}

public interface IFeatureHygieneRuleService
{
    Task<List<FeatureHygieneRule>> GetForArtAsync(int capitalProjectId);

    /// <summary>Every ART's rules, enabled and disabled, in sort order.</summary>
    Task<IReadOnlyDictionary<int, IReadOnlyList<FeatureHygieneRule>>> GetAllByArtAsync();

    /// <summary>The ARTs that have at least one rule.</summary>
    Task<IReadOnlyList<int>> GetArtIdsWithRulesAsync();

    /// <summary>
    /// Replaces the ART's rule set with the given rules: rows with a known Id are updated, rows with
    /// Id 0 are inserted, rows no longer present are deleted. Order in the list becomes the sort order.
    /// Throws when a rule is incomplete; see <see cref="HygieneRuleValidation"/>.
    /// </summary>
    Task<List<FeatureHygieneRule>> SaveForArtAsync(int capitalProjectId, IReadOnlyList<FeatureHygieneRule> rules);

    /// <summary>Replaces the target ART's rules with copies of the source ART's. Returns how many were copied.</summary>
    Task<int> CopyFromArtAsync(int sourceCapitalProjectId, int targetCapitalProjectId);

    /// <summary>A starter set for an ART with no rules yet. Not saved.</summary>
    IReadOnlyList<FeatureHygieneRule> RecommendedDefaults(int capitalProjectId);

    Task<HygieneChoiceValues> GetChoiceValuesAsync();
}

public class FeatureHygieneRuleService : IFeatureHygieneRuleService
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;
    private readonly IAuditUserProvider _auditUser;

    public FeatureHygieneRuleService(IDbContextFactory<EstimationDbContext> ctx, IAuditUserProvider auditUser)
    {
        _ctx = ctx;
        _auditUser = auditUser;
    }

    public async Task<List<FeatureHygieneRule>> GetForArtAsync(int capitalProjectId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await QueryForArt(db, capitalProjectId).AsNoTracking().ToListAsync();
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<FeatureHygieneRule>>> GetAllByArtAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var rules = await db.FeatureHygieneRules()
            .AsNoTracking()
            .Where(r => r.PiId == null)
            .OrderBy(r => r.CapitalProjectId)
            .ThenBy(r => r.SortOrder)
            .ThenBy(r => r.Id)
            .ToListAsync();

        return rules
            .GroupBy(r => r.CapitalProjectId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<FeatureHygieneRule>)g.ToList());
    }

    public async Task<IReadOnlyList<int>> GetArtIdsWithRulesAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();

        return await db.FeatureHygieneRules()
            .AsNoTracking()
            .Where(r => r.PiId == null)
            .Select(r => r.CapitalProjectId)
            .Distinct()
            .ToListAsync();
    }

    public async Task<List<FeatureHygieneRule>> SaveForArtAsync(int capitalProjectId, IReadOnlyList<FeatureHygieneRule> rules)
    {
        var problems = rules
            .SelectMany((rule, index) => HygieneRuleValidation.Problems(rule).Select(p => $"Rule {index + 1}: {p}"))
            .ToList();

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", problems));
        }

        await using var db = await _ctx.CreateDbContextAsync();

        var existing = await QueryForArt(db, capitalProjectId).ToListAsync();
        var byId = existing.ToDictionary(r => r.Id);
        var kept = new HashSet<int>();
        var user = _auditUser.GetCurrentUserName();
        var now = DateTime.UtcNow;

        for (var index = 0; index < rules.Count; index++)
        {
            var incoming = rules[index];
            var kind = HygieneFieldCatalog.KindOf(incoming.Field);
            var parametersJson = incoming.Parameters.ForCheck(incoming.Check, kind).ToJson();
            var message = string.IsNullOrWhiteSpace(incoming.Message) ? null : incoming.Message.Trim();

            if (incoming.Id > 0 && byId.TryGetValue(incoming.Id, out var row))
            {
                kept.Add(row.Id);

                var changed = row.Field != incoming.Field
                    || row.Check != incoming.Check
                    || row.ParametersJson != parametersJson
                    || row.Message != message
                    || row.IsEnabled != incoming.IsEnabled
                    || row.SortOrder != index;

                if (!changed)
                {
                    continue;
                }

                row.Field = incoming.Field;
                row.Check = incoming.Check;
                row.ParametersJson = parametersJson;
                row.Message = message;
                row.IsEnabled = incoming.IsEnabled;
                row.SortOrder = index;
                row.ModifiedBy = user;
                row.ModifiedAt = now;
                continue;
            }

            db.FeatureHygieneRules().Add(new FeatureHygieneRule
            {
                CapitalProjectId = capitalProjectId,
                PiId = null,
                Field = incoming.Field,
                Check = incoming.Check,
                ParametersJson = parametersJson,
                Message = message,
                IsEnabled = incoming.IsEnabled,
                SortOrder = index,
                CreatedBy = user,
                CreatedAt = now
            });
        }

        db.FeatureHygieneRules().RemoveRange(existing.Where(r => !kept.Contains(r.Id)));

        await db.SaveChangesAsync();

        return await QueryForArt(db, capitalProjectId).AsNoTracking().ToListAsync();
    }

    public async Task<int> CopyFromArtAsync(int sourceCapitalProjectId, int targetCapitalProjectId)
    {
        if (sourceCapitalProjectId == targetCapitalProjectId)
        {
            return 0;
        }

        var source = await GetForArtAsync(sourceCapitalProjectId);

        var copies = source
            .Select(r => new FeatureHygieneRule
            {
                Field = r.Field,
                Check = r.Check,
                ParametersJson = r.ParametersJson,
                Message = r.Message,
                IsEnabled = r.IsEnabled
            })
            .ToList();

        await SaveForArtAsync(targetCapitalProjectId, copies);

        return copies.Count;
    }

    public IReadOnlyList<FeatureHygieneRule> RecommendedDefaults(int capitalProjectId)
    {
        HygieneField[] fields =
        [
            HygieneField.Summary,
            HygieneField.Description,
            HygieneField.BusinessOutcome,
            HygieneField.StoryPoints,
            HygieneField.TargetStart,
            HygieneField.TargetEnd,
            HygieneField.Teams
        ];

        return fields
            .Select((field, index) => new FeatureHygieneRule
            {
                CapitalProjectId = capitalProjectId,
                Field = field,
                Check = HygieneCheck.NotEmpty,
                ParametersJson = new HygieneRuleParameters().ForCheck(HygieneCheck.NotEmpty, HygieneFieldCatalog.KindOf(field)).ToJson(),
                SortOrder = index,
                IsEnabled = true
            })
            .ToList();
    }

    public async Task<HygieneChoiceValues> GetChoiceValuesAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var requirementStatuses = await db.RequirementStatuses
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => s.Name)
            .ToListAsync();

        var technicalApprovals = await db.TechnicalApprovals
            .AsNoTracking()
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Name)
            .Select(t => t.Name)
            .ToListAsync();

        var fundingStatuses = await db.UnfundedOptions
            .AsNoTracking()
            .OrderBy(u => u.Order)
            .ThenBy(u => u.Name)
            .Select(u => u.Name)
            .ToListAsync();

        var jiraStatuses = await db.Features
            .AsNoTracking()
            .Where(f => f.Status != null && f.Status != "")
            .Select(f => f.Status!)
            .Distinct()
            .ToListAsync();

        return new HygieneChoiceValues(
            Tidy(requirementStatuses),
            Tidy(technicalApprovals),
            Tidy(fundingStatuses),
            Tidy(jiraStatuses.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)));
    }

    private static IQueryable<FeatureHygieneRule> QueryForArt(EstimationDbContext db, int capitalProjectId) =>
        db.FeatureHygieneRules()
            .Where(r => r.CapitalProjectId == capitalProjectId && r.PiId == null)
            .OrderBy(r => r.SortOrder)
            .ThenBy(r => r.Id);

    private static IReadOnlyList<string> Tidy(IEnumerable<string> values) =>
        values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

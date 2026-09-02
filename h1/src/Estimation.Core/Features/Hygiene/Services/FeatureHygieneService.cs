using Estimation.Core.Features.Hygiene.Data;
using Estimation.Core.Features.Hygiene.Models;
using Estimation.Core.Features.Models;
using Estimation.Core.Features.Services;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.Features.Hygiene.Services;

public sealed record FeatureHygieneRow(
    int FeatureId,
    string? JiraId,
    string? Name,
    string? Summary,
    string? Status,
    string? Teams,
    string? PiName,
    IReadOnlyList<HygieneFailure> Failures)
{
    public bool IsHealthy => Failures.Count == 0;
}

public sealed record FeatureHygieneReport(
    int CapitalProjectId,
    string ArtName,
    string? ArtJiraKey,
    int PiId,
    string PiName,
    IReadOnlyList<FeatureHygieneRule> Rules,
    IReadOnlyList<FeatureHygieneRow> Rows)
{
    public int Total => Rows.Count;

    public int Healthy => Rows.Count(r => r.IsHealthy);

    public int Unhealthy => Total - Healthy;

    public bool HasRules => Rules.Any(r => r.IsEnabled);

    public int FailureCount(int ruleId) => Rows.Count(r => r.Failures.Any(f => f.RuleId == ruleId));
}

public interface IFeatureHygieneService
{
    /// <summary>
    /// Checks every feature of the ART that is in the PI against the ART's saved rules. Null when
    /// the ART or the PI does not exist.
    /// </summary>
    Task<FeatureHygieneReport?> EvaluateAsync(int capitalProjectId, int piId, bool includePiLabelMatches = true);

    /// <summary>The same check with rules that are not saved yet, for previewing edits.</summary>
    Task<FeatureHygieneReport?> EvaluateAsync(
        int capitalProjectId,
        int piId,
        IReadOnlyList<FeatureHygieneRule> rules,
        bool includePiLabelMatches = true);

    /// <summary>
    /// Enabled rules keyed by the ART's Jira project key, for pages that show features of many ARTs.
    /// ARTs without a Jira key or without rules are absent.
    /// </summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<FeatureHygieneRule>>> GetRulesByArtJiraKeyAsync();
}

public class FeatureHygieneService : IFeatureHygieneService
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;
    private readonly IFeatureHygieneRuleService _rules;

    public FeatureHygieneService(IDbContextFactory<EstimationDbContext> ctx, IFeatureHygieneRuleService rules)
    {
        _ctx = ctx;
        _rules = rules;
    }

    public async Task<FeatureHygieneReport?> EvaluateAsync(int capitalProjectId, int piId, bool includePiLabelMatches = true)
    {
        var rules = await _rules.GetForArtAsync(capitalProjectId);
        return await EvaluateAsync(capitalProjectId, piId, rules, includePiLabelMatches);
    }

    public async Task<FeatureHygieneReport?> EvaluateAsync(
        int capitalProjectId,
        int piId,
        IReadOnlyList<FeatureHygieneRule> rules,
        bool includePiLabelMatches = true)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var art = await db.CapitalProjects
            .AsNoTracking()
            .Where(cp => cp.Id == capitalProjectId)
            .Select(cp => new { cp.Id, cp.Name, cp.JiraKey })
            .FirstOrDefaultAsync();

        var pi = await db.Pis
            .AsNoTracking()
            .Where(p => p.Id == piId)
            .Select(p => new { p.Id, p.Name })
            .FirstOrDefaultAsync();

        if (art is null || pi is null)
        {
            return null;
        }

        var artKey = art.JiraKey?.Trim();
        var features = new List<Feature>();

        if (!string.IsNullOrEmpty(artKey))
        {
            var labelRules = includePiLabelMatches
                ? await FeatureScope.LoadPiLabelRulesAsync(db, pi.Name)
                : [];

            var candidates = await db.Features
                .LikelyOnArt(artKey)
                .WithPlanningLookups()
                .AsNoTracking()
                .ToListAsync();

            features = candidates
                .Where(f => FeatureScope.BelongsToArt(f, artKey))
                .Where(f => f.PiId == pi.Id || FeatureScope.MatchesPi(f, pi.Name, labelRules))
                .OrderBy(f => f.JiraId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.Id)
                .ToList();
        }

        var rows = features
            .Select(f => new FeatureHygieneRow(
                f.Id,
                f.JiraId,
                f.Name,
                f.Summary,
                f.Status,
                JoinTeams(f),
                f.Pi?.Name,
                FeatureHygieneEvaluator.Evaluate(f, rules)))
            .ToList();

        return new FeatureHygieneReport(art.Id, art.Name, artKey, pi.Id, pi.Name, rules, rows);
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<FeatureHygieneRule>>> GetRulesByArtJiraKeyAsync()
    {
        var byArt = await _rules.GetAllByArtAsync();

        if (byArt.Count == 0)
        {
            return new Dictionary<string, IReadOnlyList<FeatureHygieneRule>>(StringComparer.OrdinalIgnoreCase);
        }

        await using var db = await _ctx.CreateDbContextAsync();

        var artIds = byArt.Keys.ToList();
        var arts = await db.CapitalProjects
            .AsNoTracking()
            .Where(cp => artIds.Contains(cp.Id) && cp.JiraKey != null && cp.JiraKey != "")
            .Select(cp => new { cp.Id, cp.JiraKey })
            .ToListAsync();

        var result = new Dictionary<string, IReadOnlyList<FeatureHygieneRule>>(StringComparer.OrdinalIgnoreCase);

        foreach (var art in arts)
        {
            var enabled = byArt[art.Id].Where(r => r.IsEnabled).ToList();

            if (enabled.Count > 0)
            {
                result.TryAdd(art.JiraKey!.Trim(), enabled);
            }
        }

        return result;
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
}

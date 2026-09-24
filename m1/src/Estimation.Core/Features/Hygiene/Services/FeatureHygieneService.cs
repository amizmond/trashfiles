using Estimation.Core.Features.Hygiene.Data;
using Estimation.Core.Features.Hygiene.Models;
using Estimation.Core.Features.Models;
using Estimation.Core.Features.Services;
using Estimation.Core.Train.Services;
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
    string? ArtJiraKeys,
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

    public IReadOnlyDictionary<int, int> AppliesCounts { get; init; } = new Dictionary<int, int>();

    public int AppliesCount(int ruleId) => AppliesCounts.TryGetValue(ruleId, out var count) ? count : Total;
}

public interface IFeatureHygieneService
{
    Task<FeatureHygieneReport?> EvaluateAsync(int capitalProjectId, int piId, bool includePiLabelMatches = true);

    Task<IReadOnlyDictionary<int, IReadOnlyList<FeatureHygieneRule>>> GetRulesByArtIdAsync();

    Task<IReadOnlyDictionary<int, IReadOnlyList<HygieneFailure>>> GetFailuresByFeatureAsync(IReadOnlyCollection<int> featureIds);
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

        await using var db = await _ctx.CreateDbContextAsync();

        var art = await db.CapitalProjects
            .AsNoTracking()
            .Where(cp => cp.Id == capitalProjectId)
            .Select(cp => new { cp.Id, cp.Name })
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

        var matcher = await ArtMatcher.LoadAsync(db);
        var artKeys = matcher.KeysOf(art.Id);
        var features = new List<Feature>();

        if (artKeys.Count > 0)
        {
            var labelRules = includePiLabelMatches
                ? await FeatureScope.LoadPiLabelRulesAsync(db, pi.Name)
                : [];

            var candidates = await db.Features
                .LikelyOnArt(artKeys)
                .WithPlanningLookups()
                .AsNoTracking()
                .ToListAsync();

            features = candidates
                .Where(f => matcher.BelongsTo(f, art.Id))
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

        var appliesCounts = rules
            .Where(r => r.Logic.IsConditional)
            .ToDictionary(r => r.Id, r => features.Count(f => FeatureHygieneEvaluator.Applies(f, r)));

        var keysText = artKeys.Count > 0 ? string.Join(", ", artKeys) : null;

        return new FeatureHygieneReport(art.Id, art.Name, keysText, pi.Id, pi.Name, rules, rows)
        {
            AppliesCounts = appliesCounts
        };
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<FeatureHygieneRule>>> GetRulesByArtIdAsync()
    {
        var byArt = await _rules.GetAllByArtAsync();
        var result = new Dictionary<int, IReadOnlyList<FeatureHygieneRule>>();

        foreach (var (artId, artRules) in byArt)
        {
            var enabled = artRules.Where(r => r.IsEnabled).ToList();

            if (enabled.Count > 0)
            {
                result[artId] = enabled;
            }
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<HygieneFailure>>> GetFailuresByFeatureAsync(IReadOnlyCollection<int> featureIds)
    {
        var result = new Dictionary<int, IReadOnlyList<HygieneFailure>>();

        if (featureIds.Count == 0)
        {
            return result;
        }

        var rulesByArtId = await GetRulesByArtIdAsync();

        if (rulesByArtId.Count == 0)
        {
            return result;
        }

        await using var db = await _ctx.CreateDbContextAsync();

        var ids = featureIds.Distinct().ToList();

        var features = await db.Features
            .Where(f => ids.Contains(f.Id))
            .WithPlanningLookups()
            .AsNoTracking()
            .ToListAsync();

        var matcher = await ArtMatcher.LoadAsync(db);

        foreach (var feature in features)
        {
            if (matcher.ArtIdOf(feature) is { } artId && rulesByArtId.TryGetValue(artId, out var rules))
            {
                result[feature.Id] = FeatureHygieneEvaluator.Evaluate(feature, rules);
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

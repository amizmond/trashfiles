using Estimation.Core.Features.Models;
using Estimation.Core.PlanningIncrement.Models;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.Features.Services;

/// <summary>
/// The one place that decides which ART and which PI a feature belongs to. Snapshots and hygiene
/// checks share it, so "the features of PI X on ART Y" means the same thing on every page.
/// </summary>
public static class FeatureScope
{
    public sealed record PiLabelRule(HashSet<string> Labels, PiLabelMatchMode Mode);

    /// <summary>
    /// The Jira project key of a feature: the stored project key when there is one, otherwise the
    /// prefix of the Jira ID up to the first dash.
    /// </summary>
    public static string? ProjectKeyOf(string? projectKey, string? jiraId)
    {
        if (!string.IsNullOrWhiteSpace(projectKey))
        {
            return projectKey.Trim();
        }

        if (string.IsNullOrWhiteSpace(jiraId))
        {
            return null;
        }

        var dash = jiraId.IndexOf('-');
        return dash > 0 ? jiraId[..dash].Trim() : jiraId.Trim();
    }

    public static string? ProjectKeyOf(Feature feature) => ProjectKeyOf(feature.ProjectKey, feature.JiraId);

    public static bool BelongsToArt(Feature feature, string? artJiraKey)
    {
        var key = artJiraKey?.Trim();

        return !string.IsNullOrEmpty(key)
            && string.Equals(ProjectKeyOf(feature), key, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The lookups a feature needs before it can be captured or checked: PI, PI objective, business
    /// outcome, requirement status, technical approval, funding status and teams.
    /// </summary>
    public static IQueryable<Feature> WithPlanningLookups(this IQueryable<Feature> features) =>
        features
            .Include(f => f.Pi)
            .Include(f => f.PiObjective)
            .Include(f => f.BusinessOutcome)
            .Include(f => f.RequirementStatus)
            .Include(f => f.TechnicalApproval)
            .Include(f => f.UnfundedOption)
            .Include(f => f.FeatureTeams).ThenInclude(ft => ft.Team)
            .AsSplitQuery();

    /// <summary>
    /// A database-side pre-filter for the features that can belong to an ART. It is deliberately
    /// generous; apply <see cref="BelongsToArt"/> to the loaded rows for the exact answer.
    /// </summary>
    public static IQueryable<Feature> LikelyOnArt(this IQueryable<Feature> features, string artJiraKey)
    {
        var key = artJiraKey.Trim().ToUpperInvariant();
        var prefix = key + "-";

        return features.Where(f =>
            (f.ProjectKey != null && f.ProjectKey.Trim().ToUpper() == key)
            || ((f.ProjectKey == null || f.ProjectKey.Trim() == "")
                && f.JiraId != null
                && f.JiraId.ToUpper().StartsWith(prefix)));
    }

    public static async Task<List<PiLabelRule>> LoadPiLabelRulesAsync(EstimationDbContext db, string piName)
    {
        var pis = await db.Pis
            .AsNoTracking()
            .Where(p => p.Name == piName && p.FeatureLabels != null && p.FeatureLabels != "")
            .Select(p => new { p.FeatureLabels, p.LabelMatchMode })
            .ToListAsync();

        return pis
            .Select(p => new PiLabelRule(PiLabelMatching.ParseLabels(p.FeatureLabels), p.LabelMatchMode))
            .Where(rule => rule.Labels.Count > 0)
            .ToList();
    }

    /// <summary>
    /// A feature is in a PI when its PI is set to it, or when one of the PI's label rules matches
    /// the feature's labels.
    /// </summary>
    public static bool MatchesPi(Feature feature, string piName, IReadOnlyList<PiLabelRule> labelRules)
    {
        if (feature.Pi is not null && string.Equals(feature.Pi.Name, piName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (labelRules.Count == 0 || string.IsNullOrEmpty(feature.Labels))
        {
            return false;
        }

        var featureLabels = PiLabelMatching.ParseLabels(feature.Labels);

        if (featureLabels.Count == 0)
        {
            return false;
        }

        return labelRules.Any(rule => rule.Mode == PiLabelMatchMode.All
            ? rule.Labels.All(featureLabels.Contains)
            : rule.Labels.Any(featureLabels.Contains));
    }
}

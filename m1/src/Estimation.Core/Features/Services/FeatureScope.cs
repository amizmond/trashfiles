using System.Linq.Expressions;
using Estimation.Core.Features.Models;
using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Train.Services;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.Features.Services;

public static class FeatureScope
{
    public sealed record PiLabelRule(HashSet<string> Labels, PiLabelMatchMode Mode);

    public static IQueryable<Feature> WithPlanningLookups(this IQueryable<Feature> features) =>
        features
            .Include(f => f.Pi)
            .Include(f => f.PiObjective)
            .Include(f => f.BusinessOutcome)
            .Include(f => f.RequirementStatus)
            .Include(f => f.TechnicalApproval)
            .Include(f => f.UnfundedOption)
            .Include(f => f.FeatureTeams).ThenInclude(ft => ft.Team)
            .Include(f => f.FeatureTeams).ThenInclude(ft => ft.TechnologyStacks)
            .AsSplitQuery();

    public static IQueryable<Feature> LikelyOnArt(this IQueryable<Feature> features, IEnumerable<string> artJiraKeys)
    {
        var keys = artJiraKeys
            .Select(JiraProjectKeys.Normalize)
            .Where(k => k is not null)
            .Select(k => k!)
            .Distinct()
            .ToList();

        if (keys.Count == 0)
        {
            return features.Where(_ => false);
        }

        var parameter = Expression.Parameter(typeof(Feature), "f");
        Expression? body = null;
        foreach (var key in keys)
        {
            var prefix = key + "-";
            Expression<Func<Feature, bool>> onKey = f =>
                (f.ProjectKey != null && f.ProjectKey.Trim().ToUpper() == key)
                || ((f.ProjectKey == null || f.ProjectKey.Trim() == "")
                    && f.JiraId != null
                    && f.JiraId.ToUpper().StartsWith(prefix));
            var rebound = new ParameterRebinder(onKey.Parameters[0], parameter).Visit(onKey.Body);
            body = body is null ? rebound : Expression.OrElse(body, rebound);
        }

        return features.Where(Expression.Lambda<Func<Feature, bool>>(body!, parameter));
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

    private sealed class ParameterRebinder(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : node;
    }
}

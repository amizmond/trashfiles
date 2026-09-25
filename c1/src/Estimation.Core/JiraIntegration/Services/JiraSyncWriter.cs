using Estimation.Core.Features.Models;
using Estimation.Core.JiraIntegration.Client;
using Estimation.Core.JiraIntegration.Client.JiraSync;
using Estimation.Core.JiraIntegration.Models;
using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Train.Models;
using Estimation.Core.Train.Services;
using Estimation.Core.Resources.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Estimation.Core.JiraIntegration.Services;

public record JiraSyncWriteResult(int Created, int Updated, IReadOnlyList<string> Warnings);

public interface IJiraSyncWriter
{
    Task<JiraSyncWriteResult> WriteAsync(
        HierarchyEntityType type,
        IReadOnlyList<JiraIssueResponse> issues,
        bool background,
        CancellationToken cancellationToken);
}

public class JiraSyncWriter : IJiraSyncWriter
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;
    private readonly IServiceProvider _services;

    public JiraSyncWriter(IDbContextFactory<EstimationDbContext> ctx, IServiceProvider services)
    {
        _ctx = ctx;
        _services = services;
    }

    public async Task<JiraSyncWriteResult> WriteAsync(
        HierarchyEntityType type,
        IReadOnlyList<JiraIssueResponse> issues,
        bool background,
        CancellationToken cancellationToken)
    {
        if (issues.Count == 0)
        {
            return new JiraSyncWriteResult(0, 0, []);
        }

        await using var db = await _ctx.CreateDbContextAsync(cancellationToken);

        var teams = await db.Teams.AsNoTracking().ToListAsync(cancellationToken);
        var pisByName = (await db.Pis.ToListAsync(cancellationToken))
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var matcher = await ArtMatcher.LoadAsync(db);
        var warnings = new List<string>();

        var (created, updated) = await WriteTypeAsync(
            db, type, issues, teams, pisByName, matcher, warnings, background, cancellationToken);

        return new JiraSyncWriteResult(created, updated, warnings);
    }

    private async Task<(int Created, int Updated)> WriteTypeAsync(
        EstimationDbContext db,
        HierarchyEntityType type,
        IReadOnlyList<JiraIssueResponse> issues,
        IReadOnlyList<Team> teams,
        Dictionary<string, Pi> pisByName,
        ArtMatcher matcher,
        List<string> warnings,
        bool background,
        CancellationToken cancellationToken)
    {
        var converters = JiraSyncModelMap.For(ClrType(type))
            .Where(b => !b.IsScalar && b.Converter is not null && (background ? b.BackgroundSync : b.ManualSync))
            .ToList();

        var keys = issues.Select(i => i.Key).ToList();
        var existing = await LoadExistingAsync(db, type, keys, cancellationToken);

        int created = 0, updated = 0;
        var pairs = new List<(JiraIssueResponse Issue, JiraIssue Entity, bool IsNew)>(issues.Count);

        foreach (var issue in issues)
        {
            JiraIssue entity;
            bool isNew;

            if (existing.TryGetValue(issue.Key, out var found))
            {
                entity = found;
                isNew = false;
                updated++;
            }
            else
            {
                entity = NewEntity(type);
                entity.JiraId = issue.Key;
                if (entity is Feature newFeature)
                {
                    newFeature.IsLinkedToTheJira = true;
                }
                AddEntity(db, entity);
                isNew = true;
                created++;
            }

            if (isNew || entity is Feature || string.IsNullOrWhiteSpace(entity.ProjectKey))
            {
                entity.ProjectKey = JiraSyncItemMapping.ProjectKeyFromJiraKey(issue.Key);
            }
            JiraSyncApplier.ApplyScalars(entity, issue, isNew, background);
            pairs.Add((issue, entity, isNew));
        }

        await db.SaveChangesAsync(cancellationToken);

        foreach (var (issue, entity, isNew) in pairs)
        {
            var context = new JiraSyncConvertContext
            {
                Db = db,
                Matcher = matcher,
                IsNew = isNew,
                Teams = teams,
                PisByName = pisByName,
                Warnings = warnings,
            };

            foreach (var binding in converters)
            {
                var converter = (IJiraValueConverter)_services.GetRequiredService(binding.Converter!);
                await converter.ApplyAsync(entity, issue, context, cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return (created, updated);
    }

    private static async Task<Dictionary<string, JiraIssue>> LoadExistingAsync(
        EstimationDbContext db, HierarchyEntityType type, List<string> keys, CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, JiraIssue>(StringComparer.OrdinalIgnoreCase);

        switch (type)
        {
            case HierarchyEntityType.Feature:
                foreach (var f in await db.Features
                    .Include(x => x.FeatureTeams)
                    .Where(x => x.JiraId != null && keys.Contains(x.JiraId))
                    .ToListAsync(cancellationToken))
                {
                    map[f.JiraId!] = f;
                }
                break;
            case HierarchyEntityType.BusinessOutcome:
                foreach (var b in await db.BusinessOutcomes
                    .Where(x => x.JiraId != null && keys.Contains(x.JiraId))
                    .ToListAsync(cancellationToken))
                {
                    map[b.JiraId!] = b;
                }
                break;
            case HierarchyEntityType.PortfolioEpic:
                foreach (var p in await db.PortfolioEpics
                    .Where(x => x.JiraId != null && keys.Contains(x.JiraId))
                    .ToListAsync(cancellationToken))
                {
                    map[p.JiraId!] = p;
                }
                break;
            case HierarchyEntityType.StrategicObjective:
                foreach (var s in await db.StrategicObjectives
                    .Where(x => x.JiraId != null && keys.Contains(x.JiraId))
                    .ToListAsync(cancellationToken))
                {
                    map[s.JiraId!] = s;
                }
                break;
        }

        return map;
    }

    private static JiraIssue NewEntity(HierarchyEntityType type)
    {
        return type switch
        {
            HierarchyEntityType.Feature => new Feature(),
            HierarchyEntityType.BusinessOutcome => new BusinessOutcome(),
            HierarchyEntityType.PortfolioEpic => new PortfolioEpic(),
            HierarchyEntityType.StrategicObjective => new StrategicObjective(),
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
    }

    private static void AddEntity(EstimationDbContext db, JiraIssue entity)
    {
        switch (entity)
        {
            case Feature f:
                db.Features.Add(f);
                break;
            case BusinessOutcome b:
                db.BusinessOutcomes.Add(b);
                break;
            case PortfolioEpic p:
                db.PortfolioEpics.Add(p);
                break;
            case StrategicObjective s:
                db.StrategicObjectives.Add(s);
                break;
        }
    }

    private static Type ClrType(HierarchyEntityType type)
    {
        return type switch
        {
            HierarchyEntityType.Feature => typeof(Feature),
            HierarchyEntityType.BusinessOutcome => typeof(BusinessOutcome),
            HierarchyEntityType.PortfolioEpic => typeof(PortfolioEpic),
            HierarchyEntityType.StrategicObjective => typeof(StrategicObjective),
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
    }
}

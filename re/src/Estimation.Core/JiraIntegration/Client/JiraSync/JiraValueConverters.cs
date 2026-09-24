using Estimation.Core.Features.Models;
using Estimation.Core.JiraIntegration.Models;
using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Train.Models;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.JiraIntegration.Client.JiraSync;

public sealed class FeatureTeamConverter : IJiraValueConverter
{
    public Task ApplyAsync(JiraIssue target, JiraIssueResponse source, JiraSyncConvertContext context, CancellationToken cancellationToken)
    {
        if (target is not Feature feature)
        {
            return Task.CompletedTask;
        }

        var desiredTeamIds = new List<int>();
        foreach (var value in JiraTeamMatcher.SplitJiraValue(source.GfedTeam))
        {
            var team = JiraTeamMatcher.Match(value, context.Teams);
            if (team is null)
            {
                context.Warnings.Add($"Team \"{value}\" on {source.Key} did not match any DB team");
                continue;
            }
            if (!desiredTeamIds.Contains(team.Id))
            {
                desiredTeamIds.Add(team.Id);
            }
        }

        var desired = new HashSet<int>(desiredTeamIds);
        var existingTeamIds = new HashSet<int>();

        foreach (var link in feature.FeatureTeams.ToList())
        {
            if (!desired.Contains(link.TeamId))
            {
                context.Db.FeatureTeams.Remove(link);
            }
            else
            {
                existingTeamIds.Add(link.TeamId);
            }
        }

        foreach (var teamId in desiredTeamIds)
        {
            if (existingTeamIds.Add(teamId))
            {
                context.Db.FeatureTeams.Add(new FeatureTeam { FeatureId = feature.Id, TeamId = teamId });
            }
        }

        return Task.CompletedTask;
    }
}

public sealed class FeaturePiConverter : IJiraValueConverter
{
    public Task ApplyAsync(JiraIssue target, JiraIssueResponse source, JiraSyncConvertContext context, CancellationToken cancellationToken)
    {
        if (target is not Feature feature)
        {
            return Task.CompletedTask;
        }

        var name = source.PlanningIncrement?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            feature.Pi = null;
            feature.PiId = null;
            return Task.CompletedTask;
        }

        if (!context.PisByName.TryGetValue(name, out var pi))
        {
            pi = new Pi { Name = name };
            context.Db.Pis.Add(pi);
            context.PisByName[name] = pi;

            feature.Pi = pi;
            return Task.CompletedTask;
        }

        feature.Pi = pi;
        feature.PiId = pi.Id;
        return Task.CompletedTask;
    }
}

public sealed class FeatureParentConverter : IJiraValueConverter
{
    public async Task ApplyAsync(JiraIssue target, JiraIssueResponse source, JiraSyncConvertContext context, CancellationToken cancellationToken)
    {
        if (target is not Feature feature || string.IsNullOrEmpty(source.ParentLink))
        {
            return;
        }

        var parent = await context.Db.BusinessOutcomes
            .FirstOrDefaultAsync(b => b.JiraId == source.ParentLink, cancellationToken);
        if (parent is not null && feature.BusinessOutcomeId != parent.Id)
        {
            feature.BusinessOutcomeId = parent.Id;
        }
    }
}

public sealed class BusinessOutcomeParentConverter : IJiraValueConverter
{
    public async Task ApplyAsync(JiraIssue target, JiraIssueResponse source, JiraSyncConvertContext context, CancellationToken cancellationToken)
    {
        if (target is not BusinessOutcome businessOutcome || string.IsNullOrEmpty(source.ParentLink))
        {
            return;
        }

        var parent = await context.Db.PortfolioEpics
            .FirstOrDefaultAsync(p => p.JiraId == source.ParentLink, cancellationToken);
        if (parent is not null && businessOutcome.PortfolioEpicId != parent.Id)
        {
            businessOutcome.PortfolioEpicId = parent.Id;
        }
    }
}

public sealed class PortfolioEpicParentConverter : IJiraValueConverter
{
    public async Task ApplyAsync(JiraIssue target, JiraIssueResponse source, JiraSyncConvertContext context, CancellationToken cancellationToken)
    {
        if (target is not PortfolioEpic portfolioEpic || string.IsNullOrEmpty(source.ParentLink))
        {
            return;
        }

        var parent = await context.Db.StrategicObjectives
            .FirstOrDefaultAsync(s => s.JiraId == source.ParentLink, cancellationToken);
        if (parent is null)
        {
            return;
        }

        var exists = await context.Db.StrategicObjectivePortfolioEpics
            .AnyAsync(l => l.StrategicObjectiveId == parent.Id && l.PortfolioEpicId == portfolioEpic.Id, cancellationToken);
        if (!exists)
        {
            context.Db.StrategicObjectivePortfolioEpics.Add(new StrategicObjectivePortfolioEpic
            {
                StrategicObjectiveId = parent.Id,
                PortfolioEpicId = portfolioEpic.Id,
            });
        }
    }
}

public sealed class StrategicObjectiveParentConverter : IJiraValueConverter
{
    public async Task ApplyAsync(JiraIssue target, JiraIssueResponse source, JiraSyncConvertContext context, CancellationToken cancellationToken)
    {
        if (target is not StrategicObjective strategicObjective)
        {
            return;
        }

        var exists = await context.Db.CapitalProjectStrategicObjectives
            .AnyAsync(l => l.CapitalProjectId == context.CapitalProjectId
                           && l.StrategicObjectiveId == strategicObjective.Id, cancellationToken);
        if (!exists)
        {
            context.Db.CapitalProjectStrategicObjectives.Add(new ArtStrategicObjective
            {
                CapitalProjectId = context.CapitalProjectId,
                StrategicObjectiveId = strategicObjective.Id,
            });
        }
    }
}

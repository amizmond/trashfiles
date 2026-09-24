
using Estimation.Core.Features.Models;
using Estimation.Core.JiraIntegration.Models;
using Estimation.Core.Train.Models;
using Estimation.Core.Resources.Models;

namespace Estimation.Core.JiraIntegration.Client;

public sealed class JiraDiffService : IJiraDiffService
{
    public FeatureDiffResult DiffFeatures(
        IReadOnlyList<JiraIssueResponse> jiraIssues,
        IReadOnlyDictionary<string, Feature> dbByJiraKey,
        IReadOnlyCollection<string> dbPiNames,
        IReadOnlyCollection<Team> dbTeams)
    {
        var piSet = new HashSet<string>(dbPiNames, StringComparer.OrdinalIgnoreCase);
        var teamsNotFound = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pisToCreate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var diffs = new List<JiraDiff>();

        foreach (var ji in jiraIssues)
        {
            if (!dbByJiraKey.TryGetValue(ji.Key, out var db))
            {
                continue;
            }

            AddScalarDiffs(diffs, ji, db);
            AddDateAndPointsDiffs(diffs, ji, db);

            var dbProject = string.IsNullOrWhiteSpace(db.ProjectKey) ? null : db.ProjectKey.Trim();
            var jiraProject = JiraSyncItemMapping.ProjectKeyFromJiraKey(ji.Key);
            if (!string.Equals(dbProject, jiraProject, StringComparison.OrdinalIgnoreCase))
            {
                diffs.Add(new JiraDiff(ji.Key, JiraSyncProperties.Project, dbProject, jiraProject));
            }

            if (!string.Equals(db.RagExplain?.Trim(), ji.RagExplain?.Trim(), StringComparison.Ordinal))
            {
                diffs.Add(new JiraDiff(ji.Key, JiraSyncProperties.RagExplain, db.RagExplain, ji.RagExplain));
            }

            var dbPiName = db.Pi?.Name;
            var jiraPiName = string.IsNullOrWhiteSpace(ji.PlanningIncrement) ? null : ji.PlanningIncrement.Trim();
            if (!string.Equals(dbPiName, jiraPiName, StringComparison.OrdinalIgnoreCase))
            {
                diffs.Add(new JiraDiff(ji.Key, JiraSyncProperties.Pi, dbPiName, jiraPiName));
            }
            if (jiraPiName is not null && !piSet.Contains(jiraPiName))
            {
                pisToCreate.Add(jiraPiName);
            }

            var dbTeamNames = db.FeatureTeams
                .Where(ft => ft.Team is not null)
                .Select(ft => JiraTeamMatcher.CanonicalName(ft.Team))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var resolvedJiraTeamNames = new List<string>();
            foreach (var jt in JiraTeamMatcher.SplitJiraValue(ji.GfedTeam))
            {
                var match = JiraTeamMatcher.Match(jt, dbTeams);
                if (match is null)
                {
                    teamsNotFound.Add(jt);
                    continue;
                }
                var canonical = JiraTeamMatcher.CanonicalName(match);
                if (!resolvedJiraTeamNames.Contains(canonical, StringComparer.OrdinalIgnoreCase))
                {
                    resolvedJiraTeamNames.Add(canonical);
                }
            }
            var resolvedSorted = resolvedJiraTeamNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            if (!dbTeamNames.SequenceEqual(resolvedSorted, StringComparer.OrdinalIgnoreCase))
            {
                diffs.Add(new JiraDiff(
                    ji.Key,
                    JiraSyncProperties.Teams,
                    dbTeamNames.Count > 0 ? string.Join(", ", dbTeamNames) : null,
                    resolvedSorted.Count > 0 ? string.Join(", ", resolvedSorted) : null));
            }
        }

        return new FeatureDiffResult(
            diffs,
            teamsNotFound.OrderBy(s => s).ToList(),
            pisToCreate.OrderBy(s => s).ToList());
    }

    public List<JiraDiff> DiffJiraIssues(
        IReadOnlyList<JiraIssueResponse> jiraIssues,
        IReadOnlyDictionary<string, BusinessOutcome> dbByJiraKey)
    {
        var diffs = new List<JiraDiff>();
        foreach (var ji in jiraIssues)
        {
            if (!dbByJiraKey.TryGetValue(ji.Key, out var db))
            {
                continue;
            }
            AddScalarDiffs(diffs, ji, db);
            AddDateAndPointsDiffs(diffs, ji, db);
        }
        return diffs;
    }

    public List<JiraDiff> DiffJiraIssues(
        IReadOnlyList<JiraIssueResponse> jiraIssues,
        IReadOnlyDictionary<string, PortfolioEpic> dbByJiraKey)
    {
        var diffs = new List<JiraDiff>();
        foreach (var ji in jiraIssues)
        {
            if (!dbByJiraKey.TryGetValue(ji.Key, out var db))
            {
                continue;
            }
            AddScalarDiffs(diffs, ji, db);
            AddDateAndPointsDiffs(diffs, ji, db);
        }
        return diffs;
    }

    public List<JiraDiff> DiffJiraIssues(
        IReadOnlyList<JiraIssueResponse> jiraIssues,
        IReadOnlyDictionary<string, StrategicObjective> dbByJiraKey)
    {
        var diffs = new List<JiraDiff>();
        foreach (var ji in jiraIssues)
        {
            if (!dbByJiraKey.TryGetValue(ji.Key, out var db))
            {
                continue;
            }
            AddScalarDiffs(diffs, ji, db);
            AddDateAndPointsDiffs(diffs, ji, db);
        }
        return diffs;
    }

    private static void AddScalarDiffs(List<JiraDiff> diffs, JiraIssueResponse ji, JiraIssue db)
    {
        if (!string.Equals(db.Summary, ji.Summary, StringComparison.Ordinal))
        {
            diffs.Add(new JiraDiff(ji.Key, JiraSyncProperties.Summary, db.Summary, ji.Summary));
        }
        if (!string.Equals(db.Description, ji.Description, StringComparison.Ordinal))
        {
            diffs.Add(new JiraDiff(ji.Key, JiraSyncProperties.Description, db.Description, ji.Description));
        }
        if (!string.Equals(db.AcceptanceCriteria, ji.AcceptanceCriteria, StringComparison.Ordinal))
        {
            diffs.Add(new JiraDiff(ji.Key, JiraSyncProperties.AcceptanceCriteria, db.AcceptanceCriteria, ji.AcceptanceCriteria));
        }
        if (!string.Equals(db.NavigatorId, ji.NavigatorId, StringComparison.Ordinal))
        {
            diffs.Add(new JiraDiff(ji.Key, JiraSyncProperties.NavigatorId, db.NavigatorId, ji.NavigatorId));
        }
        var jiraLabels = JiraSyncItemMapping.JoinLabels(ji.Labels);
        if (!string.Equals(db.Labels, jiraLabels, StringComparison.Ordinal))
        {
            diffs.Add(new JiraDiff(ji.Key, JiraSyncProperties.Labels, db.Labels, jiraLabels));
        }
        var jiraComponents = JiraSyncItemMapping.JoinComponents(ji.Components);
        if (!string.Equals(db.Components, jiraComponents, StringComparison.Ordinal))
        {
            diffs.Add(new JiraDiff(ji.Key, JiraSyncProperties.Components, db.Components, jiraComponents));
        }
        if (!string.Equals(db.Status, ji.Status, StringComparison.Ordinal))
        {
            diffs.Add(new JiraDiff(ji.Key, JiraSyncProperties.Status, db.Status, ji.Status));
        }
    }

    private static void AddDateAndPointsDiffs(List<JiraDiff> diffs, JiraIssueResponse ji, JiraIssue db)
    {
        if (db.TargetStart != ji.TargetStart)
        {
            diffs.Add(new JiraDiff(ji.Key, JiraSyncProperties.TargetStart,
                db.TargetStart?.ToString("yyyy-MM-dd"), ji.TargetStart?.ToString("yyyy-MM-dd")));
        }
        if (db.TargetEnd != ji.TargetEnd)
        {
            diffs.Add(new JiraDiff(ji.Key, JiraSyncProperties.TargetEnd,
                db.TargetEnd?.ToString("yyyy-MM-dd"), ji.TargetEnd?.ToString("yyyy-MM-dd")));
        }
        if (db.StoryPoints != ji.StoryPoints)
        {
            diffs.Add(new JiraDiff(ji.Key, JiraSyncProperties.StoryPoints,
                db.StoryPoints?.ToString(), ji.StoryPoints?.ToString()));
        }
    }
}

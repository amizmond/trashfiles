using Estimation.Core.Models;
using Estimation.Excel;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.Services;

public interface IUploadMasterService
{
    Task UploadAsync(Stream fileStream);
}

public class UploadMasterService : IUploadMasterService
{
    private readonly IDbContextFactory<EstimationDbContext> _contextFactory;
    private const int BatchSize = 500;

    public UploadMasterService(IDbContextFactory<EstimationDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    private static string? NormalizeKey(string? value) => value?.Trim().ToUpperInvariant();

    public async Task UploadAsync(Stream fileStream)
    {
        var readService = new ExcelReadService<UploadMaster>();
        var rows = readService.ReadSheet(fileStream, "Master").ToList();

        await using var db = await _contextFactory.CreateDbContextAsync();

        var capitalProjects = await db.CapitalProjects.ToListAsync();
        var capitalProjectsByName = BuildNameLookup(capitalProjects, cp => cp.Name);

        var programs = await db.StrategicObjectives.Include(pp => pp.CapitalProjectStrategicObjectives).ToListAsync();
        var programsByName = BuildNameLookup(programs, p => p.Summary);

        var portfolioEpics = await db.PortfolioEpics.Include(pe => pe.StrategicObjectivePortfolioEpics).ToListAsync();
        var portfolioEpicsByJiraId = BuildNameLookup(
            portfolioEpics.Where(pe => !string.IsNullOrWhiteSpace(pe.JiraId)), pe => pe.JiraId!);
        var portfolioEpicsByName = BuildNameLookup(
            portfolioEpics.Where(pe => !string.IsNullOrWhiteSpace(pe.Summary)), pe => pe.Summary!);

        var businessOutcomes = await db.BusinessOutcomes.ToListAsync();
        var boByJiraId = BuildNameLookup(
            businessOutcomes.Where(bo => !string.IsNullOrWhiteSpace(bo.JiraId)), bo => bo.JiraId!);
        var boByName = BuildNameLookup(
            businessOutcomes.Where(bo => !string.IsNullOrWhiteSpace(bo.Summary)), bo => bo.Summary!);

        var pis = await db.Pis.ToListAsync();
        var pisByName = BuildNameLookup(pis, p => p.Name);

        var unfundedOptions = await db.UnfundedOptions.ToListAsync();
        var unfundedByName = BuildNameLookup(unfundedOptions, u => u.Name);

        var teams = await db.Teams.ToListAsync();
        var teamsByName = BuildNameLookup(teams, t => t.Name);

        var skills = await db.Skills.ToListAsync();
        var skillsByName = BuildNameLookup(skills, s => s.Name);

        var existingFeatures = await db.Features
            .Include(f => f.FeatureTeams)
            .Include(f => f.FeatureTechnologyStacks)
            .ToListAsync();
        var featuresByJiraId = BuildNameLookup(
            existingFeatures.Where(f => !string.IsNullOrWhiteSpace(f.JiraId)), f => f.JiraId!);

        var rowIndex = 0;
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Summary))
                continue;

            CapitalProject? capitalProject = null;
            if (!string.IsNullOrWhiteSpace(row.CapitalProject))
            {
                var name = row.CapitalProject.Trim();
                capitalProjectsByName.TryGetValue(name, out capitalProject);
                if (capitalProject == null)
                {
                    capitalProject = new CapitalProject { Name = name };
                    db.CapitalProjects.Add(capitalProject);
                    await db.SaveChangesAsync();
                    capitalProjects.Add(capitalProject);
                    capitalProjectsByName[name] = capitalProject;
                }
            }

            StrategicObjective? program = null;
            if (!string.IsNullOrWhiteSpace(row.Program))
            {
                var name = row.Program.Trim();
                programsByName.TryGetValue(name, out program);
                if (program == null)
                {
                    program = new StrategicObjective { Summary = name };
                    db.StrategicObjectives.Add(program);
                    await db.SaveChangesAsync();
                    programs.Add(program);
                    programsByName[name] = program;
                }

                if (capitalProject != null &&
                    !program.CapitalProjectStrategicObjectives.Any(cpp => cpp.CapitalProjectId == capitalProject.Id))
                {
                    var cpp = new CapitalProjectStrategicObjective { CapitalProjectId = capitalProject.Id, StrategicObjectiveId = program.Id };
                    db.CapitalProjectStrategicObjectives.Add(cpp);
                    program.CapitalProjectStrategicObjectives.Add(cpp);
                }
            }

            PortfolioEpic? portfolioEpic = null;
            if (!string.IsNullOrWhiteSpace(row.BoParent) || !string.IsNullOrWhiteSpace(row.BoParentId))
            {
                var epicName = row.BoParent?.Trim();
                var epicJiraId = row.BoParentId?.Trim();

                if (!string.IsNullOrWhiteSpace(epicJiraId))
                    portfolioEpicsByJiraId.TryGetValue(epicJiraId, out portfolioEpic);
                if (portfolioEpic == null && !string.IsNullOrWhiteSpace(epicName))
                    portfolioEpicsByName.TryGetValue(epicName, out portfolioEpic);

                if (portfolioEpic == null)
                {
                    portfolioEpic = new PortfolioEpic { Summary = epicName, JiraId = epicJiraId };
                    db.PortfolioEpics.Add(portfolioEpic);
                    await db.SaveChangesAsync();
                    portfolioEpics.Add(portfolioEpic);
                    if (!string.IsNullOrWhiteSpace(epicJiraId))
                        portfolioEpicsByJiraId[epicJiraId] = portfolioEpic;
                    if (!string.IsNullOrWhiteSpace(epicName))
                        portfolioEpicsByName[epicName] = portfolioEpic;
                }

                if (program != null &&
                    !portfolioEpic.StrategicObjectivePortfolioEpics.Any(ppe => ppe.StrategicObjectiveId == program.Id))
                {
                    var ppe = new StrategicObjectivePortfolioEpic { StrategicObjectiveId = program.Id, PortfolioEpicId = portfolioEpic.Id };
                    db.StrategicObjectivePortfolioEpics.Add(ppe);
                    portfolioEpic.StrategicObjectivePortfolioEpics.Add(ppe);
                }
            }

            BusinessOutcome? businessOutcome = null;
            if (!string.IsNullOrWhiteSpace(row.BusinessOutcome) || !string.IsNullOrWhiteSpace(row.BoId))
            {
                var boName = row.BusinessOutcome?.Trim();
                var boJiraId = row.BoId?.Trim();

                if (!string.IsNullOrWhiteSpace(boJiraId))
                    boByJiraId.TryGetValue(boJiraId, out businessOutcome);
                if (businessOutcome == null && !string.IsNullOrWhiteSpace(boName))
                    boByName.TryGetValue(boName, out businessOutcome);

                if (businessOutcome == null)
                {
                    businessOutcome = new BusinessOutcome
                    {
                        Summary = boName,
                        JiraId = boJiraId,
                        PortfolioEpicId = portfolioEpic?.Id
                    };
                    db.BusinessOutcomes.Add(businessOutcome);
                    await db.SaveChangesAsync();
                    businessOutcomes.Add(businessOutcome);
                    if (!string.IsNullOrWhiteSpace(boJiraId))
                        boByJiraId[boJiraId] = businessOutcome;
                    if (!string.IsNullOrWhiteSpace(boName))
                        boByName[boName] = businessOutcome;
                }
            }

            Pi? pi = null;
            if (!string.IsNullOrWhiteSpace(row.Pi))
            {
                var name = row.Pi.Trim();
                pisByName.TryGetValue(name, out pi);
                if (pi == null)
                {
                    pi = new Pi { Name = name, Priority = row.Priority?.Trim() };
                    db.Pis.Add(pi);
                    await db.SaveChangesAsync();
                    pis.Add(pi);
                    pisByName[name] = pi;
                }
            }

            UnfundedOption? unfundedOption = null;
            if (!string.IsNullOrWhiteSpace(row.Unfunded))
            {
                var name = row.Unfunded.Trim();
                unfundedByName.TryGetValue(name, out unfundedOption);
                if (unfundedOption == null)
                {
                    unfundedOption = new UnfundedOption { Name = name };
                    db.UnfundedOptions.Add(unfundedOption);
                    await db.SaveChangesAsync();
                    unfundedOptions.Add(unfundedOption);
                    unfundedByName[name] = unfundedOption;
                }
            }

            var jiraId = row.Key?.Trim();
            var isExisting = false;
            Feature feature;

            if (!string.IsNullOrWhiteSpace(jiraId))
            {
                featuresByJiraId.TryGetValue(jiraId, out var existing);
                if (existing != null)
                {
                    feature = existing;
                    isExisting = true;
                }
                else
                {
                    feature = new Feature();
                }
            }
            else
            {
                feature = new Feature();
            }

            feature.JiraId = jiraId;
            feature.Name = row.Summary?.Trim();
            feature.Ranking = row.Ranking;
            feature.DateExpected = row.DateExpected;
            feature.BusinessOutcomeId = businessOutcome?.Id;
            feature.PiId = pi?.Id;
            feature.UnfundedOptionId = unfundedOption?.Id;

            if (!isExisting)
            {
                db.Features.Add(feature);
                existingFeatures.Add(feature);
                if (!string.IsNullOrWhiteSpace(jiraId))
                    featuresByJiraId[jiraId] = feature;
            }

            if (!string.IsNullOrWhiteSpace(row.Team))
            {
                var teamName = row.Team.Trim();
                if (teamName.StartsWith("Team ", StringComparison.OrdinalIgnoreCase))
                    teamName = teamName[5..].Trim();
                teamsByName.TryGetValue(teamName, out var team);
                if (team == null)
                {
                    team = new Team { Name = teamName };
                    db.Teams.Add(team);
                    await db.SaveChangesAsync();
                    teams.Add(team);
                    teamsByName[teamName] = team;
                }

                if (!feature.FeatureTeams.Any(ft => ft.TeamId == team.Id))
                {
                    var ft = new FeatureTeam { FeatureId = feature.Id, TeamId = team.Id };
                    db.FeatureTeams.Add(ft);
                    feature.FeatureTeams.Add(ft);
                }
            }

            // TechnologyStack import from Excel is not yet supported.
            // Previously this section imported RevisedSkills per feature.
            // With the TechnologyStack model, import should map to FeatureTechnologyStacks.

            rowIndex++;
            if (rowIndex % BatchSize == 0)
                await db.SaveChangesAsync();
        }

        await db.SaveChangesAsync();
    }

    private static Dictionary<string, T> BuildNameLookup<T>(IEnumerable<T> items, Func<T, string> keySelector)
    {
        var dict = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var key = keySelector(item);
            dict[key] = item;
        }
        return dict;
    }
}

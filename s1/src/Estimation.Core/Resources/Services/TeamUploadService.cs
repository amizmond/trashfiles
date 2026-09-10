using Estimation.Core.Train.Models;
using Estimation.Core.Train.Services;
using Estimation.Core.Resources.Models;
using Estimation.Excel;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Estimation.Core.Resources.Services;

public interface ITeamUploadService
{
    Task<byte[]> ExportAllTeamsAsync();
    Task<List<TeamUploadRow>> ParseFileAsync(Stream fileStream);
    Task SaveAsync(List<TeamUploadRow> rows);
}

public class TeamUploadService : ITeamUploadService
{
    private readonly IDbContextFactory<EstimationDbContext> _contextFactory;

    public TeamUploadService(IDbContextFactory<EstimationDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<byte[]> ExportAllTeamsAsync()
    {
        await using var db = await _contextFactory.CreateDbContextAsync();

        var teams = await db.Teams
            .Include(t => t.CapitalProjectTeams).ThenInclude(cpt => cpt.CapitalProject)
            .Include(t => t.TeamTechnologyStacks).ThenInclude(tts => tts.TechnologyStack)
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .ToListAsync();

        var projectNames = await db.CapitalProjects
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => p.Name)
            .ToListAsync();

        var rows = teams.Select(t => new TeamExportRow
        {
            Id = t.Id,
            Project = string.Join(", ", t.CapitalProjectTeams
                .Select(cpt => cpt.CapitalProject.Name)
                .OrderBy(n => n)),
            TeamName = t.Name,
            TechnologyStacks = string.Join(", ", t.TeamTechnologyStacks
                .Select(tts => tts.TechnologyStack.Name)
                .OrderBy(n => n))
        }).ToList();

        return TeamExcelExportService.GenerateTeamExport(rows, projectNames);
    }

    public async Task<List<TeamUploadRow>> ParseFileAsync(Stream fileStream)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();

        var existingTeams = await db.Teams
            .Include(t => t.CapitalProjectTeams).ThenInclude(cpt => cpt.CapitalProject)
            .Include(t => t.TeamTechnologyStacks).ThenInclude(tts => tts.TechnologyStack)
            .AsNoTracking()
            .ToListAsync();

        var allProjects = await db.CapitalProjects
            .AsNoTracking()
            .ToListAsync();

        var allTechStacks = await db.TechnologyStacks
            .AsNoTracking()
            .ToListAsync();

        var (headers, dataRows) = ExcelSheetReader.Read(fileStream, "Teams");
        var colMap = ExcelSheetReader.BuildColumnMap(headers);

        var result = new List<TeamUploadRow>();

        foreach (var row in dataRows)
        {
            var teamName = ExcelSheetReader.GetCell(row, colMap, "Team Name")?.Trim();
            if (string.IsNullOrWhiteSpace(teamName))
            {
                continue;
            }

            var projectCell = ExcelSheetReader.GetCell(row, colMap, "Project")?.Trim() ?? "";
            var techStackCell = ExcelSheetReader.GetCell(row, colMap, "Technology Stacks")?.Trim() ?? "";

            var uploadedProjects = ParseCommaSeparated(projectCell);
            var uploadedTechStacks = ParseCommaSeparated(techStackCell);

            var existingTeam = existingTeams.FirstOrDefault(t =>
                t.Name.Equals(teamName, StringComparison.OrdinalIgnoreCase));

            var uploadRow = new TeamUploadRow
            {
                ExistingTeamId = existingTeam?.Id,
                TeamName = teamName,
                ProjectNames = projectCell,
                TechnologyStacks = techStackCell,
                IsNew = existingTeam is null
            };

            if (existingTeam is not null)
            {
                var currentProjects = existingTeam.CapitalProjectTeams
                    .Select(cpt => cpt.CapitalProject.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var currentTechStacks = existingTeam.TeamTechnologyStacks
                    .Select(tts => tts.TechnologyStack.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var p in uploadedProjects)
                {
                    if (currentProjects.Contains(p))
                    {
                        uploadRow.UnchangedProjects.Add(p);
                    }
                    else
                    {
                        uploadRow.NewProjects.Add(p);
                    }
                }

                foreach (var p in currentProjects)
                {
                    if (!uploadedProjects.Any(up => up.Equals(p, StringComparison.OrdinalIgnoreCase)))
                    {
                        uploadRow.RemovedProjects.Add(p);
                    }
                }

                foreach (var ts in uploadedTechStacks)
                {
                    if (currentTechStacks.Contains(ts))
                    {
                        uploadRow.UnchangedTechStacks.Add(ts);
                    }
                    else
                    {
                        uploadRow.NewTechStacks.Add(ts);
                    }
                }

                foreach (var ts in currentTechStacks)
                {
                    if (!uploadedTechStacks.Any(uts => uts.Equals(ts, StringComparison.OrdinalIgnoreCase)))
                    {
                        uploadRow.RemovedTechStacks.Add(ts);
                    }
                }
            }
            else
            {
                uploadRow.NewProjects.AddRange(uploadedProjects);
                uploadRow.NewTechStacks.AddRange(uploadedTechStacks);
            }

            BlockExtraArtLinks(uploadRow, existingTeam?.IsArtSharedTeam ?? false);

            result.Add(uploadRow);
        }

        return result;
    }

    private static void BlockExtraArtLinks(TeamUploadRow row, bool isArtSharedTeam)
    {
        if (isArtSharedTeam || row.NewProjects.Count == 0)
        {
            return;
        }

        var kept = row.UnchangedProjects.FirstOrDefault();
        if (kept is null && row.NewProjects.Count == 1)
        {
            return;
        }

        var allowed = kept ?? row.NewProjects[0];
        var reason = kept is not null
            ? $"'{row.TeamName}' is not shared between ARTs and already belongs to '{kept}'."
            : $"'{row.TeamName}' is not shared between ARTs, so only '{allowed}' will be linked.";

        foreach (var project in row.NewProjects.Where(p => !p.Equals(allowed, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            row.NewProjects.Remove(project);
            row.BlockedProjects.Add(new TeamUploadBlockedProject { Name = project, Reason = reason });
        }
    }

    public async Task SaveAsync(List<TeamUploadRow> rows)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();

        var allProjects = await db.CapitalProjects.ToListAsync();
        var allTechStacks = await db.TechnologyStacks.ToListAsync();

        foreach (var row in rows.Where(r => r.HasChanges))
        {
            Team team;

            if (row.IsNew)
            {
                team = new Team { Name = row.TeamName };
                db.Teams.Add(team);
                await db.SaveChangesAsync();
            }
            else
            {
                team = (await db.Teams
                    .Include(t => t.CapitalProjectTeams)
                    .Include(t => t.TeamTechnologyStacks)
                    .FirstOrDefaultAsync(t => t.Id == row.ExistingTeamId))!;
            }

            foreach (var projectName in row.NewProjects)
            {
                var project = allProjects.FirstOrDefault(p =>
                    p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase));
                if (project is not null)
                {
                    var exists = team.CapitalProjectTeams.Any(cpt => cpt.CapitalProjectId == project.Id);
                    if (!exists)
                    {
                        var blockingArt = await CapitalProjectService.FindBlockingArtNameAsync(db, project.Id, team.Id);
                        if (blockingArt is not null)
                        {
                            Log.Warning(
                                "Skipped linking team {Team} to ART {Art}: the team is not shared and already belongs to {BlockingArt}",
                                team.Name, project.Name, blockingArt);
                            continue;
                        }

                        db.CapitalProjectTeams.Add(new CapitalProjectTeam
                        {
                            CapitalProjectId = project.Id,
                            TeamId = team.Id
                        });
                    }
                }
            }

            foreach (var projectName in row.RemovedProjects)
            {
                var project = allProjects.FirstOrDefault(p =>
                    p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase));
                if (project is not null)
                {
                    var link = await db.CapitalProjectTeams.FirstOrDefaultAsync(
                        cpt => cpt.CapitalProjectId == project.Id && cpt.TeamId == team.Id);
                    if (link is not null)
                    {
                        db.CapitalProjectTeams.Remove(link);
                    }
                }
            }

            foreach (var tsName in row.NewTechStacks)
            {
                var ts = allTechStacks.FirstOrDefault(t =>
                    t.Name.Equals(tsName, StringComparison.OrdinalIgnoreCase));
                if (ts is not null)
                {
                    var exists = team.TeamTechnologyStacks.Any(tts => tts.TechnologyStackId == ts.Id);
                    if (!exists)
                    {
                        db.TeamTechnologyStacks.Add(new TeamTechnologyStack
                        {
                            TeamId = team.Id,
                            TechnologyStackId = ts.Id
                        });
                    }
                }
            }

            foreach (var tsName in row.RemovedTechStacks)
            {
                var ts = allTechStacks.FirstOrDefault(t =>
                    t.Name.Equals(tsName, StringComparison.OrdinalIgnoreCase));
                if (ts is not null)
                {
                    var link = await db.TeamTechnologyStacks.FirstOrDefaultAsync(
                        tts => tts.TeamId == team.Id && tts.TechnologyStackId == ts.Id);
                    if (link is not null)
                    {
                        db.TeamTechnologyStacks.Remove(link);
                    }
                }
            }
        }

        await db.SaveChangesAsync();
    }

    private static List<string> ParseCommaSeparated(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new List<string>();
        }

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

}

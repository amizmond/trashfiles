using Estimation.Core.Resources.Models;
using Estimation.Core.Resources.Services;
using Estimation.Core.Tests.Infrastructure;
using Estimation.Core.Train.Models;
using Estimation.Excel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Estimation.Core.Tests.Resources;

public class TeamUploadServiceTests
{
    private readonly InMemoryDatabase _db = new();
    private readonly TeamUploadService _service;

    public TeamUploadServiceTests()
    {
        _service = new TeamUploadService(_db);
    }

    private static Stream Workbook(params (string DbId, string Project, string TeamName, string Stacks)[] rows)
    {
        var sheet = new ExcelWorkbookBuilder().AddSheet("Teams");
        sheet.WriteHeader("DB id", "Project", "Team Name", "Technology Stacks");

        foreach (var (dbId, project, teamName, stacks) in rows)
        {
            sheet.AddRow().Text(dbId).Text(project).Text(teamName).Text(stacks);
        }

        return new MemoryStream(sheet.Workbook.ToArray());
    }

    private Task SeedCatalogueAsync() => _db.SeedAsync(db =>
    {
        db.CapitalProjects.Add(new CapitalProject { Id = 50, Name = "Atlas" });
        db.CapitalProjects.Add(new CapitalProject { Id = 51, Name = "Borealis" });
        db.TechnologyStacks.Add(new TechnologyStack { Id = 60, Name = ".NET" });
        db.TechnologyStacks.Add(new TechnologyStack { Id = 61, Name = "Java" });
    });

    private Task SeedTeamAsync(int id, string name, int[]? projectIds = null, int[]? stackIds = null, bool isArtShared = false) =>
        _db.SeedAsync(db =>
        {
            db.Teams.Add(new Team { Id = id, Name = name, IsArtSharedTeam = isArtShared });
            foreach (var projectId in projectIds ?? Array.Empty<int>())
            {
                db.CapitalProjectTeams.Add(new CapitalProjectTeam { CapitalProjectId = projectId, TeamId = id });
            }

            foreach (var stackId in stackIds ?? Array.Empty<int>())
            {
                db.TeamTechnologyStacks.Add(new TeamTechnologyStack { TeamId = id, TechnologyStackId = stackId });
            }
        });

    [Fact]
    public async Task An_unknown_team_is_reported_as_new()
    {
        await SeedCatalogueAsync();

        var row = Assert.Single(await _service.ParseFileAsync(Workbook(("", "Atlas", "Falcons", ".NET"))));

        Assert.True(row.IsNew);
        Assert.Null(row.ExistingTeamId);
        Assert.Equal("Falcons", row.TeamName);
    }

    [Fact]
    public async Task A_known_team_is_matched_by_name_regardless_of_case()
    {
        await SeedCatalogueAsync();
        await SeedTeamAsync(20, "Falcons");

        var row = Assert.Single(await _service.ParseFileAsync(Workbook(("20", "Atlas", "FALCONS", ""))));

        Assert.False(row.IsNew);
        Assert.Equal(20, row.ExistingTeamId);
    }

    [Fact]
    public async Task A_row_without_a_team_name_is_skipped()
    {
        await SeedCatalogueAsync();

        var rows = await _service.ParseFileAsync(Workbook(
            ("", "Atlas", "   ", ".NET"),
            ("", "Atlas", "Falcons", ".NET")));

        Assert.Equal(new[] { "Falcons" }, rows.Select(r => r.TeamName));
    }

    [Fact]
    public async Task A_new_team_lists_everything_in_its_row_as_an_addition()
    {
        await SeedCatalogueAsync();

        var row = Assert.Single(await _service.ParseFileAsync(Workbook(("", "Atlas", "Falcons", ".NET, Java"))));

        Assert.Equal(new[] { "Atlas" }, row.NewProjects);
        Assert.Equal(new[] { ".NET", "Java" }, row.NewTechStacks);
        Assert.Empty(row.RemovedProjects);
        Assert.True(row.HasChanges);
    }

    [Fact]
    public async Task Entries_are_split_on_commas_and_trimmed()
    {
        await SeedCatalogueAsync();
        await SeedTeamAsync(20, "Falcons", isArtShared: true);

        var row = Assert.Single(await _service.ParseFileAsync(Workbook(("20", "  Atlas ,, Borealis  ", "Falcons", ""))));

        Assert.Equal(new[] { "Atlas", "Borealis" }, row.NewProjects);
    }

    [Fact]
    public async Task An_empty_cell_contributes_no_entries()
    {
        await SeedCatalogueAsync();

        var row = Assert.Single(await _service.ParseFileAsync(Workbook(("", "", "Falcons", ""))));

        Assert.Empty(row.NewProjects);
        Assert.Empty(row.NewTechStacks);
    }

    [Fact]
    public async Task An_unchanged_assignment_is_reported_as_unchanged()
    {
        await SeedCatalogueAsync();
        await SeedTeamAsync(20, "Falcons", projectIds: new[] { 50 }, stackIds: new[] { 60 });

        var row = Assert.Single(await _service.ParseFileAsync(Workbook(("20", "Atlas", "Falcons", ".NET"))));

        Assert.Equal(new[] { "Atlas" }, row.UnchangedProjects);
        Assert.Equal(new[] { ".NET" }, row.UnchangedTechStacks);
        Assert.False(row.HasChanges);
    }

    [Fact]
    public async Task An_added_project_is_reported_as_new()
    {
        await SeedCatalogueAsync();
        await SeedTeamAsync(20, "Falcons", projectIds: new[] { 50 }, isArtShared: true);

        var row = Assert.Single(await _service.ParseFileAsync(Workbook(("20", "Atlas, Borealis", "Falcons", ""))));

        Assert.Equal(new[] { "Borealis" }, row.NewProjects);
        Assert.Equal(new[] { "Atlas" }, row.UnchangedProjects);
    }

    [Fact]
    public async Task A_project_dropped_from_the_row_is_reported_for_removal()
    {
        await SeedCatalogueAsync();
        await SeedTeamAsync(20, "Falcons", projectIds: new[] { 50, 51 });

        var row = Assert.Single(await _service.ParseFileAsync(Workbook(("20", "Atlas", "Falcons", ""))));

        Assert.Equal(new[] { "Borealis" }, row.RemovedProjects);
    }

    [Fact]
    public async Task An_assignment_is_matched_regardless_of_case()
    {
        await SeedCatalogueAsync();
        await SeedTeamAsync(20, "Falcons", projectIds: new[] { 50 });

        var row = Assert.Single(await _service.ParseFileAsync(Workbook(("20", "atlas", "Falcons", ""))));

        Assert.Equal(new[] { "atlas" }, row.UnchangedProjects);
        Assert.Empty(row.RemovedProjects);
    }

    [Fact]
    public async Task Technology_stacks_are_diffed_the_same_way_as_projects()
    {
        await SeedCatalogueAsync();
        await SeedTeamAsync(20, "Falcons", stackIds: new[] { 60 });

        var row = Assert.Single(await _service.ParseFileAsync(Workbook(("20", "", "Falcons", "Java"))));

        Assert.Equal(new[] { "Java" }, row.NewTechStacks);
        Assert.Equal(new[] { ".NET" }, row.RemovedTechStacks);
    }

    [Fact]
    public async Task A_new_team_is_created_on_save()
    {
        await SeedCatalogueAsync();

        var rows = await _service.ParseFileAsync(Workbook(("", "Atlas", "Falcons", ".NET")));
        await _service.SaveAsync(rows);

        var team = await _db.ReadAsync(db => db.Teams
            .Include(t => t.CapitalProjectTeams)
            .Include(t => t.TeamTechnologyStacks)
            .SingleAsync());

        Assert.Equal("Falcons", team.Name);
        Assert.Equal(new[] { 50 }, team.CapitalProjectTeams.Select(c => c.CapitalProjectId));
        Assert.Equal(new[] { 60 }, team.TeamTechnologyStacks.Select(t => t.TechnologyStackId));
    }

    [Fact]
    public async Task A_project_that_does_not_exist_is_ignored_rather_than_created()
    {
        await SeedCatalogueAsync();

        var rows = await _service.ParseFileAsync(Workbook(("", "Atlas, Mystery", "Falcons", "")));
        await _service.SaveAsync(rows);

        var links = await _db.ReadAsync(db => db.CapitalProjectTeams.ToListAsync());

        Assert.Equal(new[] { 50 }, links.Select(l => l.CapitalProjectId));
        Assert.Equal(2, await _db.ReadAsync(db => db.CapitalProjects.CountAsync()));
    }

    [Fact]
    public async Task A_technology_stack_that_does_not_exist_is_ignored()
    {
        await SeedCatalogueAsync();

        var rows = await _service.ParseFileAsync(Workbook(("", "", "Falcons", "COBOL")));
        await _service.SaveAsync(rows);

        Assert.Empty(await _db.ReadAsync(db => db.TeamTechnologyStacks.ToListAsync()));
    }

    [Fact]
    public async Task An_added_project_is_linked_on_save()
    {
        await SeedCatalogueAsync();
        await SeedTeamAsync(20, "Falcons", projectIds: new[] { 50 }, isArtShared: true);

        var rows = await _service.ParseFileAsync(Workbook(("20", "Atlas, Borealis", "Falcons", "")));
        await _service.SaveAsync(rows);

        var links = await _db.ReadAsync(db => db.CapitalProjectTeams.Where(c => c.TeamId == 20).ToListAsync());

        Assert.Equal(new[] { 50, 51 }, links.Select(l => l.CapitalProjectId).OrderBy(i => i));
    }

    [Fact]
    public async Task A_second_art_for_a_non_shared_team_is_reported_as_blocked()
    {
        await SeedCatalogueAsync();
        await SeedTeamAsync(20, "Falcons", projectIds: new[] { 50 });

        var row = Assert.Single(await _service.ParseFileAsync(Workbook(("20", "Atlas, Borealis", "Falcons", ""))));

        Assert.Empty(row.NewProjects);
        var blocked = Assert.Single(row.BlockedProjects);
        Assert.Equal("Borealis", blocked.Name);
        Assert.Contains("already belongs to 'Atlas'", blocked.Reason);
    }

    [Fact]
    public async Task A_blocked_art_link_is_not_written_on_save()
    {
        await SeedCatalogueAsync();
        await SeedTeamAsync(20, "Falcons", projectIds: new[] { 50 });

        var rows = await _service.ParseFileAsync(Workbook(("20", "Atlas, Borealis", "Falcons", ".NET")));
        await _service.SaveAsync(rows);

        var links = await _db.ReadAsync(db => db.CapitalProjectTeams.Where(c => c.TeamId == 20).ToListAsync());
        var stacks = await _db.ReadAsync(db => db.TeamTechnologyStacks.Where(t => t.TeamId == 20).ToListAsync());

        Assert.Equal(new[] { 50 }, links.Select(l => l.CapitalProjectId));
        Assert.Equal(new[] { 60 }, stacks.Select(s => s.TechnologyStackId));
    }

    [Fact]
    public async Task A_new_non_shared_team_keeps_only_the_first_listed_art()
    {
        await SeedCatalogueAsync();

        var rows = await _service.ParseFileAsync(Workbook(("", "Atlas, Borealis", "Falcons", "")));
        await _service.SaveAsync(rows);

        var row = Assert.Single(rows);
        Assert.Equal(new[] { "Atlas" }, row.NewProjects);
        Assert.Equal("Borealis", Assert.Single(row.BlockedProjects).Name);

        var team = await _db.ReadAsync(db => db.Teams.FirstAsync(t => t.Name == "Falcons"));
        var links = await _db.ReadAsync(db => db.CapitalProjectTeams.Where(c => c.TeamId == team.Id).ToListAsync());
        Assert.Equal(new[] { 50 }, links.Select(l => l.CapitalProjectId));
    }

    [Fact]
    public async Task A_shared_team_reports_no_blocked_arts()
    {
        await SeedCatalogueAsync();
        await SeedTeamAsync(20, "Falcons", projectIds: new[] { 50 }, isArtShared: true);

        var row = Assert.Single(await _service.ParseFileAsync(Workbook(("20", "Atlas, Borealis", "Falcons", ""))));

        Assert.Empty(row.BlockedProjects);
        Assert.Equal(new[] { "Borealis" }, row.NewProjects);
    }

    [Fact]
    public async Task A_row_whose_only_change_is_blocked_counts_as_unchanged()
    {
        await SeedCatalogueAsync();
        await SeedTeamAsync(20, "Falcons", projectIds: new[] { 50 });

        var row = Assert.Single(await _service.ParseFileAsync(Workbook(("20", "Atlas, Borealis", "Falcons", ""))));

        Assert.False(row.HasChanges);
    }

    [Fact]
    public async Task A_removed_project_is_unlinked_on_save()
    {
        await SeedCatalogueAsync();
        await SeedTeamAsync(20, "Falcons", projectIds: new[] { 50, 51 });

        var rows = await _service.ParseFileAsync(Workbook(("20", "Atlas", "Falcons", "")));
        await _service.SaveAsync(rows);

        var links = await _db.ReadAsync(db => db.CapitalProjectTeams.Where(c => c.TeamId == 20).ToListAsync());

        Assert.Equal(new[] { 50 }, links.Select(l => l.CapitalProjectId));
    }

    [Fact]
    public async Task A_removed_technology_stack_is_unlinked_on_save()
    {
        await SeedCatalogueAsync();
        await SeedTeamAsync(20, "Falcons", stackIds: new[] { 60, 61 });

        var rows = await _service.ParseFileAsync(Workbook(("20", "", "Falcons", ".NET")));
        await _service.SaveAsync(rows);

        var links = await _db.ReadAsync(db => db.TeamTechnologyStacks.Where(t => t.TeamId == 20).ToListAsync());

        Assert.Equal(new[] { 60 }, links.Select(l => l.TechnologyStackId));
    }

    [Fact]
    public async Task A_row_with_nothing_to_change_leaves_the_links_alone()
    {
        await SeedCatalogueAsync();
        await SeedTeamAsync(20, "Falcons", projectIds: new[] { 50 }, stackIds: new[] { 60 });

        var rows = await _service.ParseFileAsync(Workbook(("20", "Atlas", "Falcons", ".NET")));
        await _service.SaveAsync(rows);

        Assert.Single(await _db.ReadAsync(db => db.CapitalProjectTeams.ToListAsync()));
        Assert.Single(await _db.ReadAsync(db => db.TeamTechnologyStacks.ToListAsync()));
    }

    [Fact]
    public async Task Several_teams_are_saved_in_one_pass()
    {
        await SeedCatalogueAsync();

        var rows = await _service.ParseFileAsync(Workbook(
            ("", "Atlas", "Falcons", ".NET"),
            ("", "Borealis", "Hawks", "Java")));
        await _service.SaveAsync(rows);

        var teams = await _db.ReadAsync(db => db.Teams.OrderBy(t => t.Name).ToListAsync());

        Assert.Equal(new[] { "Falcons", "Hawks" }, teams.Select(t => t.Name));
    }

    [Fact]
    public async Task An_export_lists_teams_alphabetically_with_their_assignments()
    {
        await SeedCatalogueAsync();
        await SeedTeamAsync(21, "Hawks", projectIds: new[] { 51 }, stackIds: new[] { 61 });
        await SeedTeamAsync(20, "Falcons", projectIds: new[] { 50 }, stackIds: new[] { 60, 61 });

        var (headers, rows) = ReadWorkbook(await _service.ExportAllTeamsAsync());

        Assert.Equal(new[] { "DB id", "Project", "Team Name", "Technology Stacks" }, headers);
        Assert.Equal(new[] { "Falcons", "Hawks" }, rows.Select(r => r[2]));
        Assert.Equal(".NET, Java", rows[0][3]);
        Assert.Equal("Atlas", rows[0][1]);
    }

    [Fact]
    public async Task An_export_round_trips_back_through_the_parser_without_reporting_changes()
    {
        await SeedCatalogueAsync();
        await SeedTeamAsync(20, "Falcons", projectIds: new[] { 50 }, stackIds: new[] { 60 });

        var rows = await _service.ParseFileAsync(new MemoryStream(await _service.ExportAllTeamsAsync()));

        var row = Assert.Single(rows);

        Assert.False(row.HasChanges);
        Assert.Equal(20, row.ExistingTeamId);
    }

    private static (List<string> Headers, List<List<string>> Rows) ReadWorkbook(byte[] workbook)
    {
        using var stream = new MemoryStream(workbook);
        return ExcelSheetReader.Read(stream, "Teams");
    }
}

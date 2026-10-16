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

    private static Stream StoryPointsWorkbook(params (string TeamName, string StoryPointsPerDay)[] rows)
    {
        var sheet = new ExcelWorkbookBuilder().AddSheet("Teams");
        sheet.WriteHeader("DB id", "Project", "Team Name", "Technology Stacks", "Story Points per Day");

        foreach (var (teamName, storyPointsPerDay) in rows)
        {
            sheet.AddRow().Text("").Text("").Text(teamName).Text("").Text(storyPointsPerDay);
        }

        return new MemoryStream(sheet.Workbook.ToArray());
    }

    private Task SeedCatalogueAsync() => _db.SeedAsync(db =>
    {
        db.CapitalProjects.Add(new Art { Id = 50, Name = "Atlas" });
        db.CapitalProjects.Add(new Art { Id = 51, Name = "Borealis" });
        db.TechnologyStacks.Add(new TechnologyStack { Id = 60, Name = ".NET" });
        db.TechnologyStacks.Add(new TechnologyStack { Id = 61, Name = "Java" });
    });

    private Task SeedTeamAsync(int id, string name, int[]? projectIds = null, int[]? stackIds = null, bool isArtShared = false,
        decimal storyPointsPerDay = 1m) =>
        _db.SeedAsync(db =>
        {
            db.Teams.Add(new Team { Id = id, Name = name, IsArtSharedTeam = isArtShared, StoryPointsPerDay = storyPointsPerDay });
            foreach (var projectId in projectIds ?? Array.Empty<int>())
            {
                db.CapitalProjectTeams.Add(new ArtTeam { CapitalProjectId = projectId, TeamId = id });
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

    private Task SeedArtStackAsync(int id, string name, int artId) =>
        _db.SeedAsync(db => db.TechnologyStacks.Add(new TechnologyStack { Id = id, Name = name, CapitalProjectId = artId }));

    [Fact]
    public async Task A_stack_of_another_art_is_reported_as_blocked()
    {
        await SeedCatalogueAsync();
        await SeedArtStackAsync(62, "SAP", 51);

        var row = Assert.Single(await _service.ParseFileAsync(Workbook(("", "Atlas", "Falcons", ".NET, SAP"))));

        Assert.Equal(new[] { ".NET" }, row.NewTechStacks);
        var blocked = Assert.Single(row.BlockedTechStacks);
        Assert.Equal("SAP", blocked.Name);
        Assert.Contains("'Borealis'", blocked.Reason);
    }

    [Fact]
    public async Task A_stack_of_an_art_listed_in_the_same_row_is_not_blocked()
    {
        await SeedCatalogueAsync();
        await SeedArtStackAsync(62, "SAP", 51);

        var row = Assert.Single(await _service.ParseFileAsync(Workbook(("", "Borealis", "Falcons", "SAP"))));

        Assert.Equal(new[] { "SAP" }, row.NewTechStacks);
        Assert.Empty(row.BlockedTechStacks);
    }

    [Fact]
    public async Task A_row_whose_only_change_is_a_blocked_stack_counts_as_unchanged()
    {
        await SeedCatalogueAsync();
        await SeedArtStackAsync(62, "SAP", 51);
        await SeedTeamAsync(20, "Falcons", projectIds: new[] { 50 });

        var row = Assert.Single(await _service.ParseFileAsync(Workbook(("20", "Atlas", "Falcons", "SAP"))));

        Assert.False(row.HasChanges);
    }

    [Fact]
    public async Task A_blocked_stack_is_not_linked_on_save()
    {
        await SeedCatalogueAsync();
        await SeedArtStackAsync(62, "SAP", 51);

        var rows = await _service.ParseFileAsync(Workbook(("", "Atlas", "Falcons", "SAP")));
        await _service.SaveAsync(rows);

        Assert.Empty(await _db.ReadAsync(db => db.TeamTechnologyStacks.ToListAsync()));
    }

    [Fact]
    public async Task The_stack_of_the_teams_art_is_linked_before_a_shared_one_with_the_same_name()
    {
        await SeedCatalogueAsync();
        await SeedArtStackAsync(62, "Java", 50);

        var rows = await _service.ParseFileAsync(Workbook(("", "Atlas", "Falcons", "Java")));
        await _service.SaveAsync(rows);

        var links = await _db.ReadAsync(db => db.TeamTechnologyStacks.ToListAsync());

        Assert.Equal(new[] { 62 }, links.Select(l => l.TechnologyStackId));
    }

    [Fact]
    public async Task A_shared_stack_is_linked_when_only_another_art_has_its_own_with_the_same_name()
    {
        await SeedCatalogueAsync();
        await SeedArtStackAsync(62, "Java", 51);

        var rows = await _service.ParseFileAsync(Workbook(("", "Atlas", "Falcons", "Java")));
        await _service.SaveAsync(rows);

        var links = await _db.ReadAsync(db => db.TeamTechnologyStacks.ToListAsync());

        Assert.Equal(new[] { 61 }, links.Select(l => l.TechnologyStackId));
    }

    [Fact]
    public async Task A_removed_stack_is_unlinked_when_another_stack_has_the_same_name()
    {
        await SeedCatalogueAsync();
        await SeedArtStackAsync(62, "Java", 50);
        await SeedTeamAsync(20, "Falcons", projectIds: new[] { 50 }, stackIds: new[] { 60, 62 });

        var rows = await _service.ParseFileAsync(Workbook(("20", "Atlas", "Falcons", ".NET")));
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
        await SeedTeamAsync(21, "Hawks", projectIds: new[] { 51 }, stackIds: new[] { 61 }, storyPointsPerDay: 2m);
        await SeedTeamAsync(20, "Falcons", projectIds: new[] { 50 }, stackIds: new[] { 60, 61 });

        var (headers, rows) = ReadWorkbook(await _service.ExportAllTeamsAsync());

        Assert.Equal(new[] { "DB id", "Project", "Team Name", "Technology Stacks", "Story Points per Day" }, headers);
        Assert.Equal(new[] { "Falcons", "Hawks" }, rows.Select(r => r[2]));
        Assert.Equal(".NET, Java", rows[0][3]);
        Assert.Equal("Atlas", rows[0][1]);
        Assert.Equal(new[] { "1", "2" }, rows.Select(r => r[4]));
    }

    [Fact]
    public async Task An_export_round_trips_back_through_the_parser_without_reporting_changes()
    {
        await SeedCatalogueAsync();
        await SeedTeamAsync(20, "Falcons", projectIds: new[] { 50 }, stackIds: new[] { 60 }, storyPointsPerDay: 1.5m);

        var rows = await _service.ParseFileAsync(new MemoryStream(await _service.ExportAllTeamsAsync()));

        var row = Assert.Single(rows);

        Assert.False(row.HasChanges);
        Assert.Equal(20, row.ExistingTeamId);
        Assert.Equal(1.5m, row.UploadedStoryPointsPerDay);
    }

    [Theory]
    [InlineData("2", "2")]
    [InlineData("1,5", "1.5")]
    [InlineData("0.1", "0.1")]
    [InlineData("10", "10")]
    [InlineData("1.239", "1.24")]
    public async Task An_existing_team_takes_the_uploaded_story_points_per_day_on_save(string cell, string expected)
    {
        await SeedTeamAsync(20, "Falcons");

        var rows = await _service.ParseFileAsync(StoryPointsWorkbook(("Falcons", cell)));
        var row = Assert.Single(rows);
        await _service.SaveAsync(rows);

        var storyPointsPerDay = decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(1m, row.CurrentStoryPointsPerDay);
        Assert.Equal(storyPointsPerDay, row.UploadedStoryPointsPerDay);
        Assert.True(row.HasChanges);
        Assert.Equal(storyPointsPerDay, await _db.ReadAsync(db => db.Teams.Select(t => t.StoryPointsPerDay).SingleAsync()));
    }

    [Fact]
    public async Task A_blank_story_points_per_day_cell_or_a_file_without_the_column_keeps_the_current_value()
    {
        await SeedTeamAsync(20, "Falcons", storyPointsPerDay: 2m);

        var blank = Assert.Single(await _service.ParseFileAsync(StoryPointsWorkbook(("Falcons", ""))));
        var oldFile = Assert.Single(await _service.ParseFileAsync(Workbook(("20", "", "Falcons", ""))));
        await _service.SaveAsync([blank, oldFile]);

        Assert.False(blank.HasChanges);
        Assert.False(oldFile.HasChanges);
        Assert.Null(blank.UploadedStoryPointsPerDay);
        Assert.Equal(2m, await _db.ReadAsync(db => db.Teams.Select(t => t.StoryPointsPerDay).SingleAsync()));
    }

    [Fact]
    public async Task A_new_team_takes_its_story_points_per_day_from_the_row_or_one()
    {
        var rows = await _service.ParseFileAsync(StoryPointsWorkbook(("Hawks", "2"), ("Owls", "")));
        await _service.SaveAsync(rows);

        var teams = await _db.ReadAsync(db => db.Teams.OrderBy(t => t.Name).ToListAsync());

        Assert.Equal(new[] { ("Hawks", 2m), ("Owls", 1m) }, teams.Select(t => (t.Name, t.StoryPointsPerDay)));
    }

    [Theory]
    [InlineData("two")]
    [InlineData("0")]
    [InlineData("0.04")]
    [InlineData("-1")]
    [InlineData("11")]
    public async Task A_story_points_per_day_that_is_not_a_number_from_0_1_to_10_is_skipped(string cell)
    {
        await SeedTeamAsync(20, "Falcons", storyPointsPerDay: 2m);

        var rows = await _service.ParseFileAsync(StoryPointsWorkbook(("Falcons", cell)));
        var row = Assert.Single(rows);
        await _service.SaveAsync(rows);

        Assert.Equal(cell, row.SkippedStoryPointsPerDay);
        Assert.Null(row.UploadedStoryPointsPerDay);
        Assert.False(row.HasChanges);
        Assert.Equal(2m, await _db.ReadAsync(db => db.Teams.Select(t => t.StoryPointsPerDay).SingleAsync()));
    }

    private static (List<string> Headers, List<List<string>> Rows) ReadWorkbook(byte[] workbook)
    {
        using var stream = new MemoryStream(workbook);
        return ExcelSheetReader.Read(stream, "Teams");
    }
}

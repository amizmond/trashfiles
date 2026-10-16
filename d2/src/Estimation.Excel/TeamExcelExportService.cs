namespace Estimation.Excel;

public class TeamExportRow
{
    public int Id { get; set; }
    public string Project { get; set; } = "";
    public string TeamName { get; set; } = null!;
    public string TechnologyStacks { get; set; } = "";
    public decimal StoryPointsPerDay { get; set; } = 1m;
}

public static class TeamExcelExportService
{
    public static byte[] GenerateTeamExport(List<TeamExportRow> teams, List<string> projectNames)
    {
        var workbook = new ExcelWorkbookBuilder();
        var sheet = workbook.AddSheet("Teams");

        sheet.SetColumnWidths(10, 40, 30, 40, 22);
        sheet.WriteHeader("DB id", "Project", "Team Name", "Technology Stacks", "Story Points per Day");

        foreach (var team in teams)
        {
            sheet.AddRow()
                .Number(team.Id)
                .Text(team.Project)
                .Text(team.TeamName)
                .Text(team.TechnologyStacks)
                .Number((double)team.StoryPointsPerDay);
        }

        if (projectNames.Count > 0)
        {
            var lastDataRow = Math.Max(teams.Count + 2, 1000);
            sheet.AddListValidation(
                columnIndex: 1,
                firstDataRow: 2,
                lastDataRow: lastDataRow,
                values: projectNames,
                errorTitle: "Invalid Project",
                error: "Please select an existing project.");
        }

        return workbook.ToArray();
    }
}

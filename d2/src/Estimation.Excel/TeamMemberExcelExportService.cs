namespace Estimation.Excel;

public class SkillColumnDefinition
{
    public string SkillName { get; set; } = null!;
    public List<string> LevelNames { get; set; } = new();
}

public class TeamMemberExportRow
{
    public string EmployeeName { get; set; } = null!;
    public string? EmployeeNumber { get; set; }
    public List<string?> SkillLevels { get; set; } = new();
}

public static class TeamMemberExcelExportService
{
    private const int FirstSkillColumnIndex = 2;

    public static byte[] GenerateTeamMemberTemplate(List<SkillColumnDefinition> skills)
    {
        return BuildWorkbook(skills, members: new List<TeamMemberExportRow>(), validationLastRow: 1000);
    }

    public static byte[] GenerateTeamMemberExport(List<SkillColumnDefinition> skills, List<TeamMemberExportRow> members)
    {
        var validationLastRow = Math.Max(members.Count + 2, 1000);
        return BuildWorkbook(skills, members, validationLastRow);
    }

    private static byte[] BuildWorkbook(
        List<SkillColumnDefinition> skills,
        List<TeamMemberExportRow> members,
        int validationLastRow)
    {
        var workbook = new ExcelWorkbookBuilder();
        var sheet = workbook.AddSheet("Team Members");

        sheet.AddColumnWidth(new ColumnWidth(1, 30));
        sheet.AddColumnWidth(new ColumnWidth(2, 20));
        if (skills.Count > 0)
        {
            sheet.AddColumnWidth(new ColumnWidth(3, (uint)(2 + skills.Count), 18));
        }

        sheet.WriteHeader(new[] { "Employee Name", "Employee Number" }.Concat(skills.Select(s => s.SkillName)));

        foreach (var member in members)
        {
            var row = sheet.AddRow()
                .Text(member.EmployeeName)
                .Text(member.EmployeeNumber);

            foreach (var level in member.SkillLevels)
            {
                row.Text(level, skipIfEmpty: true);
            }
        }

        for (var i = 0; i < skills.Count; i++)
        {
            var skill = skills[i];
            if (skill.LevelNames.Count == 0)
            {
                continue;
            }

            sheet.AddListValidation(
                columnIndex: FirstSkillColumnIndex + i,
                firstDataRow: 2,
                lastDataRow: validationLastRow,
                values: skill.LevelNames,
                errorTitle: "Invalid Level",
                error: $"Please select a valid skill level for {skill.SkillName}.");
        }

        return workbook.ToArray();
    }
}

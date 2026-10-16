namespace Estimation.Excel;

public class SkillExportRow
{
    public string SkillName { get; set; } = null!;
    public string LevelName { get; set; } = null!;
    public int? Value { get; set; }
    public string? LevelDescription { get; set; }
}

public static class SkillExcelExportService
{
    public static byte[] GenerateSkillExport(List<SkillExportRow> rows)
    {
        var workbook = new ExcelWorkbookBuilder();
        var sheet = workbook.AddSheet("Skills");

        sheet.SetColumnWidths(25, 20, 10, 35);
        sheet.WriteHeader("Skill", "Level", "Value", "Level Description");

        foreach (var row in rows)
        {
            sheet.AddRow()
                .Text(row.SkillName)
                .Text(row.LevelName)
                .Number(row.Value)
                .Text(row.LevelDescription);
        }

        return workbook.ToArray();
    }
}

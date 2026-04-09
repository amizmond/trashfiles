using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Estimation.Core.Models;
using Estimation.Excel;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.Services;

public interface ITeamMemberUploadService
{
    Task<byte[]> GenerateTemplateAsync();
    Task<List<TeamMemberUploadRow>> ParseFileAsync(Stream fileStream, int teamId);
    Task SaveAsync(List<TeamMemberUploadRow> rows, int teamId);
}

public class TeamMemberUploadService : ITeamMemberUploadService
{
    private readonly IDbContextFactory<EstimationDbContext> _contextFactory;

    public TeamMemberUploadService(IDbContextFactory<EstimationDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<byte[]> GenerateTemplateAsync()
    {
        await using var db = await _contextFactory.CreateDbContextAsync();

        var skills = await db.Skills
            .Include(s => s.Levels)
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .ToListAsync();

        var columns = skills.Select(s => new SkillColumnDefinition
        {
            SkillName = s.Name,
            LevelNames = s.Levels.OrderBy(l => l.Value).ThenBy(l => l.Name).Select(l => l.Name).ToList()
        }).ToList();

        return ExcelTemplateService.GenerateTeamMemberTemplate(columns);
    }

    public async Task<List<TeamMemberUploadRow>> ParseFileAsync(Stream fileStream, int teamId)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();

        var skills = await db.Skills
            .Include(s => s.Levels)
            .AsNoTracking()
            .ToListAsync();

        var existingHrs = await db.HumanResources
            .Include(h => h.HumanResourceSkills)
                .ThenInclude(hrs => hrs.SkillLevel)
            .AsNoTracking()
            .ToListAsync();

        // Read Excel
        var (headers, dataRows) = ReadExcel(fileStream);

        // Map header columns to skills (skip first two: Employee Name, Employee Number)
        var skillColumns = new List<(int ColumnIndex, Skill Skill)>();
        for (var i = 2; i < headers.Count; i++)
        {
            var headerName = headers[i];
            var skill = skills.FirstOrDefault(s =>
                s.Name.Equals(headerName, StringComparison.OrdinalIgnoreCase));
            if (skill != null)
            {
                skillColumns.Add((i, skill));
            }
        }

        var result = new List<TeamMemberUploadRow>();

        foreach (var row in dataRows)
        {
            var employeeName = row.ElementAtOrDefault(0)?.Trim();
            if (string.IsNullOrWhiteSpace(employeeName))
                continue;

            var employeeNumber = row.ElementAtOrDefault(1)?.Trim();

            // Find existing HR: first by EmployeeNumber, then by EmployeeName
            HumanResource? existingHr = null;
            if (!string.IsNullOrWhiteSpace(employeeNumber))
            {
                existingHr = existingHrs.FirstOrDefault(h =>
                    !string.IsNullOrWhiteSpace(h.EmployeeNumber) &&
                    h.EmployeeNumber.Equals(employeeNumber, StringComparison.OrdinalIgnoreCase));
            }
            existingHr ??= existingHrs.FirstOrDefault(h =>
                h.EmployeeName.Equals(employeeName, StringComparison.OrdinalIgnoreCase));

            var uploadRow = new TeamMemberUploadRow
            {
                EmployeeName = employeeName,
                EmployeeNumber = employeeNumber,
                IsNew = existingHr is null,
                ExistingHrId = existingHr?.Id
            };

            foreach (var (colIdx, skill) in skillColumns)
            {
                var cellValue = row.ElementAtOrDefault(colIdx)?.Trim();
                var isEmpty = string.IsNullOrWhiteSpace(cellValue);

                // Find existing skill assignment
                var existingAssignment = existingHr?.HumanResourceSkills
                    .FirstOrDefault(hrs => hrs.SkillId == skill.Id);

                var oldLevelName = existingAssignment?.SkillLevel?.Name;

                if (isEmpty)
                {
                    // Empty cell = remove skill if exists
                    if (existingAssignment != null)
                    {
                        uploadRow.Skills.Add(new SkillUploadItem
                        {
                            SkillId = skill.Id,
                            SkillName = skill.Name,
                            NewLevelName = null,
                            NewLevelId = null,
                            OldLevelName = oldLevelName,
                            IsChanged = true,
                            IsRemoved = true
                        });
                    }
                    // If no existing assignment and empty cell, skip
                }
                else
                {
                    // Find the matching skill level
                    var matchedLevel = skill.Levels.FirstOrDefault(l =>
                        l.Name.Equals(cellValue, StringComparison.OrdinalIgnoreCase));

                    var newLevelId = matchedLevel?.Id;
                    var newLevelName = matchedLevel?.Name ?? cellValue;

                    var isChanged = existingAssignment is null
                        || existingAssignment.SkillLevelId != newLevelId;

                    uploadRow.Skills.Add(new SkillUploadItem
                    {
                        SkillId = skill.Id,
                        SkillName = skill.Name,
                        NewLevelName = newLevelName,
                        NewLevelId = newLevelId,
                        OldLevelName = oldLevelName,
                        IsChanged = isChanged,
                        IsRemoved = false
                    });
                }
            }

            result.Add(uploadRow);
        }

        return result;
    }

    public async Task SaveAsync(List<TeamMemberUploadRow> rows, int teamId)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();

        var existingHrs = await db.HumanResources
            .Include(h => h.HumanResourceSkills)
            .ToListAsync();

        foreach (var row in rows)
        {
            HumanResource hr;

            if (row.IsNew)
            {
                hr = new HumanResource
                {
                    EmployeeName = row.EmployeeName,
                    FullName = row.EmployeeName,
                    EmployeeNumber = row.EmployeeNumber,
                    IsActive = true
                };
                db.HumanResources.Add(hr);
                await db.SaveChangesAsync(); // get ID

                // Add to team
                db.TeamMembers.Add(new TeamMember { TeamId = teamId, HumanResourceId = hr.Id });
            }
            else
            {
                hr = existingHrs.First(h => h.Id == row.ExistingHrId);

                // Ensure team membership
                var isMember = await db.TeamMembers.AnyAsync(
                    tm => tm.TeamId == teamId && tm.HumanResourceId == hr.Id);
                if (!isMember)
                {
                    db.TeamMembers.Add(new TeamMember { TeamId = teamId, HumanResourceId = hr.Id });
                }
            }

            foreach (var skillItem in row.Skills)
            {
                if (skillItem.IsRemoved)
                {
                    var existing = hr.HumanResourceSkills
                        .FirstOrDefault(hrs => hrs.SkillId == skillItem.SkillId);
                    if (existing != null)
                    {
                        db.HumanResourceSkills.Remove(existing);
                        hr.HumanResourceSkills.Remove(existing);
                    }
                }
                else
                {
                    var existing = hr.HumanResourceSkills
                        .FirstOrDefault(hrs => hrs.SkillId == skillItem.SkillId);
                    if (existing != null)
                    {
                        existing.SkillLevelId = skillItem.NewLevelId;
                    }
                    else
                    {
                        var newHrSkill = new HumanResourceSkill
                        {
                            HumanResourceId = hr.Id,
                            SkillId = skillItem.SkillId,
                            SkillLevelId = skillItem.NewLevelId
                        };
                        db.HumanResourceSkills.Add(newHrSkill);
                        hr.HumanResourceSkills.Add(newHrSkill);
                    }
                }
            }
        }

        await db.SaveChangesAsync();
    }

    private static (List<string> Headers, List<List<string>> Rows) ReadExcel(Stream fileStream)
    {
        using var document = SpreadsheetDocument.Open(fileStream, false);
        var workbookPart = document.WorkbookPart!;
        var sheet = workbookPart.Workbook.GetFirstChild<Sheets>()!.Elements<Sheet>().First();
        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!.Value!);

        var sharedStrings = new Dictionary<string, string>();
        var ssPart = workbookPart.SharedStringTablePart;
        if (ssPart != null)
        {
            var items = ssPart.SharedStringTable.Elements<SharedStringItem>().ToList();
            for (var i = 0; i < items.Count; i++)
            {
                sharedStrings[i.ToString()] = items[i].Text?.Text
                    ?? items[i].InnerText
                    ?? string.Empty;
            }
        }

        var rows = worksheetPart.Worksheet
            .GetFirstChild<SheetData>()!
            .Elements<Row>()
            .ToList();

        if (rows.Count == 0)
            return (new List<string>(), new List<List<string>>());

        var headers = ReadRowValues(rows[0], sharedStrings);
        var dataRows = rows.Skip(1)
            .Select(r => ReadRowValues(r, sharedStrings))
            .Where(r => r.Any(v => !string.IsNullOrWhiteSpace(v)))
            .ToList();

        return (headers, dataRows);
    }

    private static List<string> ReadRowValues(Row row, Dictionary<string, string> sharedStrings)
    {
        var values = new List<string>();
        foreach (var cell in row.Elements<Cell>())
        {
            var colIndex = GetColumnIndex(cell.CellReference!);

            // Fill gaps with empty strings
            while (values.Count < colIndex)
                values.Add(string.Empty);

            values.Add(GetCellValue(cell, sharedStrings));
        }
        return values;
    }

    private static string GetCellValue(Cell cell, Dictionary<string, string> sharedStrings)
    {
        if (cell.CellValue == null && cell.InlineString == null)
            return string.Empty;

        if (cell.DataType?.Value == CellValues.SharedString && cell.CellValue != null)
        {
            return sharedStrings.TryGetValue(cell.CellValue.Text, out var ss) ? ss : string.Empty;
        }

        if (cell.DataType?.Value == CellValues.InlineString && cell.InlineString != null)
        {
            return cell.InlineString.Text?.Text ?? cell.InlineString.InnerText ?? string.Empty;
        }

        return cell.CellValue?.Text?.Trim() ?? string.Empty;
    }

    private static int GetColumnIndex(string cellReference)
    {
        var col = 0;
        foreach (var ch in cellReference)
        {
            if (!char.IsLetter(ch)) break;
            col = col * 26 + (ch - 'A' + 1);
        }
        return col - 1;
    }
}

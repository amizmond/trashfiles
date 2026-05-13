using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

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

public class TeamExportRow
{
    public int Id { get; set; }
    public string Project { get; set; } = "";
    public string TeamName { get; set; } = null!;
    public string TechnologyStacks { get; set; } = "";
}

public class HumanResourceExportRow
{
    public string TeamName { get; set; } = "";
    public string FullName { get; set; } = null!;
    public string? EmployeeNumber { get; set; }
    public string? TeamRoleName { get; set; }
    public List<string?> SkillLevels { get; set; } = new();
}

public class SkillExportRow
{
    public string SkillName { get; set; } = null!;
    public string LevelName { get; set; } = null!;
    public int? Value { get; set; }
    public string? LevelDescription { get; set; }
}

public class FeatureExportRow
{
    public string? ProjectKey { get; set; }
    public string? JiraId { get; set; }
    public string? FeatureName { get; set; }
    public string? Summary { get; set; }
    public int? Ranking { get; set; }
    public string? Description { get; set; }
    public string? BusinessOutcome { get; set; }
    public string? Pi { get; set; }
    public string? Labels { get; set; }
    public string? Team { get; set; }
    public string? Comments { get; set; }
    public List<int?> TechStackEfforts { get; set; } = new();
}

public static class ExcelTemplateService
{
    public static string FormatBusinessOutcomeDisplay(string? jiraId, string? summary)
    {
        var name = summary?.Trim() ?? "";
        var key = jiraId?.Trim() ?? "";
        if (key.Length == 0)
        {
            return name;
        }
        return $"{key} - {name}";
    }

    public static byte[] GenerateTeamMemberTemplate(List<SkillColumnDefinition> skills)
    {
        using var memoryStream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(memoryStream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            worksheetPart.Worksheet = new Worksheet(sheetData);

            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "Team Members"
            });

            // Header row
            var headerRow = new Row { RowIndex = 1 };
            headerRow.Append(CreateTextCell("A1", "Employee Name"));
            headerRow.Append(CreateTextCell("B1", "Employee Number"));

            for (var i = 0; i < skills.Count; i++)
            {
                var colRef = GetColumnReference(i + 2); // start from column C (index 2)
                headerRow.Append(CreateTextCell($"{colRef}1", skills[i].SkillName));
            }
            sheetData.Append(headerRow);

            // Style header row bold
            var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
            stylesPart.Stylesheet = CreateStylesheet();
            stylesPart.Stylesheet.Save();

            // Apply bold style to header cells
            foreach (var cell in headerRow.Elements<Cell>())
            {
                cell.StyleIndex = 1;
            }

            // Data validations for skill columns (rows 2..1000)
            var dataValidations = new DataValidations();
            for (var i = 0; i < skills.Count; i++)
            {
                var skill = skills[i];
                if (skill.LevelNames.Count == 0)
                {
                    continue;
                }

                var colRef = GetColumnReference(i + 2);
                var formula = string.Join(",", skill.LevelNames.Select(EscapeFormulaValue));

                var validation = new DataValidation
                {
                    Type = DataValidationValues.List,
                    AllowBlank = true,
                    ShowDropDown = false, // false = show dropdown in Excel (counterintuitive OpenXml API)
                    ShowErrorMessage = true,
                    ErrorTitle = new StringValue("Invalid Level"),
                    Error = new StringValue($"Please select a valid skill level for {skill.SkillName}."),
                    SequenceOfReferences = new ListValue<StringValue>(
                        new[] { new StringValue($"{colRef}2:{colRef}1000") })
                };
                validation.Append(new Formula1($"\"{formula}\""));
                dataValidations.Append(validation);
            }

            if (dataValidations.HasChildren)
            {
                dataValidations.Count = (uint)dataValidations.ChildElements.Count;
                worksheetPart.Worksheet.Append(dataValidations);
            }

            // Set column widths
            var columns = new Columns();
            columns.Append(new Column { Min = 1, Max = 1, Width = 30, CustomWidth = true }); // Employee Name
            columns.Append(new Column { Min = 2, Max = 2, Width = 20, CustomWidth = true }); // Employee Number
            if (skills.Count > 0)
            {
                columns.Append(new Column
                {
                    Min = 3,
                    Max = (uint)(2 + skills.Count),
                    Width = 18,
                    CustomWidth = true
                });
            }
            worksheetPart.Worksheet.InsertBefore(columns, sheetData);

            workbookPart.Workbook.Save();
        }

        return memoryStream.ToArray();
    }

    public static byte[] GenerateTeamMemberExport(List<SkillColumnDefinition> skills, List<TeamMemberExportRow> members)
    {
        using var memoryStream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(memoryStream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            worksheetPart.Worksheet = new Worksheet(sheetData);

            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "Team Members"
            });

            // Header row
            var headerRow = new Row { RowIndex = 1 };
            headerRow.Append(CreateTextCell("A1", "Employee Name"));
            headerRow.Append(CreateTextCell("B1", "Employee Number"));

            for (var i = 0; i < skills.Count; i++)
            {
                var colRef = GetColumnReference(i + 2);
                headerRow.Append(CreateTextCell($"{colRef}1", skills[i].SkillName));
            }
            sheetData.Append(headerRow);

            // Data rows
            for (var rowIdx = 0; rowIdx < members.Count; rowIdx++)
            {
                var member = members[rowIdx];
                var rowNum = (uint)(rowIdx + 2);
                var dataRow = new Row { RowIndex = rowNum };

                dataRow.Append(CreateTextCell($"A{rowNum}", member.EmployeeName));
                dataRow.Append(CreateTextCell($"B{rowNum}", member.EmployeeNumber ?? ""));

                for (var i = 0; i < member.SkillLevels.Count; i++)
                {
                    var colRef = GetColumnReference(i + 2);
                    var level = member.SkillLevels[i];
                    if (!string.IsNullOrEmpty(level))
                    {
                        dataRow.Append(CreateTextCell($"{colRef}{rowNum}", level));
                    }
                }

                sheetData.Append(dataRow);
            }

            // Style header row bold
            var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
            stylesPart.Stylesheet = CreateStylesheet();
            stylesPart.Stylesheet.Save();

            foreach (var cell in headerRow.Elements<Cell>())
            {
                cell.StyleIndex = 1;
            }

            // Data validations for skill columns
            var validationStartRow = members.Count + 2;
            var validationEndRow = Math.Max(validationStartRow, 1000);
            var dataValidations = new DataValidations();
            for (var i = 0; i < skills.Count; i++)
            {
                var skill = skills[i];
                if (skill.LevelNames.Count == 0)
                {
                    continue;
                }

                var colRef = GetColumnReference(i + 2);
                var formula = string.Join(",", skill.LevelNames.Select(EscapeFormulaValue));

                var validation = new DataValidation
                {
                    Type = DataValidationValues.List,
                    AllowBlank = true,
                    ShowDropDown = false,
                    ShowErrorMessage = true,
                    ErrorTitle = new StringValue("Invalid Level"),
                    Error = new StringValue($"Please select a valid skill level for {skill.SkillName}."),
                    SequenceOfReferences = new ListValue<StringValue>(
                        new[] { new StringValue($"{colRef}2:{colRef}{validationEndRow}") })
                };
                validation.Append(new Formula1($"\"{formula}\""));
                dataValidations.Append(validation);
            }

            if (dataValidations.HasChildren)
            {
                dataValidations.Count = (uint)dataValidations.ChildElements.Count;
                worksheetPart.Worksheet.Append(dataValidations);
            }

            // Set column widths
            var columns = new Columns();
            columns.Append(new Column { Min = 1, Max = 1, Width = 30, CustomWidth = true });
            columns.Append(new Column { Min = 2, Max = 2, Width = 20, CustomWidth = true });
            if (skills.Count > 0)
            {
                columns.Append(new Column
                {
                    Min = 3,
                    Max = (uint)(2 + skills.Count),
                    Width = 18,
                    CustomWidth = true
                });
            }
            worksheetPart.Worksheet.InsertBefore(columns, sheetData);

            workbookPart.Workbook.Save();
        }

        return memoryStream.ToArray();
    }

    public static byte[] GenerateTeamExport(List<TeamExportRow> teams, List<string> projectNames)
    {
        using var memoryStream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(memoryStream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            worksheetPart.Worksheet = new Worksheet(sheetData);

            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "Teams"
            });

            // Header row
            var headerRow = new Row { RowIndex = 1 };
            headerRow.Append(CreateTextCell("A1", "DB id"));
            headerRow.Append(CreateTextCell("B1", "Project"));
            headerRow.Append(CreateTextCell("C1", "Team Name"));
            headerRow.Append(CreateTextCell("D1", "Technology Stacks"));
            sheetData.Append(headerRow);

            // Data rows
            for (var rowIdx = 0; rowIdx < teams.Count; rowIdx++)
            {
                var team = teams[rowIdx];
                var rowNum = (uint)(rowIdx + 2);
                var dataRow = new Row { RowIndex = rowNum };

                dataRow.Append(CreateNumberCell($"A{rowNum}", team.Id));
                dataRow.Append(CreateTextCell($"B{rowNum}", team.Project));
                dataRow.Append(CreateTextCell($"C{rowNum}", team.TeamName));
                dataRow.Append(CreateTextCell($"D{rowNum}", team.TechnologyStacks));

                sheetData.Append(dataRow);
            }

            // Style header row bold
            var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
            stylesPart.Stylesheet = CreateStylesheet();
            stylesPart.Stylesheet.Save();

            foreach (var cell in headerRow.Elements<Cell>())
            {
                cell.StyleIndex = 1;
            }

            // Data validation: Project dropdown (column B)
            if (projectNames.Count > 0)
            {
                var validationEndRow = Math.Max(teams.Count + 2, 1000);
                var formula = string.Join(",", projectNames.Select(EscapeFormulaValue));

                var dataValidations = new DataValidations();
                var validation = new DataValidation
                {
                    Type = DataValidationValues.List,
                    AllowBlank = true,
                    ShowDropDown = false,
                    ShowErrorMessage = true,
                    ErrorTitle = new StringValue("Invalid Project"),
                    Error = new StringValue("Please select an existing project."),
                    SequenceOfReferences = new ListValue<StringValue>(
                        new[] { new StringValue($"B2:B{validationEndRow}") })
                };
                validation.Append(new Formula1($"\"{formula}\""));
                dataValidations.Append(validation);
                dataValidations.Count = (uint)dataValidations.ChildElements.Count;
                worksheetPart.Worksheet.Append(dataValidations);
            }

            // Set column widths
            var columns = new Columns();
            columns.Append(new Column { Min = 1, Max = 1, Width = 10, CustomWidth = true });  // DB id
            columns.Append(new Column { Min = 2, Max = 2, Width = 40, CustomWidth = true });  // Project
            columns.Append(new Column { Min = 3, Max = 3, Width = 30, CustomWidth = true });  // Team Name
            columns.Append(new Column { Min = 4, Max = 4, Width = 40, CustomWidth = true });  // Technology Stacks
            worksheetPart.Worksheet.InsertBefore(columns, sheetData);

            workbookPart.Workbook.Save();
        }

        return memoryStream.ToArray();
    }

    public static byte[] GenerateHumanResourceExport(
        List<HumanResourceExportRow> rows,
        List<string> teamNames,
        List<string> teamRoleNames,
        List<SkillColumnDefinition> skills)
    {
        using var memoryStream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(memoryStream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            worksheetPart.Worksheet = new Worksheet(sheetData);

            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "Teams"
            });

            // Header row: Team, Full Name, EmployeeNumber, Team Role, [Skills...]
            var headerRow = new Row { RowIndex = 1 };
            headerRow.Append(CreateTextCell("A1", "Team"));
            headerRow.Append(CreateTextCell("B1", "Full Name"));
            headerRow.Append(CreateTextCell("C1", "EmployeeNumber"));
            headerRow.Append(CreateTextCell("D1", "Team Role"));

            for (var i = 0; i < skills.Count; i++)
            {
                var colRef = GetColumnReference(i + 4); // start from column E (index 4)
                headerRow.Append(CreateTextCell($"{colRef}1", skills[i].SkillName));
            }
            sheetData.Append(headerRow);

            // Data rows
            for (var rowIdx = 0; rowIdx < rows.Count; rowIdx++)
            {
                var row = rows[rowIdx];
                var rowNum = (uint)(rowIdx + 2);
                var dataRow = new Row { RowIndex = rowNum };

                dataRow.Append(CreateTextCell($"A{rowNum}", row.TeamName));
                dataRow.Append(CreateTextCell($"B{rowNum}", row.FullName));
                dataRow.Append(CreateTextCell($"C{rowNum}", row.EmployeeNumber ?? ""));
                dataRow.Append(CreateTextCell($"D{rowNum}", row.TeamRoleName ?? ""));

                for (var i = 0; i < row.SkillLevels.Count; i++)
                {
                    var colRef = GetColumnReference(i + 4);
                    var level = row.SkillLevels[i];
                    if (!string.IsNullOrEmpty(level))
                    {
                        dataRow.Append(CreateTextCell($"{colRef}{rowNum}", level));
                    }
                }

                sheetData.Append(dataRow);
            }

            // Style header row bold
            var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
            stylesPart.Stylesheet = CreateStylesheet();
            stylesPart.Stylesheet.Save();

            foreach (var cell in headerRow.Elements<Cell>())
            {
                cell.StyleIndex = 1;
            }

            // Data validations
            var validationEndRow = Math.Max(rows.Count + 2, 1000);
            var dataValidations = new DataValidations();

            // Team dropdown (column A)
            if (teamNames.Count > 0)
            {
                var formula = string.Join(",", teamNames.Select(EscapeFormulaValue));
                var validation = new DataValidation
                {
                    Type = DataValidationValues.List,
                    AllowBlank = true,
                    ShowDropDown = false,
                    ShowErrorMessage = true,
                    ErrorTitle = new StringValue("Invalid Team"),
                    Error = new StringValue("Please select an existing team."),
                    SequenceOfReferences = new ListValue<StringValue>(
                        new[] { new StringValue($"A2:A{validationEndRow}") })
                };
                validation.Append(new Formula1($"\"{formula}\""));
                dataValidations.Append(validation);
            }

            // Team Role dropdown (column D)
            if (teamRoleNames.Count > 0)
            {
                var formula = string.Join(",", teamRoleNames.Select(EscapeFormulaValue));
                var validation = new DataValidation
                {
                    Type = DataValidationValues.List,
                    AllowBlank = true,
                    ShowDropDown = false,
                    ShowErrorMessage = true,
                    ErrorTitle = new StringValue("Invalid Team Role"),
                    Error = new StringValue("Please select a valid team role."),
                    SequenceOfReferences = new ListValue<StringValue>(
                        new[] { new StringValue($"D2:D{validationEndRow}") })
                };
                validation.Append(new Formula1($"\"{formula}\""));
                dataValidations.Append(validation);
            }

            // Skill dropdowns (columns E+)
            for (var i = 0; i < skills.Count; i++)
            {
                var skill = skills[i];
                if (skill.LevelNames.Count == 0)
                {
                    continue;
                }

                var colRef = GetColumnReference(i + 4);
                var formula = string.Join(",", skill.LevelNames.Select(EscapeFormulaValue));

                var validation = new DataValidation
                {
                    Type = DataValidationValues.List,
                    AllowBlank = true,
                    ShowDropDown = false,
                    ShowErrorMessage = true,
                    ErrorTitle = new StringValue("Invalid Level"),
                    Error = new StringValue($"Please select a valid skill level for {skill.SkillName}."),
                    SequenceOfReferences = new ListValue<StringValue>(
                        new[] { new StringValue($"{colRef}2:{colRef}{validationEndRow}") })
                };
                validation.Append(new Formula1($"\"{formula}\""));
                dataValidations.Append(validation);
            }

            if (dataValidations.HasChildren)
            {
                dataValidations.Count = (uint)dataValidations.ChildElements.Count;
                worksheetPart.Worksheet.Append(dataValidations);
            }

            // Set column widths
            var columns = new Columns();
            columns.Append(new Column { Min = 1, Max = 1, Width = 25, CustomWidth = true });  // Team
            columns.Append(new Column { Min = 2, Max = 2, Width = 30, CustomWidth = true });  // Full Name
            columns.Append(new Column { Min = 3, Max = 3, Width = 20, CustomWidth = true });  // EmployeeNumber
            columns.Append(new Column { Min = 4, Max = 4, Width = 20, CustomWidth = true });  // Team Role
            if (skills.Count > 0)
            {
                columns.Append(new Column
                {
                    Min = 5,
                    Max = (uint)(4 + skills.Count),
                    Width = 18,
                    CustomWidth = true
                });
            }
            worksheetPart.Worksheet.InsertBefore(columns, sheetData);

            workbookPart.Workbook.Save();
        }

        return memoryStream.ToArray();
    }

    public static byte[] GenerateSkillExport(List<SkillExportRow> rows)
    {
        using var memoryStream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(memoryStream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            worksheetPart.Worksheet = new Worksheet(sheetData);

            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "Skills"
            });

            // Header row
            var headerRow = new Row { RowIndex = 1 };
            headerRow.Append(CreateTextCell("A1", "Skill"));
            headerRow.Append(CreateTextCell("B1", "Level"));
            headerRow.Append(CreateTextCell("C1", "Value"));
            headerRow.Append(CreateTextCell("D1", "Level Description"));
            sheetData.Append(headerRow);

            // Data rows
            for (var rowIdx = 0; rowIdx < rows.Count; rowIdx++)
            {
                var row = rows[rowIdx];
                var rowNum = (uint)(rowIdx + 2);
                var dataRow = new Row { RowIndex = rowNum };

                dataRow.Append(CreateTextCell($"A{rowNum}", row.SkillName));
                dataRow.Append(CreateTextCell($"B{rowNum}", row.LevelName));
                if (row.Value.HasValue)
                {
                    dataRow.Append(CreateNumberCell($"C{rowNum}", row.Value.Value));
                }
                dataRow.Append(CreateTextCell($"D{rowNum}", row.LevelDescription ?? ""));

                sheetData.Append(dataRow);
            }

            // Style header row bold
            var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
            stylesPart.Stylesheet = CreateStylesheet();
            stylesPart.Stylesheet.Save();

            foreach (var cell in headerRow.Elements<Cell>())
            {
                cell.StyleIndex = 1;
            }

            // Set column widths
            var columns = new Columns();
            columns.Append(new Column { Min = 1, Max = 1, Width = 25, CustomWidth = true });  // Skill
            columns.Append(new Column { Min = 2, Max = 2, Width = 20, CustomWidth = true });  // Level
            columns.Append(new Column { Min = 3, Max = 3, Width = 10, CustomWidth = true });  // Value
            columns.Append(new Column { Min = 4, Max = 4, Width = 35, CustomWidth = true });  // Level Description
            worksheetPart.Worksheet.InsertBefore(columns, sheetData);

            workbookPart.Workbook.Save();
        }

        return memoryStream.ToArray();
    }

    public static byte[] GenerateFeatureExport(
        List<FeatureExportRow> rows,
        List<string> projectKeys,
        List<string> piNames,
        List<string> teamNames,
        List<string> techStackNames,
        List<string> businessOutcomeOptions)
    {
        using var memoryStream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(memoryStream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            worksheetPart.Worksheet = new Worksheet(sheetData);

            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "Features"
            });

            // "Static Data" sheet holds lookup values (Business Outcome) that drive dropdowns
            // on the Features sheet. Using a sheet + defined name avoids Excel's 255-char inline
            // list limit and tolerates commas in BO names.
            const string BoDefinedName = "BusinessOutcomesList";
            const string StaticDataSheetName = "Static Data";
            if (businessOutcomeOptions.Count > 0)
            {
                var staticSheetPart = workbookPart.AddNewPart<WorksheetPart>();
                var staticSheetData = new SheetData();
                staticSheetPart.Worksheet = new Worksheet(staticSheetData);

                var staticHeader = new Row { RowIndex = 1 };
                var staticHeaderCell = CreateTextCell("A1", "Business Outcome");
                staticHeaderCell.StyleIndex = 1;
                staticHeader.Append(staticHeaderCell);
                staticSheetData.Append(staticHeader);

                for (var i = 0; i < businessOutcomeOptions.Count; i++)
                {
                    var rowNum = (uint)(i + 2);
                    var r = new Row { RowIndex = rowNum };
                    r.Append(CreateTextCell($"A{rowNum}", businessOutcomeOptions[i]));
                    staticSheetData.Append(r);
                }

                var staticColumns = new Columns();
                staticColumns.Append(new Column { Min = 1, Max = 1, Width = 50, CustomWidth = true });
                staticSheetPart.Worksheet.InsertBefore(staticColumns, staticSheetData);

                sheets.Append(new Sheet
                {
                    Id = workbookPart.GetIdOfPart(staticSheetPart),
                    SheetId = 2,
                    Name = StaticDataSheetName
                });

                var boLastRow = businessOutcomeOptions.Count + 1;
                var definedNames = new DefinedNames();
                definedNames.Append(new DefinedName(
                    $"'{StaticDataSheetName}'!$A$2:$A${boLastRow}")
                {
                    Name = BoDefinedName
                });
                workbookPart.Workbook.Append(definedNames);
            }

            // Fixed columns: A=ProjectKey, B=JiraId, C=FeatureName, D=Summary, E=Ranking,
            // F=Description, G=Business Outcome, H=PI, I=Labels, J=Team, K=Comments
            const int fixedColumnCount = 11;

            // Header row
            var headerRow = new Row { RowIndex = 1 };
            headerRow.Append(CreateTextCell("A1", "Project Key"));
            headerRow.Append(CreateTextCell("B1", "Feature Jira ID"));
            headerRow.Append(CreateTextCell("C1", "Feature Name"));
            headerRow.Append(CreateTextCell("D1", "Feature Summary"));
            headerRow.Append(CreateTextCell("E1", "Ranking"));
            headerRow.Append(CreateTextCell("F1", "Feature Description"));
            headerRow.Append(CreateTextCell("G1", "Business Outcome"));
            headerRow.Append(CreateTextCell("H1", "PI"));
            headerRow.Append(CreateTextCell("I1", "Labels"));
            headerRow.Append(CreateTextCell("J1", "Team"));
            headerRow.Append(CreateTextCell("K1", "Comments"));

            for (var i = 0; i < techStackNames.Count; i++)
            {
                var colRef = GetColumnReference(fixedColumnCount + i);
                headerRow.Append(CreateTextCell($"{colRef}1", techStackNames[i]));
            }
            sheetData.Append(headerRow);

            // Data rows
            for (var rowIdx = 0; rowIdx < rows.Count; rowIdx++)
            {
                var row = rows[rowIdx];
                var rowNum = (uint)(rowIdx + 2);
                var dataRow = new Row { RowIndex = rowNum };

                dataRow.Append(CreateTextCell($"A{rowNum}", row.ProjectKey ?? ""));
                dataRow.Append(CreateTextCell($"B{rowNum}", row.JiraId ?? ""));
                dataRow.Append(CreateTextCell($"C{rowNum}", row.FeatureName ?? ""));
                dataRow.Append(CreateTextCell($"D{rowNum}", row.Summary ?? ""));
                if (row.Ranking.HasValue)
                {
                    dataRow.Append(CreateNumberCell($"E{rowNum}", row.Ranking.Value));
                }
                dataRow.Append(CreateTextCell($"F{rowNum}", row.Description ?? ""));
                dataRow.Append(CreateTextCell($"G{rowNum}", row.BusinessOutcome ?? ""));
                dataRow.Append(CreateTextCell($"H{rowNum}", row.Pi ?? ""));
                dataRow.Append(CreateTextCell($"I{rowNum}", row.Labels ?? ""));
                dataRow.Append(CreateTextCell($"J{rowNum}", row.Team ?? ""));
                dataRow.Append(CreateTextCell($"K{rowNum}", row.Comments ?? ""));

                for (var i = 0; i < row.TechStackEfforts.Count; i++)
                {
                    if (row.TechStackEfforts[i].HasValue)
                    {
                        var colRef = GetColumnReference(fixedColumnCount + i);
                        dataRow.Append(CreateNumberCell($"{colRef}{rowNum}", row.TechStackEfforts[i]!.Value));
                    }
                }

                sheetData.Append(dataRow);
            }

            // Style header row bold
            var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
            stylesPart.Stylesheet = CreateStylesheet();
            stylesPart.Stylesheet.Save();

            foreach (var cell in headerRow.Elements<Cell>())
            {
                cell.StyleIndex = 1;
            }

            // Data validations
            var validationEndRow = Math.Max(rows.Count + 2, 1000);
            var dataValidations = new DataValidations();

            // ProjectKey dropdown (column A)
            if (projectKeys.Count > 0)
            {
                var formula = string.Join(",", projectKeys.Select(EscapeFormulaValue));
                var validation = new DataValidation
                {
                    Type = DataValidationValues.List,
                    AllowBlank = true,
                    ShowDropDown = false,
                    ShowErrorMessage = true,
                    ErrorTitle = new StringValue("Invalid Project Key"),
                    Error = new StringValue("Please select a valid project key."),
                    SequenceOfReferences = new ListValue<StringValue>(
                        new[] { new StringValue($"A2:A{validationEndRow}") })
                };
                validation.Append(new Formula1($"\"{formula}\""));
                dataValidations.Append(validation);
            }

            // Business Outcome dropdown (column G) — sourced from the hidden _Lookups sheet
            // via the BusinessOutcomesList defined name, so the list can exceed 255 chars.
            if (businessOutcomeOptions.Count > 0)
            {
                var validation = new DataValidation
                {
                    Type = DataValidationValues.List,
                    AllowBlank = true,
                    ShowDropDown = false,
                    ShowErrorMessage = true,
                    ErrorTitle = new StringValue("Invalid Business Outcome"),
                    Error = new StringValue("Please select a valid Business Outcome."),
                    SequenceOfReferences = new ListValue<StringValue>(
                        new[] { new StringValue($"G2:G{validationEndRow}") })
                };
                validation.Append(new Formula1(BoDefinedName));
                dataValidations.Append(validation);
            }

            // PI dropdown (column H)
            if (piNames.Count > 0)
            {
                var formula = string.Join(",", piNames.Select(EscapeFormulaValue));
                var validation = new DataValidation
                {
                    Type = DataValidationValues.List,
                    AllowBlank = true,
                    ShowDropDown = false,
                    ShowErrorMessage = true,
                    ErrorTitle = new StringValue("Invalid PI"),
                    Error = new StringValue("Please select a valid PI."),
                    SequenceOfReferences = new ListValue<StringValue>(
                        new[] { new StringValue($"H2:H{validationEndRow}") })
                };
                validation.Append(new Formula1($"\"{formula}\""));
                dataValidations.Append(validation);
            }

            // Team dropdown (column J)
            if (teamNames.Count > 0)
            {
                var formula = string.Join(",", teamNames.Select(EscapeFormulaValue));
                var validation = new DataValidation
                {
                    Type = DataValidationValues.List,
                    AllowBlank = true,
                    ShowDropDown = false,
                    ShowErrorMessage = true,
                    ErrorTitle = new StringValue("Invalid Team"),
                    Error = new StringValue("Please select a valid team."),
                    SequenceOfReferences = new ListValue<StringValue>(
                        new[] { new StringValue($"J2:J{validationEndRow}") })
                };
                validation.Append(new Formula1($"\"{formula}\""));
                dataValidations.Append(validation);
            }

            // Tech stack columns: whole number validation
            for (var i = 0; i < techStackNames.Count; i++)
            {
                var colRef = GetColumnReference(fixedColumnCount + i);
                var validation = new DataValidation
                {
                    Type = DataValidationValues.Whole,
                    Operator = DataValidationOperatorValues.GreaterThanOrEqual,
                    AllowBlank = true,
                    ShowErrorMessage = true,
                    ErrorTitle = new StringValue("Invalid Effort"),
                    Error = new StringValue($"Estimated Effort for {techStackNames[i]} must be a whole number >= 0."),
                    SequenceOfReferences = new ListValue<StringValue>(
                        new[] { new StringValue($"{colRef}2:{colRef}{validationEndRow}") })
                };
                validation.Append(new Formula1("0"));
                dataValidations.Append(validation);
            }

            if (dataValidations.HasChildren)
            {
                dataValidations.Count = (uint)dataValidations.ChildElements.Count;
                worksheetPart.Worksheet.Append(dataValidations);
            }

            // Set column widths
            var columns = new Columns();
            columns.Append(new Column { Min = 1, Max = 1, Width = 15, CustomWidth = true });   // Project Key
            columns.Append(new Column { Min = 2, Max = 2, Width = 18, CustomWidth = true });   // Jira ID
            columns.Append(new Column { Min = 3, Max = 3, Width = 40, CustomWidth = true });   // Feature Name
            columns.Append(new Column { Min = 4, Max = 4, Width = 40, CustomWidth = true });   // Summary
            columns.Append(new Column { Min = 5, Max = 5, Width = 10, CustomWidth = true });   // Ranking
            columns.Append(new Column { Min = 6, Max = 6, Width = 40, CustomWidth = true });   // Description
            columns.Append(new Column { Min = 7, Max = 7, Width = 35, CustomWidth = true });   // Business Outcome
            columns.Append(new Column { Min = 8, Max = 8, Width = 15, CustomWidth = true });   // PI
            columns.Append(new Column { Min = 9, Max = 9, Width = 30, CustomWidth = true });   // Labels
            columns.Append(new Column { Min = 10, Max = 10, Width = 20, CustomWidth = true }); // Team
            columns.Append(new Column { Min = 11, Max = 11, Width = 25, CustomWidth = true }); // Comments
            if (techStackNames.Count > 0)
            {
                columns.Append(new Column
                {
                    Min = (uint)(fixedColumnCount + 1),
                    Max = (uint)(fixedColumnCount + techStackNames.Count),
                    Width = 18,
                    CustomWidth = true
                });
            }
            worksheetPart.Worksheet.InsertBefore(columns, sheetData);

            workbookPart.Workbook.Save();
        }

        return memoryStream.ToArray();
    }

    private static Cell CreateNumberCell(string cellReference, int value)
    {
        return new Cell
        {
            CellReference = cellReference,
            DataType = CellValues.Number,
            CellValue = new CellValue(value)
        };
    }

    private static string EscapeFormulaValue(string value)
    {
        // Double up any quotes inside the value
        return value.Replace("\"", "\"\"");
    }

    private static Stylesheet CreateStylesheet()
    {
        return new Stylesheet(
            new Fonts(
                new Font(), // 0 - default
                new Font(new Bold()) // 1 - bold
            ),
            new Fills(
                new Fill(new PatternFill { PatternType = PatternValues.None }),
                new Fill(new PatternFill { PatternType = PatternValues.Gray125 })
            ),
            new Borders(
                new Border()
            ),
            new CellFormats(
                new CellFormat(), // 0 - default
                new CellFormat { FontId = 1, ApplyFont = true } // 1 - bold
            )
        );
    }

    private static Cell CreateTextCell(string cellReference, string text)
    {
        return new Cell
        {
            CellReference = cellReference,
            DataType = CellValues.String,
            CellValue = new CellValue(text)
        };
    }

    private static string GetColumnReference(int zeroBasedIndex)
    {
        var result = "";
        var index = zeroBasedIndex;
        while (index >= 0)
        {
            result = (char)('A' + index % 26) + result;
            index = index / 26 - 1;
        }
        return result;
    }
}

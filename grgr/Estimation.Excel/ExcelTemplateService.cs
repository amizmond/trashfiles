using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Estimation.Excel;

public class SkillColumnDefinition
{
    public string SkillName { get; set; } = null!;
    public List<string> LevelNames { get; set; } = new();
}

public static class ExcelTemplateService
{
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
                if (skill.LevelNames.Count == 0) continue;

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

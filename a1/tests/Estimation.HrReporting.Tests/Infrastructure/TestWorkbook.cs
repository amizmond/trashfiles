using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Estimation.HrReporting.Models;

namespace Estimation.HrReporting.Tests.Infrastructure;

internal sealed record InlineText(string Value);

internal sealed record ErrorCell(string Value);

internal sealed record RichText(params string[] Runs);

internal sealed class TestWorkbook
{
    private readonly List<List<object?>> _rows = [];
    private readonly List<string> _headers;

    private TestWorkbook(List<string> headers)
    {
        _headers = headers;
    }

    public string SheetName { get; set; } = "Headcount";

    public List<List<object?>> TitleRows { get; } = [];

    public static TestWorkbook WithAllColumns() => new(HeadcountColumns.All.Select(c => c.Header).ToList());

    public static TestWorkbook WithHeaders(params string[] headers) => new(headers.ToList());

    public IReadOnlyList<string> Headers => _headers;

    public TestWorkbook AddPerson(string brid, Action<Dictionary<string, object?>>? configure = null)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["BRID"] = brid,
            ["Employee Name"] = $"Person {brid}",
            ["HC"] = 1,
            ["FTE"] = 1,
            ["Team"] = "Argon",
            ["Train"] = "Core Payments",
            ["Country"] = "United Kingdom",
            ["City"] = "London"
        };
        configure?.Invoke(values);
        _rows.Add(_headers.Select(h => values.GetValueOrDefault(h)).ToList());
        return this;
    }

    public TestWorkbook AddRawRow(params object?[] cells)
    {
        _rows.Add(cells.ToList());
        return this;
    }

    public MemoryStream ToStream()
    {
        var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var sharedStringPart = workbookPart.AddNewPart<SharedStringTablePart>();
            var sharedStrings = new List<OpenXmlElement>();
            var sharedIndex = new Dictionary<string, int>();

            var otherSheet = workbookPart.AddNewPart<WorksheetPart>();
            otherSheet.Worksheet = new Worksheet(new SheetData());

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            uint rowIndex = 1;

            foreach (var cells in TitleRows.Append(_headers.Cast<object?>().ToList()).Concat(_rows))
            {
                var row = new Row { RowIndex = rowIndex };
                for (var i = 0; i < cells.Count; i++)
                {
                    var cell = CreateCell(cells[i], $"{Letters(i)}{rowIndex}", sharedStrings, sharedIndex);
                    if (cell is not null)
                    {
                        row.Append(cell);
                    }
                }

                sheetData.Append(row);
                rowIndex++;
            }

            worksheetPart.Worksheet = new Worksheet(sheetData);
            sharedStringPart.SharedStringTable = new SharedStringTable(sharedStrings);

            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(otherSheet), SheetId = 1, Name = "Notes" });
            sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = 2, Name = SheetName });
            workbookPart.Workbook.Save();
        }

        stream.Position = 0;
        return stream;
    }

    private static Cell? CreateCell(
        object? value, string reference, List<OpenXmlElement> sharedStrings, Dictionary<string, int> sharedIndex)
    {
        switch (value)
        {
            case null:
                return null;
            case string text:
                if (!sharedIndex.TryGetValue(text, out var index))
                {
                    index = sharedStrings.Count;
                    sharedIndex[text] = index;
                    sharedStrings.Add(new SharedStringItem(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
                }

                return new Cell { CellReference = reference, DataType = CellValues.SharedString, CellValue = new CellValue(index) };
            case RichText rich:
                var item = new SharedStringItem(rich.Runs.Select(r => new Run(new Text(r) { Space = SpaceProcessingModeValues.Preserve })));
                sharedStrings.Add(item);
                return new Cell { CellReference = reference, DataType = CellValues.SharedString, CellValue = new CellValue(sharedStrings.Count - 1) };
            case InlineText inline:
                return new Cell
                {
                    CellReference = reference,
                    DataType = CellValues.InlineString,
                    InlineString = new InlineString(new Text(inline.Value))
                };
            case ErrorCell error:
                return new Cell { CellReference = reference, DataType = CellValues.Error, CellValue = new CellValue(error.Value) };
            case DateTime date:
                return new Cell { CellReference = reference, StyleIndex = 0, CellValue = new CellValue(date.ToOADate()) };
            case int number:
                return new Cell { CellReference = reference, CellValue = new CellValue(number) };
            case double number:
                return new Cell { CellReference = reference, CellValue = new CellValue(number) };
            case decimal number:
                return new Cell { CellReference = reference, CellValue = new CellValue(number) };
            default:
                throw new ArgumentException($"Unsupported test cell value {value.GetType()}");
        }
    }

    private static string Letters(int index)
    {
        var letters = string.Empty;
        var value = index + 1;
        while (value > 0)
        {
            var remainder = (value - 1) % 26;
            letters = (char)('A' + remainder) + letters;
            value = (value - 1) / 26;
        }

        return letters;
    }
}

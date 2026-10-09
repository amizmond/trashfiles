namespace Estimation.Excel;

/// <summary>
/// Reads a single worksheet into a header row plus data rows, resolving shared strings and
/// gap-filling sparse cells. Shared by the upload services, which previously each carried their
/// own copy of this logic.
/// </summary>
public static class ExcelSheetReader
{
    /// <summary>
    /// Opens <paramref name="fileStream"/> and reads the first sheet whose name matches one of
    /// <paramref name="preferredSheetNames"/> (case-insensitive, in priority order), falling back
    /// to the first sheet. Returns the trimmed header row and the non-empty data rows; each row is
    /// a list of cell values indexed by column (gaps filled with empty strings).
    /// </summary>
    public static (List<string> Headers, List<List<string>> Rows) Read(
        Stream fileStream,
        params string[] preferredSheetNames)
    {
        if (fileStream.CanSeek)
        {
            fileStream.Position = 0;
        }

        using var document = SpreadsheetDocument.Open(fileStream, false);
        var workbookPart = document.WorkbookPart!;

        var sheets = workbookPart.Workbook!.GetFirstChild<Sheets>()!.Elements<Sheet>().ToList();
        return ReadSheet(workbookPart, SelectSheet(sheets, preferredSheetNames));
    }

    public static (List<string> Headers, List<List<string>> Rows)? ReadSheet(Stream fileStream, string sheetName)
    {
        if (fileStream.CanSeek)
        {
            fileStream.Position = 0;
        }

        using var document = SpreadsheetDocument.Open(fileStream, false);
        var workbookPart = document.WorkbookPart!;

        var sheet = workbookPart.Workbook!.GetFirstChild<Sheets>()!.Elements<Sheet>()
            .FirstOrDefault(s => sheetName.Equals(s.Name?.Value, StringComparison.OrdinalIgnoreCase));

        return sheet is null ? null : ReadSheet(workbookPart, sheet);
    }

    private static (List<string> Headers, List<List<string>> Rows) ReadSheet(WorkbookPart workbookPart, Sheet targetSheet)
    {
        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(targetSheet.Id!.Value!);

        var sharedStrings = LoadSharedStrings(workbookPart);

        var rows = worksheetPart.Worksheet!
            .GetFirstChild<SheetData>()!
            .Elements<Row>()
            .ToList();

        if (rows.Count == 0)
        {
            return (new List<string>(), new List<List<string>>());
        }

        var headers = ReadRowValues(rows[0], sharedStrings);
        var dataRows = rows.Skip(1)
            .Select(r => ReadRowValues(r, sharedStrings))
            .Where(r => r.Any(v => !string.IsNullOrWhiteSpace(v)))
            .ToList();

        return (headers, dataRows);
    }

    /// <summary>Builds a case-insensitive header-name to column-index lookup from a header row.</summary>
    public static Dictionary<string, int> BuildColumnMap(IReadOnlyList<string> headers)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Count; i++)
        {
            map[headers[i].Trim()] = i;
        }

        return map;
    }

    /// <summary>Returns the cell value under the given header, or null when absent.</summary>
    public static string? GetCell(IReadOnlyList<string> row, IReadOnlyDictionary<string, int> columnMap, string header)
    {
        if (columnMap.TryGetValue(header, out var index) && index < row.Count)
        {
            return row[index];
        }

        return null;
    }

    private static Sheet SelectSheet(List<Sheet> sheets, string[] preferredSheetNames)
    {
        foreach (var name in preferredSheetNames)
        {
            var match = sheets.FirstOrDefault(s =>
                name.Equals(s.Name?.Value, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return sheets.First();
    }

    private static Dictionary<string, string> LoadSharedStrings(WorkbookPart workbookPart)
    {
        var sharedStrings = new Dictionary<string, string>();
        var sharedStringPart = workbookPart.SharedStringTablePart;
        if (sharedStringPart is null)
        {
            return sharedStrings;
        }

        var items = sharedStringPart.SharedStringTable!.Elements<SharedStringItem>().ToList();
        for (var i = 0; i < items.Count; i++)
        {
            sharedStrings[i.ToString()] = items[i].Text?.Text
                ?? items[i].InnerText
                ?? string.Empty;
        }

        return sharedStrings;
    }

    private static List<string> ReadRowValues(Row row, Dictionary<string, string> sharedStrings)
    {
        var values = new List<string>();
        foreach (var cell in row.Elements<Cell>())
        {
            var columnIndex = GetColumnIndex(cell.CellReference!);

            while (values.Count < columnIndex)
            {
                values.Add(string.Empty);
            }

            values.Add(GetCellValue(cell, sharedStrings));
        }

        return values;
    }

    private static string GetCellValue(Cell cell, Dictionary<string, string> sharedStrings)
    {
        if (cell.CellValue == null && cell.InlineString == null)
        {
            return string.Empty;
        }

        if (cell.DataType?.Value == CellValues.SharedString && cell.CellValue != null)
        {
            return sharedStrings.TryGetValue(cell.CellValue.Text, out var sharedString) ? sharedString : string.Empty;
        }

        if (cell.DataType?.Value == CellValues.InlineString && cell.InlineString != null)
        {
            return cell.InlineString.Text?.Text ?? cell.InlineString.InnerText ?? string.Empty;
        }

        return cell.CellValue?.Text?.Trim() ?? string.Empty;
    }

    private static int GetColumnIndex(string cellReference)
    {
        var column = 0;
        foreach (var ch in cellReference)
        {
            if (!char.IsLetter(ch))
            {
                break;
            }

            column = column * 26 + (ch - 'A' + 1);
        }

        return column - 1;
    }
}

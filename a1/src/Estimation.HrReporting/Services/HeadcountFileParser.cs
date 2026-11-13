using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Estimation.HrReporting.Models;

namespace Estimation.HrReporting.Services;

public sealed class HeadcountParseResult
{
    public const int MaxListedErrors = 200;

    private readonly List<string> _errors = [];

    public HeadcountParseResult(string fileName)
    {
        FileName = fileName;
    }

    public string FileName { get; }

    public int? HeaderRowNumber { get; internal set; }

    public List<HeadcountRow> Rows { get; } = [];

    public IReadOnlyList<string> Errors => _errors;

    public int ErrorCount { get; private set; }

    public List<string> IgnoredHeaders { get; } = [];

    public bool CanSave => ErrorCount == 0 && Rows.Count > 0;

    internal void AddError(string message)
    {
        ErrorCount++;
        if (_errors.Count < MaxListedErrors)
        {
            _errors.Add(message);
        }
    }
}

public static class HeadcountFileParser
{
    public const string SheetName = "Headcount";
    public const int HeaderSearchRows = 20;

    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-dd HH:mm:ss",
        "dd/MM/yyyy",
        "d/M/yyyy",
        "dd.MM.yyyy",
        "d.M.yyyy",
        "dd-MM-yyyy",
        "dd-MMM-yyyy",
        "d-MMM-yyyy",
        "dd MMM yyyy",
        "d MMM yyyy",
        "dd-MMM-yy",
        "d-MMM-yy"
    ];

    private static readonly Dictionary<string, HeadcountColumn> ColumnsByHeader =
        HeadcountColumns.All.ToDictionary(c => c.NormalizedHeader, StringComparer.Ordinal);

    public static HeadcountParseResult Parse(Stream stream, string fileName)
    {
        var result = new HeadcountParseResult(fileName);

        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        SpreadsheetDocument document;
        try
        {
            document = SpreadsheetDocument.Open(stream, false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result.AddError("The file could not be opened as an Excel workbook (.xlsx).");
            return result;
        }

        using (document)
        {
            var workbookPart = document.WorkbookPart;
            var sheets = workbookPart?.Workbook?.Sheets?.Elements<Sheet>().ToList() ?? [];
            var sheet = sheets.FirstOrDefault(s =>
                string.Equals(s.Name?.Value?.Trim(), SheetName, StringComparison.OrdinalIgnoreCase));

            if (workbookPart is null || sheet is null)
            {
                var names = sheets.Select(s => s.Name?.Value).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
                result.AddError(names.Count == 0
                    ? $"The file has no sheet named '{SheetName}'."
                    : $"The file has no sheet named '{SheetName}'. Sheets in the file: {string.Join(", ", names)}.");
                return result;
            }

            ReadSheet(HeadcountWorkbookReader.ReadRows(workbookPart, sheet), result);
        }

        return result;
    }

    private static void ReadSheet(IEnumerable<SheetRow> rows, HeadcountParseResult result)
    {
        Dictionary<int, HeadcountColumn>? map = null;
        var scanned = 0;

        foreach (var row in rows)
        {
            if (map is null)
            {
                if (IsHeaderRow(row))
                {
                    result.HeaderRowNumber = row.RowNumber;
                    map = MapHeader(row, result);
                    if (map is null)
                    {
                        return;
                    }

                    continue;
                }

                if (++scanned >= HeaderSearchRows)
                {
                    break;
                }

                continue;
            }

            if (row.IsBlank)
            {
                continue;
            }

            ReadDataRow(row, map, result);
        }

        if (map is null)
        {
            result.AddError(
                $"No header row with a '{HeadcountColumns.Brid.Header}' column was found in the first {HeaderSearchRows} rows of the '{SheetName}' sheet.");
            return;
        }

        if (result.Rows.Count == 0 && result.ErrorCount == 0)
        {
            result.AddError($"The '{SheetName}' sheet has no data rows under the header.");
            return;
        }

        foreach (var duplicate in result.Rows
                     .GroupBy(r => r.Brid, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
        {
            result.AddError(
                $"BRID {duplicate.Key} is on more than one row: rows {string.Join(", ", duplicate.Select(r => r.RowNumber))}.");
        }
    }

    private static bool IsHeaderRow(SheetRow row) =>
        row.Cells.Values.Any(c => HeadcountColumn.NormalizeHeader(c.Text) == HeadcountColumns.Brid.NormalizedHeader);

    private static Dictionary<int, HeadcountColumn>? MapHeader(SheetRow header, HeadcountParseResult result)
    {
        var map = new Dictionary<int, HeadcountColumn>();
        var seen = new Dictionary<HeadcountColumn, int>();
        var failed = false;

        foreach (var (index, cell) in header.Cells.OrderBy(c => c.Key))
        {
            var normalized = HeadcountColumn.NormalizeHeader(cell.Text);
            if (normalized.Length == 0)
            {
                continue;
            }

            if (!ColumnsByHeader.TryGetValue(normalized, out var column))
            {
                result.IgnoredHeaders.Add(cell.Text.Trim());
                continue;
            }

            if (seen.TryGetValue(column, out var firstIndex))
            {
                result.AddError(
                    $"The column '{column.Header}' appears more than once (columns {HeadcountWorkbookReader.ColumnLetters(firstIndex)} and {HeadcountWorkbookReader.ColumnLetters(index)}).");
                failed = true;
                continue;
            }

            seen[column] = index;
            map[index] = column;
        }

        var missing = HeadcountColumns.All.Where(c => !seen.ContainsKey(c)).Select(c => c.Header).ToList();
        if (missing.Count > 0)
        {
            result.AddError(missing.Count == 1
                ? $"The column '{missing[0]}' is missing from the header row (row {header.RowNumber})."
                : $"{missing.Count} columns are missing from the header row (row {header.RowNumber}): {string.Join(", ", missing)}.");
            failed = true;
        }

        return failed ? null : map;
    }

    private static void ReadDataRow(SheetRow sheetRow, Dictionary<int, HeadcountColumn> map, HeadcountParseResult result)
    {
        var row = new HeadcountRow { RowNumber = sheetRow.RowNumber };

        foreach (var (index, column) in map)
        {
            var cell = sheetRow.Get(index);
            if (TryReadValue(cell, column, sheetRow.RowNumber, result, out var value))
            {
                column.SetValue(row, value);
            }
        }

        if (string.IsNullOrEmpty(row.Brid))
        {
            result.AddError($"Row {sheetRow.RowNumber}: {HeadcountColumns.Brid.Header} is empty.");
            return;
        }

        result.Rows.Add(row);
    }

    private static bool TryReadValue(
        SheetCell? cell, HeadcountColumn column, int rowNumber, HeadcountParseResult result, out object? value)
    {
        value = null;
        if (cell is null || string.IsNullOrWhiteSpace(cell.Value.Text))
        {
            return column != HeadcountColumns.Brid;
        }

        var raw = cell.Value;

        switch (column.Kind)
        {
            case HeadcountValueKind.Text:
            {
                var text = (raw.Kind == SheetCellKind.Number ? NumberText(raw.Text) : raw.Text).Trim();
                if (text.Length > column.MaxLength)
                {
                    result.AddError(
                        $"Row {rowNumber}, {column.Header}: the value is {text.Length} characters long; the limit is {column.MaxLength}.");
                    return false;
                }

                value = text;
                return true;
            }

            case HeadcountValueKind.Number:
            {
                var text = raw.Text.Trim();
                if (text == "-")
                {
                    return true;
                }

                if (raw.Kind is SheetCellKind.Number or SheetCellKind.Text
                    && decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    value = Math.Round(number, 4, MidpointRounding.AwayFromZero);
                    return true;
                }

                result.AddError($"Row {rowNumber}, {column.Header}: '{text}' is not a number.");
                return false;
            }

            case HeadcountValueKind.Date:
            {
                var text = raw.Text.Trim();
                if (text == "-")
                {
                    return true;
                }

                if (TryReadDate(raw.Kind, text, out var date))
                {
                    value = date;
                    return true;
                }

                result.AddError($"Row {rowNumber}, {column.Header}: '{text}' is not a date.");
                return false;
            }
        }

        return false;
    }

    private static bool TryReadDate(SheetCellKind kind, string text, out DateTime date)
    {
        date = default;

        switch (kind)
        {
            case SheetCellKind.Number:
                return TryFromSerial(text, out date);

            case SheetCellKind.Date:
                if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var iso))
                {
                    date = iso.Date;
                    return InRange(date);
                }

                return false;

            case SheetCellKind.Text:
                if (TryFromSerial(text, out date))
                {
                    return true;
                }

                if (DateTime.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture,
                        DateTimeStyles.AllowWhiteSpaces, out var parsed))
                {
                    date = parsed.Date;
                    return InRange(date);
                }

                return false;

            default:
                return false;
        }
    }

    private static bool TryFromSerial(string text, out DateTime date)
    {
        date = default;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial)
            || serial < 1 || serial >= 2958466)
        {
            return false;
        }

        date = DateTime.FromOADate(serial).Date;
        return InRange(date);
    }

    private static bool InRange(DateTime date) => date.Year is >= 1900 and <= 2200;

    private static string NumberText(string text)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return text;
        }

        return Math.Abs(number) < 1e15 && Math.Abs(number % 1) < double.Epsilon
            ? number.ToString("0", CultureInfo.InvariantCulture)
            : number.ToString("G15", CultureInfo.InvariantCulture);
    }
}

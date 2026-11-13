using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Estimation.HrReporting.Services;

internal enum SheetCellKind
{
    Text,
    Number,
    Date,
    Boolean,
    Error
}

internal readonly record struct SheetCell(string Text, SheetCellKind Kind);

internal sealed class SheetRow
{
    public SheetRow(int rowNumber, IReadOnlyDictionary<int, SheetCell> cells)
    {
        RowNumber = rowNumber;
        Cells = cells;
    }

    public int RowNumber { get; }

    public IReadOnlyDictionary<int, SheetCell> Cells { get; }

    public bool IsBlank => Cells.Values.All(c => string.IsNullOrWhiteSpace(c.Text));

    public SheetCell? Get(int columnIndex) => Cells.TryGetValue(columnIndex, out var cell) ? cell : null;
}

internal static class HeadcountWorkbookReader
{
    public static IEnumerable<SheetRow> ReadRows(WorkbookPart workbookPart, Sheet sheet)
    {
        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!.Value!);
        var sharedStrings = LoadSharedStrings(workbookPart);
        var builder = new RowBuilder(sharedStrings);

        using var reader = OpenXmlReader.Create(worksheetPart);

        while (reader.Read())
        {
            var type = reader.ElementType;

            if (type == typeof(Row))
            {
                if (reader.IsStartElement)
                {
                    if (builder.Complete() is { } unfinished)
                    {
                        yield return unfinished;
                    }

                    builder.StartRow(Attribute(reader, "r"));
                }
                else if (reader.IsEndElement && builder.Complete() is { } row)
                {
                    yield return row;
                }
            }
            else if (type == typeof(Cell))
            {
                if (reader.IsStartElement)
                {
                    builder.StartCell(Attribute(reader, "r"), Attribute(reader, "t"));
                }
                else if (reader.IsEndElement)
                {
                    builder.EndCell();
                }
            }
            else if (type == typeof(CellValue) && reader.IsStartElement)
            {
                builder.SetValue(reader.GetText());
            }
            else if (type == typeof(Text) && reader.IsStartElement)
            {
                builder.AppendInlineText(reader.GetText());
            }
        }

        if (builder.Complete() is { } last)
        {
            yield return last;
        }
    }

    internal static int ColumnIndex(string cellReference)
    {
        var column = 0;
        foreach (var ch in cellReference)
        {
            if (!char.IsLetter(ch))
            {
                break;
            }

            column = column * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }

        return column - 1;
    }

    internal static string ColumnLetters(int columnIndex)
    {
        var letters = string.Empty;
        var value = columnIndex + 1;
        while (value > 0)
        {
            var remainder = (value - 1) % 26;
            letters = (char)('A' + remainder) + letters;
            value = (value - 1) / 26;
        }

        return letters;
    }

    private static string? Attribute(OpenXmlReader reader, string localName)
    {
        foreach (var attribute in reader.Attributes)
        {
            if (attribute.LocalName == localName)
            {
                return attribute.Value;
            }
        }

        return null;
    }

    private static List<string> LoadSharedStrings(WorkbookPart workbookPart)
    {
        var result = new List<string>();
        var part = workbookPart.SharedStringTablePart;
        if (part is null)
        {
            return result;
        }

        using var reader = OpenXmlReader.Create(part);
        StringBuilder? current = null;
        var phoneticDepth = 0;

        while (reader.Read())
        {
            var type = reader.ElementType;

            if (type == typeof(SharedStringItem))
            {
                if (reader.IsStartElement)
                {
                    if (current is not null)
                    {
                        result.Add(current.ToString());
                    }

                    current = new StringBuilder();
                    phoneticDepth = 0;
                }
                else if (reader.IsEndElement && current is not null)
                {
                    result.Add(current.ToString());
                    current = null;
                }
            }
            else if (type == typeof(PhoneticRun))
            {
                if (reader.IsStartElement)
                {
                    phoneticDepth++;
                }
                else if (reader.IsEndElement && phoneticDepth > 0)
                {
                    phoneticDepth--;
                }
            }
            else if (type == typeof(Text) && reader.IsStartElement && phoneticDepth == 0)
            {
                current?.Append(reader.GetText());
            }
        }

        if (current is not null)
        {
            result.Add(current.ToString());
        }

        return result;
    }

    private sealed class RowBuilder
    {
        private readonly List<string> _sharedStrings;
        private Dictionary<int, SheetCell>? _cells;
        private int _rowNumber;
        private int _nextColumn;
        private bool _inCell;
        private int _cellColumn;
        private string? _cellType;
        private string? _value;
        private StringBuilder? _inlineText;

        public RowBuilder(List<string> sharedStrings) => _sharedStrings = sharedStrings;

        public void StartRow(string? reference)
        {
            _rowNumber = int.TryParse(reference, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                ? number
                : _rowNumber + 1;
            _cells = new Dictionary<int, SheetCell>();
            _nextColumn = 0;
        }

        public void StartCell(string? reference, string? type)
        {
            EndCell();
            _inCell = true;
            _cellColumn = string.IsNullOrEmpty(reference) ? _nextColumn : ColumnIndex(reference);
            _cellType = type;
            _value = null;
            _inlineText = null;
        }

        public void SetValue(string text)
        {
            if (_inCell)
            {
                _value = text;
            }
        }

        public void AppendInlineText(string text)
        {
            if (_inCell)
            {
                (_inlineText ??= new StringBuilder()).Append(text);
            }
        }

        public void EndCell()
        {
            if (!_inCell)
            {
                return;
            }

            _inCell = false;
            _nextColumn = _cellColumn + 1;

            if (_cells is not null && Resolve() is { } cell)
            {
                _cells[_cellColumn] = cell;
            }
        }

        public SheetRow? Complete()
        {
            EndCell();
            if (_cells is null)
            {
                return null;
            }

            var row = new SheetRow(_rowNumber, _cells);
            _cells = null;
            return row;
        }

        private SheetCell? Resolve()
        {
            switch (_cellType)
            {
                case "s":
                    return int.TryParse(_value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                           && index >= 0 && index < _sharedStrings.Count
                        ? new SheetCell(_sharedStrings[index], SheetCellKind.Text)
                        : null;
                case "inlineStr":
                    var inline = _inlineText?.ToString() ?? _value;
                    return inline is null ? null : new SheetCell(inline, SheetCellKind.Text);
                case "str":
                    return _value is null ? null : new SheetCell(_value, SheetCellKind.Text);
                case "b":
                    return _value is null ? null : new SheetCell(_value == "1" ? "TRUE" : "FALSE", SheetCellKind.Boolean);
                case "e":
                    return _value is null ? null : new SheetCell(_value, SheetCellKind.Error);
                case "d":
                    return _value is null ? null : new SheetCell(_value, SheetCellKind.Date);
                default:
                    return string.IsNullOrEmpty(_value) ? null : new SheetCell(_value, SheetCellKind.Number);
            }
        }
    }
}

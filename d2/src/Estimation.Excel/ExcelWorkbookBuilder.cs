using static Estimation.Excel.ExcelPrimitives;

namespace Estimation.Excel;

public readonly record struct ColumnWidth(uint Min, uint Max, double Width)
{
    public ColumnWidth(uint column, double width) : this(column, column, width)
    {
    }
}

public sealed class ExcelWorkbookBuilder
{
    private readonly MemoryStream _stream = new();
    private readonly SpreadsheetDocument _document;
    private readonly WorkbookPart _workbookPart;
    private readonly Workbook _workbook;
    private readonly Sheets _sheets;
    private readonly Stylesheet _stylesheet;
    private readonly List<ExcelSheetBuilder> _sheetBuilders = new();
    private uint _nextSheetId = 1;

    private readonly Dictionary<string, uint> _fillStyleByColor = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, uint> _fontStyleByColor = new(StringComparer.OrdinalIgnoreCase);
    private uint _nextFillId = FirstDynamicFillId;
    private uint _nextFontId = FirstDynamicFontId;
    private uint _nextStyleIndex = FirstDynamicStyleIndex;

    public ExcelWorkbookBuilder()
    {
        _document = SpreadsheetDocument.Create(_stream, SpreadsheetDocumentType.Workbook);
        _workbookPart = _document.AddWorkbookPart();
        _workbook = _workbookPart.Workbook = new Workbook();

        var stylesPart = _workbookPart.AddNewPart<WorkbookStylesPart>();
        _stylesheet = CreateStylesheet();
        stylesPart.Stylesheet = _stylesheet;

        _sheets = _workbook.AppendChild(new Sheets());
    }

    public uint GetOrCreateFillStyle(string hexColor)
    {
        var argb = NormalizeArgb(hexColor);
        if (_fillStyleByColor.TryGetValue(argb, out var existing))
        {
            return existing;
        }

        var fills = _stylesheet.GetFirstChild<Fills>()!;
        fills.Append(CreateSolidFill(argb));
        var fillId = _nextFillId++;

        var cellFormats = _stylesheet.GetFirstChild<CellFormats>()!;
        cellFormats.Append(new CellFormat { FillId = fillId, ApplyFill = true });
        var styleIndex = _nextStyleIndex++;

        _fillStyleByColor[argb] = styleIndex;
        return styleIndex;
    }

    public uint GetOrCreateFontColorStyle(string hexColor)
    {
        var argb = NormalizeArgb(hexColor);
        if (_fontStyleByColor.TryGetValue(argb, out var existing))
        {
            return existing;
        }

        var fonts = _stylesheet.GetFirstChild<Fonts>()!;
        fonts.Append(CreateColorFont(argb));
        var fontId = _nextFontId++;

        var cellFormats = _stylesheet.GetFirstChild<CellFormats>()!;
        cellFormats.Append(new CellFormat { FontId = fontId, ApplyFont = true });
        var styleIndex = _nextStyleIndex++;

        _fontStyleByColor[argb] = styleIndex;
        return styleIndex;
    }

    public ExcelSheetBuilder AddSheet(string name)
    {
        var worksheetPart = _workbookPart.AddNewPart<WorksheetPart>();
        _sheets.Append(new Sheet
        {
            Id = _workbookPart.GetIdOfPart(worksheetPart),
            SheetId = _nextSheetId++,
            Name = XmlSafe(name)
        });

        var sheetBuilder = new ExcelSheetBuilder(this, worksheetPart);
        _sheetBuilders.Add(sheetBuilder);
        return sheetBuilder;
    }

    public ExcelWorkbookBuilder AddDefinedName(string name, string reference)
    {
        var definedNames = _workbook.GetFirstChild<DefinedNames>();
        if (definedNames is null)
        {
            definedNames = new DefinedNames();
            _workbook.Append(definedNames);
        }

        definedNames.Append(new DefinedName(reference) { Name = name });
        return this;
    }

    public byte[] ToArray()
    {
        _stylesheet.Save();

        foreach (var sheetBuilder in _sheetBuilders)
        {
            sheetBuilder.Finalise();
        }

        _workbook.Save();
        _document.Dispose();
        return _stream.ToArray();
    }
}

public sealed class ExcelSheetBuilder
{
    private readonly WorksheetPart _worksheetPart;
    private readonly SheetData _sheetData = new();
    private Columns? _columns;
    private DataValidations? _dataValidations;
    private bool _freezeTopRow;
    private string? _autoFilterReference;
    private uint _nextRowIndex = 1;

    public ExcelWorkbookBuilder Workbook { get; }

    internal ExcelSheetBuilder(ExcelWorkbookBuilder workbook, WorksheetPart worksheetPart)
    {
        Workbook = workbook;
        _worksheetPart = worksheetPart;
    }

    public ExcelSheetBuilder SetColumnWidths(params double[] widths)
    {
        for (var i = 0; i < widths.Length; i++)
        {
            AddColumnWidth(new ColumnWidth((uint)(i + 1), widths[i]));
        }

        return this;
    }

    public ExcelSheetBuilder AddColumnWidth(ColumnWidth width)
    {
        _columns ??= new Columns();
        _columns.Append(new Column
        {
            Min = width.Min,
            Max = width.Max,
            Width = width.Width,
            CustomWidth = true
        });
        return this;
    }

    public ExcelSheetBuilder WriteHeader(params string[] headers)
    {
        return WriteHeader((IEnumerable<string>)headers);
    }

    public ExcelSheetBuilder WriteHeader(IEnumerable<string> headers)
    {
        return WriteHeaderCore(headers, ExcelPrimitives.HeaderStyleIndex);
    }

    public ExcelSheetBuilder WriteColoredHeader(IEnumerable<string> headers)
    {
        return WriteHeaderCore(headers, ExcelPrimitives.HeaderFillStyleIndex);
    }

    private ExcelSheetBuilder WriteHeaderCore(IEnumerable<string> headers, uint styleIndex)
    {
        var row = AddRow();
        foreach (var header in headers)
        {
            row.Text(header);
        }

        foreach (var cell in row.Row.Elements<Cell>())
        {
            cell.StyleIndex = styleIndex;
        }

        return this;
    }

    public ExcelSheetBuilder FreezeTopRow()
    {
        _freezeTopRow = true;
        return this;
    }

    public ExcelSheetBuilder SetAutoFilter(int columnCount, int dataRowCount)
    {
        if (columnCount <= 0)
        {
            return this;
        }

        var lastColumn = GetColumnReference(columnCount - 1);
        var lastRow = Math.Max(1, dataRowCount + 1);
        _autoFilterReference = $"A1:{lastColumn}{lastRow}";
        return this;
    }

    public ExcelRowBuilder AddRow()
    {
        var row = new Row { RowIndex = _nextRowIndex++ };
        _sheetData.Append(row);
        return new ExcelRowBuilder(row, _nextRowIndex - 1);
    }

    public ExcelSheetBuilder AddListValidation(
        int columnIndex,
        int firstDataRow,
        int lastDataRow,
        IEnumerable<string> values,
        string errorTitle,
        string error,
        bool skipIfFormulaExceeds255 = false)
    {
        var formula = XmlSafe(string.Join(",", values.Select(EscapeFormulaValue)));
        if (skipIfFormulaExceeds255 && formula.Length > 255)
        {
            return this;
        }

        var validation = CreateListValidation(columnIndex, firstDataRow, lastDataRow, errorTitle, error);
        validation.Append(new Formula1($"\"{formula}\""));
        AppendValidation(validation);
        return this;
    }

    public ExcelSheetBuilder AddNamedListValidation(
        int columnIndex,
        int firstDataRow,
        int lastDataRow,
        string definedName,
        string errorTitle,
        string error,
        bool blocking = true)
    {
        var validation = CreateListValidation(columnIndex, firstDataRow, lastDataRow, errorTitle, error);
        validation.ShowErrorMessage = blocking;
        validation.Append(new Formula1(definedName));
        AppendValidation(validation);
        return this;
    }

    public ExcelSheetBuilder AddWholeNumberValidation(
        int columnIndex,
        int firstDataRow,
        int lastDataRow,
        string errorTitle,
        string error,
        int minimum = 0)
    {
        var colRef = GetColumnReference(columnIndex);
        var validation = new DataValidation
        {
            Type = DataValidationValues.Whole,
            Operator = DataValidationOperatorValues.GreaterThanOrEqual,
            AllowBlank = true,
            ShowErrorMessage = true,
            ErrorTitle = new StringValue(errorTitle),
            Error = new StringValue(error),
            SequenceOfReferences = new ListValue<StringValue>(
                new[] { new StringValue($"{colRef}{firstDataRow}:{colRef}{lastDataRow}") })
        };
        validation.Append(new Formula1(minimum.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        AppendValidation(validation);
        return this;
    }

    private static DataValidation CreateListValidation(
        int columnIndex,
        int firstDataRow,
        int lastDataRow,
        string errorTitle,
        string error)
    {
        var colRef = GetColumnReference(columnIndex);
        return new DataValidation
        {
            Type = DataValidationValues.List,
            AllowBlank = true,
            ShowDropDown = false,
            ShowErrorMessage = true,
            ErrorTitle = new StringValue(errorTitle),
            Error = new StringValue(error),
            SequenceOfReferences = new ListValue<StringValue>(
                new[] { new StringValue($"{colRef}{firstDataRow}:{colRef}{lastDataRow}") })
        };
    }

    private void AppendValidation(DataValidation validation)
    {
        _dataValidations ??= new DataValidations();
        _dataValidations.Append(validation);
    }

    internal void Finalise()
    {
        var worksheet = new Worksheet();

        if (_freezeTopRow)
        {
            worksheet.Append(new SheetViews(
                new SheetView(
                    new Pane
                    {
                        VerticalSplit = 1D,
                        TopLeftCell = "A2",
                        ActivePane = PaneValues.BottomLeft,
                        State = PaneStateValues.Frozen
                    },
                    new Selection { Pane = PaneValues.BottomLeft })
                {
                    WorkbookViewId = 0U
                }));
        }

        if (_columns is not null)
        {
            worksheet.Append(_columns);
        }

        worksheet.Append(_sheetData);

        if (_autoFilterReference is not null)
        {
            worksheet.Append(new AutoFilter { Reference = _autoFilterReference });
        }

        if (_dataValidations is { HasChildren: true })
        {
            _dataValidations.Count = (uint)_dataValidations.ChildElements.Count;
            worksheet.Append(_dataValidations);
        }

        _worksheetPart.Worksheet = worksheet;
    }
}

public sealed class ExcelRowBuilder
{
    private readonly uint _rowIndex;
    private int _columnIndex;

    internal Row Row { get; }

    internal ExcelRowBuilder(Row row, uint rowIndex)
    {
        Row = row;
        _rowIndex = rowIndex;
    }

    public ExcelRowBuilder Text(string? value, bool skipIfEmpty = false, bool derived = false, bool wrap = false)
    {
        if (skipIfEmpty && string.IsNullOrEmpty(value))
        {
            _columnIndex++;
            return this;
        }

        var cell = CreateTextCell(CurrentReference(), value ?? "");
        if (wrap)
        {
            cell.StyleIndex = ExcelPrimitives.WrapTextStyleIndex;
        }
        else if (derived)
        {
            cell.StyleIndex = ExcelPrimitives.DerivedStyleIndex;
        }

        Row.Append(cell);
        _columnIndex++;
        return this;
    }

    public ExcelRowBuilder Formula(string formula, string? cachedValue = null, bool derived = false)
    {
        var styleIndex = derived ? (uint?)ExcelPrimitives.DerivedStyleIndex : null;
        Row.Append(CreateFormulaCell(CurrentReference(), formula, cachedValue ?? "", styleIndex));
        _columnIndex++;
        return this;
    }

    public ExcelRowBuilder StyledText(string? value, uint styleIndex)
    {
        var cell = CreateTextCell(CurrentReference(), value ?? "");
        cell.StyleIndex = styleIndex;
        Row.Append(cell);
        _columnIndex++;
        return this;
    }

    public ExcelRowBuilder StyledNumber(int? value, uint styleIndex)
    {
        var cell = value.HasValue
            ? CreateNumberCell(CurrentReference(), value.Value)
            : CreateTextCell(CurrentReference(), "");
        cell.StyleIndex = styleIndex;
        Row.AppendChild(cell);
        _columnIndex++;
        return this;
    }

    public ExcelRowBuilder Date(DateTime? value)
    {
        if (value.HasValue)
        {
            Row.Append(CreateDateCell(CurrentReference(), value.Value));
        }

        _columnIndex++;
        return this;
    }

    public ExcelRowBuilder Number(int? value)
    {
        if (value.HasValue)
        {
            Row.Append(CreateNumberCell(CurrentReference(), value.Value));
        }

        _columnIndex++;
        return this;
    }

    public ExcelRowBuilder Number(double? value)
    {
        if (value.HasValue)
        {
            Row.Append(CreateNumberCell(CurrentReference(), value.Value));
        }

        _columnIndex++;
        return this;
    }

    public ExcelRowBuilder Skip(int count = 1)
    {
        _columnIndex += count;
        return this;
    }

    private string CurrentReference()
    {
        return $"{GetColumnReference(_columnIndex)}{_rowIndex}";
    }
}

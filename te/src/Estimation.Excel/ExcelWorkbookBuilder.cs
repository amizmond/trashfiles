using static Estimation.Excel.ExcelPrimitives;

namespace Estimation.Excel;

/// <summary>
/// Width of a single spreadsheet column, expressed in the same units Excel uses.
/// </summary>
public readonly record struct ColumnWidth(uint Min, uint Max, double Width)
{
    public ColumnWidth(uint column, double width) : this(column, column, width)
    {
    }
}

/// <summary>
/// Fluent builder over DocumentFormat.OpenXml for the single-purpose workbooks this project
/// produces (exports and templates). It centralises the scaffolding that every export service
/// used to repeat by hand: document/part creation, the bold-header stylesheet, column widths,
/// and list/whole-number data validations.
/// </summary>
/// <example>
/// <code>
/// return new ExcelWorkbookBuilder()
///     .AddSheet("Skills")
///         .SetColumnWidths(25, 20, 10, 35)
///         .WriteHeader("Skill", "Level", "Value", "Level Description")
///         .Workbook
///     .ToArray();
/// </code>
/// </example>
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

    // Runtime-registered fills/fonts/formats appended after the built-in ones.
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

        // One shared stylesheet for the whole workbook; cell style index 1 is bold.
        var stylesPart = _workbookPart.AddNewPart<WorkbookStylesPart>();
        _stylesheet = CreateStylesheet();
        stylesPart.Stylesheet = _stylesheet;

        _sheets = _workbook.AppendChild(new Sheets());
    }

    /// <summary>
    /// Returns a cell-style index that paints a cell with the given solid colour, creating the fill
    /// and cell format on first use and caching it so repeated colours reuse one style. Accepts
    /// "#RRGGBB", "RRGGBB" or "AARRGGBB".
    /// </summary>
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

    /// <summary>
    /// Returns a cell-style index that renders text in the given solid colour, creating the font and
    /// cell format on first use and caching it so repeated colours reuse one style. Accepts
    /// "#RRGGBB", "RRGGBB" or "AARRGGBB".
    /// </summary>
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

    /// <summary>Adds a worksheet and returns its builder for writing rows, widths and validations.</summary>
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

    /// <summary>
    /// Registers a workbook-scoped defined name (named range), e.g. so a data validation can
    /// reference a lookup column on another sheet without the 255-char inline-list limit.
    /// </summary>
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

    /// <summary>Finalises every sheet and returns the workbook as a byte array.</summary>
    public byte[] ToArray()
    {
        // Persist the stylesheet last so any fills/formats registered at runtime are included.
        _stylesheet.Save();

        foreach (var sheetBuilder in _sheetBuilders)
        {
            sheetBuilder.Finalise();
        }

        _workbook.Save();
        _document.Dispose(); // flushes the package into the underlying stream
        return _stream.ToArray();
    }
}

/// <summary>
/// Builds the content of a single worksheet. Cells are written through <see cref="WriteHeader"/>
/// and <see cref="AddRow"/>; widths and validations are buffered and emitted in the OpenXML-required
/// order (columns, then rows, then data validations) when the parent workbook is finalised.
/// </summary>
public sealed class ExcelSheetBuilder
{
    private readonly WorksheetPart _worksheetPart;
    private readonly SheetData _sheetData = new();
    private Columns? _columns;
    private DataValidations? _dataValidations;
    private bool _freezeTopRow;
    private string? _autoFilterReference;
    private uint _nextRowIndex = 1;

    /// <summary>The owning workbook, exposed so callers can fluently chain back to <c>ToArray</c>.</summary>
    public ExcelWorkbookBuilder Workbook { get; }

    internal ExcelSheetBuilder(ExcelWorkbookBuilder workbook, WorksheetPart worksheetPart)
    {
        Workbook = workbook;
        _worksheetPart = worksheetPart;
    }

    /// <summary>Sets column widths for single columns 1..n in order.</summary>
    public ExcelSheetBuilder SetColumnWidths(params double[] widths)
    {
        for (var i = 0; i < widths.Length; i++)
        {
            AddColumnWidth(new ColumnWidth((uint)(i + 1), widths[i]));
        }

        return this;
    }

    /// <summary>Adds one column-width entry (single column or a contiguous range).</summary>
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

    /// <summary>Writes the bold header row from the given column titles.</summary>
    public ExcelSheetBuilder WriteHeader(params string[] headers)
    {
        return WriteHeader((IEnumerable<string>)headers);
    }

    /// <summary>Writes the bold header row from the given column titles.</summary>
    public ExcelSheetBuilder WriteHeader(IEnumerable<string> headers)
    {
        return WriteHeaderCore(headers, ExcelPrimitives.HeaderStyleIndex);
    }

    /// <summary>
    /// Writes the header row using the coloured (#e8eaf6) sticky-header style instead of plain bold.
    /// Pair with <see cref="FreezeTopRow"/> to keep the header visible while scrolling.
    /// </summary>
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

    /// <summary>
    /// Freezes the first (header) row so it stays pinned to the top while the user scrolls.
    /// </summary>
    public ExcelSheetBuilder FreezeTopRow()
    {
        _freezeTopRow = true;
        return this;
    }

    /// <summary>
    /// Enables column filter dropdowns over the header + data range, spanning
    /// <paramref name="columnCount"/> columns and <paramref name="dataRowCount"/> data rows
    /// (the header row is added automatically). No-op when there are no columns.
    /// </summary>
    public ExcelSheetBuilder SetAutoFilter(int columnCount, int dataRowCount)
    {
        if (columnCount <= 0)
        {
            return this;
        }

        var lastColumn = GetColumnReference(columnCount - 1);
        var lastRow = Math.Max(1, dataRowCount + 1); // +1 for the header row
        _autoFilterReference = $"A1:{lastColumn}{lastRow}";
        return this;
    }

    /// <summary>Starts a new data row and returns a writer for its cells.</summary>
    public ExcelRowBuilder AddRow()
    {
        var row = new Row { RowIndex = _nextRowIndex++ };
        _sheetData.Append(row);
        return new ExcelRowBuilder(row, _nextRowIndex - 1);
    }

    /// <summary>
    /// Adds a list (dropdown) validation backed by an inline value list over a column range.
    /// When <paramref name="skipIfFormulaExceeds255"/> is set, the validation is silently skipped
    /// if the inline list would exceed Excel's 255-character limit (rather than corrupting the file).
    /// </summary>
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

    /// <summary>
    /// Adds a list (dropdown) validation whose source is a workbook defined name
    /// (see <see cref="ExcelWorkbookBuilder.AddDefinedName"/>), avoiding the inline-list size limit.
    /// When <paramref name="blocking"/> is false the list acts as a non-restrictive suggestion
    /// dropdown — Excel still shows the arrow but accepts values outside the list (used for
    /// free-text columns such as feature Status).
    /// </summary>
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

    /// <summary>Adds a "whole number &gt;= <paramref name="minimum"/>" validation over a column range.</summary>
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
            ShowDropDown = false, // false = show the dropdown arrow (counter-intuitive OpenXML API)
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

    /// <summary>
    /// Assembles the worksheet in the order OpenXML requires: column widths, then the row data,
    /// then data validations. Called by the owning workbook from <see cref="ExcelWorkbookBuilder.ToArray"/>.
    /// </summary>
    internal void Finalise()
    {
        var worksheet = new Worksheet();

        // OpenXML requires this child order: sheetViews, cols, sheetData, autoFilter, dataValidations.
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

/// <summary>
/// Writes cells into a single row left to right. Each call advances the column cursor, so callers
/// emit cells in column order; empty/absent cells can be skipped while keeping later columns aligned.
/// </summary>
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

    /// <summary>
    /// Writes a text cell at the current column. A null value is treated as empty.
    /// When <paramref name="skipIfEmpty"/> is set, an empty value leaves the cell unwritten
    /// (the column cursor still advances so following cells stay aligned).
    /// <paramref name="wrap"/> applies the wrapped multi-line style so embedded line breaks
    /// render as real lines in Excel.
    /// </summary>
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

    /// <summary>
    /// Writes a formula cell at the current column. <paramref name="formula"/> is the Excel
    /// expression without the leading '=' (e.g. <c>IFERROR(VLOOKUP(...),"")</c>);
    /// <paramref name="cachedValue"/> is stored so the result shows before Excel recalculates.
    /// Set <paramref name="derived"/> to flag the cell with the grey read-only style.
    /// </summary>
    public ExcelRowBuilder Formula(string formula, string? cachedValue = null, bool derived = false)
    {
        var styleIndex = derived ? (uint?)ExcelPrimitives.DerivedStyleIndex : null;
        Row.Append(CreateFormulaCell(CurrentReference(), formula, cachedValue ?? "", styleIndex));
        _columnIndex++;
        return this;
    }

    /// <summary>
    /// Writes a text cell at the current column using a specific cell-style index (e.g. a colour
    /// fill obtained from <see cref="ExcelWorkbookBuilder.GetOrCreateFillStyle"/>).
    /// </summary>
    public ExcelRowBuilder StyledText(string? value, uint styleIndex)
    {
        var cell = CreateTextCell(CurrentReference(), value ?? "");
        cell.StyleIndex = styleIndex;
        Row.Append(cell);
        _columnIndex++;
        return this;
    }

    /// <summary>
    /// Writes a number cell at the current column using a specific cell-style index (e.g. a colour
    /// fill obtained from <see cref="ExcelWorkbookBuilder.GetOrCreateFillStyle"/>). A null value
    /// still writes the styled cell so the fill spans the whole column.
    /// </summary>
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

    /// <summary>
    /// Writes a real date cell at the current column (styled <c>yyyy-mm-dd</c>). A null value leaves
    /// the cell unwritten (the column cursor still advances so following cells stay aligned).
    /// </summary>
    public ExcelRowBuilder Date(DateTime? value)
    {
        if (value.HasValue)
        {
            Row.Append(CreateDateCell(CurrentReference(), value.Value));
        }

        _columnIndex++;
        return this;
    }

    /// <summary>
    /// Writes a number cell at the current column. A null value leaves the cell unwritten
    /// (the column cursor still advances so following cells stay aligned).
    /// </summary>
    public ExcelRowBuilder Number(int? value)
    {
        if (value.HasValue)
        {
            Row.Append(CreateNumberCell(CurrentReference(), value.Value));
        }

        _columnIndex++;
        return this;
    }

    /// <summary>
    /// Writes a possibly-fractional number cell (e.g. 0.5 leave days) at the current column. A null
    /// value leaves the cell unwritten (the column cursor still advances so following cells stay aligned).
    /// </summary>
    public ExcelRowBuilder Number(double? value)
    {
        if (value.HasValue)
        {
            Row.Append(CreateNumberCell(CurrentReference(), value.Value));
        }

        _columnIndex++;
        return this;
    }

    /// <summary>Advances the column cursor by <paramref name="count"/> without writing anything.</summary>
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

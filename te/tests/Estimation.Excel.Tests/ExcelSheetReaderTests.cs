namespace Estimation.Excel.Tests;

public class ExcelSheetReaderTests
{
    [Fact]
    public void The_first_row_becomes_the_header_and_the_rest_become_data()
    {
        var sheet = new ExcelWorkbookBuilder().AddSheet("Data");
        sheet.WriteHeader("Name", "Level");
        sheet.AddRow().Text("Ada").Text("Senior");
        sheet.AddRow().Text("Grace").Text("Principal");

        var (headers, rows) = WorkbookProbe.Read(sheet.Workbook.ToArray());

        Assert.Equal(new[] { "Name", "Level" }, headers);
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "Ada", "Senior" }, rows[0]);
        Assert.Equal(new[] { "Grace", "Principal" }, rows[1]);
    }

    [Fact]
    public void A_sheet_with_no_rows_reads_as_empty()
    {
        var bytes = new ExcelWorkbookBuilder().AddSheet("Data").Workbook.ToArray();

        var (headers, rows) = WorkbookProbe.Read(bytes);

        Assert.Empty(headers);
        Assert.Empty(rows);
    }

    [Fact]
    public void A_sheet_with_only_a_header_reads_as_no_data()
    {
        var bytes = new ExcelWorkbookBuilder().AddSheet("Data").WriteHeader("Name").Workbook.ToArray();

        var (headers, rows) = WorkbookProbe.Read(bytes);

        Assert.Equal(new[] { "Name" }, headers);
        Assert.Empty(rows);
    }

    [Fact]
    public void Rows_that_hold_nothing_but_blanks_are_dropped()
    {
        var sheet = new ExcelWorkbookBuilder().AddSheet("Data");
        sheet.WriteHeader("Name");
        sheet.AddRow().Text("Ada");
        sheet.AddRow().Text("");
        sheet.AddRow().Text("   ");
        sheet.AddRow();
        sheet.AddRow().Text("Grace");

        var (_, rows) = WorkbookProbe.Read(sheet.Workbook.ToArray());

        Assert.Equal(2, rows.Count);
        Assert.Equal("Ada", rows[0][0]);
        Assert.Equal("Grace", rows[1][0]);
    }

    [Fact]
    public void Gaps_between_cells_are_filled_so_columns_stay_aligned()
    {
        var sheet = new ExcelWorkbookBuilder().AddSheet("Data");
        sheet.WriteHeader("A", "B", "C", "D");
        sheet.AddRow().Text("a").Text("", skipIfEmpty: true).Text("", skipIfEmpty: true).Text("d");

        var (_, rows) = WorkbookProbe.Read(sheet.Workbook.ToArray());

        Assert.Equal(new[] { "a", "", "", "d" }, rows.Single());
    }

    [Fact]
    public void A_gap_in_the_first_column_is_filled_too()
    {
        var sheet = new ExcelWorkbookBuilder().AddSheet("Data");
        sheet.WriteHeader("A", "B");
        sheet.AddRow().Text("", skipIfEmpty: true).Text("b");

        var (_, rows) = WorkbookProbe.Read(sheet.Workbook.ToArray());

        Assert.Equal(new[] { "", "b" }, rows.Single());
    }

    [Fact]
    public void Trailing_cells_beyond_the_header_are_still_returned()
    {
        var sheet = new ExcelWorkbookBuilder().AddSheet("Data");
        sheet.WriteHeader("A");
        sheet.AddRow().Text("a").Text("extra");

        var (headers, rows) = WorkbookProbe.Read(sheet.Workbook.ToArray());

        Assert.Single(headers);
        Assert.Equal(new[] { "a", "extra" }, rows.Single());
    }

    [Fact]
    public void Numbers_come_back_as_their_text_representation()
    {
        var sheet = new ExcelWorkbookBuilder().AddSheet("Data");
        sheet.WriteHeader("Count", "Ratio");
        sheet.AddRow().Number(7).Number(0.25d);

        var (_, rows) = WorkbookProbe.Read(sheet.Workbook.ToArray());

        Assert.Equal("7", rows.Single()[0]);
        Assert.Equal("0.25", rows.Single()[1]);
    }

    [Fact]
    public void A_stream_that_has_already_been_read_is_rewound_first()
    {
        var bytes = new ExcelWorkbookBuilder()
            .AddSheet("Data").WriteHeader("Name").Workbook.ToArray();

        using var stream = new MemoryStream(bytes);
        stream.Position = stream.Length;

        var (headers, _) = ExcelSheetReader.Read(stream);

        Assert.Equal(new[] { "Name" }, headers);
    }

    [Fact]
    public void Without_a_preferred_name_the_first_sheet_wins()
    {
        var bytes = ThreeSheetWorkbook();

        var (headers, _) = WorkbookProbe.Read(bytes);

        Assert.Equal(new[] { "Alpha header" }, headers);
    }

    [Fact]
    public void A_preferred_sheet_is_selected_by_name()
    {
        var bytes = ThreeSheetWorkbook();

        var (headers, _) = WorkbookProbe.Read(bytes, "Beta");

        Assert.Equal(new[] { "Beta header" }, headers);
    }

    [Theory]
    [InlineData("beta")]
    [InlineData("BETA")]
    [InlineData("BeTa")]
    public void Sheet_names_are_matched_without_regard_to_case(string requested)
    {
        var bytes = ThreeSheetWorkbook();

        var (headers, _) = WorkbookProbe.Read(bytes, requested);

        Assert.Equal(new[] { "Beta header" }, headers);
    }

    [Fact]
    public void The_first_matching_preferred_name_wins()
    {
        var bytes = ThreeSheetWorkbook();

        var (headers, _) = WorkbookProbe.Read(bytes, "Gamma", "Beta");

        Assert.Equal(new[] { "Gamma header" }, headers);
    }

    [Fact]
    public void Preferred_names_that_do_not_exist_are_passed_over()
    {
        var bytes = ThreeSheetWorkbook();

        var (headers, _) = WorkbookProbe.Read(bytes, "Missing", "Absent", "Beta");

        Assert.Equal(new[] { "Beta header" }, headers);
    }

    [Fact]
    public void When_no_preferred_name_matches_the_first_sheet_is_the_fallback()
    {
        var bytes = ThreeSheetWorkbook();

        var (headers, _) = WorkbookProbe.Read(bytes, "Missing", "Absent");

        Assert.Equal(new[] { "Alpha header" }, headers);
    }

    [Fact]
    public void Shared_strings_are_resolved_to_their_text()
    {
        var bytes = SharedStringWorkbook();

        var (headers, rows) = WorkbookProbe.Read(bytes);

        Assert.Equal(new[] { "Header A", "Header B" }, headers);
        Assert.Equal("Value A", rows.Single()[0]);
    }

    [Fact]
    public void Inline_strings_are_read_as_text()
    {
        var bytes = SharedStringWorkbook();

        var (_, rows) = WorkbookProbe.Read(bytes);

        Assert.Equal("Inline B", rows.Single()[1]);
    }

    [Fact]
    public void A_rich_text_shared_string_is_flattened_to_its_runs()
    {
        var bytes = SharedStringWorkbook();

        var (_, rows) = WorkbookProbe.Read(bytes);

        Assert.Equal("boldplain", rows.Single()[2]);
    }

    [Fact]
    public void A_shared_string_index_that_does_not_exist_reads_as_empty()
    {
        var bytes = SharedStringWorkbook();

        var (_, rows) = WorkbookProbe.Read(bytes);

        Assert.Equal("", rows.Single()[3]);
    }

    [Fact]
    public void A_cell_with_no_value_at_all_reads_as_empty()
    {
        var bytes = SharedStringWorkbook();

        var (_, rows) = WorkbookProbe.Read(bytes);

        Assert.Equal("", rows.Single()[4]);
    }

    [Fact]
    public void Column_maps_are_built_from_header_names()
    {
        var map = ExcelSheetReader.BuildColumnMap(new[] { "Name", "Level", "Value" });

        Assert.Equal(0, map["Name"]);
        Assert.Equal(1, map["Level"]);
        Assert.Equal(2, map["Value"]);
    }

    [Fact]
    public void Column_maps_ignore_case()
    {
        var map = ExcelSheetReader.BuildColumnMap(new[] { "Employee Name" });

        Assert.Equal(0, map["EMPLOYEE NAME"]);
        Assert.Equal(0, map["employee name"]);
    }

    [Fact]
    public void Column_maps_trim_surrounding_whitespace_from_headers()
    {
        var map = ExcelSheetReader.BuildColumnMap(new[] { "  Name  ", "\tLevel\t" });

        Assert.Equal(0, map["Name"]);
        Assert.Equal(1, map["Level"]);
    }

    [Fact]
    public void A_repeated_header_resolves_to_its_last_column()
    {
        var map = ExcelSheetReader.BuildColumnMap(new[] { "Name", "Level", "Name" });

        Assert.Equal(2, map["Name"]);
        Assert.Equal(2, map.Count);
    }

    [Fact]
    public void An_empty_header_row_produces_an_empty_map()
    {
        Assert.Empty(ExcelSheetReader.BuildColumnMap(Array.Empty<string>()));
    }

    [Fact]
    public void A_cell_is_looked_up_through_the_column_map()
    {
        var map = ExcelSheetReader.BuildColumnMap(new[] { "Name", "Level" });
        var row = new[] { "Ada", "Senior" };

        Assert.Equal("Ada", ExcelSheetReader.GetCell(row, map, "Name"));
        Assert.Equal("Senior", ExcelSheetReader.GetCell(row, map, "Level"));
    }

    [Fact]
    public void Looking_up_a_cell_ignores_case()
    {
        var map = ExcelSheetReader.BuildColumnMap(new[] { "Name" });

        Assert.Equal("Ada", ExcelSheetReader.GetCell(new[] { "Ada" }, map, "NAME"));
    }

    [Fact]
    public void A_header_that_is_not_in_the_map_reads_as_null()
    {
        var map = ExcelSheetReader.BuildColumnMap(new[] { "Name" });

        Assert.Null(ExcelSheetReader.GetCell(new[] { "Ada" }, map, "Missing"));
    }

    [Fact]
    public void A_row_that_stops_short_of_the_column_reads_as_null()
    {
        var map = ExcelSheetReader.BuildColumnMap(new[] { "Name", "Level", "Value" });

        Assert.Null(ExcelSheetReader.GetCell(new[] { "Ada" }, map, "Value"));
    }

    [Fact]
    public void An_empty_cell_reads_as_an_empty_string_not_null()
    {
        var map = ExcelSheetReader.BuildColumnMap(new[] { "Name", "Level" });

        Assert.Equal("", ExcelSheetReader.GetCell(new[] { "Ada", "" }, map, "Level"));
    }

    private static byte[] ThreeSheetWorkbook()
    {
        var builder = new ExcelWorkbookBuilder();
        builder.AddSheet("Alpha").WriteHeader("Alpha header").AddRow().Text("alpha");
        builder.AddSheet("Beta").WriteHeader("Beta header").AddRow().Text("beta");
        builder.AddSheet("Gamma").WriteHeader("Gamma header").AddRow().Text("gamma");
        return builder.ToArray();
    }

    private static byte[] SharedStringWorkbook()
    {
        using var stream = new MemoryStream();

        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var sharedStringPart = workbookPart.AddNewPart<SharedStringTablePart>();
            sharedStringPart.SharedStringTable = new SharedStringTable(
                new SharedStringItem(new Text("Header A")),
                new SharedStringItem(new Text("Header B")),
                new SharedStringItem(new Text("Value A")),
                new SharedStringItem(
                    new Run(new RunProperties(new Bold()), new Text("bold")),
                    new Run(new Text("plain"))));

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            worksheetPart.Worksheet = new Worksheet(new SheetData(
                new Row(SharedCell("A1", 0), SharedCell("B1", 1)) { RowIndex = 1U },
                new Row(
                    SharedCell("A2", 2),
                    InlineCell("B2", "Inline B"),
                    SharedCell("C2", 3),
                    SharedCell("D2", 99),
                    new Cell { CellReference = "E2" }) { RowIndex = 2U }));

            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1U,
                Name = "Data"
            });

            workbookPart.Workbook.Save();
        }

        return stream.ToArray();
    }

    [Fact]
    public void Reading_a_named_sheet_returns_that_sheet()
    {
        var builder = new ExcelWorkbookBuilder();
        builder.AddSheet("Data").WriteHeader("Name");
        builder.AddSheet("Info").WriteHeader("Field", "Value").AddRow().Text("ART").Text("Core");

        using var stream = new MemoryStream(builder.ToArray());
        var sheet = ExcelSheetReader.ReadSheet(stream, "info");

        Assert.NotNull(sheet);
        Assert.Equal(new[] { "Field", "Value" }, sheet.Value.Headers);
        Assert.Equal(new[] { "ART", "Core" }, Assert.Single(sheet.Value.Rows));
    }

    [Fact]
    public void Reading_a_missing_named_sheet_returns_null_instead_of_the_first_sheet()
    {
        var bytes = new ExcelWorkbookBuilder().AddSheet("Data").WriteHeader("Name").Workbook.ToArray();

        using var stream = new MemoryStream(bytes);

        Assert.Null(ExcelSheetReader.ReadSheet(stream, "Info"));
    }

    private static Cell SharedCell(string reference, int index) => new()
    {
        CellReference = reference,
        DataType = CellValues.SharedString,
        CellValue = new CellValue(index.ToString())
    };

    private static Cell InlineCell(string reference, string text) => new()
    {
        CellReference = reference,
        DataType = CellValues.InlineString,
        InlineString = new InlineString(new Text(text))
    };
}

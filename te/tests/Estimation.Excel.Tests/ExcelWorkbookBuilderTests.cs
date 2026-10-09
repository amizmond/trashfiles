namespace Estimation.Excel.Tests;

public class ExcelWorkbookBuilderTests
{
    [Fact]
    public void An_empty_workbook_is_still_a_readable_package()
    {
        var bytes = new ExcelWorkbookBuilder().AddSheet("Empty").Workbook.ToArray();

        Assert.NotEmpty(bytes);
        Assert.Equal(new[] { "Empty" }, WorkbookProbe.SheetNames(bytes));
    }

    [Fact]
    public void A_generated_workbook_passes_open_xml_schema_validation()
    {
        var bytes = BuildKitchenSinkWorkbook();

        Assert.Empty(WorkbookProbe.SchemaErrors(bytes));
    }

    [Fact]
    public void Sheets_are_added_in_order_with_ascending_ids()
    {
        var builder = new ExcelWorkbookBuilder();
        builder.AddSheet("Alpha");
        builder.AddSheet("Beta");
        builder.AddSheet("Gamma");

        var bytes = builder.ToArray();

        Assert.Equal(new[] { "Alpha", "Beta", "Gamma" }, WorkbookProbe.SheetNames(bytes));
        Assert.Equal(
            new uint[] { 1, 2, 3 },
            WorkbookProbe.Inspect(bytes, d => d.WorkbookPart!.Workbook!
                .GetFirstChild<Sheets>()!
                .Elements<Sheet>()
                .Select(s => s.SheetId!.Value)
                .ToArray()));
    }

    [Fact]
    public void Each_sheet_keeps_its_own_rows()
    {
        var builder = new ExcelWorkbookBuilder();
        builder.AddSheet("Alpha").WriteHeader("A1 header").AddRow().Text("alpha value");
        builder.AddSheet("Beta").WriteHeader("B1 header").AddRow().Text("beta value");

        var bytes = builder.ToArray();

        var alpha = WorkbookProbe.Read(bytes, "Alpha");
        var beta = WorkbookProbe.Read(bytes, "Beta");

        Assert.Equal(new[] { "A1 header" }, alpha.Headers);
        Assert.Equal("alpha value", alpha.Rows.Single().Single());
        Assert.Equal("beta value", beta.Rows.Single().Single());
    }

    [Fact]
    public void Header_cells_get_the_bold_style()
    {
        var bytes = new ExcelWorkbookBuilder()
            .AddSheet("S").WriteHeader("One", "Two").Workbook.ToArray();

        Assert.Equal(ExcelPrimitives.HeaderStyleIndex, WorkbookProbe.StyleIndexOf(bytes, "S", "A1"));
        Assert.Equal(ExcelPrimitives.HeaderStyleIndex, WorkbookProbe.StyleIndexOf(bytes, "S", "B1"));
    }

    [Fact]
    public void A_coloured_header_uses_the_sticky_header_style_instead()
    {
        var bytes = new ExcelWorkbookBuilder()
            .AddSheet("S").WriteColoredHeader(new[] { "One", "Two" }).Workbook.ToArray();

        Assert.Equal(ExcelPrimitives.HeaderFillStyleIndex, WorkbookProbe.StyleIndexOf(bytes, "S", "A1"));
        Assert.Equal(ExcelPrimitives.HeaderFillStyleIndex, WorkbookProbe.StyleIndexOf(bytes, "S", "B1"));
    }

    [Fact]
    public void Data_rows_are_numbered_from_two_when_a_header_was_written()
    {
        var sheet = new ExcelWorkbookBuilder().AddSheet("S");
        sheet.WriteHeader("H");
        sheet.AddRow().Text("first");
        sheet.AddRow().Text("second");

        var bytes = sheet.Workbook.ToArray();

        Assert.Equal(new[] { "A2" }, WorkbookProbe.CellReferences(bytes, "S", 2));
        Assert.Equal(new[] { "A3" }, WorkbookProbe.CellReferences(bytes, "S", 3));
    }

    [Fact]
    public void Column_widths_are_written_for_single_columns_in_order()
    {
        var bytes = new ExcelWorkbookBuilder()
            .AddSheet("S").SetColumnWidths(25, 20, 10).Workbook.ToArray();

        Assert.Equal(new[] { "1-1:25", "2-2:20", "3-3:10" }, WorkbookProbe.ColumnWidths(bytes, "S"));
    }

    [Fact]
    public void A_column_width_can_span_a_contiguous_range()
    {
        var bytes = new ExcelWorkbookBuilder()
            .AddSheet("S")
            .AddColumnWidth(new ColumnWidth(1, 30))
            .AddColumnWidth(new ColumnWidth(2, 6, 18))
            .Workbook.ToArray();

        Assert.Equal(new[] { "1-1:30", "2-6:18" }, WorkbookProbe.ColumnWidths(bytes, "S"));
    }

    [Fact]
    public void A_single_column_width_covers_only_that_column()
    {
        var width = new ColumnWidth(4, 12.5);

        Assert.Equal(4U, width.Min);
        Assert.Equal(4U, width.Max);
        Assert.Equal(12.5, width.Width);
    }

    [Fact]
    public void A_sheet_without_widths_writes_no_columns_element()
    {
        var bytes = new ExcelWorkbookBuilder().AddSheet("S").WriteHeader("H").Workbook.ToArray();

        Assert.Empty(WorkbookProbe.ColumnWidths(bytes, "S"));
    }

    [Fact]
    public void Null_text_becomes_an_empty_cell_rather_than_a_missing_one()
    {
        var sheet = new ExcelWorkbookBuilder().AddSheet("S");
        sheet.AddRow().Text(null).Text("after");

        var bytes = sheet.Workbook.ToArray();

        Assert.Equal(new[] { "A1", "B1" }, WorkbookProbe.CellReferences(bytes, "S", 1));
        Assert.Equal("", WorkbookProbe.ValueOf(bytes, "S", "A1"));
    }

    [Fact]
    public void Skipping_an_empty_value_leaves_a_gap_but_keeps_later_columns_aligned()
    {
        var sheet = new ExcelWorkbookBuilder().AddSheet("S");
        sheet.AddRow().Text("a").Text("", skipIfEmpty: true).Text("c");

        var bytes = sheet.Workbook.ToArray();

        Assert.Equal(new[] { "A1", "C1" }, WorkbookProbe.CellReferences(bytes, "S", 1));
        Assert.Equal(new[] { "a", "", "c" }, WorkbookProbe.Read(bytes).Headers);
    }

    [Fact]
    public void Skip_advances_the_cursor_without_writing_cells()
    {
        var sheet = new ExcelWorkbookBuilder().AddSheet("S");
        sheet.AddRow().Text("a").Skip(3).Text("e");

        var bytes = sheet.Workbook.ToArray();

        Assert.Equal(new[] { "A1", "E1" }, WorkbookProbe.CellReferences(bytes, "S", 1));
    }

    [Fact]
    public void A_null_number_leaves_the_cell_unwritten_but_keeps_the_row_aligned()
    {
        var sheet = new ExcelWorkbookBuilder().AddSheet("S");
        sheet.AddRow().Text("a").Number((int?)null).Text("c");

        var bytes = sheet.Workbook.ToArray();

        Assert.Equal(new[] { "A1", "C1" }, WorkbookProbe.CellReferences(bytes, "S", 1));
    }

    [Fact]
    public void A_null_date_leaves_the_cell_unwritten_but_keeps_the_row_aligned()
    {
        var sheet = new ExcelWorkbookBuilder().AddSheet("S");
        sheet.AddRow().Text("a").Date(null).Text("c");

        var bytes = sheet.Workbook.ToArray();

        Assert.Equal(new[] { "A1", "C1" }, WorkbookProbe.CellReferences(bytes, "S", 1));
    }

    [Fact]
    public void A_date_is_written_as_a_styled_serial_number()
    {
        var date = new DateTime(2026, 7, 30);
        var sheet = new ExcelWorkbookBuilder().AddSheet("S");
        sheet.AddRow().Date(date);

        var bytes = sheet.Workbook.ToArray();

        Assert.Equal(ExcelPrimitives.DateStyleIndex, WorkbookProbe.StyleIndexOf(bytes, "S", "A1"));
        Assert.Equal(
            date.ToOADate(),
            double.Parse(WorkbookProbe.ValueOf(bytes, "S", "A1")!, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void A_wrapped_cell_uses_the_wrap_style()
    {
        var sheet = new ExcelWorkbookBuilder().AddSheet("S");
        sheet.AddRow().Text("line one\nline two", wrap: true);

        var bytes = sheet.Workbook.ToArray();

        Assert.Equal(ExcelPrimitives.WrapTextStyleIndex, WorkbookProbe.StyleIndexOf(bytes, "S", "A1"));
    }

    [Fact]
    public void A_derived_cell_uses_the_read_only_style()
    {
        var sheet = new ExcelWorkbookBuilder().AddSheet("S");
        sheet.AddRow().Text("computed", derived: true);

        var bytes = sheet.Workbook.ToArray();

        Assert.Equal(ExcelPrimitives.DerivedStyleIndex, WorkbookProbe.StyleIndexOf(bytes, "S", "A1"));
    }

    [Fact]
    public void Wrapping_wins_over_the_derived_style_when_both_are_requested()
    {
        var sheet = new ExcelWorkbookBuilder().AddSheet("S");
        sheet.AddRow().Text("both", derived: true, wrap: true);

        var bytes = sheet.Workbook.ToArray();

        Assert.Equal(ExcelPrimitives.WrapTextStyleIndex, WorkbookProbe.StyleIndexOf(bytes, "S", "A1"));
    }

    [Fact]
    public void A_formula_cell_stores_the_expression_and_its_cached_result()
    {
        var sheet = new ExcelWorkbookBuilder().AddSheet("S");
        sheet.AddRow().Formula("1+1", "2");

        var bytes = sheet.Workbook.ToArray();

        Assert.Equal("1+1", WorkbookProbe.Inspect(bytes, d => WorkbookProbe.Cell(d, "S", "A1")!.CellFormula!.Text));
        Assert.Equal("2", WorkbookProbe.ValueOf(bytes, "S", "A1"));
    }

    [Fact]
    public void Text_with_control_characters_saves_and_reads_back_without_them()
    {
        var sheet = new ExcelWorkbookBuilder().AddSheet("S");
        sheet.WriteHeader("Sum\u000Bmary");
        sheet.AddRow()
            .Text("line one\u000Bline two\u0001")
            .StyledText("\u0001status", ExcelPrimitives.DerivedStyleIndex)
            .Formula("HYPERLINK(\"https://jira/PAY-1\",\"PAY-1\")", "PAY\u000B-1");

        var bytes = sheet.Workbook.ToArray();

        var (headers, rows) = WorkbookProbe.Read(bytes, "S");
        Assert.Equal(new[] { "Summary" }, headers);
        Assert.Equal(new[] { "line oneline two", "status", "PAY-1" }, rows.Single());
        Assert.Empty(WorkbookProbe.SchemaErrors(bytes));
    }

    [Fact]
    public void Emoji_survive_the_control_character_clean_up()
    {
        var sheet = new ExcelWorkbookBuilder().AddSheet("S");
        sheet.AddRow().Text("Ship it \U0001F680\u000B now");

        var bytes = sheet.Workbook.ToArray();

        Assert.Equal("Ship it \U0001F680 now", WorkbookProbe.ValueOf(bytes, "S", "A1"));
    }

    [Fact]
    public void A_sheet_name_with_a_control_character_still_saves()
    {
        var bytes = new ExcelWorkbookBuilder().AddSheet("Team\u0001 A").Workbook.ToArray();

        Assert.Equal(new[] { "Team A" }, WorkbookProbe.SheetNames(bytes));
    }

    [Fact]
    public void A_derived_formula_cell_is_flagged_read_only()
    {
        var sheet = new ExcelWorkbookBuilder().AddSheet("S");
        sheet.AddRow().Formula("A1*2", derived: true);

        var bytes = sheet.Workbook.ToArray();

        Assert.Equal(ExcelPrimitives.DerivedStyleIndex, WorkbookProbe.StyleIndexOf(bytes, "S", "A1"));
    }

    [Fact]
    public void Styled_text_uses_the_supplied_style_index()
    {
        var builder = new ExcelWorkbookBuilder();
        var style = builder.GetOrCreateFillStyle("#FF0000");
        var sheet = builder.AddSheet("S");
        sheet.AddRow().StyledText("painted", style);

        var bytes = builder.ToArray();

        Assert.Equal(style, WorkbookProbe.StyleIndexOf(bytes, "S", "A1"));
        Assert.Equal("FFFF0000", WorkbookProbe.FillColorOf(bytes, style));
    }

    [Fact]
    public void Styled_number_uses_the_supplied_style_index()
    {
        var builder = new ExcelWorkbookBuilder();
        var style = builder.GetOrCreateFillStyle("#FF0000");
        var sheet = builder.AddSheet("S");
        sheet.AddRow().StyledNumber(42, style);

        var bytes = builder.ToArray();

        Assert.Equal(style, WorkbookProbe.StyleIndexOf(bytes, "S", "A1"));
        Assert.Equal("FFFF0000", WorkbookProbe.FillColorOf(bytes, style));
    }

    [Fact]
    public void Styled_number_stays_numeric_so_excel_sorts_it_as_a_number()
    {
        var builder = new ExcelWorkbookBuilder();
        var style = builder.GetOrCreateFillStyle("#FF0000");
        var sheet = builder.AddSheet("S");
        sheet.AddRow().StyledNumber(10, style).StyledNumber(3, style);

        var bytes = builder.ToArray();

        Assert.Equal("10", WorkbookProbe.ValueOf(bytes, "S", "A1"));
        Assert.Equal("3", WorkbookProbe.ValueOf(bytes, "S", "B1"));
        Assert.Equal(CellValues.Number, WorkbookProbe.Inspect(bytes, d => WorkbookProbe.Cell(d, "S", "A1")!.DataType?.Value));
    }

    [Fact]
    public void A_null_styled_number_still_paints_the_cell_and_keeps_the_row_aligned()
    {
        var builder = new ExcelWorkbookBuilder();
        var style = builder.GetOrCreateFillStyle("#FF0000");
        var sheet = builder.AddSheet("S");
        sheet.AddRow().Text("a").StyledNumber(null, style).Text("c");

        var bytes = builder.ToArray();

        Assert.Equal(new[] { "A1", "B1", "C1" }, WorkbookProbe.CellReferences(bytes, "S", 1));
        Assert.Equal(style, WorkbookProbe.StyleIndexOf(bytes, "S", "B1"));
        Assert.Equal("", WorkbookProbe.ValueOf(bytes, "S", "B1"));
    }

    [Fact]
    public void Styled_numbers_reuse_one_style_per_colour()
    {
        var builder = new ExcelWorkbookBuilder();
        var red = builder.GetOrCreateFillStyle("#FF0000");
        var green = builder.GetOrCreateFillStyle("#00FF00");
        var sheet = builder.AddSheet("S");
        sheet.AddRow().StyledNumber(1, red).StyledNumber(2, green).StyledNumber(3, red);

        var bytes = builder.ToArray();

        Assert.Equal(red, WorkbookProbe.StyleIndexOf(bytes, "S", "A1"));
        Assert.Equal(green, WorkbookProbe.StyleIndexOf(bytes, "S", "B1"));
        Assert.Equal(red, WorkbookProbe.StyleIndexOf(bytes, "S", "C1"));
        Assert.Equal((int)ExcelPrimitives.FirstDynamicFillId + 2, WorkbookProbe.FillCount(bytes));
    }

    [Fact]
    public void A_workbook_with_styled_numbers_is_schema_valid()
    {
        var builder = new ExcelWorkbookBuilder();
        var style = builder.GetOrCreateFillStyle("#FF0000");
        var sheet = builder.AddSheet("S");
        sheet.WriteColoredHeader(new[] { "Points" }).FreezeTopRow();
        sheet.AddRow().StyledNumber(7, style);
        sheet.AddRow().StyledNumber(null, style);

        Assert.Empty(WorkbookProbe.SchemaErrors(builder.ToArray()));
    }

    [Fact]
    public void The_first_dynamic_style_starts_after_the_built_in_formats()
    {
        var builder = new ExcelWorkbookBuilder();

        Assert.Equal(ExcelPrimitives.FirstDynamicStyleIndex, builder.GetOrCreateFillStyle("#FF0000"));
    }

    [Fact]
    public void The_same_fill_colour_reuses_one_style()
    {
        var builder = new ExcelWorkbookBuilder();

        var first = builder.GetOrCreateFillStyle("#FF0000");
        var second = builder.GetOrCreateFillStyle("#FF0000");

        Assert.Equal(first, second);
        Assert.Equal((int)ExcelPrimitives.FirstDynamicFillId + 1, WorkbookProbe.FillCount(Finish(builder)));
    }

    [Theory]
    [InlineData("#FF0000")]
    [InlineData("FF0000")]
    [InlineData("ff0000")]
    [InlineData("FFFF0000")]
    public void Equivalent_colour_notations_collapse_onto_one_fill_style(string notation)
    {
        var builder = new ExcelWorkbookBuilder();

        var canonical = builder.GetOrCreateFillStyle("#FF0000");
        var alternative = builder.GetOrCreateFillStyle(notation);

        Assert.Equal(canonical, alternative);
    }

    [Fact]
    public void Different_fill_colours_get_different_styles()
    {
        var builder = new ExcelWorkbookBuilder();

        var red = builder.GetOrCreateFillStyle("#FF0000");
        var green = builder.GetOrCreateFillStyle("#00FF00");

        var bytes = Finish(builder);

        Assert.NotEqual(red, green);
        Assert.Equal("FFFF0000", WorkbookProbe.FillColorOf(bytes, red));
        Assert.Equal("FF00FF00", WorkbookProbe.FillColorOf(bytes, green));
    }

    [Fact]
    public void The_same_font_colour_reuses_one_style()
    {
        var builder = new ExcelWorkbookBuilder();

        var first = builder.GetOrCreateFontColorStyle("#9E9E9E");
        var second = builder.GetOrCreateFontColorStyle("9e9e9e");

        Assert.Equal(first, second);
        Assert.Equal((int)ExcelPrimitives.FirstDynamicFontId + 1, WorkbookProbe.FontCount(Finish(builder)));
    }

    [Fact]
    public void A_font_colour_style_paints_the_text_not_the_background()
    {
        var builder = new ExcelWorkbookBuilder();
        var style = builder.GetOrCreateFontColorStyle("#9E9E9E");

        var bytes = Finish(builder);

        Assert.Equal("FF9E9E9E", WorkbookProbe.FontColorOf(bytes, style));
    }

    [Fact]
    public void Fill_and_font_styles_of_the_same_colour_stay_separate()
    {
        var builder = new ExcelWorkbookBuilder();

        var fill = builder.GetOrCreateFillStyle("#123456");
        var font = builder.GetOrCreateFontColorStyle("#123456");

        Assert.NotEqual(fill, font);
    }

    [Fact]
    public void Fills_and_fonts_draw_from_one_shared_style_counter()
    {
        var builder = new ExcelWorkbookBuilder();

        var first = builder.GetOrCreateFillStyle("#111111");
        var second = builder.GetOrCreateFontColorStyle("#222222");
        var third = builder.GetOrCreateFillStyle("#333333");

        Assert.Equal(ExcelPrimitives.FirstDynamicStyleIndex, first);
        Assert.Equal(ExcelPrimitives.FirstDynamicStyleIndex + 1, second);
        Assert.Equal(ExcelPrimitives.FirstDynamicStyleIndex + 2, third);
    }

    [Fact]
    public void Runtime_registered_styles_survive_into_the_saved_stylesheet()
    {
        var builder = new ExcelWorkbookBuilder();
        var sheet = builder.AddSheet("S");
        var style = builder.GetOrCreateFillStyle("#ABCDEF");
        sheet.AddRow().StyledText("x", style);

        var bytes = builder.ToArray();

        Assert.Equal("FFABCDEF", WorkbookProbe.FillColorOf(bytes, style));
        Assert.Empty(WorkbookProbe.SchemaErrors(bytes));
    }

    [Fact]
    public void An_auto_filter_spans_the_header_and_data_range()
    {
        var bytes = new ExcelWorkbookBuilder()
            .AddSheet("S").SetAutoFilter(columnCount: 4, dataRowCount: 9).Workbook.ToArray();

        Assert.Equal("A1:D10", WorkbookProbe.AutoFilterReference(bytes, "S"));
    }

    [Fact]
    public void An_auto_filter_over_no_data_still_covers_the_header_row()
    {
        var bytes = new ExcelWorkbookBuilder()
            .AddSheet("S").SetAutoFilter(columnCount: 3, dataRowCount: 0).Workbook.ToArray();

        Assert.Equal("A1:C1", WorkbookProbe.AutoFilterReference(bytes, "S"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void An_auto_filter_over_no_columns_is_skipped(int columnCount)
    {
        var bytes = new ExcelWorkbookBuilder()
            .AddSheet("S").SetAutoFilter(columnCount, dataRowCount: 5).Workbook.ToArray();

        Assert.Null(WorkbookProbe.AutoFilterReference(bytes, "S"));
    }

    [Fact]
    public void Freezing_the_top_row_adds_a_frozen_pane_below_the_header()
    {
        var bytes = new ExcelWorkbookBuilder()
            .AddSheet("S").WriteColoredHeader(new[] { "H" }).FreezeTopRow().Workbook.ToArray();

        Assert.True(WorkbookProbe.HasFrozenTopRow(bytes, "S"));
    }

    [Fact]
    public void A_sheet_that_is_not_frozen_has_no_sheet_views()
    {
        var bytes = new ExcelWorkbookBuilder().AddSheet("S").WriteHeader("H").Workbook.ToArray();

        Assert.False(WorkbookProbe.HasFrozenTopRow(bytes, "S"));
    }

    [Fact]
    public void Worksheet_children_are_written_in_the_order_open_xml_requires()
    {
        var sheet = new ExcelWorkbookBuilder()
            .AddSheet("S")
            .SetColumnWidths(10, 10)
            .FreezeTopRow()
            .WriteHeader("A", "B")
            .SetAutoFilter(2, 1);
        sheet.AddRow().Text("x").Text("y");
        sheet.AddListValidation(0, 2, 10, new[] { "x" }, "t", "e");

        var bytes = sheet.Workbook.ToArray();

        Assert.Equal(
            new[] { "sheetViews", "cols", "sheetData", "autoFilter", "dataValidations" },
            WorkbookProbe.WorksheetChildOrder(bytes, "S"));
    }

    [Fact]
    public void An_inline_list_validation_quotes_and_escapes_its_values()
    {
        var sheet = new ExcelWorkbookBuilder().AddSheet("S");
        sheet.AddListValidation(1, 2, 100, new[] { "Alpha", "Say \"hi\"" }, "Invalid", "Pick one");

        var validation = Assert.Single(WorkbookProbe.Validations(sheet.Workbook.ToArray(), "S"));

        Assert.Equal(DataValidationValues.List, validation.Type);
        Assert.Equal("B2:B100", validation.Range);
        Assert.Equal("\"Alpha,Say \"\"hi\"\"\"", validation.Formula);
        Assert.Equal("Invalid", validation.ErrorTitle);
        Assert.Equal("Pick one", validation.Error);
        Assert.True(validation.ShowErrorMessage);
    }

    [Fact]
    public void An_inline_list_value_with_a_control_character_still_saves()
    {
        var sheet = new ExcelWorkbookBuilder().AddSheet("S");
        sheet.AddListValidation(0, 2, 10, new[] { "In\u000BProgress", "Done" }, "Invalid", "Pick one");

        var validation = Assert.Single(WorkbookProbe.Validations(sheet.Workbook.ToArray(), "S"));

        Assert.Equal("\"InProgress,Done\"", validation.Formula);
    }

    [Fact]
    public void An_oversized_inline_list_is_skipped_when_the_caller_asks_for_it()
    {
        var values = Enumerable.Range(0, 40).Select(i => $"value-number-{i:D3}").ToList();
        var sheet = new ExcelWorkbookBuilder().AddSheet("S");

        sheet.AddListValidation(0, 2, 100, values, "Invalid", "Pick one", skipIfFormulaExceeds255: true);

        Assert.True(string.Join(",", values).Length > 255);
        Assert.Empty(WorkbookProbe.Validations(sheet.Workbook.ToArray(), "S"));
    }

    [Fact]
    public void An_oversized_inline_list_is_kept_when_the_caller_does_not_ask_to_skip()
    {
        var values = Enumerable.Range(0, 40).Select(i => $"value-number-{i:D3}").ToList();
        var sheet = new ExcelWorkbookBuilder().AddSheet("S");

        sheet.AddListValidation(0, 2, 100, values, "Invalid", "Pick one");

        Assert.Single(WorkbookProbe.Validations(sheet.Workbook.ToArray(), "S"));
    }

    [Fact]
    public void A_list_that_exactly_fits_the_limit_is_kept()
    {
        var values = new[] { new string('x', 255) };
        var sheet = new ExcelWorkbookBuilder().AddSheet("S");

        sheet.AddListValidation(0, 2, 100, values, "Invalid", "Pick one", skipIfFormulaExceeds255: true);

        Assert.Single(WorkbookProbe.Validations(sheet.Workbook.ToArray(), "S"));
    }

    [Fact]
    public void A_named_list_validation_references_the_defined_name()
    {
        var builder = new ExcelWorkbookBuilder();
        builder.AddDefinedName("StatusList", "Lookups!$A$1:$A$20");
        var sheet = builder.AddSheet("S");
        sheet.AddNamedListValidation(2, 2, 500, "StatusList", "Invalid", "Pick a status");

        var validation = Assert.Single(WorkbookProbe.Validations(builder.ToArray(), "S"));

        Assert.Equal(DataValidationValues.List, validation.Type);
        Assert.Equal("C2:C500", validation.Range);
        Assert.Equal("StatusList", validation.Formula);
        Assert.True(validation.ShowErrorMessage);
    }

    [Fact]
    public void A_non_blocking_named_list_only_suggests_values()
    {
        var builder = new ExcelWorkbookBuilder();
        var sheet = builder.AddSheet("S");
        sheet.AddNamedListValidation(0, 2, 500, "StatusList", "Invalid", "Pick a status", blocking: false);

        var validation = Assert.Single(WorkbookProbe.Validations(builder.ToArray(), "S"));

        Assert.False(validation.ShowErrorMessage);
    }

    [Fact]
    public void A_whole_number_validation_rejects_negatives()
    {
        var sheet = new ExcelWorkbookBuilder().AddSheet("S");
        sheet.AddWholeNumberValidation(3, 2, 200, "Invalid", "Whole numbers only");

        var validation = Assert.Single(WorkbookProbe.Validations(sheet.Workbook.ToArray(), "S"));

        Assert.Equal(DataValidationValues.Whole, validation.Type);
        Assert.Equal(DataValidationOperatorValues.GreaterThanOrEqual, validation.Operator);
        Assert.Equal("D2:D200", validation.Range);
        Assert.Equal("0", validation.Formula);
    }

    [Fact]
    public void A_whole_number_validation_can_start_at_a_higher_minimum()
    {
        var sheet = new ExcelWorkbookBuilder().AddSheet("S");
        sheet.AddWholeNumberValidation(0, 2, 10, "Invalid", "From one", minimum: 1);

        var validation = Assert.Single(WorkbookProbe.Validations(sheet.Workbook.ToArray(), "S"));

        Assert.Equal("A2:A10", validation.Range);
        Assert.Equal("1", validation.Formula);
    }

    [Fact]
    public void The_validation_count_matches_the_number_of_validations()
    {
        var sheet = new ExcelWorkbookBuilder().AddSheet("S");
        sheet.AddListValidation(0, 2, 100, new[] { "a" }, "t", "e");
        sheet.AddWholeNumberValidation(1, 2, 100, "t", "e");
        sheet.AddNamedListValidation(2, 2, 100, "Names", "t", "e");

        var bytes = sheet.Workbook.ToArray();

        Assert.Equal(3U, WorkbookProbe.ValidationCount(bytes, "S"));
        Assert.Equal(3, WorkbookProbe.Validations(bytes, "S").Count);
    }

    [Fact]
    public void A_sheet_without_validations_writes_no_validations_element()
    {
        var bytes = new ExcelWorkbookBuilder().AddSheet("S").WriteHeader("H").Workbook.ToArray();

        Assert.Null(WorkbookProbe.ValidationCount(bytes, "S"));
    }

    [Fact]
    public void Validations_are_scoped_to_the_sheet_that_declared_them()
    {
        var builder = new ExcelWorkbookBuilder();
        builder.AddSheet("WithRules").AddListValidation(0, 2, 10, new[] { "a" }, "t", "e");
        builder.AddSheet("WithoutRules").WriteHeader("H");

        var bytes = builder.ToArray();

        Assert.Single(WorkbookProbe.Validations(bytes, "WithRules"));
        Assert.Empty(WorkbookProbe.Validations(bytes, "WithoutRules"));
    }

    [Fact]
    public void Defined_names_are_registered_on_the_workbook()
    {
        var builder = new ExcelWorkbookBuilder();
        builder.AddDefinedName("StatusList", "Lookups!$A$1:$A$20");
        builder.AddDefinedName("TeamList", "Lookups!$B$1:$B$50");
        builder.AddSheet("S").WriteHeader("H");

        Assert.Equal(
            new[] { "StatusList=Lookups!$A$1:$A$20", "TeamList=Lookups!$B$1:$B$50" },
            WorkbookProbe.DefinedNames(builder.ToArray()));
    }

    [Fact]
    public void A_workbook_without_defined_names_declares_none()
    {
        var bytes = new ExcelWorkbookBuilder().AddSheet("S").WriteHeader("H").Workbook.ToArray();

        Assert.Empty(WorkbookProbe.DefinedNames(bytes));
    }

    [Fact]
    public void Adding_a_defined_name_returns_the_builder_for_chaining()
    {
        var builder = new ExcelWorkbookBuilder();

        Assert.Same(builder, builder.AddDefinedName("A", "Sheet1!$A$1"));
    }

    [Fact]
    public void A_sheet_builder_exposes_the_workbook_it_belongs_to()
    {
        var builder = new ExcelWorkbookBuilder();

        Assert.Same(builder, builder.AddSheet("S").Workbook);
    }

    [Fact]
    public void Header_and_data_survive_a_full_round_trip()
    {
        var sheet = new ExcelWorkbookBuilder().AddSheet("Round Trip");
        sheet.WriteHeader("Name", "Count", "Note");
        sheet.AddRow().Text("first").Number(1).Text("note one");
        sheet.AddRow().Text("second").Number(2).Text("note two");

        var (headers, rows) = WorkbookProbe.Read(sheet.Workbook.ToArray());

        Assert.Equal(new[] { "Name", "Count", "Note" }, headers);
        Assert.Equal(new[] { "first", "1", "note one" }, rows[0]);
        Assert.Equal(new[] { "second", "2", "note two" }, rows[1]);
    }

    private static byte[] Finish(ExcelWorkbookBuilder builder)
    {
        builder.AddSheet("S").WriteHeader("H");
        return builder.ToArray();
    }

    private static byte[] BuildKitchenSinkWorkbook()
    {
        var builder = new ExcelWorkbookBuilder();
        builder.AddDefinedName("StatusList", "Lookups!$A$1:$A$3");

        var fill = builder.GetOrCreateFillStyle("#FFCC00");
        var font = builder.GetOrCreateFontColorStyle("#9E9E9E");

        var lookups = builder.AddSheet("Lookups");
        lookups.WriteHeader("Status");
        lookups.AddRow().Text("Open");
        lookups.AddRow().Text("Closed");

        var data = builder.AddSheet("Data");
        data.SetColumnWidths(30, 12, 12, 18, 40);
        data.FreezeTopRow();
        data.WriteColoredHeader(new[] { "Name", "Count", "Ratio", "Due", "Notes" });
        data.AddRow()
            .Text("first")
            .Number(3)
            .Number(0.5d)
            .Date(new DateTime(2026, 7, 30))
            .Text("line one\nline two", wrap: true);
        data.AddRow()
            .StyledText("painted", fill)
            .Number((int?)null)
            .Formula("B2*2", "6", derived: true)
            .Date(null)
            .StyledText("greyed", font);
        data.SetAutoFilter(5, 2);
        data.AddListValidation(1, 2, 1000, new[] { "1", "2" }, "Invalid", "Pick one");
        data.AddNamedListValidation(0, 2, 1000, "StatusList", "Invalid", "Pick a status", blocking: false);
        data.AddWholeNumberValidation(2, 2, 1000, "Invalid", "Whole numbers only");

        return builder.ToArray();
    }
}

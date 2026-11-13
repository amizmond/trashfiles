using Estimation.HrReporting.Models;
using Estimation.HrReporting.Services;
using Estimation.HrReporting.Tests.Infrastructure;
using Xunit;

namespace Estimation.HrReporting.Tests;

public class HeadcountFileParserTests
{
    [Fact]
    public void Parses_every_column_with_its_type()
    {
        var workbook = TestWorkbook.WithAllColumns()
            .AddPerson("G0012345", v =>
            {
                v["Employee Name"] = "Anna Smith";
                v["Operational Status"] = "Operational";
                v["Employee Position ID"] = "P-000123";
                v["Length of Service Date"] = new DateTime(2019, 3, 18);
                v["Org Unit ID"] = 50012345;
                v["Cost Centre"] = "0042";
                v["Length of Service In Years"] = 7;
                v["ControlSpan"] = "12";
                v["HC"] = 1;
                v["FTE"] = 0.8;
                v["Vendor"] = "-";
                v["Actions"] = "Move to Payments domain";
            });

        var result = HeadcountFileParser.Parse(workbook.ToStream(), "headcount.xlsx");

        Assert.True(result.CanSave, string.Join(Environment.NewLine, result.Errors));
        var row = Assert.Single(result.Rows);
        Assert.Equal(2, row.RowNumber);
        Assert.Equal("G0012345", row.Brid);
        Assert.Equal("Anna Smith", row.EmployeeName);
        Assert.Equal("Operational", row.OperationalStatus);
        Assert.Equal("P-000123", row.EmployeePositionId);
        Assert.Equal(new DateTime(2019, 3, 18), row.LengthOfServiceDate);
        Assert.Equal("50012345", row.OrgUnitId);
        Assert.Equal("0042", row.CostCentre);
        Assert.Equal(7m, row.LengthOfServiceInYears);
        Assert.Equal("12", row.ControlSpan);
        Assert.Equal(1m, row.Hc);
        Assert.Equal(0.8m, row.Fte);
        Assert.Equal("-", row.Vendor);
        Assert.Equal("Move to Payments domain", row.Actions);
        Assert.Equal("Argon", row.Team);
        Assert.Null(row.Building);
        Assert.Empty(result.IgnoredHeaders);
        Assert.Equal(1, result.HeaderRowNumber);
    }

    [Fact]
    public void Finds_the_header_below_title_rows()
    {
        var workbook = TestWorkbook.WithAllColumns().AddPerson("A1").AddPerson("A2");
        workbook.TitleRows.Add(["Workday headcount report"]);
        workbook.TitleRows.Add([]);
        workbook.TitleRows.Add(["Run on 2026-11-12"]);

        var result = HeadcountFileParser.Parse(workbook.ToStream(), "headcount.xlsx");

        Assert.True(result.CanSave, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal(4, result.HeaderRowNumber);
        Assert.Equal([5, 6], result.Rows.Select(r => r.RowNumber));
    }

    [Fact]
    public void Matches_headers_regardless_of_case_and_spacing()
    {
        var headers = HeadcountColumns.All.Select(c => c.Header).ToArray();
        headers[0] = " brid ";
        headers[Array.IndexOf(headers, "Colleague / Non-Perm Type")] = "Colleague/Non-Perm Type";
        headers[Array.IndexOf(headers, "ART / NonART")] = "art /nonart";
        var workbook = TestWorkbook.WithHeaders(headers);
        workbook.AddRawRow(["B1", "Name"]);

        var result = HeadcountFileParser.Parse(workbook.ToStream(), "headcount.xlsx");

        Assert.True(result.CanSave, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal("B1", Assert.Single(result.Rows).Brid);
    }

    [Fact]
    public void Ignores_extra_columns_and_reports_them()
    {
        var headers = HeadcountColumns.All.Select(c => c.Header).Append("Manager Comment").ToArray();
        var workbook = TestWorkbook.WithHeaders(headers);
        workbook.AddRawRow(["B1"]);

        var result = HeadcountFileParser.Parse(workbook.ToStream(), "headcount.xlsx");

        Assert.True(result.CanSave);
        Assert.Equal(["Manager Comment"], result.IgnoredHeaders);
    }

    [Fact]
    public void Blocks_a_file_without_the_Headcount_sheet()
    {
        var workbook = TestWorkbook.WithAllColumns().AddPerson("B1");
        workbook.SheetName = "Sheet1";

        var result = HeadcountFileParser.Parse(workbook.ToStream(), "headcount.xlsx");

        Assert.False(result.CanSave);
        var error = Assert.Single(result.Errors);
        Assert.Contains("no sheet named 'Headcount'", error);
        Assert.Contains("Notes, Sheet1", error);
    }

    [Fact]
    public void Blocks_a_file_with_missing_columns_and_names_them()
    {
        var headers = HeadcountColumns.All.Select(c => c.Header)
            .Where(h => h is not "Vendor" and not "Target Level")
            .ToArray();
        var workbook = TestWorkbook.WithHeaders(headers);
        workbook.AddRawRow(["B1"]);

        var result = HeadcountFileParser.Parse(workbook.ToStream(), "headcount.xlsx");

        Assert.False(result.CanSave);
        Assert.Empty(result.Rows);
        var error = Assert.Single(result.Errors);
        Assert.Contains("2 columns are missing", error);
        Assert.Contains("Vendor", error);
        Assert.Contains("Target Level", error);
    }

    [Fact]
    public void Blocks_a_column_that_appears_twice()
    {
        var headers = HeadcountColumns.All.Select(c => c.Header).Append("City").ToArray();
        var workbook = TestWorkbook.WithHeaders(headers);
        workbook.AddRawRow(["B1"]);

        var result = HeadcountFileParser.Parse(workbook.ToStream(), "headcount.xlsx");

        Assert.False(result.CanSave);
        Assert.Contains(result.Errors, e => e.Contains("'City' appears more than once"));
    }

    [Fact]
    public void Blocks_a_sheet_without_a_header_row()
    {
        var workbook = TestWorkbook.WithHeaders("Name", "Team");
        workbook.AddRawRow(["Anna", "Argon"]);

        var result = HeadcountFileParser.Parse(workbook.ToStream(), "headcount.xlsx");

        Assert.False(result.CanSave);
        Assert.Contains("No header row with a 'BRID' column", Assert.Single(result.Errors));
    }

    [Fact]
    public void Blocks_rows_without_a_BRID()
    {
        var workbook = TestWorkbook.WithAllColumns()
            .AddPerson("B1")
            .AddPerson("", v => v["BRID"] = null)
            .AddPerson("B3");

        var result = HeadcountFileParser.Parse(workbook.ToStream(), "headcount.xlsx");

        Assert.False(result.CanSave);
        Assert.Equal(["Row 3: BRID is empty."], result.Errors);
        Assert.Equal(["B1", "B3"], result.Rows.Select(r => r.Brid));
    }

    [Fact]
    public void Blocks_the_same_BRID_on_two_rows_ignoring_case()
    {
        var workbook = TestWorkbook.WithAllColumns()
            .AddPerson("B1")
            .AddPerson("B2")
            .AddPerson("b1");

        var result = HeadcountFileParser.Parse(workbook.ToStream(), "headcount.xlsx");

        Assert.False(result.CanSave);
        Assert.Equal(["BRID B1 is on more than one row: rows 2, 4."], result.Errors);
    }

    [Fact]
    public void Skips_blank_rows()
    {
        var workbook = TestWorkbook.WithAllColumns().AddPerson("B1");
        workbook.AddRawRow(["  ", null, ""]);
        workbook.AddPerson("B2");

        var result = HeadcountFileParser.Parse(workbook.ToStream(), "headcount.xlsx");

        Assert.True(result.CanSave, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal(["B1", "B2"], result.Rows.Select(r => r.Brid));
    }

    [Fact]
    public void Blocks_a_header_without_data_rows()
    {
        var result = HeadcountFileParser.Parse(TestWorkbook.WithAllColumns().ToStream(), "headcount.xlsx");

        Assert.False(result.CanSave);
        Assert.Contains("has no data rows", Assert.Single(result.Errors));
    }

    [Theory]
    [InlineData("15/03/2021")]
    [InlineData("2021-03-15")]
    [InlineData("15-Mar-2021")]
    [InlineData("44270")]
    public void Reads_dates_written_as_text(string text)
    {
        var workbook = TestWorkbook.WithAllColumns().AddPerson("B1", v => v["Length of Service Date"] = text);

        var result = HeadcountFileParser.Parse(workbook.ToStream(), "headcount.xlsx");

        Assert.True(result.CanSave, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal(new DateTime(2021, 3, 15), Assert.Single(result.Rows).LengthOfServiceDate);
    }

    [Fact]
    public void Treats_a_dash_as_empty_in_number_and_date_columns()
    {
        var workbook = TestWorkbook.WithAllColumns().AddPerson("B1", v =>
        {
            v["Length of Service Date"] = "-";
            v["Length of Service In Years"] = " - ";
            v["FTE"] = "0.5";
        });

        var result = HeadcountFileParser.Parse(workbook.ToStream(), "headcount.xlsx");

        Assert.True(result.CanSave, string.Join(Environment.NewLine, result.Errors));
        var row = Assert.Single(result.Rows);
        Assert.Null(row.LengthOfServiceDate);
        Assert.Null(row.LengthOfServiceInYears);
        Assert.Equal(0.5m, row.Fte);
    }

    [Fact]
    public void Blocks_values_that_are_not_numbers_or_dates()
    {
        var workbook = TestWorkbook.WithAllColumns().AddPerson("B1", v =>
        {
            v["FTE"] = "full";
            v["Length of Service Date"] = "31/02/2020";
            v["HC"] = new ErrorCell("#N/A");
        });

        var result = HeadcountFileParser.Parse(workbook.ToStream(), "headcount.xlsx");

        Assert.False(result.CanSave);
        Assert.Contains("Row 2, FTE: 'full' is not a number.", result.Errors);
        Assert.Contains("Row 2, Length of Service Date: '31/02/2020' is not a date.", result.Errors);
        Assert.Contains("Row 2, HC: '#N/A' is not a number.", result.Errors);
    }

    [Fact]
    public void Keeps_formula_errors_as_text_in_text_columns()
    {
        var workbook = TestWorkbook.WithAllColumns().AddPerson("B1", v => v["Team"] = new ErrorCell("#N/A"));

        var result = HeadcountFileParser.Parse(workbook.ToStream(), "headcount.xlsx");

        Assert.True(result.CanSave);
        Assert.Equal("#N/A", Assert.Single(result.Rows).Team);
    }

    [Fact]
    public void Blocks_text_longer_than_its_column()
    {
        var workbook = TestWorkbook.WithAllColumns().AddPerson("B1", v => v["Country"] = new string('x', 101));

        var result = HeadcountFileParser.Parse(workbook.ToStream(), "headcount.xlsx");

        Assert.False(result.CanSave);
        Assert.Equal(["Row 2, Country: the value is 101 characters long; the limit is 100."], result.Errors);
    }

    [Fact]
    public void Reads_inline_and_rich_text_cells_and_trims_them()
    {
        var workbook = TestWorkbook.WithAllColumns().AddPerson("B1", v =>
        {
            v["Employee Name"] = new InlineText("  Anna Smith ");
            v["Job"] = new RichText("Software ", "Engineer");
        });

        var result = HeadcountFileParser.Parse(workbook.ToStream(), "headcount.xlsx");

        Assert.True(result.CanSave, string.Join(Environment.NewLine, result.Errors));
        var row = Assert.Single(result.Rows);
        Assert.Equal("Anna Smith", row.EmployeeName);
        Assert.Equal("Software Engineer", row.Job);
    }

    [Fact]
    public void Lists_at_most_200_problems_but_counts_them_all()
    {
        var workbook = TestWorkbook.WithAllColumns();
        for (var i = 0; i < 250; i++)
        {
            workbook.AddPerson($"B{i}", v => v["FTE"] = "x");
        }

        var result = HeadcountFileParser.Parse(workbook.ToStream(), "headcount.xlsx");

        Assert.Equal(250, result.ErrorCount);
        Assert.Equal(HeadcountParseResult.MaxListedErrors, result.Errors.Count);
    }

    [Fact]
    public void Blocks_a_file_that_is_not_a_workbook()
    {
        using var stream = new MemoryStream("BRID,Employee Name\nB1,Anna"u8.ToArray());

        var result = HeadcountFileParser.Parse(stream, "headcount.csv");

        Assert.False(result.CanSave);
        Assert.Equal(["The file could not be opened as an Excel workbook (.xlsx)."], result.Errors);
    }
}

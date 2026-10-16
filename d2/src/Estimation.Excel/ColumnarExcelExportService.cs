namespace Estimation.Excel;

public class ColumnarExportColumn
{
    public string Header { get; set; } = "";
    public double Width { get; set; } = 18;
    public List<string>? DropdownValues { get; set; }
}

public static class ColumnarExcelExportService
{
    public static byte[] GenerateColumnarExport(
        string sheetName,
        List<ColumnarExportColumn> columns,
        List<List<string?>> rows)
    {
        var workbook = new ExcelWorkbookBuilder();
        var sheet = workbook.AddSheet(sheetName);

        for (var i = 0; i < columns.Count; i++)
        {
            sheet.AddColumnWidth(new ColumnWidth((uint)(i + 1), columns[i].Width));
        }

        sheet.WriteHeader(columns.Select(c => c.Header));

        foreach (var row in rows)
        {
            var dataRow = sheet.AddRow();
            for (var i = 0; i < columns.Count && i < row.Count; i++)
            {
                dataRow.Text(row[i], skipIfEmpty: true);
            }
        }

        var lastDataRow = Math.Max(rows.Count + 2, 1000);
        for (var i = 0; i < columns.Count; i++)
        {
            var values = columns[i].DropdownValues;
            if (values is null || values.Count == 0)
            {
                continue;
            }

            sheet.AddListValidation(
                columnIndex: i,
                firstDataRow: 2,
                lastDataRow: lastDataRow,
                values: values,
                errorTitle: "Invalid value",
                error: $"Please select a valid value for {columns[i].Header}.",
                skipIfFormulaExceeds255: true);
        }

        return workbook.ToArray();
    }
}

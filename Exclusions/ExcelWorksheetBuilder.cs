using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace DynamicExcel.Exclusions;
public class ExcelWorksheetBuilder : IExcelWorksheetBuilder
{
    private readonly IExcelRowWriter _rowWriter;

    public ExcelWorksheetBuilder(IExcelRowWriter rowWriter)
    {
        _rowWriter = rowWriter;
    }

    public void BuildSummaryWorksheet(WorksheetPart worksheetPart, SummaryData data, ExcelStyleIndices styles)
    {
        using var writer = OpenXmlWriter.Create(worksheetPart);
        writer.WriteStartElement(new Worksheet());

        WriteColumns(writer);
        WriteSummaryData(writer, data, styles);

        writer.WriteEndElement();
    }

    private void WriteColumns(OpenXmlWriter writer)
    {
        writer.WriteStartElement(new Columns());
        writer.WriteElement(new Column { Min = 1, Max = 1, Width = 25, CustomWidth = true });
        writer.WriteElement(new Column { Min = 2, Max = 2, Width = 40, CustomWidth = true });
        writer.WriteElement(new Column { Min = 3, Max = 3, Width = 15, CustomWidth = true });
        writer.WriteEndElement();
    }

    private void WriteSummaryData(OpenXmlWriter writer, SummaryData data, ExcelStyleIndices styles)
    {
        writer.WriteStartElement(new SheetData());
        uint currentRow = 1;

        _rowWriter.WriteInfoSection(writer, data.Info, currentRow, styles.HeaderStyle);
        currentRow += 6;

        _rowWriter.WriteEmptyRow(writer, currentRow++);

        _rowWriter.WriteSummarySection(writer, data.Results, currentRow, styles);
        if (data.Results == null)
        {
            currentRow++;
        }
        else
        {
            currentRow += (uint)(data.Results.Count + 1);
        }

        _rowWriter.WriteEmptyRow(writer, currentRow++);

        _rowWriter.WriteDifferenceSection(writer, data.Difference, currentRow, styles);
        currentRow += 3;

        _rowWriter.WriteEmptyRow(writer, currentRow++);

        var exclusionStartRow = currentRow;
        _rowWriter.WriteExclusionSection(writer, data.ExclusionReasons, currentRow, styles);

        writer.WriteEndElement();

        writer.WriteStartElement(new MergeCells());
        writer.WriteElement(new MergeCell { Reference = $"A{exclusionStartRow}:C{exclusionStartRow}" });
        writer.WriteEndElement();
    }
}

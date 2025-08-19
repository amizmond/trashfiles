using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Spreadsheet;

namespace DynamicExcel.Exclusions;

public class ExcelRowWriter : IExcelRowWriter
{
    public void WriteInfoSection(OpenXmlWriter writer, SummaryInfo? info, uint startRow, uint headerStyle)
    {
        uint currentRow = startRow;
        WriteInfoRow(writer, currentRow++, "Regulator", info?.Regulator ?? string.Empty, headerStyle);
        WriteInfoRow(writer, currentRow++, "Eagle Context ID", info?.EagleContextId ?? string.Empty, headerStyle);
        WriteInfoRow(writer, currentRow++, "Axis Ccr Batch ID", info?.AxisCcrBatchId ?? string.Empty, headerStyle);
        WriteInfoRow(writer, currentRow++, "Axis Parent Batch ID", info?.AxisParentBatchId ?? string.Empty, headerStyle);
        WriteInfoRow(writer, currentRow++, "Axis Batch ID", info?.AxisBatchId ?? string.Empty, headerStyle);
        WriteInfoRow(writer, currentRow, "Reviewed By", info?.ReviewedBy ?? string.Empty, headerStyle);
    }

    public void WriteSummarySection(OpenXmlWriter writer, List<SummaryResult>? results, uint startRow, ExcelStyleIndices styles)
    {
        WriteHeaderRow(writer, startRow++, new[] { "Group", "Count" }, styles.HeaderStyle);

        if (results != null && results.Any())
        {
            foreach (var result in results)
            {
                WriteDataRow(writer, startRow++, result.Group ?? string.Empty, result.Count, styles.BorderOnlyStyle, styles.BorderNumberStyle);
            }
        }
    }

    public void WriteDifferenceSection(OpenXmlWriter writer, Difference? difference, uint startRow, ExcelStyleIndices styles)
    {
        WriteHeaderRow(writer, startRow++, new[] { "Difference Breakdown", "Count" }, styles.HeaderStyle);
        WriteDataRow(writer, startRow++, "Missing in Axis", difference?.Missing ?? 0, styles.BorderOnlyStyle, styles.BorderNumberStyle, true);
        WriteDataRow(writer, startRow, "Extra in Axis", difference?.Extra ?? 0, styles.BorderOnlyStyle, styles.BorderNumberStyle, true);
    }

    public void WriteExclusionSection(OpenXmlWriter writer, List<ExclusionReasons>? reasons, uint startRow, ExcelStyleIndices styles)
    {
        WriteMergedHeaderRow(writer, startRow++, "Axis Exclusion Reasons", 3, styles.HeaderStyle);
        WriteHeaderRow(writer, startRow++, new[] { "Exclusion Reason", "Exclusion ID", "Count" }, styles.HeaderStyle);

        if (reasons != null && reasons.Any())
        {
            foreach (var reason in reasons)
            {
                WriteExclusionRow(writer, startRow++, reason, styles.BorderOnlyStyle, styles.BorderNumberStyle);
            }
        }
    }

    private void WriteInfoRow(OpenXmlWriter writer, uint rowIndex, string label, string value, uint headerStyle)
    {
        writer.WriteStartElement(new Row { RowIndex = rowIndex });

        writer.WriteElement(new Cell
        {
            CellReference = $"A{rowIndex}",
            DataType = CellValues.String,
            StyleIndex = headerStyle,
            CellValue = new CellValue(label),
        });

        var isNumeric = long.TryParse(value, out long numericValue);
        writer.WriteElement(new Cell
        {
            CellReference = $"B{rowIndex}",
            DataType = isNumeric ? CellValues.Number : CellValues.String,
            CellValue = new CellValue(isNumeric ? numericValue.ToString() : value),
        });

        writer.WriteEndElement();
    }

    private void WriteHeaderRow(OpenXmlWriter writer, uint rowIndex, string[] headers, uint headerStyle)
    {
        writer.WriteStartElement(new Row { RowIndex = rowIndex });

        for (int i = 0; i < headers.Length; i++)
        {
            writer.WriteElement(new Cell
            {
                CellReference = $"{(char)('A' + i)}{rowIndex}",
                DataType = CellValues.String,
                StyleIndex = headerStyle,
                CellValue = new CellValue(headers[i]),
            });
        }

        writer.WriteEndElement();
    }

    private void WriteMergedHeaderRow(OpenXmlWriter writer, uint rowIndex, string header, int mergeColumns, uint headerStyle)
    {
        writer.WriteStartElement(new Row { RowIndex = rowIndex });

        for (int i = 0; i < mergeColumns; i++)
        {
            writer.WriteElement(new Cell
            {
                CellReference = $"{(char)('A' + i)}{rowIndex}",
                DataType = CellValues.String,
                StyleIndex = headerStyle,
                CellValue = new CellValue(i == 0 ? header : string.Empty),
            });
        }

        writer.WriteEndElement();
    }

    private void WriteDataRow(OpenXmlWriter writer, uint rowIndex, string label, long count, uint textStyle, uint numberStyle, bool includeEmptyColumn = false)
    {
        writer.WriteStartElement(new Row { RowIndex = rowIndex });

        writer.WriteElement(new Cell
        {
            CellReference = $"A{rowIndex}",
            DataType = CellValues.String,
            StyleIndex = textStyle,
            CellValue = new CellValue(label),
        });

        writer.WriteElement(new Cell
        {
            CellReference = $"B{rowIndex}",
            StyleIndex = numberStyle,
            CellValue = new CellValue(count.ToString()),
        });

        if (includeEmptyColumn)
        {
            writer.WriteElement(new Cell
            {
                CellReference = $"C{rowIndex}",
                DataType = CellValues.String,
                StyleIndex = textStyle,
                CellValue = new CellValue(string.Empty),
            });
        }

        writer.WriteEndElement();
    }

    private void WriteExclusionRow(OpenXmlWriter writer, uint rowIndex, ExclusionReasons reason, uint textStyle, uint numberStyle)
    {
        writer.WriteStartElement(new Row { RowIndex = rowIndex });

        writer.WriteElement(new Cell
        {
            CellReference = $"A{rowIndex}",
            DataType = CellValues.String,
            StyleIndex = textStyle,
            CellValue = new CellValue(reason.Reason ?? string.Empty),
        });

        writer.WriteElement(new Cell
        {
            CellReference = $"B{rowIndex}",
            DataType = CellValues.String,
            StyleIndex = textStyle,
            CellValue = new CellValue(reason.ExclusionId ?? string.Empty),
        });

        writer.WriteElement(new Cell
        {
            CellReference = $"C{rowIndex}",
            StyleIndex = numberStyle,
            CellValue = new CellValue(reason.Count.ToString()),
        });

        writer.WriteEndElement();
    }

    public void WriteEmptyRow(OpenXmlWriter writer, uint rowIndex)
    {
        writer.WriteStartElement(new Row { RowIndex = rowIndex });
        writer.WriteEndElement();
    }
}

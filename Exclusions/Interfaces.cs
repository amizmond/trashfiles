using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;

namespace DynamicExcel.Exclusions;

public class ExcelStyleIndices
{
    public uint HeaderStyle { get; set; }
    public uint BorderOnlyStyle { get; set; }
    public uint BorderNumberStyle { get; set; }
}

public class SummaryData
{
    public SummaryInfo? Info { get; set; }

    public List<SummaryResult>? Results { get; set; }

    public Difference? Difference { get; set; }

    public List<ExclusionReasons>? ExclusionReasons { get; set; }
}

public interface IExcelStyleManager
{
    ExcelStyleIndices InitializeStyles(WorkbookPart workbookPart);
}

public interface IExcelWorksheetBuilder
{
    void BuildSummaryWorksheet(WorksheetPart worksheetPart, SummaryData data, ExcelStyleIndices styles);
}

public interface IExcelDocumentManager
{
    MemoryStream CreateSummarySheet(MemoryStream existingStream, SummaryData data);
}

public interface IExcelRowWriter
{
    void WriteInfoSection(OpenXmlWriter writer, SummaryInfo? info, uint startRow, uint headerStyle);

    void WriteSummarySection(OpenXmlWriter writer, List<SummaryResult>? results, uint startRow, ExcelStyleIndices styles);

    void WriteDifferenceSection(OpenXmlWriter writer, Difference? difference, uint startRow, ExcelStyleIndices styles);

    void WriteExclusionSection(OpenXmlWriter writer, List<ExclusionReasons>? reasons, uint startRow, ExcelStyleIndices styles);

    void WriteEmptyRow(OpenXmlWriter writer, uint rowIndex);
}

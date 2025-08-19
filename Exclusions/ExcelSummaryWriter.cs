namespace DynamicExcel.Exclusions;

public class ExcelSummaryWriter
{
    private readonly IExcelDocumentManager _documentManager;

    public ExcelSummaryWriter(IExcelDocumentManager documentManager)
    {
        _documentManager = documentManager;
    }
    public ExcelSummaryWriter() : this(CreateDefaultDocumentManager())
    {
    }

    private static IExcelDocumentManager CreateDefaultDocumentManager()
    {
        var styleManager = new ExcelStyleManager();
        var rowWriter = new ExcelRowWriter();
        var worksheetBuilder = new ExcelWorksheetBuilder(rowWriter);

        return new ExcelDocumentManager(styleManager, worksheetBuilder);
    }

    public MemoryStream WriteSummary(
        MemoryStream existingStream,
        SummaryInfo summary,
        List<SummaryResult> summaryList,
        Difference difference,
        List<ExclusionReasons> reasons)
    {
        var summaryData = new SummaryData
        {
            Info = summary,
            Results = summaryList,
            Difference = difference,
            ExclusionReasons = reasons
        };

        return _documentManager.CreateSummarySheet(existingStream, summaryData);
    }
}

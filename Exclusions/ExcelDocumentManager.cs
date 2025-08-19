using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace DynamicExcel.Exclusions;

public class ExcelDocumentManager : IExcelDocumentManager
{
    private readonly IExcelStyleManager _styleManager;
    private readonly IExcelWorksheetBuilder _worksheetBuilder;

    public ExcelDocumentManager(IExcelStyleManager styleManager, IExcelWorksheetBuilder worksheetBuilder)
    {
        _styleManager = styleManager;
        _worksheetBuilder = worksheetBuilder;
    }

    public MemoryStream CreateSummarySheet(MemoryStream existingStream, SummaryData data)
    {
        var resultStream = CopyStream(existingStream);

        using (var document = SpreadsheetDocument.Open(resultStream, true))
        {
            var workbookPart = document.WorkbookPart;
            var styles = _styleManager.InitializeStyles(workbookPart);

            var worksheetPart = AddSummaryWorksheet(workbookPart);
            _worksheetBuilder.BuildSummaryWorksheet(worksheetPart, data, styles);

            workbookPart.Workbook.Save();
        }

        resultStream.Position = 0;
        return resultStream;
    }

    private MemoryStream CopyStream(MemoryStream source)
    {
        var destination = new MemoryStream();
        source.Position = 0;
        source.CopyTo(destination);
        destination.Position = 0;
        return destination;
    }

    private WorksheetPart AddSummaryWorksheet(WorkbookPart workbookPart)
    {
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetId = workbookPart.GetIdOfPart(worksheetPart);

        var sheets = EnsureSheets(workbookPart);
        var nextSheetId = GetNextSheetId(sheets);

        var newSheet = new Sheet
        {
            Id = sheetId,
            SheetId = nextSheetId,
            Name = "Summary",
        };

        sheets.InsertAt(newSheet, 0);
        return worksheetPart;
    }

    private Sheets EnsureSheets(WorkbookPart workbookPart)
    {
        var sheets = workbookPart.Workbook.GetFirstChild<Sheets>();
        if (sheets == null)
        {
            sheets = new Sheets();
            workbookPart.Workbook.AppendChild(sheets);
        }
        return sheets;
    }

    private uint GetNextSheetId(Sheets sheets)
    {
        var existingSheets = sheets.Elements<Sheet>().ToList();
        return existingSheets.Any() ? existingSheets.Max(s => s.SheetId!.Value) + 1 : 1;
    }
}

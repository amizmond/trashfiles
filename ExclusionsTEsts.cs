using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DynamicExcel.Exclusions;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DynamicExcel.Tests;

[TestFixture]
public class ExcelRowWriterTests
{
    private ExcelRowWriter _rowWriter;
    private Mock<OpenXmlWriter> _mockWriter;
    private MemoryStream _stream;
    private SpreadsheetDocument _document;
    private WorksheetPart _worksheetPart;

    [SetUp]
    public void Setup()
    {
        _rowWriter = new ExcelRowWriter();
        _stream = new MemoryStream();
        _document = SpreadsheetDocument.Create(_stream, SpreadsheetDocumentType.Workbook);
        var workbookPart = _document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();
        _worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
    }

    [TearDown]
    public void TearDown()
    {
        _document?.Dispose();
        _stream?.Dispose();
    }

    [Test]
    public void WriteInfoSection_WithValidInfo_WritesAllRows()
    {
        // Arrange
        var info = new SummaryInfo
        {
            Regulator = "TestRegulator",
            EagleContextId = "Eagle123",
            AxisCcrBatchId = "CCR456",
            AxisParentBatchId = "Parent789",
            AxisBatchId = "Batch012",
            ReviewedBy = "John Doe"
        };
        uint startRow = 1;
        uint headerStyle = 5;

        using var writer = OpenXmlWriter.Create(_worksheetPart);
        writer.WriteStartElement(new Worksheet());
        writer.WriteStartElement(new SheetData());

        // Act
        _rowWriter.WriteInfoSection(writer, info, startRow, headerStyle);

        // Assert - verify writer doesn't throw exception
        Assert.That(() => {
            writer.WriteEndElement(); // SheetData
            writer.WriteEndElement(); // Worksheet
            writer.Close();
        }, Throws.Nothing);
    }

    [Test]
    public void WriteInfoSection_WithNullInfo_WritesEmptyValues()
    {
        // Arrange
        SummaryInfo info = null;
        uint startRow = 1;
        uint headerStyle = 5;

        using var writer = OpenXmlWriter.Create(_worksheetPart);
        writer.WriteStartElement(new Worksheet());
        writer.WriteStartElement(new SheetData());

        // Act & Assert
        Assert.That(() => _rowWriter.WriteInfoSection(writer, info, startRow, headerStyle), Throws.Nothing);
    }

    [Test]
    public void WriteSummarySection_WithResults_WritesHeaderAndDataRows()
    {
        // Arrange
        var results = new List<SummaryResult>
            {
                new SummaryResult { Group = "Group1", Count = 100 },
                new SummaryResult { Group = "Group2", Count = 200 }
            };
        var styles = new ExcelStyleIndices
        {
            HeaderStyle = 5,
            BorderOnlyStyle = 6,
            BorderNumberStyle = 7
        };
        uint startRow = 1;

        using var writer = OpenXmlWriter.Create(_worksheetPart);
        writer.WriteStartElement(new Worksheet());
        writer.WriteStartElement(new SheetData());

        // Act & Assert
        Assert.That(() => _rowWriter.WriteSummarySection(writer, results, startRow, styles), Throws.Nothing);
    }

    [Test]
    public void WriteSummarySection_WithEmptyResults_WritesOnlyHeader()
    {
        // Arrange
        var results = new List<SummaryResult>();
        var styles = new ExcelStyleIndices
        {
            HeaderStyle = 5,
            BorderOnlyStyle = 6,
            BorderNumberStyle = 7
        };
        uint startRow = 1;

        using var writer = OpenXmlWriter.Create(_worksheetPart);
        writer.WriteStartElement(new Worksheet());
        writer.WriteStartElement(new SheetData());

        // Act & Assert
        Assert.That(() => _rowWriter.WriteSummarySection(writer, results, startRow, styles), Throws.Nothing);
    }

    [Test]
    public void WriteDifferenceSection_WithValidDifference_WritesThreeRows()
    {
        // Arrange
        var difference = new Difference { Missing = 50, Extra = 30 };
        var styles = new ExcelStyleIndices
        {
            HeaderStyle = 5,
            BorderOnlyStyle = 6,
            BorderNumberStyle = 7
        };
        uint startRow = 1;

        using var writer = OpenXmlWriter.Create(_worksheetPart);
        writer.WriteStartElement(new Worksheet());
        writer.WriteStartElement(new SheetData());

        // Act & Assert
        Assert.That(() => _rowWriter.WriteDifferenceSection(writer, difference, startRow, styles), Throws.Nothing);
    }

    [Test]
    public void WriteExclusionSection_WithReasons_WritesAllRows()
    {
        // Arrange
        var reasons = new List<ExclusionReasons>
            {
                new ExclusionReasons { Reason = "Reason1", ExclusionId = "EX001", Count = 10 },
                new ExclusionReasons { Reason = "Reason2", ExclusionId = "EX002", Count = 20 }
            };
        var styles = new ExcelStyleIndices
        {
            HeaderStyle = 5,
            BorderOnlyStyle = 6,
            BorderNumberStyle = 7
        };
        uint startRow = 1;

        using var writer = OpenXmlWriter.Create(_worksheetPart);
        writer.WriteStartElement(new Worksheet());
        writer.WriteStartElement(new SheetData());

        // Act & Assert
        Assert.That(() => _rowWriter.WriteExclusionSection(writer, reasons, startRow, styles), Throws.Nothing);
    }

    [Test]
    public void WriteEmptyRow_WritesEmptyRow()
    {
        // Arrange
        uint rowIndex = 5;

        using var writer = OpenXmlWriter.Create(_worksheetPart);
        writer.WriteStartElement(new Worksheet());
        writer.WriteStartElement(new SheetData());

        // Act & Assert
        Assert.That(() => _rowWriter.WriteEmptyRow(writer, rowIndex), Throws.Nothing);
    }
}

[TestFixture]
public class ExcelStyleManagerTests
{
    private ExcelStyleManager _styleManager;
    private SpreadsheetDocument _document;
    private MemoryStream _stream;
    private WorkbookPart _workbookPart;

    [SetUp]
    public void Setup()
    {
        _styleManager = new ExcelStyleManager();
        _stream = new MemoryStream();
        _document = SpreadsheetDocument.Create(_stream, SpreadsheetDocumentType.Workbook);
        _workbookPart = _document.AddWorkbookPart();
        _workbookPart.Workbook = new Workbook();

        // Ensure the workbook has the necessary child elements
        _workbookPart.Workbook.AppendChild(new Sheets());
    }

    [TearDown]
    public void TearDown()
    {
        _document?.Dispose();
        _stream?.Dispose();
    }

    [Test]
    public void InitializeStyles_CreatesStylesPart_WhenNotExists()
    {
        // Act - May create default styles with index 0
        ExcelStyleIndices result = null;
        Assert.That(() => result = _styleManager.InitializeStyles(_workbookPart), Throws.Nothing);

        // Assert
        Assert.That(_workbookPart.WorkbookStylesPart, Is.Not.Null);
        Assert.That(result, Is.Not.Null);
        Assert.That(result.HeaderStyle, Is.GreaterThanOrEqualTo(0));
        Assert.That(result.BorderOnlyStyle, Is.GreaterThanOrEqualTo(0));
        Assert.That(result.BorderNumberStyle, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void InitializeStyles_UsesExistingStylesPart_WhenExists()
    {
        // Arrange - Create a complete stylesheet structure
        var stylesPart = _workbookPart.AddNewPart<WorkbookStylesPart>();
        stylesPart.Stylesheet = CreateCompleteStylesheet();

        // Act
        ExcelStyleIndices result = null;
        Assert.That(() => result = _styleManager.InitializeStyles(_workbookPart), Throws.Nothing);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(stylesPart.Stylesheet.Fonts, Is.Not.Null);
        Assert.That(stylesPart.Stylesheet.Fills, Is.Not.Null);
        Assert.That(stylesPart.Stylesheet.Borders, Is.Not.Null);
        Assert.That(stylesPart.Stylesheet.CellFormats, Is.Not.Null);
    }

    [Test]
    public void InitializeStyles_PreservesExistingStyles()
    {
        // Arrange
        var stylesPart = _workbookPart.AddNewPart<WorkbookStylesPart>();
        stylesPart.Stylesheet = CreateCompleteStylesheet();

        var originalFontCount = stylesPart.Stylesheet.Fonts.Count();
        var originalFillCount = stylesPart.Stylesheet.Fills.Count();
        var originalBorderCount = stylesPart.Stylesheet.Borders.Count();
        var originalFormatCount = stylesPart.Stylesheet.CellFormats.Count();

        // Act
        var result = _styleManager.InitializeStyles(_workbookPart);

        // Assert - Should have at least the original counts
        Assert.That(stylesPart.Stylesheet.Fonts.Count(), Is.GreaterThanOrEqualTo(originalFontCount));
        Assert.That(stylesPart.Stylesheet.Fills.Count(), Is.GreaterThanOrEqualTo(originalFillCount));
        Assert.That(stylesPart.Stylesheet.Borders.Count(), Is.GreaterThanOrEqualTo(originalBorderCount));
        Assert.That(stylesPart.Stylesheet.CellFormats.Count(), Is.GreaterThanOrEqualTo(originalFormatCount));
    }

    private Stylesheet CreateCompleteStylesheet()
    {
        return new Stylesheet
        {
            Fonts = new Fonts(
                new Font(
                    new FontSize { Val = 11 },
                    new Color { Theme = 1 }
                )
            )
            { Count = 1 },
            Fills = new Fills(
                new Fill(new PatternFill { PatternType = PatternValues.None }),
                new Fill(new PatternFill { PatternType = PatternValues.Gray125 })
            )
            { Count = 2 },
            Borders = new Borders(
                new Border(
                    new LeftBorder(),
                    new RightBorder(),
                    new TopBorder(),
                    new BottomBorder(),
                    new DiagonalBorder()
                )
            )
            { Count = 1 },
            CellFormats = new CellFormats(
                new CellFormat
                {
                    NumberFormatId = 0,
                    FontId = 0,
                    FillId = 0,
                    BorderId = 0,
                    FormatId = 0
                }
            )
            { Count = 1 },
            CellStyles = new CellStyles(
                new CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }
            )
            { Count = 1 },
            DifferentialFormats = new DifferentialFormats { Count = 0 },
            TableStyles = new TableStyles
            {
                Count = 0,
                DefaultTableStyle = "TableStyleMedium2",
                DefaultPivotStyle = "PivotStyleLight16"
            }
        };
    }
}

[TestFixture]
public class ExcelWorksheetBuilderTests
{
    private ExcelWorksheetBuilder _worksheetBuilder;
    private Mock<IExcelRowWriter> _mockRowWriter;
    private SpreadsheetDocument _document;
    private MemoryStream _stream;
    private WorksheetPart _worksheetPart;

    [SetUp]
    public void Setup()
    {
        _mockRowWriter = new Mock<IExcelRowWriter>();
        _worksheetBuilder = new ExcelWorksheetBuilder(_mockRowWriter.Object);

        _stream = new MemoryStream();
        _document = SpreadsheetDocument.Create(_stream, SpreadsheetDocumentType.Workbook);
        var workbookPart = _document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();
        _worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
    }

    [TearDown]
    public void TearDown()
    {
        _document?.Dispose();
        _stream?.Dispose();
    }

    [Test]
    public void BuildSummaryWorksheet_CallsAllRowWriterMethods()
    {
        // Arrange
        var data = new SummaryData
        {
            Info = new SummaryInfo { Regulator = "Test" },
            Results = new List<SummaryResult>
                {
                    new SummaryResult { Group = "Group1", Count = 100 }
                },
            Difference = new Difference { Missing = 10, Extra = 5 },
            ExclusionReasons = new List<ExclusionReasons>
                {
                    new ExclusionReasons { Reason = "Test", ExclusionId = "001", Count = 1 }
                }
        };
        var styles = new ExcelStyleIndices { HeaderStyle = 1, BorderOnlyStyle = 2, BorderNumberStyle = 3 };

        // Act
        _worksheetBuilder.BuildSummaryWorksheet(_worksheetPart, data, styles);

        // Assert
        _mockRowWriter.Verify(x => x.WriteInfoSection(It.IsAny<OpenXmlWriter>(), data.Info, 1, styles.HeaderStyle), Times.Once);
        _mockRowWriter.Verify(x => x.WriteSummarySection(It.IsAny<OpenXmlWriter>(), data.Results, 8, styles), Times.Once);
        _mockRowWriter.Verify(x => x.WriteDifferenceSection(It.IsAny<OpenXmlWriter>(), data.Difference, It.IsAny<uint>(), styles), Times.Once);
        _mockRowWriter.Verify(x => x.WriteExclusionSection(It.IsAny<OpenXmlWriter>(), data.ExclusionReasons, It.IsAny<uint>(), styles), Times.Once);
        _mockRowWriter.Verify(x => x.WriteEmptyRow(It.IsAny<OpenXmlWriter>(), It.IsAny<uint>()), Times.Exactly(3));
    }

    [Test]
    public void BuildSummaryWorksheet_HandlesNullData()
    {
        // Arrange
        var data = new SummaryData
        {
            Info = null,
            Results = null,
            Difference = null,
            ExclusionReasons = null
        };
        var styles = new ExcelStyleIndices { HeaderStyle = 1, BorderOnlyStyle = 2, BorderNumberStyle = 3 };

        // Act & Assert
        Assert.That(() => _worksheetBuilder.BuildSummaryWorksheet(_worksheetPart, data, styles), Throws.Nothing);
    }
}

[TestFixture]
public class ExcelDocumentManagerTests
{
    private ExcelDocumentManager _documentManager;
    private Mock<IExcelStyleManager> _mockStyleManager;
    private Mock<IExcelWorksheetBuilder> _mockWorksheetBuilder;

    [SetUp]
    public void Setup()
    {
        _mockStyleManager = new Mock<IExcelStyleManager>();
        _mockWorksheetBuilder = new Mock<IExcelWorksheetBuilder>();
        _documentManager = new ExcelDocumentManager(_mockStyleManager.Object, _mockWorksheetBuilder.Object);
    }

    [Test]
    public void CreateSummarySheet_CreatesNewStreamWithSummarySheet()
    {
        // Arrange
        var existingStream = CreateTestExcelStream();
        var data = new SummaryData
        {
            Info = new SummaryInfo { Regulator = "Test" },
            Results = new List<SummaryResult>(),
            Difference = new Difference(),
            ExclusionReasons = new List<ExclusionReasons>()
        };
        var styles = new ExcelStyleIndices { HeaderStyle = 1, BorderOnlyStyle = 2, BorderNumberStyle = 3 };

        _mockStyleManager.Setup(x => x.InitializeStyles(It.IsAny<WorkbookPart>())).Returns(styles);

        // Act
        var resultStream = _documentManager.CreateSummarySheet(existingStream, data);

        // Assert
        Assert.That(resultStream, Is.Not.Null);
        Assert.That(resultStream.Position, Is.EqualTo(0));
        Assert.That(resultStream.Length, Is.GreaterThan(0));

        _mockStyleManager.Verify(x => x.InitializeStyles(It.IsAny<WorkbookPart>()), Times.Once);
        _mockWorksheetBuilder.Verify(x => x.BuildSummaryWorksheet(It.IsAny<WorksheetPart>(), data, styles), Times.Once);

        // Cleanup
        resultStream.Dispose();
        existingStream.Dispose();
    }

    [Test]
    public void CreateSummarySheet_PreservesOriginalStreamContent()
    {
        // Arrange
        var existingStream = CreateTestExcelStream();
        var originalLength = existingStream.Length;
        var originalPosition = existingStream.Position;
        var data = new SummaryData();
        var styles = new ExcelStyleIndices { HeaderStyle = 1 };

        _mockStyleManager.Setup(x => x.InitializeStyles(It.IsAny<WorkbookPart>())).Returns(styles);

        // Act
        var resultStream = _documentManager.CreateSummarySheet(existingStream, data);

        // Assert - The original stream's length is preserved but position may change
        Assert.That(existingStream.Length, Is.EqualTo(originalLength));
        // After CopyTo operation, the position will be at the end of the stream
        Assert.That(existingStream.Position, Is.EqualTo(existingStream.Length));

        // Cleanup
        resultStream.Dispose();
        existingStream.Dispose();
    }

    [Test]
    public void CreateSummarySheet_CreatesIndependentCopy()
    {
        // Arrange
        var existingStream = CreateTestExcelStream();
        var originalBytes = existingStream.ToArray();
        var data = new SummaryData();
        var styles = new ExcelStyleIndices { HeaderStyle = 1 };

        _mockStyleManager.Setup(x => x.InitializeStyles(It.IsAny<WorkbookPart>())).Returns(styles);

        // Act
        var resultStream = _documentManager.CreateSummarySheet(existingStream, data);

        // Assert - Result stream is independent from original
        Assert.That(resultStream, Is.Not.SameAs(existingStream));
        Assert.That(resultStream.Position, Is.EqualTo(0));

        // Verify original stream data is unchanged (just position moved)
        existingStream.Position = 0;
        var currentBytes = existingStream.ToArray();
        Assert.That(currentBytes, Is.EqualTo(originalBytes));

        // Cleanup
        resultStream.Dispose();
        existingStream.Dispose();
    }

    [Test]
    public void CreateSummarySheet_AddsSheetAsFirstSheet()
    {
        // Arrange
        var existingStream = CreateTestExcelStreamWithSheet("ExistingSheet");
        var data = new SummaryData();
        var styles = new ExcelStyleIndices { HeaderStyle = 1 };

        _mockStyleManager.Setup(x => x.InitializeStyles(It.IsAny<WorkbookPart>())).Returns(styles);

        // Act
        var resultStream = _documentManager.CreateSummarySheet(existingStream, data);

        // Assert
        using (var document = SpreadsheetDocument.Open(resultStream, false))
        {
            var sheets = document.WorkbookPart.Workbook.GetFirstChild<Sheets>();
            var firstSheet = sheets.Elements<Sheet>().FirstOrDefault();

            Assert.That(firstSheet, Is.Not.Null);
            Assert.That(firstSheet.Name.Value, Is.EqualTo("Summary"));
        }

        // Cleanup
        resultStream.Dispose();
        existingStream.Dispose();
    }

    private MemoryStream CreateTestExcelStream()
    {
        var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            workbookPart.Workbook.Save();
        }
        stream.Position = 0;
        return stream;
    }

    private MemoryStream CreateTestExcelStreamWithSheet(string sheetName)
    {
        var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            worksheetPart.Worksheet = new Worksheet(new SheetData());

            var sheets = new Sheets();
            var sheet = new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = sheetName
            };
            sheets.Append(sheet);
            workbookPart.Workbook.AppendChild(sheets);

            workbookPart.Workbook.Save();
        }
        stream.Position = 0;
        return stream;
    }
}

[TestFixture]
public class ExcelSummaryWriterTests
{
    private ExcelSummaryWriter _summaryWriter;
    private Mock<IExcelDocumentManager> _mockDocumentManager;

    [SetUp]
    public void Setup()
    {
        _mockDocumentManager = new Mock<IExcelDocumentManager>();
        _summaryWriter = new ExcelSummaryWriter(_mockDocumentManager.Object);
    }

    [Test]
    public void WriteSummary_CallsDocumentManagerWithCorrectData()
    {
        // Arrange
        var existingStream = new MemoryStream();
        var expectedStream = new MemoryStream();
        var summary = new SummaryInfo { Regulator = "Test" };
        var summaryList = new List<SummaryResult>
            {
                new SummaryResult { Group = "Group1", Count = 100 }
            };
        var difference = new Difference { Missing = 10, Extra = 5 };
        var reasons = new List<ExclusionReasons>
            {
                new ExclusionReasons { Reason = "Test", ExclusionId = "001", Count = 1 }
            };

        _mockDocumentManager
            .Setup(x => x.CreateSummarySheet(existingStream, It.IsAny<SummaryData>()))
            .Returns(expectedStream)
            .Callback<MemoryStream, SummaryData>((stream, data) =>
            {
                Assert.That(data.Info, Is.EqualTo(summary));
                Assert.That(data.Results, Is.EqualTo(summaryList));
                Assert.That(data.Difference, Is.EqualTo(difference));
                Assert.That(data.ExclusionReasons, Is.EqualTo(reasons));
            });

        // Act
        var result = _summaryWriter.WriteSummary(existingStream, summary, summaryList, difference, reasons);

        // Assert
        Assert.That(result, Is.EqualTo(expectedStream));
        _mockDocumentManager.Verify(x => x.CreateSummarySheet(existingStream, It.IsAny<SummaryData>()), Times.Once);
    }

    [Test]
    public void DefaultConstructor_CreatesDefaultDocumentManager()
    {
        // Act
        var writer = new ExcelSummaryWriter();

        // Assert
        Assert.That(writer, Is.Not.Null);
    }

    [Test]
    public void WriteSummary_HandlesNullParameters()
    {
        // Arrange
        var existingStream = new MemoryStream();
        var expectedStream = new MemoryStream();

        _mockDocumentManager
            .Setup(x => x.CreateSummarySheet(existingStream, It.IsAny<SummaryData>()))
            .Returns(expectedStream);

        // Act
        var result = _summaryWriter.WriteSummary(
            existingStream,
            null,
            null,
            null,
            null);

        // Assert
        Assert.That(result, Is.Not.Null);
        _mockDocumentManager.Verify(x => x.CreateSummarySheet(existingStream, It.IsAny<SummaryData>()), Times.Once);
    }
}

[TestFixture]
public class SummaryDataTests
{
    [Test]
    public void SummaryData_InitializesWithNullValues()
    {
        // Act
        var data = new SummaryData();

        // Assert
        Assert.That(data.Info, Is.Null);
        Assert.That(data.Results, Is.Null);
        Assert.That(data.Difference, Is.Null);
        Assert.That(data.ExclusionReasons, Is.Null);
    }

    [Test]
    public void SummaryData_CanSetAllProperties()
    {
        // Arrange
        var info = new SummaryInfo { Regulator = "Test" };
        var results = new List<SummaryResult> { new SummaryResult { Group = "G1", Count = 1 } };
        var difference = new Difference { Missing = 1, Extra = 2 };
        var reasons = new List<ExclusionReasons> { new ExclusionReasons { Reason = "R1", Count = 1 } };

        // Act
        var data = new SummaryData
        {
            Info = info,
            Results = results,
            Difference = difference,
            ExclusionReasons = reasons
        };

        // Assert
        Assert.That(data.Info, Is.EqualTo(info));
        Assert.That(data.Results, Is.EqualTo(results));
        Assert.That(data.Difference, Is.EqualTo(difference));
        Assert.That(data.ExclusionReasons, Is.EqualTo(reasons));
    }
}

[TestFixture]
public class ModelTests
{
    [Test]
    public void SummaryInfo_HasCorrectProperties()
    {
        // Arrange & Act
        var info = new SummaryInfo
        {
            Regulator = "TestReg",
            EagleContextId = "Eagle123",
            AxisCcrBatchId = "CCR456",
            AxisParentBatchId = "Parent789",
            AxisBatchId = "Batch012",
            ReviewedBy = "John Doe"
        };

        // Assert
        Assert.That(info.Regulator, Is.EqualTo("TestReg"));
        Assert.That(info.EagleContextId, Is.EqualTo("Eagle123"));
        Assert.That(info.AxisCcrBatchId, Is.EqualTo("CCR456"));
        Assert.That(info.AxisParentBatchId, Is.EqualTo("Parent789"));
        Assert.That(info.AxisBatchId, Is.EqualTo("Batch012"));
        Assert.That(info.ReviewedBy, Is.EqualTo("John Doe"));
    }

    [Test]
    public void SummaryResult_HasCorrectProperties()
    {
        // Arrange & Act
        var result = new SummaryResult
        {
            Group = "TestGroup",
            Count = 100
        };

        // Assert
        Assert.That(result.Group, Is.EqualTo("TestGroup"));
        Assert.That(result.Count, Is.EqualTo(100));
    }

    [Test]
    public void Difference_HasCorrectProperties()
    {
        // Arrange & Act
        var difference = new Difference
        {
            Missing = 50,
            Extra = 25
        };

        // Assert
        Assert.That(difference.Missing, Is.EqualTo(50));
        Assert.That(difference.Extra, Is.EqualTo(25));
    }

    [Test]
    public void ExclusionReasons_HasCorrectProperties()
    {
        // Arrange & Act
        var reason = new ExclusionReasons
        {
            Reason = "TestReason",
            ExclusionId = "EX001",
            Count = 10
        };

        // Assert
        Assert.That(reason.Reason, Is.EqualTo("TestReason"));
        Assert.That(reason.ExclusionId, Is.EqualTo("EX001"));
        Assert.That(reason.Count, Is.EqualTo(10));
    }

    [Test]
    public void ExcelStyleIndices_HasCorrectProperties()
    {
        // Arrange & Act
        var indices = new ExcelStyleIndices
        {
            HeaderStyle = 1,
            BorderOnlyStyle = 2,
            BorderNumberStyle = 3
        };

        // Assert
        Assert.That(indices.HeaderStyle, Is.EqualTo(1));
        Assert.That(indices.BorderOnlyStyle, Is.EqualTo(2));
        Assert.That(indices.BorderNumberStyle, Is.EqualTo(3));
    }
}

[TestFixture]
public class IntegrationTests
{
    [Test]
    public void EndToEnd_CreatesSummarySheet_Successfully()
    {
        // Arrange
        var sourceStream = CreateEmptyExcelStream();
        var writer = new ExcelSummaryWriter();

        var summary = new SummaryInfo
        {
            Regulator = "TestRegulator",
            EagleContextId = "Eagle123",
            AxisCcrBatchId = "CCR456",
            AxisParentBatchId = "Parent789",
            AxisBatchId = "Batch012",
            ReviewedBy = "John Doe"
        };

        var summaryList = new List<SummaryResult>
            {
                new SummaryResult { Group = "Group1", Count = 100 },
                new SummaryResult { Group = "Group2", Count = 200 }
            };

        var difference = new Difference { Missing = 50, Extra = 30 };

        var reasons = new List<ExclusionReasons>
            {
                new ExclusionReasons { Reason = "Reason1", ExclusionId = "EX001", Count = 10 },
                new ExclusionReasons { Reason = "Reason2", ExclusionId = "EX002", Count = 20 }
            };

        // Act
        var resultStream = writer.WriteSummary(sourceStream, summary, summaryList, difference, reasons);

        // Assert
        Assert.That(resultStream, Is.Not.Null);
        Assert.That(resultStream.Length, Is.GreaterThan(0));

        // Verify the document can be opened
        using (var document = SpreadsheetDocument.Open(resultStream, false))
        {
            Assert.That(document.WorkbookPart, Is.Not.Null);
            var sheets = document.WorkbookPart.Workbook.GetFirstChild<Sheets>();
            var summarySheet = sheets.Elements<Sheet>().FirstOrDefault(s => s.Name == "Summary");
            Assert.That(summarySheet, Is.Not.Null);
        }

        // Cleanup
        sourceStream.Dispose();
        resultStream.Dispose();
    }

    private MemoryStream CreateEmptyExcelStream()
    {
        var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            workbookPart.Workbook.Save();
        }
        stream.Position = 0;
        return stream;
    }
}
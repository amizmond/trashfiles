
namespace DynamicExcel.Tests;

using Write;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

[TestFixture]
public class ExcelCellWriterSharedStringTableTests
{
    private ExcelCellWriter _cellWriter;
    private MemoryStream _memoryStream;
    private SpreadsheetDocument _document;
    private WorkbookPart _workbookPart;

    [SetUp]
    public void SetUp()
    {
        _cellWriter = new ExcelCellWriter();
        _memoryStream = new MemoryStream();
        _document = SpreadsheetDocument.Create(_memoryStream, SpreadsheetDocumentType.Workbook);
        _workbookPart = _document.AddWorkbookPart();
    }

    [TearDown]
    public void TearDown()
    {
        _document?.Dispose();
        _memoryStream?.Dispose();
    }

    [Test]
    public void InitializeSharedStringTable_ShouldCreateSharedStringTablePart()
    {
        _cellWriter.InitializeSharedStringTable(_workbookPart);
        Assert.That(_workbookPart.SharedStringTablePart, Is.Not.Null);
    }

    [Test]
    public void InitializeSharedStringTable_NullWorkbookPart_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _cellWriter.InitializeSharedStringTable(null));
    }

    [Test]
    public void InitializeSharedStringTable_CalledTwice_ShouldResetState()
    {
        _cellWriter.InitializeSharedStringTable(_workbookPart);

        var worksheetPart = _workbookPart.AddNewPart<WorksheetPart>();
        using (var writer = OpenXmlWriter.Create(worksheetPart))
        {
            writer.WriteStartElement(new Worksheet());
            writer.WriteStartElement(new SheetData());
            _cellWriter.WriteCellValue(writer, "A1", "Test1", typeof(string), null);
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        _cellWriter.InitializeSharedStringTable(_workbookPart);

        var worksheetPart2 = _workbookPart.AddNewPart<WorksheetPart>();
        using (var writer = OpenXmlWriter.Create(worksheetPart2))
        {
            writer.WriteStartElement(new Worksheet());
            writer.WriteStartElement(new SheetData());
            _cellWriter.WriteCellValue(writer, "A1", "Test2", typeof(string), null);
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        _cellWriter.FinalizeSharedStringTable();

        var sharedStringTable = ReadSharedStringTable();
        Assert.That(sharedStringTable.Count, Is.EqualTo(1));
        Assert.That(sharedStringTable[0], Is.EqualTo("Test2"));
    }

    [Test]
    public void FinalizeSharedStringTable_WithUniqueStrings_ShouldWriteCorrectTable()
    {
        _cellWriter.InitializeSharedStringTable(_workbookPart);
        var testStrings = new[] { "String1", "String2", "String3" };

        var worksheetPart = _workbookPart.AddNewPart<WorksheetPart>();
        using (var writer = OpenXmlWriter.Create(worksheetPart))
        {
            writer.WriteStartElement(new Worksheet());
            writer.WriteStartElement(new SheetData());

            for (var i = 0; i < testStrings.Length; i++)
            {
                _cellWriter.WriteCellValue(writer, $"A{i + 1}", testStrings[i], typeof(string), null);
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        _cellWriter.FinalizeSharedStringTable();

        var sharedStrings = ReadSharedStringTable();
        Assert.That(sharedStrings.Count, Is.EqualTo(3));
        CollectionAssert.AreEquivalent(testStrings, sharedStrings);
    }

    [Test]
    public void FinalizeSharedStringTable_WithDuplicateStrings_ShouldStoreUniqueOnly()
    {
        _cellWriter.InitializeSharedStringTable(_workbookPart);
        var testData = new[] { "Duplicate", "Unique", "Duplicate", "Duplicate", "Unique2" };

        var worksheetPart = _workbookPart.AddNewPart<WorksheetPart>();
        using (var writer = OpenXmlWriter.Create(worksheetPart))
        {
            writer.WriteStartElement(new Worksheet());
            writer.WriteStartElement(new SheetData());

            for (var i = 0; i < testData.Length; i++)
            {
                _cellWriter.WriteCellValue(writer, $"A{i + 1}", testData[i], typeof(string), null);
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        _cellWriter.FinalizeSharedStringTable();

        var sharedStrings = ReadSharedStringTable();
        var uniqueStrings = testData.Distinct().ToList();
        Assert.That(sharedStrings.Count, Is.EqualTo(uniqueStrings.Count));
        CollectionAssert.AreEquivalent(uniqueStrings, sharedStrings);
    }

    [Test]
    public void FinalizeSharedStringTable_WithoutInitialization_ShouldHandleGracefully()
    {
        Assert.DoesNotThrow(() => _cellWriter.FinalizeSharedStringTable());
    }

    [Test]
    public void FinalizeSharedStringTable_WithEmptyData_ShouldCreateEmptyTable()
    {
        _cellWriter.InitializeSharedStringTable(_workbookPart);
        _cellWriter.FinalizeSharedStringTable();

        var sharedStrings = ReadSharedStringTable();
        Assert.That(sharedStrings.Count, Is.EqualTo(0));
    }

    [Test]
    public void FinalizeSharedStringTable_ShouldCleanupInternalState()
    {
        _cellWriter.InitializeSharedStringTable(_workbookPart);

        var worksheetPart = _workbookPart.AddNewPart<WorksheetPart>();
        using (var writer = OpenXmlWriter.Create(worksheetPart))
        {
            writer.WriteStartElement(new Worksheet());
            writer.WriteStartElement(new SheetData());
            _cellWriter.WriteCellValue(writer, "A1", "Test", typeof(string), null);
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        _cellWriter.FinalizeSharedStringTable();

        var worksheetPart2 = _workbookPart.AddNewPart<WorksheetPart>();
        using (var writer = OpenXmlWriter.Create(worksheetPart2))
        {
            writer.WriteStartElement(new Worksheet());
            writer.WriteStartElement(new SheetData());

            Assert.Throws<InvalidOperationException>(() =>
                _cellWriter.WriteCellValue(writer, "A1", "NewString", typeof(string), null));

            writer.WriteEndElement();
            writer.WriteEndElement();
        }
    }

    [Test]
    public void GetOrCreateSharedStringId_NewString_ShouldReturnIncrementingIds()
    {
        _cellWriter.InitializeSharedStringTable(_workbookPart);

        var worksheetPart = _workbookPart.AddNewPart<WorksheetPart>();
        using (var writer = OpenXmlWriter.Create(worksheetPart))
        {
            writer.WriteStartElement(new Worksheet());
            writer.WriteStartElement(new SheetData());

            writer.WriteStartElement(new Row { RowIndex = 1 });
            _cellWriter.WriteCellValue(writer, "A1", "First", typeof(string), null);
            writer.WriteEndElement();

            writer.WriteStartElement(new Row { RowIndex = 2 });
            _cellWriter.WriteCellValue(writer, "A2", "Second", typeof(string), null);
            writer.WriteEndElement();

            writer.WriteStartElement(new Row { RowIndex = 3 });
            _cellWriter.WriteCellValue(writer, "A3", "Third", typeof(string), null);
            writer.WriteEndElement();

            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        _cellWriter.FinalizeSharedStringTable();

        var cellValues = ReadCellValues();
        Assert.That(cellValues.ContainsKey("A1"), Is.True);
        Assert.That(cellValues.ContainsKey("A2"), Is.True);
        Assert.That(cellValues.ContainsKey("A3"), Is.True);
        Assert.That(cellValues["A1"], Is.EqualTo("0"));
        Assert.That(cellValues["A2"], Is.EqualTo("1"));
        Assert.That(cellValues["A3"], Is.EqualTo("2"));
    }

    [Test]
    public void GetOrCreateSharedStringId_ExistingString_ShouldReturnSameId()
    {
        _cellWriter.InitializeSharedStringTable(_workbookPart);

        var worksheetPart = _workbookPart.AddNewPart<WorksheetPart>();
        using (var writer = OpenXmlWriter.Create(worksheetPart))
        {
            writer.WriteStartElement(new Worksheet());
            writer.WriteStartElement(new SheetData());

            writer.WriteStartElement(new Row { RowIndex = 1 });
            _cellWriter.WriteCellValue(writer, "A1", "Repeated", typeof(string), null);
            writer.WriteEndElement();

            writer.WriteStartElement(new Row { RowIndex = 2 });
            _cellWriter.WriteCellValue(writer, "A2", "Different", typeof(string), null);
            writer.WriteEndElement();

            writer.WriteStartElement(new Row { RowIndex = 3 });
            _cellWriter.WriteCellValue(writer, "A3", "Repeated", typeof(string), null);
            writer.WriteEndElement();

            writer.WriteStartElement(new Row { RowIndex = 4 });
            _cellWriter.WriteCellValue(writer, "A4", "Repeated", typeof(string), null);
            writer.WriteEndElement();

            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        _cellWriter.FinalizeSharedStringTable();

        var cellValues = ReadCellValues();
        Assert.That(cellValues.ContainsKey("A1"), Is.True);
        Assert.That(cellValues.ContainsKey("A2"), Is.True);
        Assert.That(cellValues.ContainsKey("A3"), Is.True);
        Assert.That(cellValues.ContainsKey("A4"), Is.True);
        Assert.That(cellValues["A1"], Is.EqualTo(cellValues["A3"]));
        Assert.That(cellValues["A1"], Is.EqualTo(cellValues["A4"]));
        Assert.That(cellValues["A2"], Is.Not.EqualTo(cellValues["A1"]));
    }

    [Test]
    public void GetOrCreateSharedStringId_EmptyString_ShouldBeHandled()
    {
        _cellWriter.InitializeSharedStringTable(_workbookPart);

        var worksheetPart = _workbookPart.AddNewPart<WorksheetPart>();
        using (var writer = OpenXmlWriter.Create(worksheetPart))
        {
            writer.WriteStartElement(new Worksheet());
            writer.WriteStartElement(new SheetData());

            writer.WriteStartElement(new Row { RowIndex = 1 });
            _cellWriter.WriteCellValue(writer, "A1", string.Empty, typeof(string), null);
            writer.WriteEndElement();

            writer.WriteStartElement(new Row { RowIndex = 2 });
            _cellWriter.WriteCellValue(writer, "A2", "NonEmpty", typeof(string), null);
            writer.WriteEndElement();

            writer.WriteStartElement(new Row { RowIndex = 3 });
            _cellWriter.WriteCellValue(writer, "A3", string.Empty, typeof(string), null);
            writer.WriteEndElement();

            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        _cellWriter.FinalizeSharedStringTable();

        var cellValues = ReadCellValues();
        Assert.That(cellValues.ContainsKey("A1"), Is.True);
        Assert.That(cellValues.ContainsKey("A2"), Is.True);
        Assert.That(cellValues.ContainsKey("A3"), Is.True);
        Assert.That(cellValues["A1"], Is.EqualTo(cellValues["A3"]));
        Assert.That(cellValues["A2"], Is.Not.EqualTo(cellValues["A1"]));

        var sharedStrings = ReadSharedStringTable();
        Assert.That(sharedStrings.Contains(string.Empty), Is.True);
        Assert.That(sharedStrings.Contains("NonEmpty"), Is.True);
    }

    [Test]
    public void GetOrCreateSharedStringId_CaseSensitive_ShouldTreatAsUnique()
    {
        _cellWriter.InitializeSharedStringTable(_workbookPart);

        var worksheetPart = _workbookPart.AddNewPart<WorksheetPart>();
        using (var writer = OpenXmlWriter.Create(worksheetPart))
        {
            writer.WriteStartElement(new Worksheet());
            writer.WriteStartElement(new SheetData());

            writer.WriteStartElement(new Row { RowIndex = 1 });
            _cellWriter.WriteCellValue(writer, "A1", "Test", typeof(string), null);
            writer.WriteEndElement();

            writer.WriteStartElement(new Row { RowIndex = 2 });
            _cellWriter.WriteCellValue(writer, "A2", "test", typeof(string), null);
            writer.WriteEndElement();

            writer.WriteStartElement(new Row { RowIndex = 3 });
            _cellWriter.WriteCellValue(writer, "A3", "TEST", typeof(string), null);
            writer.WriteEndElement();

            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        _cellWriter.FinalizeSharedStringTable();

        var cellValues = ReadCellValues();
        Assert.That(cellValues.ContainsKey("A1"), Is.True);
        Assert.That(cellValues.ContainsKey("A2"), Is.True);
        Assert.That(cellValues.ContainsKey("A3"), Is.True);
        Assert.That(cellValues["A1"], Is.Not.EqualTo(cellValues["A2"]));
        Assert.That(cellValues["A1"], Is.Not.EqualTo(cellValues["A3"]));
        Assert.That(cellValues["A2"], Is.Not.EqualTo(cellValues["A3"]));

        var sharedStrings = ReadSharedStringTable();
        Assert.That(sharedStrings.Count, Is.EqualTo(3));
    }

    [Test]
    public void GetOrCreateSharedStringId_SpecialCharacters_ShouldHandleCorrectly()
    {
        _cellWriter.InitializeSharedStringTable(_workbookPart);
        var specialStrings = new[]
        {
            "Line1\nLine2",
            "Tab\tSeparated",
            "Special&<>\"'Characters",
            "Unicode: 你好 مرحبا",
            "Emoji: 😀"
        };

        var worksheetPart = _workbookPart.AddNewPart<WorksheetPart>();
        using (var writer = OpenXmlWriter.Create(worksheetPart))
        {
            writer.WriteStartElement(new Worksheet());
            writer.WriteStartElement(new SheetData());

            for (var i = 0; i < specialStrings.Length; i++)
            {
                writer.WriteStartElement(new Row { RowIndex = (uint)(i + 1) });
                _cellWriter.WriteCellValue(writer, $"A{i + 1}", specialStrings[i], typeof(string), null);
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        _cellWriter.FinalizeSharedStringTable();

        var sharedStrings = ReadSharedStringTable();
        Assert.That(sharedStrings.Count, Is.EqualTo(specialStrings.Length));

        foreach (var str in sharedStrings)
        {
            Assert.That(str, Is.Not.Null);
            Assert.That(str, Does.Not.Contain('\x00'));
        }
    }

    private List<string> ReadSharedStringTable()
    {
        var sharedStringPart = _workbookPart.SharedStringTablePart;
        if (sharedStringPart == null) return new List<string>();

        var strings = new List<string>();
        using (var reader = OpenXmlReader.Create(sharedStringPart))
        {
            while (reader.Read())
            {
                if (reader.ElementType == typeof(SharedStringItem))
                {
                    var item = (SharedStringItem)reader.LoadCurrentElement();
                    var text = item.InnerText;
                    strings.Add(text ?? string.Empty);
                }
            }
        }

        return strings;
    }

    private Dictionary<string, string> ReadCellValues()
    {
        var cellValues = new Dictionary<string, string>();

        foreach (var worksheetPart in _workbookPart.WorksheetParts)
        {
            if (worksheetPart.Worksheet == null)
            {
                worksheetPart.Worksheet = new Worksheet();
            }

            using (var reader = OpenXmlReader.Create(worksheetPart))
            {
                while (reader.Read())
                {
                    if (reader.ElementType == typeof(Cell))
                    {
                        var cell = (Cell)reader.LoadCurrentElement();
                        if (cell.CellReference?.Value != null && cell.CellValue?.Text != null)
                        {
                            cellValues[cell.CellReference.Value] = cell.CellValue.Text;
                        }
                    }
                }
            }
        }

        return cellValues;
    }
}

[TestFixture]
public class ExcelCellWriterIntegrationTests
{
    [Test]
    public void WriteCellValue_MixedDataTypes_ShouldOnlyUseSharedStringsForStrings()
    {
        var cellWriter = new ExcelCellWriter();
        using var memoryStream = new MemoryStream();
        using var document = SpreadsheetDocument.Create(memoryStream, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();

        cellWriter.InitializeSharedStringTable(workbookPart);

        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        using (var writer = OpenXmlWriter.Create(worksheetPart))
        {
            writer.WriteStartElement(new Worksheet());
            writer.WriteStartElement(new SheetData());
            writer.WriteStartElement(new Row());

            cellWriter.WriteCellValue(writer, "A1", "StringValue", typeof(string), null);
            cellWriter.WriteCellValue(writer, "B1", 123, typeof(int), null);
            cellWriter.WriteCellValue(writer, "C1", 45.67, typeof(double), null);
            cellWriter.WriteCellValue(writer, "D1", true, typeof(bool), null);
            cellWriter.WriteCellValue(writer, "E1", new DateTime(2025, 1, 1, 12, 0, 0), typeof(DateTime), null);
            cellWriter.WriteCellValue(writer, "F1", TimeSpan.FromHours(2), typeof(TimeSpan), null);
            cellWriter.WriteCellValue(writer, "G1", "AnotherString", typeof(string), null);

            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        cellWriter.FinalizeSharedStringTable();

        var sharedStringPart = workbookPart.SharedStringTablePart;
        Assert.That(sharedStringPart, Is.Not.Null);

        var sharedStrings = new List<string>();
        using (var reader = OpenXmlReader.Create(sharedStringPart))
        {
            while (reader.Read())
            {
                if (reader.ElementType == typeof(SharedStringItem))
                {
                    var item = (SharedStringItem)reader.LoadCurrentElement();
                    sharedStrings.Add(item.InnerText);
                }
            }
        }

        Assert.That(sharedStrings.Count, Is.EqualTo(2));
        CollectionAssert.Contains(sharedStrings, "StringValue");
        CollectionAssert.Contains(sharedStrings, "AnotherString");
    }

    [Test]
    public void LargeDataset_Performance_ShouldHandleEfficiently()
    {
        var cellWriter = new ExcelCellWriter();
        using var memoryStream = new MemoryStream();
        using var document = SpreadsheetDocument.Create(memoryStream, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();

        cellWriter.InitializeSharedStringTable(workbookPart);

        var testData = new List<string>();
        for (var i = 0; i < 100; i++)
        {
            testData.Add($"UniqueString{i}");
            for (var j = 0; j < 10; j++)
            {
                testData.Add($"RepeatedString{i % 10}");
            }
        }

        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        using (var writer = OpenXmlWriter.Create(worksheetPart))
        {
            writer.WriteStartElement(new Worksheet());
            writer.WriteStartElement(new SheetData());

            for (var i = 0; i < testData.Count; i++)
            {
                cellWriter.WriteCellValue(writer, $"A{i + 1}", testData[i], typeof(string), null);
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        cellWriter.FinalizeSharedStringTable();

        var sharedStringPart = workbookPart.SharedStringTablePart;
        Assert.That(sharedStringPart, Is.Not.Null);

        var uniqueStrings = new HashSet<string>();
        using (var reader = OpenXmlReader.Create(sharedStringPart))
        {
            while (reader.Read())
            {
                if (reader.ElementType == typeof(SharedStringItem))
                {
                    var item = (SharedStringItem)reader.LoadCurrentElement();
                    uniqueStrings.Add(item.InnerText);
                }
            }
        }

        Assert.That(uniqueStrings.Count, Is.EqualTo(110));
    }
}

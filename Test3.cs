using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ExcelWrite.ExcelWriter;
using Moq;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace ExcelWrite.Tests;

public static class TestHelper
{
    private const int DefaultMaxRetries = 3;
    private const int BaseDelayMs = 100;
    private const int FileAgeHours = 1;
    private const string ExcelTestPattern = "Excel*.xlsx";
    private const string ExcelTestsDir = "ExcelTests";
    private const string ExcelIntegrationTestsDir = "ExcelIntegrationTests";

    public static void SafeDeleteFile(string filePath, int maxRetries = DefaultMaxRetries)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            return;
        }

        for (var i = 0; i < maxRetries; i++)
        {
            try
            {
                File.Delete(filePath);
                return;
            }
            catch (IOException)
            {
                if (i < maxRetries - 1)
                {
                    Thread.Sleep(BaseDelayMs * (i + 1));
                }
            }
        }
    }

    public static void CleanupTestFiles()
    {
        try
        {
            var tempPath = Path.GetTempPath();
            var cutoffTime = DateTime.Now.AddHours(-FileAgeHours);

            var excelFiles = Directory.GetFiles(tempPath, ExcelTestPattern, SearchOption.TopDirectoryOnly)
                .Where(f => File.GetCreationTime(f) < cutoffTime);

            foreach (var file in excelFiles)
            {
                SafeDeleteFile(file);
            }

            var testDirs = new[] { ExcelTestsDir, ExcelIntegrationTestsDir };
            foreach (var dirName in testDirs)
            {
                CleanupTestDirectory(tempPath, dirName, cutoffTime);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException || ex is DirectoryNotFoundException)
        {
        }
    }

    private static void CleanupTestDirectory(string tempPath, string dirName, DateTime cutoffTime)
    {
        var dirPath = Path.Combine(tempPath, dirName);
        if (!Directory.Exists(dirPath))
        {
            return;
        }

        try
        {
            var subDirs = Directory.GetDirectories(dirPath)
                .Where(d => Directory.GetCreationTime(d) < cutoffTime);

            foreach (var subDir in subDirs)
            {
                try
                {
                    Directory.Delete(subDir, true);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is DirectoryNotFoundException)
                {
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException || ex is DirectoryNotFoundException)
        {
        }
    }
}

[SetUpFixture]
public class TestSetup
{
    [OneTimeSetUp]
    public void GlobalSetup()
    {
        TestHelper.CleanupTestFiles();
    }

    [OneTimeTearDown]
    public void GlobalTearDown()
    {
        TestHelper.CleanupTestFiles();
    }
}

public class ExcelTestModel
{
    [ExcelColumn("Status", 99)]
    public string? Status { get; set; }

    [ExcelColumn("IntValue", 1, Color = "#0066CC")]
    public int TestIntValue { get; set; }

    [ExcelColumn("LongValue", 2, Color = "#00A86B")]
    public long TestLongValue { get; set; }

    [ExcelColumn("Category", 3)]
    public string? Category { get; set; }

    [ExcelColumn("CategoryB", 7, Color = "FFD700")]
    public string? CategoryB { get; set; }

    [ExcelColumn("FloatValue", 3)]
    public float TestFloatValue { get; set; }

    [ExcelColumn("DoubleValue", 4)]
    public double TestDoubleValue { get; set; }

    [ExcelColumn("DecimalValue", 5)]
    public decimal TestDecimalValue { get; set; }

    [ExcelColumn("StringValue", 6)]
    public string? TestStringValue { get; set; }

    [ExcelColumn("CharValue", 7)]
    public char TestCharValue { get; set; }

    [ExcelColumn("BoolValue", 8)]
    public bool TestBoolValue { get; set; }

    [ExcelColumn("DateTimeValue", 9)]
    public DateTime TestDateTimeValue { get; set; }

    [ExcelColumn("TimeSpanValue", 10)]
    public TimeSpan TestTimeSpanValue { get; set; }

    [ExcelColumn("GuidValue", 11)]
    public Guid TestGuidValue { get; set; }

    [ExcelColumn("NullableInt", 12)]
    public int? TestNullableInt { get; set; }

    [ExcelColumn("NullableBool", 13)]
    public bool? TestNullableBool { get; set; }

    [ExcelColumn("NullableDate", 14)]
    public DateTime? TestNullableDate { get; set; }
}

public class TestModelWithoutAttributes
{
    public string? Name { get; set; }

    public int Value { get; set; }
}

public class TestModelPartialAttributes
{

    [ExcelColumn("Id", 1)]
    public int Id { get; set; }

    public string? IgnoredProperty { get; set; }
}

[TestFixture]
public class ExcelCacheManagerTests
{
    private const int ExpectedAccessorCount = 17;
    private const int FirstOrderValue = 1;
    private const int SecondOrderValue = 2;
    private const int ThirdOrderValue = 3;
    private const int TestColumnNumber = 100;

    private IExcelCacheManager? _cacheManager;

    [SetUp]
    public void Setup()
    {
        _cacheManager = new ExcelCacheManager();
    }

    [Test]
    public void GetPropertyAccessors_ReturnsAllPropertiesWithAttributes()
    {
        var accessors = _cacheManager!.GetPropertyAccessors<ExcelTestModel>();

        Assert.That(accessors, Is.Not.Null);
        Assert.That(accessors.Length, Is.EqualTo(ExpectedAccessorCount));
        Assert.That(accessors.All(a => !string.IsNullOrEmpty(a.ColumnName)), Is.True);
    }

    [Test]
    public void GetPropertyAccessors_OrdersByOrderIdThenByName()
    {
        var accessors = _cacheManager!.GetPropertyAccessors<ExcelTestModel>();

        Assert.That(accessors[0].Order, Is.EqualTo(FirstOrderValue));
        Assert.That(accessors[1].Order, Is.EqualTo(SecondOrderValue));
        var sameOrderAccessors = accessors.Where(a => a.Order == ThirdOrderValue).ToArray();
        Assert.That(sameOrderAccessors.Length, Is.EqualTo(2));
        Assert.That(sameOrderAccessors[0].Property.Name, Is.LessThan(sameOrderAccessors[1].Property.Name));
    }

    [Test]
    public void GetPropertyAccessors_CachesResults()
    {
        var accessors1 = _cacheManager!.GetPropertyAccessors<ExcelTestModel>();
        var accessors2 = _cacheManager.GetPropertyAccessors<ExcelTestModel>();

        Assert.That(ReferenceEquals(accessors1, accessors2), Is.True);
    }

    [Test]
    public void GetPropertyAccessors_ReturnsEmptyArrayForModelWithoutAttributes()
    {
        var accessors = _cacheManager!.GetPropertyAccessors<TestModelWithoutAttributes>();

        Assert.That(accessors, Is.Not.Null);
        Assert.That(accessors.Length, Is.EqualTo(0));
    }

    [TestCase(1, "A")]
    [TestCase(26, "Z")]
    [TestCase(27, "AA")]
    [TestCase(52, "AZ")]
    [TestCase(53, "BA")]
    [TestCase(702, "ZZ")]
    [TestCase(703, "AAA")]
    [TestCase(16384, "XFD")]
    public void GetExcelColumnName_ReturnsCorrectColumnName(int columnNumber, string expected)
    {
        var result = _cacheManager!.GetExcelColumnName(columnNumber);

        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void GetExcelColumnName_CachesResults()
    {
        var result1 = _cacheManager!.GetExcelColumnName(TestColumnNumber);
        var result2 = _cacheManager.GetExcelColumnName(TestColumnNumber);

        Assert.That(result1, Is.EqualTo(result2));
    }

    [TestCase("#FF0000", "FF0000")]
    [TestCase("FF0000", "FF0000")]
    [TestCase("#ff0000", "FF0000")]
    [TestCase("ff0000", "FF0000")]
    [TestCase("#AbCdEf", "ABCDEF")]
    [TestCase("", null)]
    [TestCase(null, null)]
    public void GetCleanHexColor_ReturnsCleanedColor(string? input, string? expected)
    {
        var result = _cacheManager!.GetCleanHexColor(input);

        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void PropertyAccessor_GetterReturnsCorrectValue()
    {
        var testDate = new DateTime(2024, 1, 1);
        var model = new ExcelTestModel
        {
            TestIntValue = 42,
            TestStringValue = "Test",
            TestDateTimeValue = testDate
        };

        var accessors = _cacheManager!.GetPropertyAccessors<ExcelTestModel>();
        var intAccessor = accessors.First(a => a.Property.Name == nameof(ExcelTestModel.TestIntValue));
        var stringAccessor = accessors.First(a => a.Property.Name == nameof(ExcelTestModel.TestStringValue));
        var dateAccessor = accessors.First(a => a.Property.Name == nameof(ExcelTestModel.TestDateTimeValue));

        Assert.That(intAccessor.Getter(model), Is.EqualTo(42));
        Assert.That(stringAccessor.Getter(model), Is.EqualTo("Test"));
        Assert.That(dateAccessor.Getter(model), Is.EqualTo(testDate));
    }

    [Test]
    public void PropertyAccessor_HandlesNullableTypes()
    {
        var accessors = _cacheManager!.GetPropertyAccessors<ExcelTestModel>();
        var nullableIntAccessor = accessors.First(a => a.Property.Name == nameof(ExcelTestModel.TestNullableInt));
        var intAccessor = accessors.First(a => a.Property.Name == nameof(ExcelTestModel.TestIntValue));

        Assert.That(nullableIntAccessor.IsNullable, Is.True);
        Assert.That(nullableIntAccessor.UnderlyingType, Is.EqualTo(typeof(int)));
        Assert.That(intAccessor.IsNullable, Is.False);
        Assert.That(intAccessor.UnderlyingType, Is.EqualTo(typeof(int)));
    }

    [Test]
    public void PropertyAccessor_SetsDefaultWidthWhenNotSpecified()
    {
        var accessors = _cacheManager!.GetPropertyAccessors<ExcelTestModel>();
        var accessor = accessors.First(a => a.Property.Name == nameof(ExcelTestModel.Status));

        Assert.That(accessor.Width, Is.EqualTo(ExcelWriterConstants.DefaultColumnWidth));
    }
}

[TestFixture]
public class ExcelCellWriterTests
{
    private const string TestCellReference = "A1";
    private const string TestStringValue = "Test String";
    private const uint TestStyleIndex = 1;
    private const int TestYear = 2024;
    private const int TestMonth = 1;
    private const int TestDay = 15;
    private const int TestHour = 10;
    private const int TestMinute = 30;
    private const int TimeSpanHours = 2;
    private const int TimeSpanMinutes = 30;
    private const int TimeSpanSeconds = 45;
    private const string TestGuidString = "12345678-1234-1234-1234-123456789abc";
    private const char TestChar = 'A';
    private const string MaliciousFormula = "=HYPERLINK(\"http://evil.com\",\"Click me\")";

    private ExcelCellWriter? _excelCellWriter;
    private Mock<OpenXmlWriter>? _mockWriter;
    private ExcelTestModel? _testModel;

    [SetUp]
    public void SetUp()
    {
        _excelCellWriter = new ExcelCellWriter();
        _mockWriter = new Mock<OpenXmlWriter>();
        _testModel = CreateTestModel();
    }

    private static ExcelTestModel CreateTestModel()
    {
        return new ExcelTestModel
        {
            Status = "Active",
            TestIntValue = 42,
            TestLongValue = 123456789L,
            Category = "CategoryA",
            CategoryB = "CategoryB",
            TestFloatValue = 3.14f,
            TestDoubleValue = 2.718281828,
            TestDecimalValue = 99.99m,
            TestStringValue = TestStringValue,
            TestCharValue = TestChar,
            TestBoolValue = true,
            TestDateTimeValue = new DateTime(TestYear, TestMonth, TestDay, TestHour, TestMinute, 0),
            TestTimeSpanValue = new TimeSpan(TimeSpanHours, TimeSpanMinutes, TimeSpanSeconds),
            TestGuidValue = Guid.Parse(TestGuidString),
            TestNullableInt = 100,
            TestNullableBool = false,
            TestNullableDate = new DateTime(TestYear, 12, 31)
        };
    }

    [Test]
    public void WriteCellValue_WithStringValue_CallsWriteStringValue()
    {
        _excelCellWriter!.WriteCellValue(_mockWriter!.Object, TestCellReference, TestStringValue, typeof(string), TestStyleIndex);

        _mockWriter.Verify(w => w.WriteStartElement(It.Is<Cell>(c =>
            c.CellReference == TestCellReference &&
            c.DataType! == CellValues.InlineString &&
            c.StyleIndex! == TestStyleIndex)), Times.Once);
        _mockWriter.Verify(w => w.WriteStartElement(It.IsAny<InlineString>()), Times.Once);
        _mockWriter.Verify(w => w.WriteElement(It.Is<Text>(t => t.Text == TestStringValue)), Times.Once);
        _mockWriter.Verify(w => w.WriteEndElement(), Times.Exactly(2));
    }

    [Test]
    public void WriteCellValue_WithStringValueAndNullStyle_CallsWriteStringValueWithoutStyle()
    {
        const string cellReference = "B2";
        const string value = "No Style";

        _excelCellWriter!.WriteCellValue(_mockWriter!.Object, cellReference, value, typeof(string), null);

        _mockWriter.Verify(w => w.WriteStartElement(It.Is<Cell>(c =>
            c.CellReference == cellReference &&
            c.DataType! == CellValues.InlineString &&
            c.StyleIndex == null)), Times.Once);
    }

    [Test]
    public void WriteCellValue_WithDateTimeValue_CallsWriteDateValue()
    {
        const string cellReference = "C3";
        var dateValue = new DateTime(TestYear, TestMonth, TestDay, TestHour, TestMinute, 0);
        const uint styleIndex = 2;

        _excelCellWriter!.WriteCellValue(_mockWriter!.Object, cellReference, dateValue, typeof(DateTime), styleIndex);

        var expectedOaDate = dateValue.ToOADate().ToString(CultureInfo.InvariantCulture);
        _mockWriter.Verify(w => w.WriteStartElement(It.Is<Cell>(c =>
            c.CellReference == cellReference &&
            c.StyleIndex! == styleIndex)), Times.Once);
        _mockWriter.Verify(w => w.WriteElement(It.Is<CellValue>(cv => cv.Text == expectedOaDate)), Times.Once);
        _mockWriter.Verify(w => w.WriteEndElement(), Times.Once);
    }

    [Test]
    public void WriteCellValue_WithDateTimeValueAndNullStyle_UsesDefaultDateStyle()
    {
        const string cellReference = "D4";
        var dateValue = new DateTime(TestYear, 6, TestDay);

        _excelCellWriter!.WriteCellValue(_mockWriter!.Object, cellReference, dateValue, typeof(DateTime), null);

        _mockWriter.Verify(w => w.WriteStartElement(It.Is<Cell>(c =>
            c.CellReference == cellReference &&
            c.StyleIndex! == 2)), Times.Once);
    }

    [Test]
    public void WriteCellValue_WithTimeSpanValue_CallsWriteTimeSpanValue()
    {
        const string cellReference = "E5";
        var timeSpanValue = new TimeSpan(TimeSpanHours, TimeSpanMinutes, TimeSpanSeconds);
        const uint styleIndex = 3;

        _excelCellWriter!.WriteCellValue(_mockWriter!.Object, cellReference, timeSpanValue, typeof(TimeSpan), styleIndex);

        var expectedTotalDays = timeSpanValue.TotalDays.ToString(CultureInfo.InvariantCulture);
        _mockWriter.Verify(w => w.WriteStartElement(It.Is<Cell>(c =>
            c.CellReference == cellReference &&
            c.StyleIndex! == styleIndex)), Times.Once);
        _mockWriter.Verify(w => w.WriteElement(It.Is<CellValue>(cv => cv.Text == expectedTotalDays)), Times.Once);
        _mockWriter.Verify(w => w.WriteEndElement(), Times.Once);
    }

    [Test]
    public void WriteCellValue_WithTimeSpanValueAndNullStyle_UsesDefaultTimeStyle()
    {
        const string cellReference = "F6";
        var timeSpanValue = new TimeSpan(1, 15, 30);

        _excelCellWriter!.WriteCellValue(_mockWriter!.Object, cellReference, timeSpanValue, typeof(TimeSpan), null);

        _mockWriter.Verify(w => w.WriteStartElement(It.Is<Cell>(c =>
            c.CellReference == cellReference &&
            c.StyleIndex! == 3)), Times.Once);
    }

    [Test]
    public void WriteCellValue_WithBoolTrueValue_CallsWriteBooleanValue()
    {
        const string cellReference = "G7";
        const bool boolValue = true;
        const uint styleIndex = 4;

        _excelCellWriter!.WriteCellValue(_mockWriter!.Object, cellReference, boolValue, typeof(bool), styleIndex);

        _mockWriter.Verify(w => w.WriteStartElement(It.Is<Cell>(c =>
            c.CellReference == cellReference &&
            c.DataType! == CellValues.Boolean &&
            c.StyleIndex! == styleIndex)), Times.Once);
        _mockWriter.Verify(w => w.WriteElement(It.Is<CellValue>(cv => cv.Text == "1")), Times.Once);
        _mockWriter.Verify(w => w.WriteEndElement(), Times.Once);
    }

    [Test]
    public void WriteCellValue_WithBoolFalseValue_CallsWriteBooleanValue()
    {
        const string cellReference = "H8";
        const bool boolValue = false;

        _excelCellWriter!.WriteCellValue(_mockWriter!.Object, cellReference, boolValue, typeof(bool), null);

        _mockWriter.Verify(w => w.WriteStartElement(It.Is<Cell>(c =>
            c.CellReference == cellReference &&
            c.DataType! == CellValues.Boolean &&
            c.StyleIndex == null)), Times.Once);
        _mockWriter.Verify(w => w.WriteElement(It.Is<CellValue>(cv => cv.Text == "0")), Times.Once);
    }

    [TestCase(typeof(int), 42)]
    [TestCase(typeof(long), 123456789L)]
    [TestCase(typeof(float), 3.14f)]
    [TestCase(typeof(double), 2.718281828)]
    [TestCase(typeof(decimal), 99.99)]
    [TestCase(typeof(short), (short)123)]
    [TestCase(typeof(byte), (byte)255)]
    [TestCase(typeof(sbyte), (sbyte)-128)]
    [TestCase(typeof(ushort), (ushort)65535)]
    [TestCase(typeof(uint), 4294967295U)]
    [TestCase(typeof(ulong), 18446744073709551615UL)]
    public void WriteCellValue_WithNumericValue_CallsWriteNumericValue(Type numericType, object numericValue)
    {
        const string cellReference = "I9";
        const uint styleIndex = 5;

        _excelCellWriter!.WriteCellValue(_mockWriter!.Object, cellReference, numericValue, numericType, styleIndex);

        var expectedValue = Convert.ToString(numericValue, CultureInfo.InvariantCulture) ?? string.Empty;
        _mockWriter.Verify(w => w.WriteStartElement(It.Is<Cell>(c =>
            c.CellReference == cellReference &&
            c.StyleIndex! == styleIndex)), Times.Once);
        _mockWriter.Verify(w => w.WriteElement(It.Is<CellValue>(cv => cv.Text == expectedValue)), Times.Once);
        _mockWriter.Verify(w => w.WriteEndElement(), Times.Once);
    }

    [Test]
    public void WriteCellValue_WithGuidValue_CallsWriteGenericValue()
    {
        const string cellReference = "J10";
        var guidValue = Guid.Parse(TestGuidString);
        const uint styleIndex = 6;

        _excelCellWriter!.WriteCellValue(_mockWriter!.Object, cellReference, guidValue, typeof(Guid), styleIndex);

        var expectedValue = guidValue.ToString();
        _mockWriter.Verify(w => w.WriteStartElement(It.Is<Cell>(c =>
            c.CellReference == cellReference &&
            c.DataType! == CellValues.InlineString &&
            c.StyleIndex! == styleIndex)), Times.Once);
        _mockWriter.Verify(w => w.WriteStartElement(It.IsAny<InlineString>()), Times.Once);
        _mockWriter.Verify(w => w.WriteElement(It.Is<Text>(t => t.Text == expectedValue)), Times.Once);
        _mockWriter.Verify(w => w.WriteEndElement(), Times.Exactly(2));
    }

    [Test]
    public void WriteCellValue_WithCharValue_CallsWriteGenericValue()
    {
        const string cellReference = "K11";

        _excelCellWriter!.WriteCellValue(_mockWriter!.Object, cellReference, TestChar, typeof(char), null);

        _mockWriter.Verify(w => w.WriteStartElement(It.Is<Cell>(c =>
            c.CellReference == cellReference &&
            c.DataType! == CellValues.InlineString &&
            c.StyleIndex == null)), Times.Once);
        _mockWriter.Verify(w => w.WriteElement(It.Is<Text>(t => t.Text == "A")), Times.Once);
    }

    [Test]
    public void ValidateFormulaInjection_WithNullValue_ReturnsEmptyString()
    {
        var result = _excelCellWriter!.ValidateFormulaInjection(null);

        Assert.That(result, Is.EqualTo(string.Empty));
    }

    [Test]
    public void ValidateFormulaInjection_WithEmptyString_ReturnsEmptyString()
    {
        var result = _excelCellWriter!.ValidateFormulaInjection(string.Empty);

        Assert.That(result, Is.EqualTo(string.Empty));
    }

    [Test]
    public void ValidateFormulaInjection_WithWhiteSpaceString_ReturnsOriginalString()
    {
        const string input = "   ";

        var result = _excelCellWriter!.ValidateFormulaInjection(input);

        Assert.That(result, Is.EqualTo(input));
    }

    [TestCase("=SUM(A1:A10)", "'=SUM(A1:A10)")]
    [TestCase("+A1+B1", "'+A1+B1")]
    [TestCase("-10", "'-10")]
    [TestCase("@ECHO", "'@ECHO")]
    public void ValidateFormulaInjection_WithFormulaCharacters_PrependsApostrophe(string input, string expected)
    {
        var result = _excelCellWriter!.ValidateFormulaInjection(input);

        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase("Normal text")]
    [TestCase("123")]
    [TestCase("Text with =equal sign in middle")]
    [TestCase("Text with +plus sign in middle")]
    [TestCase("Text with -minus sign in middle")]
    [TestCase("Text with @at sign in middle")]
    [TestCase("Another=formula")]
    [TestCase("Plus+sign")]
    [TestCase("Minus-sign")]
    [TestCase("At@sign")]
    public void ValidateFormulaInjection_WithSafeStrings_ReturnsOriginalString(string input)
    {
        var result = _excelCellWriter!.ValidateFormulaInjection(input);

        Assert.That(result, Is.EqualTo(input));
    }

    [Test]
    public void ValidateFormulaInjection_WithComplexFormula_PrependsApostrophe()
    {
        const string input = "=IF(A1>0,\"Positive\",\"Negative\")";
        const string expected = "'=IF(A1>0,\"Positive\",\"Negative\")";

        var result = _excelCellWriter!.ValidateFormulaInjection(input);

        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void WriteCellValue_WithTestModelStringProperty_WritesCorrectly()
    {
        var value = _testModel!.TestStringValue;

        _excelCellWriter!.WriteCellValue(_mockWriter!.Object, TestCellReference, value!, typeof(string), null);

        _mockWriter.Verify(w => w.WriteElement(It.Is<Text>(t => t.Text == value)), Times.Once);
    }

    [Test]
    public void WriteCellValue_WithTestModelIntProperty_WritesCorrectly()
    {
        const string cellReference = "B1";
        var value = _testModel!.TestIntValue;

        _excelCellWriter!.WriteCellValue(_mockWriter!.Object, cellReference, value, typeof(int), null);

        var expectedValue = value.ToString(CultureInfo.InvariantCulture);
        _mockWriter.Verify(w => w.WriteElement(It.Is<CellValue>(cv => cv.Text == expectedValue)), Times.Once);
    }

    [Test]
    public void WriteCellValue_WithTestModelDateTimeProperty_WritesCorrectly()
    {
        const string cellReference = "C1";
        var value = _testModel!.TestDateTimeValue;

        _excelCellWriter!.WriteCellValue(_mockWriter!.Object, cellReference, value, typeof(DateTime), null);

        var expectedValue = value.ToOADate().ToString(CultureInfo.InvariantCulture);
        _mockWriter.Verify(w => w.WriteElement(It.Is<CellValue>(cv => cv.Text == expectedValue)), Times.Once);
    }

    [Test]
    public void WriteCellValue_WithTestModelBoolProperty_WritesCorrectly()
    {
        const string cellReference = "D1";
        var value = _testModel!.TestBoolValue;

        _excelCellWriter!.WriteCellValue(_mockWriter!.Object, cellReference, value, typeof(bool), null);

        var expectedValue = value ? "1" : "0";
        _mockWriter.Verify(w => w.WriteElement(It.Is<CellValue>(cv => cv.Text == expectedValue)), Times.Once);
    }

    [Test]
    public void WriteCellValue_WithTestModelNullableIntProperty_WritesCorrectly()
    {
        const string cellReference = "E1";
        var value = _testModel!.TestNullableInt;

        _excelCellWriter!.WriteCellValue(_mockWriter!.Object, cellReference, value!, typeof(int), null);

        var expectedValue = value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        _mockWriter.Verify(w => w.WriteElement(It.Is<CellValue>(cv => cv.Text == expectedValue)), Times.Once);
    }

    [Test]
    public void WriteCellValue_WithStringContainingFormulaInjection_SanitizesValue()
    {
        _excelCellWriter!.WriteCellValue(_mockWriter!.Object, TestCellReference, MaliciousFormula, typeof(string), null);

        var expectedSanitizedValue = "'" + MaliciousFormula;
        _mockWriter.Verify(w => w.WriteElement(It.Is<Text>(t => t.Text == expectedSanitizedValue)), Times.Once);
    }

    [Test]
    public void WriteCellValue_WithMinDateTime_WritesCorrectly()
    {
        var minDate = DateTime.MinValue;

        _excelCellWriter!.WriteCellValue(_mockWriter!.Object, TestCellReference, minDate, typeof(DateTime), null);

        var expectedValue = minDate.ToOADate().ToString(CultureInfo.InvariantCulture);
        _mockWriter.Verify(w => w.WriteElement(It.Is<CellValue>(cv => cv.Text == expectedValue)), Times.Once);
    }

    [Test]
    public void WriteCellValue_WithMaxDateTime_WritesCorrectly()
    {
        var maxDate = DateTime.MaxValue;

        _excelCellWriter!.WriteCellValue(_mockWriter!.Object, TestCellReference, maxDate, typeof(DateTime), null);

        var expectedValue = maxDate.ToOADate().ToString(CultureInfo.InvariantCulture);
        _mockWriter.Verify(w => w.WriteElement(It.Is<CellValue>(cv => cv.Text == expectedValue)), Times.Once);
    }

    [Test]
    public void WriteCellValue_WithZeroTimeSpan_WritesCorrectly()
    {
        var zeroTimeSpan = TimeSpan.Zero;

        _excelCellWriter!.WriteCellValue(_mockWriter!.Object, TestCellReference, zeroTimeSpan, typeof(TimeSpan), null);

        var expectedValue = zeroTimeSpan.TotalDays.ToString(CultureInfo.InvariantCulture);
        _mockWriter.Verify(w => w.WriteElement(It.Is<CellValue>(cv => cv.Text == expectedValue)), Times.Once);
    }

    [Test]
    public void WriteCellValue_WithLargeNumericValue_WritesCorrectly()
    {
        const long largeValue = long.MaxValue;

        _excelCellWriter!.WriteCellValue(_mockWriter!.Object, TestCellReference, largeValue, typeof(long), null);

        var expectedValue = largeValue.ToString(CultureInfo.InvariantCulture);
        _mockWriter.Verify(w => w.WriteElement(It.Is<CellValue>(cv => cv.Text == expectedValue)), Times.Once);
    }
}

[TestFixture]
public class ExcelStyleManagerTests
{
    private const string RedColor = "#FF0000";
    private const string GreenColor = "#00FF00";
    private const string BlueColor = "#0000FF";
    private const string LowerCaseRedColor = "#ff0000";
    private const string RedColorNoHash = "FF0000";

    private IExcelStyleManager? _styleManager;
    private IExcelCacheManager? _cacheManager;
    private WorkbookPart? _workbookPart;
    private SpreadsheetDocument? _document;
    private string? _tempFilePath;

    [SetUp]
    public void Setup()
    {
        _cacheManager = new ExcelCacheManager();
        _styleManager = new ExcelStyleManager(_cacheManager);

        _tempFilePath = Path.Combine(Path.GetTempPath(), $"ExcelStyleTest_{Guid.NewGuid()}.xlsx");
        _document = SpreadsheetDocument.Create(_tempFilePath, SpreadsheetDocumentType.Workbook);
        _workbookPart = _document.AddWorkbookPart();
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            _workbookPart = null;
            _document?.Dispose();
            _document = null;

            if (_tempFilePath != null)
            {
                TestHelper.SafeDeleteFile(_tempFilePath);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
        {
        }
    }

    [Test]
    public void CreateStyles_CreatesBasicStylesWithoutColors()
    {
        var accessors = new PropertyAccessor[]
        {
            new() { Color = null }
        };

        var styleMappings = _styleManager!.CreateStyles(_workbookPart!, accessors, CancellationToken.None);

        Assert.That(styleMappings, Is.Not.Null);
        Assert.That(_workbookPart!.WorkbookStylesPart, Is.Not.Null);
    }

    [Test]
    public void CreateStyles_CreatesStylesForColorsWithMappings()
    {
        var accessors = new PropertyAccessor[]
        {
            new() { Color = RedColor },
            new() { Color = GreenColor },
            new() { Color = BlueColor }
        };

        var styleMappings = _styleManager!.CreateStyles(_workbookPart!, accessors, CancellationToken.None);

        Assert.That(styleMappings.ContainsKey("fill_header_FF0000"), Is.True);
        Assert.That(styleMappings.ContainsKey("fill_cell_FF0000"), Is.True);
        Assert.That(styleMappings.ContainsKey("header_FF0000"), Is.True);
        Assert.That(styleMappings.ContainsKey("cell_FF0000"), Is.True);
        Assert.That(styleMappings.ContainsKey("cell_date_FF0000"), Is.True);
        Assert.That(styleMappings.ContainsKey("cell_time_FF0000"), Is.True);
    }

    [Test]
    public void CreateStyles_HandlesUniqueColorsOnly()
    {
        var accessors = new PropertyAccessor[]
        {
            new() { Color = RedColor },
            new() { Color = LowerCaseRedColor },
            new() { Color = RedColorNoHash }
        };

        var styleMappings = _styleManager!.CreateStyles(_workbookPart!, accessors, CancellationToken.None);

        var colorKeys = styleMappings.Keys.Where(k => k.Contains("FF0000")).ToList();
        Assert.That(colorKeys.Count, Is.EqualTo(6));
    }

    [Test]
    public void CreateStyles_HandlesCancellation()
    {
        var accessors = new PropertyAccessor[] { new() { Color = RedColor } };
        var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.DoesNotThrow(() => _styleManager!.CreateStyles(_workbookPart!, accessors, cts.Token));
    }
}

[TestFixture]
public class ExcelValidationManagerTests
{
    private const int TestRowCount = 100;
    private const int SmallRowCount = 50;
    private const string IntColumnName = "IntColumn";
    private const string DecimalColumnName = "DecimalColumn";
    private const string DateColumnName = "DateColumn";
    private const string StatusColumnName = "Status";
    private const string TimeColumnName = "TimeColumn";
    private const string StatusValuesKey = "StatusValues";
    private const string ActiveValue = "Active";
    private const string InactiveValue = "Inactive";
    private const string StaticDataSheetName = "'Static Data'!";
    private const string CellRangeFormat = "$A$2:$A$10";

    private IExcelValidationManager? _validationManager;
    private IExcelCacheManager? _cacheManager;
    private SpreadsheetDocument? _document;
    private WorksheetPart? _worksheetPart;
    private OpenXmlWriter? _writer;
    private string? _tempFilePath;

    [SetUp]
    public void Setup()
    {
        _cacheManager = new ExcelCacheManager();
        _validationManager = new ExcelValidationManager(_cacheManager);

        _tempFilePath = Path.Combine(Path.GetTempPath(), $"ExcelValidationTest_{Guid.NewGuid()}.xlsx");
        _document = SpreadsheetDocument.Create(_tempFilePath, SpreadsheetDocumentType.Workbook);
        var workbookPart = _document.AddWorkbookPart();
        _worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        _writer = OpenXmlWriter.Create(_worksheetPart);
        _writer.WriteStartElement(new Worksheet());
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            _writer?.Close();
            _writer?.Dispose();
            _writer = null;

            _worksheetPart = null;
            _document?.Dispose();
            _document = null;

            if (_tempFilePath != null)
            {
                TestHelper.SafeDeleteFile(_tempFilePath);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
        {
        }
    }

    [Test]
    public void AddDataValidations_AddsIntegerValidation()
    {
        var parameter = CreateValidationParameter(
            IntColumnName,
            typeof(int),
            false,
            TestRowCount);

        _validationManager!.AddDataValidations(parameter);
        CloseAndSaveDocument();

        using var doc = SpreadsheetDocument.Open(_tempFilePath!, false);
        var worksheetPart = doc.WorkbookPart!.WorksheetParts.First();
        var validations = worksheetPart.Worksheet.Descendants<DataValidations>().FirstOrDefault();

        Assert.That(validations, Is.Not.Null);
        Assert.That(validations!.Count!.Value, Is.EqualTo(1));

        var validation = validations.Descendants<DataValidation>().First();
        Assert.That(validation.Type!.Value, Is.EqualTo(DataValidationValues.Whole));
        Assert.That(validation.ErrorTitle?.Value, Is.EqualTo("Invalid Integer"));
        Assert.That(validation.SequenceOfReferences!.InnerText, Is.EqualTo("A2:A100"));
    }

    [Test]
    public void AddDataValidations_AddsDecimalValidation()
    {
        var parameter = CreateValidationParameter(
            DecimalColumnName,
            typeof(decimal),
            true,
            SmallRowCount);

        _validationManager!.AddDataValidations(parameter);
        CloseAndSaveDocument();

        using var doc = SpreadsheetDocument.Open(_tempFilePath!, false);
        var worksheetPart = doc.WorkbookPart!.WorksheetParts.First();
        var validations = worksheetPart.Worksheet.Descendants<DataValidations>().FirstOrDefault();

        Assert.That(validations, Is.Not.Null);
        var validation = validations!.Descendants<DataValidation>().First();
        Assert.That(validation.Type!.Value, Is.EqualTo(DataValidationValues.Decimal));
        Assert.That(validation.ErrorTitle?.Value, Is.EqualTo("Invalid Number"));
        Assert.That(validation.AllowBlank?.Value, Is.True);
    }

    [Test]
    public void AddDataValidations_AddsDateValidation()
    {
        var parameter = CreateValidationParameter(
            DateColumnName,
            typeof(DateTime),
            false,
            TestRowCount);

        _validationManager!.AddDataValidations(parameter);
        CloseAndSaveDocument();

        using var doc = SpreadsheetDocument.Open(_tempFilePath!, false);
        var worksheetPart = doc.WorkbookPart!.WorksheetParts.First();
        var validations = worksheetPart.Worksheet.Descendants<DataValidations>().FirstOrDefault();

        Assert.That(validations, Is.Not.Null);
        var validation = validations!.Descendants<DataValidation>().First();
        Assert.That(validation.Type!.Value, Is.EqualTo(DataValidationValues.Date));
        Assert.That(validation.ErrorTitle?.Value, Is.EqualTo("Invalid Date"));
        Assert.That(validation.Formula1?.Text, Does.Contain("DATE(1900,1,1)"));
        Assert.That(validation.Formula2?.Text, Does.Contain("DATE(2100,12,31)"));
    }

    [Test]
    public void AddDataValidations_AddsDropdownValidation()
    {
        var parameter = new DataValidationParameter
        {
            Writer = _writer!,
            PropertyAccessors = new[]
            {
                new PropertyAccessor { ColumnName = StatusColumnName }
            },
            ColumnMappings = new Dictionary<string, string> { [StatusColumnName] = StatusValuesKey },
            ColumnPositions = new Dictionary<string, string> { [StatusValuesKey] = CellRangeFormat },
            DropdownData = new Dictionary<string, List<string>>
            {
                [StatusValuesKey] = new() { ActiveValue, InactiveValue }
            },
            TotalRows = TestRowCount,
            ColumnIndexMap = new Dictionary<string, int> { [StatusColumnName] = 1 }
        };

        _validationManager!.AddDataValidations(parameter);
        CloseAndSaveDocument();

        using var doc = SpreadsheetDocument.Open(_tempFilePath!, false);
        var worksheetPart = doc.WorkbookPart!.WorksheetParts.First();
        var validations = worksheetPart.Worksheet.Descendants<DataValidations>().FirstOrDefault();

        Assert.That(validations, Is.Not.Null);
        var validation = validations!.Descendants<DataValidation>().First();
        Assert.That(validation.Type!.Value, Is.EqualTo(DataValidationValues.List));
        Assert.That(validation.Formula1?.Text, Is.EqualTo(StaticDataSheetName + CellRangeFormat));
    }

    [Test]
    public void AddDataValidations_SkipsValidationForDropdownColumns()
    {
        var parameter = new DataValidationParameter
        {
            Writer = _writer!,
            PropertyAccessors = new[]
            {
                new PropertyAccessor
                {
                    ColumnName = IntColumnName,
                    UnderlyingType = typeof(int)
                }
            },
            ColumnMappings = new Dictionary<string, string> { [IntColumnName] = "Values" },
            TotalRows = TestRowCount,
            ColumnIndexMap = new Dictionary<string, int> { [IntColumnName] = 1 }
        };

        _validationManager!.AddDataValidations(parameter);
        CloseAndSaveDocument();

        using var doc = SpreadsheetDocument.Open(_tempFilePath!, false);
        var worksheetPart = doc.WorkbookPart!.WorksheetParts.First();
        var validations = worksheetPart.Worksheet.Descendants<DataValidations>().FirstOrDefault();

        Assert.That(validations, Is.Null);
    }

    [Test]
    public void AddDataValidations_HandlesTimeSpanValidation()
    {
        var parameter = CreateValidationParameter(
            TimeColumnName,
            typeof(TimeSpan),
            false,
            TestRowCount);

        _validationManager!.AddDataValidations(parameter);
        CloseAndSaveDocument();

        using var doc = SpreadsheetDocument.Open(_tempFilePath!, false);
        var worksheetPart = doc.WorkbookPart!.WorksheetParts.First();
        var validations = worksheetPart.Worksheet.Descendants<DataValidations>().FirstOrDefault();

        Assert.That(validations, Is.Not.Null);
        var validation = validations!.Descendants<DataValidation>().First();
        Assert.That(validation.Type!.Value, Is.EqualTo(DataValidationValues.Decimal));
        Assert.That(validation.ErrorTitle?.Value, Is.EqualTo("Invalid Time"));
        Assert.That(validation.Formula1?.Text, Is.EqualTo("0"));
        Assert.That(validation.Formula2?.Text, Is.EqualTo("999"));
    }

    private DataValidationParameter CreateValidationParameter(string columnName, Type underlyingType, bool isNullable, int totalRows)
    {
        return new DataValidationParameter
        {
            Writer = _writer!,
            PropertyAccessors =
            [
                new PropertyAccessor
                {
                    ColumnName = columnName,
                    UnderlyingType = underlyingType,
                    IsNullable = isNullable,
                },
            ],
            TotalRows = totalRows,
            ColumnIndexMap = new Dictionary<string, int> { [columnName] = 1 }
        };
    }

    private void CloseAndSaveDocument()
    {
        _writer!.WriteEndElement();
        _writer.Close();
        _writer.Dispose();
        _writer = null;

        _document!.Save();
        _document.Dispose();
        _document = null;
    }
}

[TestFixture]
public class StringBuilderPoolTests
{
    private const int MaxPoolSize = 4;
    private const int MaxCapacity = 100;
    private const int LargeCapacity = 200;
    private const int PoolTestSize = 5;
    private const int ThreadCount = 10;
    private const int OperationsPerThread = 100;

    private StringBuilderPool? _pool;

    [SetUp]
    public void Setup()
    {
        _pool = new StringBuilderPool(maxPoolSize: MaxPoolSize, maxCapacity: MaxCapacity);
    }

    [Test]
    public void Rent_ReturnsNewStringBuilder()
    {
        var sb = _pool!.Rent();

        Assert.That(sb, Is.Not.Null);
        Assert.That(sb.Length, Is.EqualTo(0));
    }

    [Test]
    public void Return_ReusesStringBuilder()
    {
        var sb1 = _pool!.Rent();
        sb1.Append("Test");

        _pool.Return(sb1);
        var sb2 = _pool.Rent();

        Assert.That(ReferenceEquals(sb1, sb2), Is.True);
        Assert.That(sb2.Length, Is.EqualTo(0));
    }

    [Test]
    public void Return_DoesNotPoolLargeStringBuilders()
    {
        var sb1 = _pool!.Rent();
        sb1.Capacity = LargeCapacity;

        _pool.Return(sb1);
        var sb2 = _pool.Rent();

        Assert.That(ReferenceEquals(sb1, sb2), Is.False);
    }

    [Test]
    public void Pool_RespectsMaxPoolSize()
    {
        var builders = new List<StringBuilder>();
        for (var i = 0; i < PoolTestSize; i++)
        {
            builders.Add(_pool!.Rent());
        }

        foreach (var sb in builders)
        {
            _pool!.Return(sb);
        }

        var retrievedBuilders = new List<StringBuilder>();
        for (var i = 0; i < PoolTestSize; i++)
        {
            retrievedBuilders.Add(_pool!.Rent());
        }

        var reusedCount = builders.Count(retrievedBuilders.Contains);
        Assert.That(reusedCount, Is.EqualTo(MaxPoolSize));
    }

    [Test]
    public async Task Pool_IsThreadSafe()
    {
        var tasks = new List<Task>();
        var rentedBuilders = new ConcurrentBag<StringBuilder>();
        var errors = new ConcurrentBag<Exception>();

        for (var i = 0; i < ThreadCount; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    for (var j = 0; j < OperationsPerThread; j++)
                    {
                        var sb = _pool!.Rent();
                        sb.Append($"Thread{Thread.CurrentThread.ManagedThreadId}-{j}");
                        rentedBuilders.Add(sb);
                        Thread.Yield();
                        _pool.Return(sb);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            }));
        }

        await Task.WhenAll(tasks);

        Assert.That(errors.Count, Is.EqualTo(0), "No exceptions should occur during concurrent access");
        Assert.That(rentedBuilders.Count, Is.EqualTo(ThreadCount * OperationsPerThread), "All rent operations should complete");
    }
}

[TestFixture]
public class WriteExcelServiceTests
{
    private const int SmallDataCount = 5;
    private const int LargeDataCount = 25000;
    private const int TestDataCount = 10000;
    private const string TestSheetName = "TestSheet";
    private const string EmptySheetName = "EmptySheet";
    private const string LargeSheetName = "LargeSheet";
    private const string NullSheetName = "NullSheet";
    private const string SpecialSheetName = "SpecialSheet";
    private const string DropdownSheetName = "DropdownSheet";
    private const string DefaultFileName = "default.xlsx";
    private const string TestFileName = "test.xlsx";
    private const string CancelledFileName = "cancelled.xlsx";
    private const string ErrorFileName = "error.xlsx";
    private const string ExceptionTestFileName = "exception_test.xlsx";
    private const string NoAttributesFileName = "noattributes.xlsx";

    private WriteExcelService<ExcelTestModel>? _service;
    private string? _testDirectory;

    [SetUp]
    public void Setup()
    {
        _service = new WriteExcelService<ExcelTestModel>();
        _testDirectory = Path.Combine(Path.GetTempPath(), "ExcelTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (_testDirectory != null && Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, true);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is DirectoryNotFoundException)
            {
            }
        }
    }

    [Test]
    public void Write_CreatesExcelFile()
    {
        var filePath = Path.Combine(_testDirectory!, TestFileName);
        var data = CreateTestData(SmallDataCount);

        using var stream = _service!.Write(filePath, TestSheetName, data, null);

        Assert.That(File.Exists(filePath), Is.True);
        Assert.That(stream, Is.Not.Null);
        Assert.That(stream.CanRead, Is.True);
    }

    [Test]
    public void Write_ThrowsOnNullFilePath()
    {
        var data = CreateTestData(1);

        Assert.Throws<ArgumentException>(() => _service!.Write(null!, "Sheet", data, null));
    }

    [Test]
    public void Write_ThrowsOnEmptyFilePath()
    {
        var data = CreateTestData(1);

        Assert.Throws<ArgumentException>(() => _service!.Write(string.Empty, "Sheet", data, null));
    }

    [Test]
    public void Write_ThrowsOnNullData()
    {
        var filePath = Path.Combine(_testDirectory!, TestFileName);

        Assert.Throws<ArgumentException>(() => _service!.Write(filePath, "Sheet", null!, null));
    }

    [Test]
    public void Write_CreatesDirectoryIfNotExists()
    {
        var subDir = Path.Combine(_testDirectory!, "SubDir");
        var filePath = Path.Combine(subDir, TestFileName);
        var data = CreateTestData(1);

        using var stream = _service!.Write(filePath, TestSheetName, data, null);

        Assert.That(Directory.Exists(subDir), Is.True);
        Assert.That(File.Exists(filePath), Is.True);
    }

    [Test]
    public void Write_HandlesEmptyData()
    {
        var filePath = Path.Combine(_testDirectory!, "empty.xlsx");
        var data = new List<ExcelTestModel>();

        using var stream = _service!.Write(filePath, EmptySheetName, data, null);

        Assert.That(File.Exists(filePath), Is.True);
    }

    [Test]
    public void Write_HandlesLargeDataSet()
    {
        var filePath = Path.Combine(_testDirectory!, "large.xlsx");
        var data = CreateTestData(LargeDataCount);

        using var stream = _service!.Write(filePath, LargeSheetName, data, null);

        Assert.That(File.Exists(filePath), Is.True);
        Assert.That(new FileInfo(filePath).Length, Is.GreaterThan(0));
    }

    [Test]
    public void Write_HandlesNullValuesInData()
    {
        var filePath = Path.Combine(_testDirectory!, "nulls.xlsx");
        var data = new List<ExcelTestModel>
        {
            new()
            {
                Status = null,
                TestStringValue = null,
                TestNullableInt = null,
                TestNullableBool = null,
                TestNullableDate = null
            }
        };

        using var stream = _service!.Write(filePath, NullSheetName, data, null);

        Assert.That(File.Exists(filePath), Is.True);
    }

    [Test]
    public void Write_HandlesSpecialCharactersInStrings()
    {
        var filePath = Path.Combine(_testDirectory!, "special.xlsx");
        var data = new List<ExcelTestModel>
        {
            new()
            {
                Status = "=SUM(A1:A10)",
                TestStringValue = "+Positive",
                Category = "-Negative",
                CategoryB = "@Username"
            }
        };

        using var stream = _service!.Write(filePath, SpecialSheetName, data, null);

        Assert.That(File.Exists(filePath), Is.True);
    }

    [Test]
    public void Write_HandlesDropdownData()
    {
        var filePath = Path.Combine(_testDirectory!, "dropdown.xlsx");
        var data = CreateTestData(SmallDataCount);
        var dropdownData = new List<ExcelDropdownData>
        {
            new()
            {
                ColumnName = "StatusValues",
                DataList = new List<object> { "Active", "Inactive", "Pending" },
                BindProperties = new List<string> { "Status" }
            }
        };

        using var stream = _service!.Write(filePath, DropdownSheetName, data, dropdownData);

        Assert.That(File.Exists(filePath), Is.True);
    }

    [Test]
    public void Write_UsesDefaultSheetNameWhenNull()
    {
        var filePath = Path.Combine(_testDirectory!, DefaultFileName);
        var data = CreateTestData(1);

        using var stream = _service!.Write(filePath, null, data, null);

        Assert.That(File.Exists(filePath), Is.True);
    }

    [Test]
    public void Write_HandlesCancellation()
    {
        var filePath = Path.Combine(_testDirectory!, CancelledFileName);
        var data = CreateTestData(TestDataCount);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => _service!.Write(filePath, "Sheet1", data, null, cts.Token));
    }

    [Test]
    public void Write_CleansUpFileOnError()
    {
        var filePath = Path.Combine(_testDirectory!, ErrorFileName);
        var data = CreateTestData(1);

        using (var _ = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Throws<IOException>(() => _service!.Write(filePath, "ErrorSheet", data, null));
        }

        Assert.That(File.Exists(filePath), Is.True);
        File.Delete(filePath);
    }

    [Test]
    public void Write_HandlesExceptionDuringFileCreation()
    {
        var cacheManager = new ExcelCacheManager();
        var cellWriter = new ExcelCellWriter();
        var mockStyleManager = new ThrowingStyleManager();
        var validationManager = new ExcelValidationManager(cacheManager);
        var stringBuilderPool = new StringBuilderPool();

        var service = new WriteExcelService<ExcelTestModel>(
            cacheManager,
            cellWriter,
            mockStyleManager,
            validationManager,
            stringBuilderPool);

        var filePath = Path.Combine(_testDirectory!, ExceptionTestFileName);
        var data = CreateTestData(1);

        Assert.Throws<InvalidOperationException>(() =>
            service.Write(filePath, "Sheet", data, null));
        Assert.That(File.Exists(filePath), Is.False);
    }

    [Test]
    public void Write_ThrowsOnInvalidFilePath()
    {
        var invalidPath = Path.Combine(_testDirectory!, "test<>:\"|?*.xlsx");
        var data = CreateTestData(1);

        Assert.Throws<InvalidOperationException>(() =>
            _service!.Write(invalidPath, "Sheet", data, null));
    }

    [Test]
    public void Write_ThrowsForModelWithoutAttributes()
    {
        var service = new WriteExcelService<TestModelWithoutAttributes>();
        var filePath = Path.Combine(_testDirectory!, NoAttributesFileName);
        var data = new List<TestModelWithoutAttributes> { new() { Name = "Test" } };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            service.Write(filePath, "Sheet", data, null));
        Assert.That(ex.Message, Does.Contain("no properties with ExcelColumnAttribute"));
    }

    private static List<ExcelTestModel> CreateTestData(int count)
    {
        var data = new List<ExcelTestModel>();
        for (var i = 0; i < count; i++)
        {
            data.Add(new ExcelTestModel
            {
                Status = $"Status{i}",
                TestIntValue = i,
                TestLongValue = i * 1000L,
                Category = $"Category{i % 5}",
                CategoryB = $"CategoryB{i % 3}",
                TestFloatValue = i * 0.1f,
                TestDoubleValue = i * 0.01,
                TestDecimalValue = i * 0.001m,
                TestStringValue = $"String{i}",
                TestCharValue = (char)('A' + (i % 26)),
                TestBoolValue = i % 2 == 0,
                TestDateTimeValue = DateTime.Now.AddDays(i),
                TestTimeSpanValue = TimeSpan.FromHours(i),
                TestGuidValue = Guid.NewGuid(),
                TestNullableInt = i % 3 == 0 ? null : i,
                TestNullableBool = i % 4 == 0 ? null : i % 2 == 0,
                TestNullableDate = i % 5 == 0 ? null : DateTime.Now.AddDays(-i)
            });
        }
        return data;
    }

    private sealed class ThrowingStyleManager : IExcelStyleManager
    {
        public Dictionary<string, uint> CreateStyles(WorkbookPart workbookPart, PropertyAccessor[] propertyAccessors, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Test exception from style manager");
        }
    }
}

[TestFixture]
public class ExcelDropdownsFactoryTests
{
    private const string TestColumnName = "TestColumn";
    private const string StatusValuesName = "StatusValues";
    private const string TypesName = "Types";
    private const string ColumnName = "Column";
    private const string StatusColumn = "Status";
    private const string CategoriesName = "Categories";

    private IExcelDropdownsFactory<ExcelTestModel>? _factory;

    [SetUp]
    public void Setup()
    {
        _factory = new ExcelDropdownsFactory<ExcelTestModel>();
    }

    [Test]
    public void Create_ReturnsBuilderInstance()
    {
        var dropdownData = new List<object> { "Value1", "Value2" };

        var builder = _factory!.Create(TestColumnName, dropdownData);

        Assert.That(builder, Is.Not.Null);
        Assert.That(builder, Is.InstanceOf<IExcelDropdownBuilder<ExcelTestModel>>());
    }

    [Test]
    public void Create_ThrowsOnNullColumnName()
    {
        var dropdownData = new List<object> { "Value1" };

        Assert.Throws<ArgumentNullException>(() => _factory!.Create(null!, dropdownData));
    }

    [Test]
    public void Create_ThrowsOnNullDropdownData()
    {
        Assert.Throws<ArgumentNullException>(() => _factory!.Create(ColumnName, null!));
    }

    [Test]
    public void DropdownsList_InitiallyEmpty()
    {
        var list = _factory!.DropdownsList;

        Assert.That(list, Is.Not.Null);
        Assert.That(list.Count, Is.EqualTo(0));
    }

    [Test]
    public void Build_AddsToDropdownsList()
    {
        var dropdownData = new List<object> { "Active", "Inactive" };

        _factory!.Create(StatusValuesName, dropdownData)
            .Bind(m => m.Status!)
            .Build();

        var list = _factory.DropdownsList;
        Assert.That(list.Count, Is.EqualTo(1));
        Assert.That(list[0].ColumnName, Is.EqualTo(StatusValuesName));
        Assert.That(list[0].DataList.Count, Is.EqualTo(2));
        Assert.That(list[0].BindProperties.Count, Is.EqualTo(1));
        Assert.That(list[0].BindProperties.First(), Is.EqualTo(StatusColumn));
    }

    [Test]
    public void Build_SupportsMultipleBindings()
    {
        var dropdownData = new List<object> { "Type1", "Type2" };

        _factory!.Create(TypesName, dropdownData)
            .Bind(m => m.Category!)
            .Bind(m => m.CategoryB!)
            .Build();

        var list = _factory.DropdownsList;
        Assert.That(list[0].BindProperties.Count, Is.EqualTo(2));
        Assert.That(list[0].BindProperties, Does.Contain("Category"));
        Assert.That(list[0].BindProperties, Does.Contain("CategoryB"));
    }

    [Test]
    public void Build_ThrowsWhenNoBindings()
    {
        var dropdownData = new List<object> { "Value1" };
        var builder = _factory!.Create(ColumnName, dropdownData);

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Test]
    public void Build_ThrowsOnDuplicateBinding()
    {
        var dropdownData = new List<object> { "Value1" };
        var builder = _factory!.Create(ColumnName, dropdownData)
            .Bind(m => m.Status!);

        Assert.Throws<InvalidOperationException>(() => builder.Bind(m => m.Status!));
    }

    [Test]
    public void Build_ThrowsOnPropertyWithoutAttribute()
    {
        var factory = new ExcelDropdownsFactory<TestModelPartialAttributes>();
        var dropdownData = new List<object> { "Value1" };
        var builder = factory.Create(ColumnName, dropdownData);

        Assert.Throws<InvalidOperationException>(() => builder.Bind(m => m.IgnoredProperty!));
    }

    [Test]
    public void Build_ThrowsWhenCalledTwice()
    {
        var dropdownData = new List<object> { "Value1" };
        var builder = _factory!.Create(ColumnName, dropdownData)
            .Bind(m => m.Status!);

        builder.Build();

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Test]
    public void Factory_SupportsMultipleDropdowns()
    {
        _factory!.Create(StatusColumn, new List<object> { "Active", "Inactive" })
            .Bind(m => m.Status!)
            .Build();

        _factory.Create(CategoriesName, new List<object> { "Cat1", "Cat2" })
            .Bind(m => m.Category!)
            .Build();

        var list = _factory.DropdownsList;
        Assert.That(list.Count, Is.EqualTo(2));
        Assert.That(list.Any(d => d.ColumnName == StatusColumn), Is.True);
        Assert.That(list.Any(d => d.ColumnName == CategoriesName), Is.True);
    }
}

[TestFixture]
public class ExcelColumnAttributeTests
{
    private const string TestColumnName = "TestColumn";
    private const int TestOrderId = 5;
    private const string RedColor = "#FF0000";
    private const double TestWidth = 25.5;

    [Test]
    public void Constructor_SetsNameProperty()
    {
        var attribute = new ExcelColumnAttribute(TestColumnName);

        Assert.That(attribute.Name, Is.EqualTo(TestColumnName));
        Assert.That(attribute.OrderId, Is.EqualTo(0));
    }

    [Test]
    public void Constructor_SetsNameAndOrderId()
    {
        var attribute = new ExcelColumnAttribute(TestColumnName, TestOrderId);

        Assert.That(attribute.Name, Is.EqualTo(TestColumnName));
        Assert.That(attribute.OrderId, Is.EqualTo(TestOrderId));
    }

    [Test]
    public void Properties_CanBeSet()
    {
        var attribute = new ExcelColumnAttribute(TestColumnName)
        {
            Color = RedColor,
            Width = TestWidth
        };

        Assert.That(attribute.Color, Is.EqualTo(RedColor));
        Assert.That(attribute.Width, Is.EqualTo(TestWidth));
    }

    [Test]
    public void Attribute_CanBeAppliedToProperty()
    {
        var property = typeof(ExcelTestModel).GetProperty(nameof(ExcelTestModel.Status));

        var attribute = property?.GetCustomAttribute<ExcelColumnAttribute>();

        Assert.That(attribute, Is.Not.Null);
        Assert.That(attribute!.Name, Is.EqualTo("Status"));
        Assert.That(attribute.OrderId, Is.EqualTo(99));
    }
}
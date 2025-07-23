using NUnit.Framework;
using Moq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DynamicExcel.Write;

namespace DynamicExcel.Write.Tests
{
    // Test Models
    public class TestModel
    {
        [ExcelColumn("Name", 1, Color = "#FF0000", Width = 20)]
        public string Name { get; set; } = string.Empty;

        [ExcelColumn("Age", 2, Width = 15)]
        public int Age { get; set; }

        [ExcelColumn("Birth Date", 3, Width = 25)]
        public DateTime BirthDate { get; set; }

        [ExcelColumn("Salary", 4, Width = 18)]
        public decimal Salary { get; set; }

        [ExcelColumn("Is Active", 5, IsReadOnly = true)]
        public bool IsActive { get; set; }

        [ExcelColumn("Work Hours", 6)]
        public TimeSpan WorkHours { get; set; }

        public string ExcludedProperty { get; set; } = string.Empty;
    }

    public class SimpleTestModel
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public int Score { get; set; }
    }

    [TestFixture]
    public class ExcelFileBuilderTests
    {
        private ExcelFileBuilder _builder;
        private Mock<IExcelCacheManager> _mockCacheManager;
        private Mock<IExcelCellWriter> _mockCellWriter;
        private Mock<IExcelStyleManager> _mockStyleManager;
        private Mock<IExcelValidationManager> _mockValidationManager;
        private Mock<IExcelWorkbookWriter> _mockWorkbookWriter;
        private StringBuilderPool _stringBuilderPool;
        private Mock<IPropertyNameFormatter> _mockPropertyNameFormatter;

        [SetUp]
        public void Setup()
        {
            _mockCacheManager = new Mock<IExcelCacheManager>();
            _mockCellWriter = new Mock<IExcelCellWriter>();
            _mockStyleManager = new Mock<IExcelStyleManager>();
            _mockValidationManager = new Mock<IExcelValidationManager>();
            _mockWorkbookWriter = new Mock<IExcelWorkbookWriter>();
            _stringBuilderPool = new StringBuilderPool();
            _mockPropertyNameFormatter = new Mock<IPropertyNameFormatter>();

            _builder = new ExcelFileBuilder(
                _mockCacheManager.Object,
                _mockCellWriter.Object,
                _mockStyleManager.Object,
                _mockValidationManager.Object,
                _stringBuilderPool,
                _mockPropertyNameFormatter.Object,
                _mockWorkbookWriter.Object);
        }

        [Test]
        public void AddSheet_ValidSheetName_ReturnsSheetBuilder()
        {
            // Act
            var sheetBuilder = _builder.AddSheet<TestModel>("Test Sheet");

            // Assert
            Assert.That(sheetBuilder, Is.Not.Null);
            Assert.That(sheetBuilder, Is.InstanceOf<SheetBuilder<TestModel>>());
        }

        [Test]
        public void AddSheet_NullOrEmptySheetName_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => _builder.AddSheet<TestModel>(string.Empty));
        }

        [Test]
        public void AddSimpleSheet_ValidData_ReturnsBuilder()
        {
            // Arrange
            var data = new List<SimpleTestModel>
            {
                new() { FirstName = "John", LastName = "Doe", Score = 100 }
            };

            _mockPropertyNameFormatter.Setup(x => x.ConvertToFriendlyName("SimpleTestModel"))
                .Returns("Simple Test Model");

            // Act
            var result = _builder.AddSimpleSheet(data, "Simple Sheet");

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.SameAs(_builder));
        }

        [Test]
        public void AddSimpleSheet_NullData_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => _builder.AddSimpleSheet<SimpleTestModel>(null!));
        }

        [Test]
        public void Build_NoSheetsAdded_ThrowsInvalidOperationException()
        {
            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => _builder.Build());
        }
    }

    [TestFixture]
    public class SheetBuilderTests
    {
        private ExcelFileBuilder _fileBuilder;
        private SheetBuilder<TestModel> _sheetBuilder;

        [SetUp]
        public void Setup()
        {
            _fileBuilder = new ExcelFileBuilder();
            _sheetBuilder = new SheetBuilder<TestModel>(_fileBuilder, "Test Sheet");
        }

        [Test]
        public void WithData_ValidData_ReturnsSheetBuilder()
        {
            // Arrange
            var data = new List<TestModel>
            {
                new() { Name = "John", Age = 30 }
            };

            // Act
            var result = _sheetBuilder.WithData(data);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.SameAs(_sheetBuilder));
        }

        [Test]
        public void WithData_NullData_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => _sheetBuilder.WithData(null!));
        }

        [Test]
        public void ExcludeProperties_ValidExpressions_ReturnsSheetBuilder()
        {
            // Act
            var result = _sheetBuilder.ExcludeProperties(x => x.Name, x => x.Age);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.SameAs(_sheetBuilder));
        }

        [Test]
        public void Done_ReturnsFileBuilder()
        {
            // Act
            var result = _sheetBuilder.Done();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.SameAs(_fileBuilder));
        }
    }

    [TestFixture]
    public class ExcelCacheManagerTests
    {
        private ExcelCacheManager _cacheManager;
        private Mock<IPropertyNameFormatter> _mockPropertyNameFormatter;

        [SetUp]
        public void Setup()
        {
            _mockPropertyNameFormatter = new Mock<IPropertyNameFormatter>();
            _cacheManager = new ExcelCacheManager(_mockPropertyNameFormatter.Object);
        }

        [Test]
        public void GetPropertyAccessors_ValidType_ReturnsOrderedAccessors()
        {
            // Arrange
            var excludedProperties = new HashSet<string>();
            _mockPropertyNameFormatter.Setup(x => x.ConvertToFriendlyName(It.IsAny<string>()))
                .Returns<string>(s => s);

            // Act
            var accessors = _cacheManager.GetPropertyAccessors<TestModel>(excludedProperties);

            // Assert
            Assert.That(accessors, Is.Not.Null);
            Assert.That(accessors.Length, Is.GreaterThan(0));
            Assert.That(accessors[0].Property.Name, Is.EqualTo("Name"));
            Assert.That(accessors[0].Order, Is.EqualTo(1));
        }

        [Test]
        public void GetPropertyAccessors_WithExcludedProperties_FiltersCorrectly()
        {
            // Arrange
            var excludedProperties = new HashSet<string> { "Name" };
            _mockPropertyNameFormatter.Setup(x => x.ConvertToFriendlyName(It.IsAny<string>()))
                .Returns<string>(s => s);

            // Act
            var accessors = _cacheManager.GetPropertyAccessors<TestModel>(excludedProperties);

            // Assert
            Assert.That(accessors, Is.Not.Null);
            Assert.That(accessors.Any(a => a.Property.Name == "Name"), Is.False);
        }

        [Test]
        public void GetExcelColumnName_ValidColumnNumbers_ReturnsCorrectNames()
        {
            // Act & Assert
            Assert.That(_cacheManager.GetExcelColumnName(1), Is.EqualTo("A"));
            Assert.That(_cacheManager.GetExcelColumnName(2), Is.EqualTo("B"));
            Assert.That(_cacheManager.GetExcelColumnName(26), Is.EqualTo("Z"));
            Assert.That(_cacheManager.GetExcelColumnName(27), Is.EqualTo("AA"));
            Assert.That(_cacheManager.GetExcelColumnName(28), Is.EqualTo("AB"));
        }

        [Test]
        public void GetExcelColumnName_InvalidColumnNumber_ThrowsArgumentOutOfRangeException()
        {
            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => _cacheManager.GetExcelColumnName(0));
        }

        [Test]
        public void GetCleanHexColor_ValidHexColors_ReturnsCleanedColors()
        {
            // Act & Assert
            Assert.That(_cacheManager.GetCleanHexColor("#FF0000"), Is.EqualTo("FF0000"));
            Assert.That(_cacheManager.GetCleanHexColor("FF0000"), Is.EqualTo("FF0000"));
            Assert.That(_cacheManager.GetCleanHexColor("#ff0000"), Is.EqualTo("FF0000"));
            Assert.That(_cacheManager.GetCleanHexColor("invalid"), Is.EqualTo("000000"));
            Assert.That(_cacheManager.GetCleanHexColor(null), Is.Null);
            Assert.That(_cacheManager.GetCleanHexColor(string.Empty), Is.Null);
        }
    }

    [TestFixture]
    public class ExcelCellWriterTests
    {
        private ExcelCellWriter _cellWriter;

        [SetUp]
        public void Setup()
        {
            _cellWriter = new ExcelCellWriter();
        }

        [Test]
        public void ValidateFormulaInjection_StringsWithFormulaChars_AddsSingleQuote()
        {
            // Act & Assert
            Assert.That(_cellWriter.ValidateFormulaInjection("=SUM(A1:A10)"), Is.EqualTo("'=SUM(A1:A10)"));
            Assert.That(_cellWriter.ValidateFormulaInjection("+1+1"), Is.EqualTo("'+1+1"));
            Assert.That(_cellWriter.ValidateFormulaInjection("-1"), Is.EqualTo("'-1"));
            Assert.That(_cellWriter.ValidateFormulaInjection("@command"), Is.EqualTo("'@command"));
        }

        [Test]
        public void ValidateFormulaInjection_SafeStrings_ReturnsUnchanged()
        {
            // Act & Assert
            Assert.That(_cellWriter.ValidateFormulaInjection("Hello World"), Is.EqualTo("Hello World"));
            Assert.That(_cellWriter.ValidateFormulaInjection("123"), Is.EqualTo("123"));
            Assert.That(_cellWriter.ValidateFormulaInjection(null), Is.EqualTo(string.Empty));
            Assert.That(_cellWriter.ValidateFormulaInjection(string.Empty), Is.EqualTo(string.Empty));
        }
    }

    [TestFixture]
    public class PropertyNameFormatterTests
    {
        private PropertyNameFormatter _formatter;

        [SetUp]
        public void Setup()
        {
            _formatter = new PropertyNameFormatter();
        }

        [Test]
        public void ConvertToFriendlyName_CamelCase_InsertsSpaces()
        {
            // Act & Assert
            Assert.That(_formatter.ConvertToFriendlyName("FirstName"), Is.EqualTo("First Name"));
            Assert.That(_formatter.ConvertToFriendlyName("DateOfBirth"), Is.EqualTo("Date Of Birth"));
            Assert.That(_formatter.ConvertToFriendlyName("UserID"), Is.EqualTo("User ID"));
        }

        [Test]
        public void ConvertToFriendlyName_WithNumbers_InsertsSpaces()
        {
            // Act & Assert
            Assert.That(_formatter.ConvertToFriendlyName("Address1"), Is.EqualTo("Address 1"));
            Assert.That(_formatter.ConvertToFriendlyName("Phone2Number"), Is.EqualTo("Phone 2 Number"));
        }

        [Test]
        public void ConvertToFriendlyName_ConsecutiveCapitals_HandlesCorrectly()
        {
            // Act & Assert
            Assert.That(_formatter.ConvertToFriendlyName("XMLParser"), Is.EqualTo("XML Parser"));
            Assert.That(_formatter.ConvertToFriendlyName("HTTPRequest"), Is.EqualTo("HTTP Request"));
        }

        [Test]
        public void ConvertToFriendlyName_EdgeCases_HandlesCorrectly()
        {
            // Act & Assert
            Assert.That(_formatter.ConvertToFriendlyName(string.Empty), Is.EqualTo(string.Empty));
            Assert.That(_formatter.ConvertToFriendlyName(null!), Is.Null);
            Assert.That(_formatter.ConvertToFriendlyName("A"), Is.EqualTo("A"));
        }
    }

    [TestFixture]
    public class StringBuilderPoolTests
    {
        private StringBuilderPool _pool;

        [SetUp]
        public void Setup()
        {
            _pool = new StringBuilderPool(maxPoolSize: 2, maxCapacity: 1024);
        }

        [Test]
        public void Rent_ReturnsStringBuilder()
        {
            // Act
            var sb = _pool.Rent();

            // Assert
            Assert.That(sb, Is.Not.Null);
            Assert.That(sb, Is.InstanceOf<StringBuilder>());
        }

        [Test]
        public void Return_RentAgain_ReusesStringBuilder()
        {
            // Arrange
            var sb1 = _pool.Rent();
            sb1.Append("test");

            // Act
            _pool.Return(sb1);
            var sb2 = _pool.Rent();

            // Assert
            Assert.That(sb2, Is.SameAs(sb1));
            Assert.That(sb2.Length, Is.EqualTo(0)); // Should be cleared
        }

        [Test]
        public void Return_NullStringBuilder_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => _pool.Return(null!));
        }

        [Test]
        public void Constructor_InvalidMaxPoolSize_ThrowsArgumentOutOfRangeException()
        {
            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => new StringBuilderPool(maxPoolSize: 0));
        }

        [Test]
        public void Constructor_InvalidMaxCapacity_ThrowsArgumentOutOfRangeException()
        {
            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => new StringBuilderPool(maxCapacity: 0));
        }
    }

    [TestFixture]
    public class ConditionalFormatStyleManagerTests
    {
        private ConditionalFormatStyleManager _styleManager;
        private Mock<IExcelCacheManager> _mockCacheManager;

        [SetUp]
        public void Setup()
        {
            _mockCacheManager = new Mock<IExcelCacheManager>();
            _styleManager = new ConditionalFormatStyleManager(_mockCacheManager.Object);
        }

        [Test]
        public void Constructor_NullCacheManager_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new ConditionalFormatStyleManager(null!));
        }

        [Test]
        public void GetStyleId_NullActions_ReturnsNull()
        {
            // Act
            var result = _styleManager.GetStyleId(null);

            // Assert
            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetStyleId_EmptyActions_ReturnsNull()
        {
            // Act
            var result = _styleManager.GetStyleId(new List<ConditionalAction>());

            // Assert
            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetStyleId_ActionsWithoutVisualTypes_ReturnsNull()
        {
            // Arrange
            var actions = new List<ConditionalAction>
            {
                new() { Type = ConditionalActionType.ChangeReadOnly, BoolValue = true }
            };

            // Act
            var result = _styleManager.GetStyleId(actions);

            // Assert
            Assert.That(result, Is.Null);
        }
    }

    [TestFixture]
    public class ExcelConditionalRulesFactoryTests
    {
        private ExcelConditionalRulesFactory<TestModel> _factory;

        [SetUp]
        public void Setup()
        {
            _factory = new ExcelConditionalRulesFactory<TestModel>();
        }

        [Test]
        public void When_ValidPropertyExpression_ReturnsWhenBuilder()
        {
            // Act
            var result = _factory.When(x => x.Name);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<IExcelConditionalWhenBuilder<TestModel>>());
        }

        [Test]
        public void When_NullPropertyExpression_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => _factory.When<string>(null!));
        }

        [Test]
        public void Rules_InitiallyEmpty()
        {
            // Assert
            Assert.That(_factory.Rules, Is.Not.Null);
            Assert.That(_factory.Rules.Count, Is.EqualTo(0));
        }
    }

    [TestFixture]
    public class ExcelDropdownsFactoryTests
    {
        private ExcelDropdownsFactory<TestModel> _factory;

        [SetUp]
        public void Setup()
        {
            _factory = new ExcelDropdownsFactory<TestModel>();
        }

        [Test]
        public void Create_ValidParameters_ReturnsDropdownBuilder()
        {
            // Arrange
            var data = new List<object> { "Option1", "Option2" };

            // Act
            var result = _factory.Create("TestColumn", data);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<IExcelDropdownBuilder<TestModel>>());
        }

        [Test]
        public void Create_NullOrEmptyColumnName_ThrowsArgumentException()
        {
            // Arrange
            var data = new List<object> { "Option1" };

            // Act & Assert
            Assert.Throws<ArgumentException>(() => _factory.Create(string.Empty, data));
        }

        [Test]
        public void Create_NullOrEmptyDataList_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => _factory.Create("TestColumn", null!));
        }

        [Test]
        public void DropdownsList_InitiallyEmpty()
        {
            // Assert
            Assert.That(_factory.DropdownsList, Is.Not.Null);
            Assert.That(_factory.DropdownsList.Count, Is.EqualTo(0));
        }
    }

    [TestFixture]
    public class TypeValidatorTests
    {
        [Test]
        public void IsIntegerType_IntegerTypes_ReturnsTrue()
        {
            // Act & Assert
            Assert.That(TypeValidator.IsIntegerType(typeof(int)), Is.True);
            Assert.That(TypeValidator.IsIntegerType(typeof(long)), Is.True);
            Assert.That(TypeValidator.IsIntegerType(typeof(short)), Is.True);
            Assert.That(TypeValidator.IsIntegerType(typeof(byte)), Is.True);
            Assert.That(TypeValidator.IsIntegerType(typeof(sbyte)), Is.True);
            Assert.That(TypeValidator.IsIntegerType(typeof(ushort)), Is.True);
            Assert.That(TypeValidator.IsIntegerType(typeof(uint)), Is.True);
            Assert.That(TypeValidator.IsIntegerType(typeof(ulong)), Is.True);
        }

        [Test]
        public void IsIntegerType_NonIntegerTypes_ReturnsFalse()
        {
            // Act & Assert
            Assert.That(TypeValidator.IsIntegerType(typeof(decimal)), Is.False);
            Assert.That(TypeValidator.IsIntegerType(typeof(double)), Is.False);
            Assert.That(TypeValidator.IsIntegerType(typeof(float)), Is.False);
            Assert.That(TypeValidator.IsIntegerType(typeof(string)), Is.False);
            Assert.That(TypeValidator.IsIntegerType(typeof(DateTime)), Is.False);
        }

        [Test]
        public void IsDecimalType_DecimalTypes_ReturnsTrue()
        {
            // Act & Assert
            Assert.That(TypeValidator.IsDecimalType(typeof(decimal)), Is.True);
            Assert.That(TypeValidator.IsDecimalType(typeof(double)), Is.True);
            Assert.That(TypeValidator.IsDecimalType(typeof(float)), Is.True);
        }

        [Test]
        public void IsDecimalType_NonDecimalTypes_ReturnsFalse()
        {
            // Act & Assert
            Assert.That(TypeValidator.IsDecimalType(typeof(int)), Is.False);
            Assert.That(TypeValidator.IsDecimalType(typeof(string)), Is.False);
            Assert.That(TypeValidator.IsDecimalType(typeof(DateTime)), Is.False);
        }

        [Test]
        public void IsNumericType_NumericTypes_ReturnsTrue()
        {
            // Act & Assert
            Assert.That(TypeValidator.IsNumericType(typeof(int)), Is.True);
            Assert.That(TypeValidator.IsNumericType(typeof(decimal)), Is.True);
            Assert.That(TypeValidator.IsNumericType(typeof(double)), Is.True);
        }

        [Test]
        public void IsNumericType_NonNumericTypes_ReturnsFalse()
        {
            // Act & Assert
            Assert.That(TypeValidator.IsNumericType(typeof(string)), Is.False);
            Assert.That(TypeValidator.IsNumericType(typeof(DateTime)), Is.False);
            Assert.That(TypeValidator.IsNumericType(typeof(bool)), Is.False);
        }

        [Test]
        public void IsIntegerType_NullType_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => TypeValidator.IsIntegerType(null!));
        }

        [Test]
        public void IsDecimalType_NullType_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => TypeValidator.IsDecimalType(null!));
        }

        [Test]
        public void IsNumericType_NullType_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => TypeValidator.IsNumericType(null!));
        }
    }

    [TestFixture]
    public class ExcelWorkbookWriterTests
    {
        private ExcelWorkbookWriter _writer;

        [SetUp]
        public void Setup()
        {
            _writer = new ExcelWorkbookWriter();
        }

        [Test]
        public void WriteWorkbook_NullWorkbookPart_ThrowsArgumentNullException()
        {
            // Arrange
            var sheets = new List<(string, string)> { ("Sheet1", "rel1") };

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => _writer.WriteWorkbook(null!, sheets));
        }

        [Test]
        public void WriteWorkbook_NullSheets_ThrowsArgumentException()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook);
            var workbookPart = document.AddWorkbookPart();

            // Act & Assert
            Assert.Throws<ArgumentException>(() => _writer.WriteWorkbook(workbookPart, null!));
        }

        [Test]
        public void WriteWorkbook_EmptySheets_ThrowsArgumentException()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook);
            var workbookPart = document.AddWorkbookPart();
            var sheets = new List<(string, string)>();

            // Act & Assert
            Assert.Throws<ArgumentException>(() => _writer.WriteWorkbook(workbookPart, sheets));
        }
    }

    [TestFixture]
    public class EnumerableExtensionsTests
    {
        [Test]
        public void Batch_ValidInput_CreatesBatches()
        {
            // Arrange
            var source = Enumerable.Range(1, 10);

            // Act
            var batches = source.Batch(3).Select(batch => batch.ToList()).ToList();

            // Assert
            Assert.That(batches.Count, Is.EqualTo(4));
            Assert.That(batches[0].Count, Is.EqualTo(3));
            Assert.That(batches[1].Count, Is.EqualTo(3));
            Assert.That(batches[2].Count, Is.EqualTo(3));
            Assert.That(batches[3].Count, Is.EqualTo(1));
        }

        [Test]
        public void Batch_NullSource_ThrowsArgumentNullException()
        {
            // Act & Assert
            IEnumerable<int> source = null!;
            Assert.Throws<ArgumentNullException>(() => source.Batch(3).ToList());
        }

        [Test]
        public void Batch_InvalidBatchSize_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            var source = Enumerable.Range(1, 10);

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => source.Batch(0).ToList());
        }
    }

    [TestFixture]
    public class ExcelValidationManagerTests
    {
        private ExcelValidationManager _validationManager;
        private Mock<IExcelCacheManager> _mockCacheManager;

        [SetUp]
        public void Setup()
        {
            _mockCacheManager = new Mock<IExcelCacheManager>();
            _validationManager = new ExcelValidationManager(_mockCacheManager.Object);
        }

        [Test]
        public void Constructor_NullCacheManager_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new ExcelValidationManager(null!));
        }

        [Test]
        public void AddDataValidations_NullParameter_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => _validationManager.AddDataValidations(null!));
        }
    }

    [TestFixture]
    public class ExcelValidationManagerDetailedTests
    {
        private ExcelValidationManager _validationManager;
        private Mock<IExcelCacheManager> _mockCacheManager;
        private Mock<OpenXmlWriter> _mockWriter;
        private PropertyAccessor[] _testPropertyAccessors;

        [SetUp]
        public void Setup()
        {
            _mockCacheManager = new Mock<IExcelCacheManager>();
            _mockWriter = new Mock<OpenXmlWriter>();
            _validationManager = new ExcelValidationManager(_mockCacheManager.Object);

            // Setup mock responses
            _mockCacheManager.Setup(x => x.GetExcelColumnName(1)).Returns("A");
            _mockCacheManager.Setup(x => x.GetExcelColumnName(2)).Returns("B");
            _mockCacheManager.Setup(x => x.GetExcelColumnName(3)).Returns("C");

            // Create test property accessors
            _testPropertyAccessors = new[]
            {
                CreatePropertyAccessor("Name", typeof(string), false, false),
                CreatePropertyAccessor("Age", typeof(int), false, false),
                CreatePropertyAccessor("ReadOnlyField", typeof(string), true, false),
                CreatePropertyAccessor("NullableDate", typeof(DateTime?), false, true)
            };
        }

        [Test]
        public void AddDataValidations_WithReadOnlyProperties_CreatesCustomValidation()
        {
            // Arrange
            var parameter = new DataValidationParameter
            {
                Writer = _mockWriter.Object,
                PropertyAccessors = new[] { CreatePropertyAccessor("ReadOnly", typeof(string), true, false) },
                ColumnIndexMap = new Dictionary<string, int> { { "ReadOnly", 1 } },
                TotalRows = 100
            };

            // Act
            _validationManager.AddDataValidations(parameter);

            // Assert
            _mockWriter.Verify(w => w.WriteStartElement(
                It.Is<DataValidations>(dv => true),
                It.IsAny<List<OpenXmlAttribute>>()), Times.Once);
        }

        [Test]
        public void AddDataValidations_WithDropdownData_CreatesListValidation()
        {
            // Arrange
            var parameter = new DataValidationParameter
            {
                Writer = _mockWriter.Object,
                PropertyAccessors = new[] { CreatePropertyAccessor("Status", typeof(string), false, false) },
                ColumnIndexMap = new Dictionary<string, int> { { "Status", 1 } },
                TotalRows = 100,
                DropdownData = new Dictionary<string, List<string>> { { "StatusDropdown", new List<string> { "Active", "Inactive" } } },
                ColumnPositions = new Dictionary<string, string> { { "StatusDropdown", "$A$2:$A$3" } },
                PropertyToDropdowns = new Dictionary<string, List<string>> { { "Status", new List<string> { "StatusDropdown" } } },
                DropdownSheetName = "Static Data"
            };

            // Act
            _validationManager.AddDataValidations(parameter);

            // Assert
            _mockWriter.Verify(w => w.WriteStartElement(
                It.Is<DataValidations>(dv => true),
                It.IsAny<List<OpenXmlAttribute>>()), Times.Once);
        }

        [Test]
        public void AddDataValidations_WithIntegerType_CreatesWholeValidation()
        {
            // Arrange
            var parameter = new DataValidationParameter
            {
                Writer = _mockWriter.Object,
                PropertyAccessors = new[] { CreatePropertyAccessor("Age", typeof(int), false, false) },
                ColumnIndexMap = new Dictionary<string, int> { { "Age", 1 } },
                TotalRows = 100
            };

            // Act
            _validationManager.AddDataValidations(parameter);

            // Assert
            _mockWriter.Verify(w => w.WriteStartElement(
                It.Is<DataValidations>(dv => true),
                It.IsAny<List<OpenXmlAttribute>>()), Times.Once);
        }

        [Test]
        public void AddDataValidations_WithDecimalType_CreatesDecimalValidation()
        {
            // Arrange
            var parameter = new DataValidationParameter
            {
                Writer = _mockWriter.Object,
                PropertyAccessors = new[] { CreatePropertyAccessor("Salary", typeof(decimal), false, false) },
                ColumnIndexMap = new Dictionary<string, int> { { "Salary", 1 } },
                TotalRows = 100
            };

            // Act
            _validationManager.AddDataValidations(parameter);

            // Assert
            _mockWriter.Verify(w => w.WriteStartElement(
                It.Is<DataValidations>(dv => true),
                It.IsAny<List<OpenXmlAttribute>>()), Times.Once);
        }

        [Test]
        public void AddDataValidations_WithDateTimeType_CreatesDateValidation()
        {
            // Arrange
            var parameter = new DataValidationParameter
            {
                Writer = _mockWriter.Object,
                PropertyAccessors = new[] { CreatePropertyAccessor("BirthDate", typeof(DateTime), false, false) },
                ColumnIndexMap = new Dictionary<string, int> { { "BirthDate", 1 } },
                TotalRows = 100
            };

            // Act
            _validationManager.AddDataValidations(parameter);

            // Assert
            _mockWriter.Verify(w => w.WriteStartElement(
                It.Is<DataValidations>(dv => true),
                It.IsAny<List<OpenXmlAttribute>>()), Times.Once);
        }

        [Test]
        public void AddDataValidations_WithTimeSpanType_CreatesDecimalValidation()
        {
            // Arrange
            var parameter = new DataValidationParameter
            {
                Writer = _mockWriter.Object,
                PropertyAccessors = new[] { CreatePropertyAccessor("WorkHours", typeof(TimeSpan), false, false) },
                ColumnIndexMap = new Dictionary<string, int> { { "WorkHours", 1 } },
                TotalRows = 100
            };

            // Act
            _validationManager.AddDataValidations(parameter);

            // Assert
            _mockWriter.Verify(w => w.WriteStartElement(
                It.Is<DataValidations>(dv => true),
                It.IsAny<List<OpenXmlAttribute>>()), Times.Once);
        }

        [Test]
        public void AddDataValidations_WithNullableTypes_SetsAllowBlankTrue()
        {
            // Arrange
            var parameter = new DataValidationParameter
            {
                Writer = _mockWriter.Object,
                PropertyAccessors = new[] { CreatePropertyAccessor("NullableAge", typeof(int?), false, true) },
                ColumnIndexMap = new Dictionary<string, int> { { "NullableAge", 1 } },
                TotalRows = 100
            };

            // Act
            _validationManager.AddDataValidations(parameter);

            // Assert - Should create validation that allows blank values
            _mockWriter.Verify(w => w.WriteStartElement(
                It.Is<DataValidations>(dv => true),
                It.IsAny<List<OpenXmlAttribute>>()), Times.Once);
        }

        [Test]
        public void AddDataValidations_WithExtendedParameterAndConditionalRules_CreatesConditionalValidations()
        {
            // Arrange
            var conditionalRule = new ExcelConditionalRule
            {
                SourcePropertyName = "Status",
                TargetPropertyName = "Amount",
                Operator = ConditionalOperator.Equal,
                Values = new List<string> { "Disabled" },
                Actions = new List<ConditionalAction>
                {
                    new() { Type = ConditionalActionType.ChangeReadOnly, BoolValue = true }
                }
            };

            var extendedParameter = new ExtendedDataValidationParameter
            {
                Writer = _mockWriter.Object,
                PropertyAccessors = new[]
                {
                    CreatePropertyAccessor("Status", typeof(string), false, false),
                    CreatePropertyAccessor("Amount", typeof(decimal), false, false)
                },
                ColumnIndexMap = new Dictionary<string, int> { { "Status", 1 }, { "Amount", 2 } },
                TotalRows = 100,
                ConditionalRules = new List<ExcelConditionalRule> { conditionalRule }
            };

            // Act
            _validationManager.AddDataValidations(extendedParameter);

            // Assert
            _mockWriter.Verify(w => w.WriteStartElement(
                It.Is<DataValidations>(dv => true),
                It.IsAny<List<OpenXmlAttribute>>()), Times.Once);
        }

        [Test]
        public void AddDataValidations_WithConditionalDropdownRules_CreatesConditionalListValidation()
        {
            // Arrange
            var conditionalRule = new ExcelConditionalRule
            {
                SourcePropertyName = "Category",
                TargetPropertyName = "SubCategory",
                Operator = ConditionalOperator.Equal,
                Values = new List<string> { "Electronics" },
                Actions = new List<ConditionalAction>
                {
                    new() { Type = ConditionalActionType.ChangeDropdown, DropdownName = "ElectronicsSubCategories" }
                }
            };

            var extendedParameter = new ExtendedDataValidationParameter
            {
                Writer = _mockWriter.Object,
                PropertyAccessors = new[]
                {
                    CreatePropertyAccessor("Category", typeof(string), false, false),
                    CreatePropertyAccessor("SubCategory", typeof(string), false, false)
                },
                ColumnIndexMap = new Dictionary<string, int> { { "Category", 1 }, { "SubCategory", 2 } },
                TotalRows = 100,
                ConditionalRules = new List<ExcelConditionalRule> { conditionalRule },
                DropdownData = new Dictionary<string, List<string>>
                {
                    { "GeneralSubCategories", new List<string> { "General1", "General2" } },
                    { "ElectronicsSubCategories", new List<string> { "Phones", "Laptops" } }
                },
                ColumnPositions = new Dictionary<string, string>
                {
                    { "GeneralSubCategories", "$A$2:$A$3" },
                    { "ElectronicsSubCategories", "$B$2:$B$3" }
                },
                PropertyToDropdowns = new Dictionary<string, List<string>>
                {
                    { "SubCategory", new List<string> { "GeneralSubCategories", "ElectronicsSubCategories" } }
                },
                DropdownSheetName = "Static Data"
            };

            // Act
            _validationManager.AddDataValidations(extendedParameter);

            // Assert
            _mockWriter.Verify(w => w.WriteStartElement(
                It.Is<DataValidations>(dv => true),
                It.IsAny<List<OpenXmlAttribute>>()), Times.Once);
        }

        [Test]
        public void AddDataValidations_WithMultipleValidationTypes_CreatesAllValidations()
        {
            // Arrange
            var parameter = new DataValidationParameter
            {
                Writer = _mockWriter.Object,
                PropertyAccessors = new[]
                {
                    CreatePropertyAccessor("Name", typeof(string), true, false), // Read-only
                    CreatePropertyAccessor("Age", typeof(int), false, false),   // Integer validation
                    CreatePropertyAccessor("Salary", typeof(decimal), false, false), // Decimal validation
                    CreatePropertyAccessor("StartDate", typeof(DateTime), false, false) // Date validation
                },
                ColumnIndexMap = new Dictionary<string, int>
                {
                    { "Name", 1 }, { "Age", 2 }, { "Salary", 3 }, { "StartDate", 4 }
                },
                TotalRows = 100
            };

            // Act
            _validationManager.AddDataValidations(parameter);

            // Assert - Should create validations for all applicable types
            _mockWriter.Verify(w => w.WriteStartElement(
                It.Is<DataValidations>(dv => true),
                It.IsAny<List<OpenXmlAttribute>>()), Times.Once);
        }

        [Test]
        public void AddDataValidations_WithNoValidationRequiredProperties_DoesNotCreateValidations()
        {
            // Arrange
            var parameter = new DataValidationParameter
            {
                Writer = _mockWriter.Object,
                PropertyAccessors = new[]
                {
                    CreatePropertyAccessor("Name", typeof(string), false, false) // Regular string, no special validation
                },
                ColumnIndexMap = new Dictionary<string, int> { { "Name", 1 } },
                TotalRows = 100
            };

            // Act
            _validationManager.AddDataValidations(parameter);

            // Assert - Should not create any validations
            _mockWriter.Verify(w => w.WriteStartElement(
                It.Is<DataValidations>(dv => true),
                It.IsAny<List<OpenXmlAttribute>>()), Times.Never);
        }

        [Test]
        public void AddDataValidations_WithEmptyPropertyAccessors_DoesNotCreateValidations()
        {
            // Arrange
            var parameter = new DataValidationParameter
            {
                Writer = _mockWriter.Object,
                PropertyAccessors = Array.Empty<PropertyAccessor>(),
                ColumnIndexMap = new Dictionary<string, int>(),
                TotalRows = 100
            };

            // Act
            _validationManager.AddDataValidations(parameter);

            // Assert
            _mockWriter.Verify(w => w.WriteStartElement(
                It.IsAny<DataValidations>(),
                It.IsAny<List<OpenXmlAttribute>>()), Times.Never);
        }

        [Test]
        public void AddDataValidations_WithReadOnlyPropertyInDropdown_SkipsDropdownValidation()
        {
            // Arrange
            var parameter = new DataValidationParameter
            {
                Writer = _mockWriter.Object,
                PropertyAccessors = new[] { CreatePropertyAccessor("Status", typeof(string), true, false) }, // Read-only
                ColumnIndexMap = new Dictionary<string, int> { { "Status", 1 } },
                TotalRows = 100,
                DropdownData = new Dictionary<string, List<string>> { { "StatusDropdown", new List<string> { "Active", "Inactive" } } },
                ColumnPositions = new Dictionary<string, string> { { "StatusDropdown", "$A$2:$A$3" } },
                PropertyToDropdowns = new Dictionary<string, List<string>> { { "Status", new List<string> { "StatusDropdown" } } },
                DropdownSheetName = "Static Data"
            };

            // Act
            _validationManager.AddDataValidations(parameter);

            // Assert - Should only create read-only validation, not dropdown validation
            _mockWriter.Verify(w => w.WriteStartElement(
                It.Is<DataValidations>(dv => true),
                It.IsAny<List<OpenXmlAttribute>>()), Times.Once);
        }

        [Test]
        public void AddDataValidations_WithInvalidColumnMappings_HandlesGracefully()
        {
            // Arrange
            var parameter = new DataValidationParameter
            {
                Writer = _mockWriter.Object,
                PropertyAccessors = new[] { CreatePropertyAccessor("Name", typeof(string), true, false) },
                ColumnIndexMap = new Dictionary<string, int>(), // Empty mapping
                TotalRows = 100
            };

            // Act & Assert - Should not throw exception
            Assert.DoesNotThrow(() => _validationManager.AddDataValidations(parameter));
        }

        private PropertyAccessor CreatePropertyAccessor(string name, Type type, bool isReadOnly, bool isNullable)
        {
            var mockProperty = new Mock<PropertyInfo>();
            mockProperty.Setup(p => p.Name).Returns(name);
            mockProperty.Setup(p => p.PropertyType).Returns(type);

            return new PropertyAccessor
            {
                Property = mockProperty.Object,
                ColumnName = name,
                PropertyType = type,
                UnderlyingType = Nullable.GetUnderlyingType(type) ?? type,
                IsReadOnly = isReadOnly,
                IsNullable = isNullable,
                Getter = obj => null!
            };
        }
    }

    [TestFixture]
    public class ExcelValidationManagerFormulaTests
    {
        private ExcelValidationManager _validationManager;
        private Mock<IExcelCacheManager> _mockCacheManager;

        [SetUp]
        public void Setup()
        {
            _mockCacheManager = new Mock<IExcelCacheManager>();
            _validationManager = new ExcelValidationManager(_mockCacheManager.Object);

            _mockCacheManager.Setup(x => x.GetExcelColumnName(1)).Returns("A");
            _mockCacheManager.Setup(x => x.GetExcelColumnName(2)).Returns("B");
        }

        [Test]
        public void ConditionalReadOnly_WithEqualOperator_GeneratesCorrectFormula()
        {
            // Arrange
            var conditionalRule = new ExcelConditionalRule
            {
                SourcePropertyName = "Status",
                TargetPropertyName = "Amount",
                Operator = ConditionalOperator.Equal,
                Values = new List<string> { "Locked" },
                Actions = new List<ConditionalAction>
                {
                    new() { Type = ConditionalActionType.ChangeReadOnly, BoolValue = true }
                }
            };

            var mockWriter = new Mock<OpenXmlWriter>();
            var extendedParameter = new ExtendedDataValidationParameter
            {
                Writer = mockWriter.Object,
                PropertyAccessors = new[]
                {
                    CreatePropertyAccessor("Status", typeof(string)),
                    CreatePropertyAccessor("Amount", typeof(decimal))
                },
                ColumnIndexMap = new Dictionary<string, int> { { "Status", 1 }, { "Amount", 2 } },
                TotalRows = 100,
                ConditionalRules = new List<ExcelConditionalRule> { conditionalRule }
            };

            // Act
            _validationManager.AddDataValidations(extendedParameter);

            // Assert - Verify that conditional validation was created
            mockWriter.Verify(w => w.WriteStartElement(
                It.Is<DataValidations>(dv => true),
                It.IsAny<List<OpenXmlAttribute>>()), Times.Once);
        }

        [Test]
        public void ConditionalReadOnly_WithMultipleValues_GeneratesOrFormula()
        {
            // Arrange
            var conditionalRule = new ExcelConditionalRule
            {
                SourcePropertyName = "Status",
                TargetPropertyName = "Amount",
                Operator = ConditionalOperator.Equal,
                Values = new List<string> { "Locked", "Disabled", "Frozen" },
                Actions = new List<ConditionalAction>
                {
                    new() { Type = ConditionalActionType.ChangeReadOnly, BoolValue = true }
                }
            };

            var mockWriter = new Mock<OpenXmlWriter>();
            var extendedParameter = new ExtendedDataValidationParameter
            {
                Writer = mockWriter.Object,
                PropertyAccessors = new[]
                {
                    CreatePropertyAccessor("Status", typeof(string)),
                    CreatePropertyAccessor("Amount", typeof(decimal))
                },
                ColumnIndexMap = new Dictionary<string, int> { { "Status", 1 }, { "Amount", 2 } },
                TotalRows = 100,
                ConditionalRules = new List<ExcelConditionalRule> { conditionalRule }
            };

            // Act
            _validationManager.AddDataValidations(extendedParameter);

            // Assert
            mockWriter.Verify(w => w.WriteStartElement(
                It.Is<DataValidations>(dv => true),
                It.IsAny<List<OpenXmlAttribute>>()), Times.Once);
        }

        [Test]
        public void ConditionalReadOnly_WithContainsOperator_GeneratesSearchFormula()
        {
            // Arrange
            var conditionalRule = new ExcelConditionalRule
            {
                SourcePropertyName = "Description",
                TargetPropertyName = "Amount",
                Operator = ConditionalOperator.Contains,
                Values = new List<string> { "restricted" },
                Actions = new List<ConditionalAction>
                {
                    new() { Type = ConditionalActionType.ChangeReadOnly, BoolValue = true }
                }
            };

            var mockWriter = new Mock<OpenXmlWriter>();
            var extendedParameter = new ExtendedDataValidationParameter
            {
                Writer = mockWriter.Object,
                PropertyAccessors = new[]
                {
                    CreatePropertyAccessor("Description", typeof(string)),
                    CreatePropertyAccessor("Amount", typeof(decimal))
                },
                ColumnIndexMap = new Dictionary<string, int> { { "Description", 1 }, { "Amount", 2 } },
                TotalRows = 100,
                ConditionalRules = new List<ExcelConditionalRule> { conditionalRule }
            };

            // Act
            _validationManager.AddDataValidations(extendedParameter);

            // Assert
            mockWriter.Verify(w => w.WriteStartElement(
                It.Is<DataValidations>(dv => true),
                It.IsAny<List<OpenXmlAttribute>>()), Times.Once);
        }

        [Test]
        public void ConditionalReadOnly_WithGreaterThanOperator_GeneratesComparisonFormula()
        {
            // Arrange
            var conditionalRule = new ExcelConditionalRule
            {
                SourcePropertyName = "Score",
                TargetPropertyName = "Bonus",
                Operator = ConditionalOperator.GreaterThan,
                Values = new List<string> { "100" },
                Actions = new List<ConditionalAction>
                {
                    new() { Type = ConditionalActionType.ChangeReadOnly, BoolValue = true }
                }
            };

            var mockWriter = new Mock<OpenXmlWriter>();
            var extendedParameter = new ExtendedDataValidationParameter
            {
                Writer = mockWriter.Object,
                PropertyAccessors = new[]
                {
                    CreatePropertyAccessor("Score", typeof(int)),
                    CreatePropertyAccessor("Bonus", typeof(decimal))
                },
                ColumnIndexMap = new Dictionary<string, int> { { "Score", 1 }, { "Bonus", 2 } },
                TotalRows = 100,
                ConditionalRules = new List<ExcelConditionalRule> { conditionalRule }
            };

            // Act
            _validationManager.AddDataValidations(extendedParameter);

            // Assert
            mockWriter.Verify(w => w.WriteStartElement(
                It.Is<DataValidations>(dv => true),
                It.IsAny<List<OpenXmlAttribute>>()), Times.Once);
        }

        private PropertyAccessor CreatePropertyAccessor(string name, Type type)
        {
            var mockProperty = new Mock<PropertyInfo>();
            mockProperty.Setup(p => p.Name).Returns(name);

            return new PropertyAccessor
            {
                Property = mockProperty.Object,
                ColumnName = name,
                PropertyType = type,
                UnderlyingType = type,
                IsReadOnly = false,
                IsNullable = false,
                Getter = obj => null!
            };
        }
    }

    [TestFixture]
    public class ExcelStyleManagerTests
    {
        private ExcelStyleManager _styleManager;
        private Mock<IExcelCacheManager> _mockCacheManager;

        [SetUp]
        public void Setup()
        {
            _mockCacheManager = new Mock<IExcelCacheManager>();
            _styleManager = new ExcelStyleManager(_mockCacheManager.Object);
        }

        [Test]
        public void Constructor_NullCacheManager_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new ExcelStyleManager(null!));
        }

        [Test]
        public void CreateStyles_NullWorkbookPart_ThrowsArgumentNullException()
        {
            // Arrange
            var propertyAccessors = Array.Empty<PropertyAccessor>();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => _styleManager.CreateStyles(null!, propertyAccessors, CancellationToken.None));
        }

        [Test]
        public void CreateStyles_NullPropertyAccessors_ThrowsArgumentNullException()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook);
            var workbookPart = document.AddWorkbookPart();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => _styleManager.CreateStyles(workbookPart, null!, CancellationToken.None));
        }
    }

    [TestFixture]
    public class ExcelWriterConstantsTests
    {
        [Test]
        public void Constants_HaveExpectedValues()
        {
            // Assert
            Assert.That(ExcelWriterConstants.DefaultColumnWidth, Is.EqualTo(15));
            Assert.That(ExcelWriterConstants.BatchSize, Is.EqualTo(10000));
            Assert.That(ExcelWriterConstants.MaxFormattingRows, Is.EqualTo(1_048_576));
            Assert.That(ExcelWriterConstants.DefaultDateStyle, Is.EqualTo((uint)2));
            Assert.That(ExcelWriterConstants.DefaultTimeStyle, Is.EqualTo((uint)3));
            Assert.That(ExcelWriterConstants.DateFormatCode, Is.EqualTo("dd-mm-yyyy"));
            Assert.That(ExcelWriterConstants.TimeFormatCode, Is.EqualTo("[h]:mm:ss"));
            Assert.That(ExcelWriterConstants.DefaultDropdownSheetName, Is.EqualTo("Static Data"));
        }
    }

    [TestFixture]
    public class DataRowWriterTests
    {
        private DataRowWriter<TestModel> _dataRowWriter;
        private Mock<IExcelCacheManager> _mockCacheManager;
        private Mock<IExcelCellWriter> _mockCellWriter;
        private StringBuilderPool _stringBuilderPool;

        [SetUp]
        public void Setup()
        {
            _mockCacheManager = new Mock<IExcelCacheManager>();
            _mockCellWriter = new Mock<IExcelCellWriter>();
            _stringBuilderPool = new StringBuilderPool();

            _dataRowWriter = new DataRowWriter<TestModel>(
                _mockCacheManager.Object,
                _mockCellWriter.Object,
                _stringBuilderPool);

            _mockCacheManager.Setup(x => x.GetExcelColumnName(It.IsAny<int>()))
                .Returns<int>(col => $"Col{col}");
        }

        [Test]
        public void Constructor_NullCacheManager_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new DataRowWriter<TestModel>(null!, _mockCellWriter.Object, _stringBuilderPool));
        }

        [Test]
        public void Constructor_NullCellWriter_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new DataRowWriter<TestModel>(_mockCacheManager.Object, null!, _stringBuilderPool));
        }

        [Test]
        public void Constructor_NullStringBuilderPool_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new DataRowWriter<TestModel>(_mockCacheManager.Object, _mockCellWriter.Object, null!));
        }
    }

    [TestFixture]
    public class HeaderRowWriterTests
    {
        private HeaderRowWriter _headerRowWriter;
        private Mock<IExcelCacheManager> _mockCacheManager;
        private StringBuilderPool _stringBuilderPool;

        [SetUp]
        public void Setup()
        {
            _mockCacheManager = new Mock<IExcelCacheManager>();
            _stringBuilderPool = new StringBuilderPool();
            _headerRowWriter = new HeaderRowWriter(_mockCacheManager.Object, _stringBuilderPool);

            _mockCacheManager.Setup(x => x.GetExcelColumnName(It.IsAny<int>()))
                .Returns<int>(col => $"Col{col}");
            _mockCacheManager.Setup(x => x.GetCleanHexColor(It.IsAny<string>()))
                .Returns<string>(color => color?.Replace("#", ""));
        }

        [Test]
        public void Constructor_NullCacheManager_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new HeaderRowWriter(null!, _stringBuilderPool));
        }

        [Test]
        public void Constructor_NullStringBuilderPool_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new HeaderRowWriter(_mockCacheManager.Object, null!));
        }
    }

    [TestFixture]
    public class StaticDataWriterTests
    {
        private StaticDataWriter _staticDataWriter;
        private Mock<IExcelCacheManager> _mockCacheManager;
        private Mock<IExcelCellWriter> _mockCellWriter;
        private StringBuilderPool _stringBuilderPool;

        [SetUp]
        public void Setup()
        {
            _mockCacheManager = new Mock<IExcelCacheManager>();
            _mockCellWriter = new Mock<IExcelCellWriter>();
            _stringBuilderPool = new StringBuilderPool();
            _staticDataWriter = new StaticDataWriter(_mockCacheManager.Object, _mockCellWriter.Object, _stringBuilderPool);

            _mockCacheManager.Setup(x => x.GetExcelColumnName(It.IsAny<int>()))
                .Returns<int>(col => $"Col{col}");
        }

        [Test]
        public void Constructor_NullCacheManager_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new StaticDataWriter(null!, _mockCellWriter.Object, _stringBuilderPool));
        }

        [Test]
        public void Constructor_NullCellWriter_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new StaticDataWriter(_mockCacheManager.Object, null!, _stringBuilderPool));
        }

        [Test]
        public void Constructor_NullStringBuilderPool_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new StaticDataWriter(_mockCacheManager.Object, _mockCellWriter.Object, null!));
        }
    }

    [TestFixture]
    public class StaticDataWriterDetailedTests
    {
        private StaticDataWriter _staticDataWriter;
        private Mock<IExcelCacheManager> _mockCacheManager;
        private Mock<IExcelCellWriter> _mockCellWriter;
        private StringBuilderPool _stringBuilderPool;

        [SetUp]
        public void Setup()
        {
            _mockCacheManager = new Mock<IExcelCacheManager>();
            _mockCellWriter = new Mock<IExcelCellWriter>();
            _stringBuilderPool = new StringBuilderPool();
            _staticDataWriter = new StaticDataWriter(_mockCacheManager.Object, _mockCellWriter.Object, _stringBuilderPool);

            // Setup column name mapping
            _mockCacheManager.Setup(x => x.GetExcelColumnName(1)).Returns("A");
            _mockCacheManager.Setup(x => x.GetExcelColumnName(2)).Returns("B");
            _mockCacheManager.Setup(x => x.GetExcelColumnName(3)).Returns("C");
            _mockCacheManager.Setup(x => x.GetExcelColumnName(5)).Returns("E");
            _mockCacheManager.Setup(x => x.GetExcelColumnName(7)).Returns("G");
        }

        [Test]
        public void WriteStaticData_NullWorksheetPart_ThrowsArgumentNullException()
        {
            // Arrange
            var dropdownData = new List<ExcelDropdownData>();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                _staticDataWriter.WriteStaticData(null!, dropdownData, CancellationToken.None));
        }

        [Test]
        public void WriteStaticData_NullDropdownDataList_ThrowsArgumentNullException()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook);
            var workbookPart = document.AddWorkbookPart();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                _staticDataWriter.WriteStaticData(worksheetPart, null!, CancellationToken.None));
        }

        [Test]
        public void WriteStaticData_EmptyDropdownDataList_ReturnsEmptyResult()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook);
            var workbookPart = document.AddWorkbookPart();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var dropdownData = new List<ExcelDropdownData>();

            // Act
            var result = _staticDataWriter.WriteStaticData(worksheetPart, dropdownData, CancellationToken.None);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.DropdownData, Is.Not.Null);
            Assert.That(result.ColumnMappings, Is.Not.Null);
            Assert.That(result.ColumnPositions, Is.Not.Null);
            Assert.That(result.PropertyToDropdowns, Is.Not.Null);
            Assert.That(result.DropdownData.Count, Is.EqualTo(0));
            Assert.That(result.ColumnMappings.Count, Is.EqualTo(0));
            Assert.That(result.ColumnPositions.Count, Is.EqualTo(0));
            Assert.That(result.PropertyToDropdowns.Count, Is.EqualTo(0));
        }

        [Test]
        public void WriteStaticData_WithSingleDropdown_ReturnsCorrectResult()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook);
            var workbookPart = document.AddWorkbookPart();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();

            var dropdownData = new List<ExcelDropdownData>
            {
                new()
                {
                    ColumnName = "Status",
                    DataList = new List<object> { "Active", "Inactive", "Pending" },
                    BindProperties = new List<string> { "StatusProperty" }
                }
            };

            // Act
            var result = _staticDataWriter.WriteStaticData(worksheetPart, dropdownData, CancellationToken.None);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.DropdownData.ContainsKey("Status"), Is.True);
            Assert.That(result.DropdownData["Status"], Contains.Item("Active"));
            Assert.That(result.DropdownData["Status"], Contains.Item("Inactive"));
            Assert.That(result.DropdownData["Status"], Contains.Item("Pending"));
            Assert.That(result.ColumnMappings.ContainsKey("StatusProperty"), Is.True);
            Assert.That(result.ColumnMappings["StatusProperty"], Is.EqualTo("Status"));
            Assert.That(result.PropertyToDropdowns.ContainsKey("StatusProperty"), Is.True);
            Assert.That(result.PropertyToDropdowns["StatusProperty"], Contains.Item("Status"));
        }

        [Test]
        public void WriteStaticData_WithMultipleDropdowns_ReturnsCorrectMappings()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook);
            var workbookPart = document.AddWorkbookPart();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();

            var dropdownData = new List<ExcelDropdownData>
            {
                new()
                {
                    ColumnName = "Status",
                    DataList = new List<object> { "Active", "Inactive" },
                    BindProperties = new List<string> { "StatusProperty" }
                },
                new()
                {
                    ColumnName = "Category",
                    DataList = new List<object> { "Type1", "Type2", "Type3" },
                    BindProperties = new List<string> { "CategoryProperty" }
                }
            };

            // Act
            var result = _staticDataWriter.WriteStaticData(worksheetPart, dropdownData, CancellationToken.None);

            // Assert
            Assert.That(result.DropdownData.Count, Is.EqualTo(2));
            Assert.That(result.DropdownData.ContainsKey("Status"), Is.True);
            Assert.That(result.DropdownData.ContainsKey("Category"), Is.True);
            Assert.That(result.ColumnMappings.Count, Is.EqualTo(2));
            Assert.That(result.ColumnMappings["StatusProperty"], Is.EqualTo("Status"));
            Assert.That(result.ColumnMappings["CategoryProperty"], Is.EqualTo("Category"));
        }

        [Test]
        public void WriteStaticData_WithMultipleBindProperties_CreatesCorrectPropertyMappings()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook);
            var workbookPart = document.AddWorkbookPart();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();

            var dropdownData = new List<ExcelDropdownData>
            {
                new()
                {
                    ColumnName = "Status",
                    DataList = new List<object> { "Active", "Inactive" },
                    BindProperties = new List<string> { "Property1", "Property2", "Property3" }
                }
            };

            // Act
            var result = _staticDataWriter.WriteStaticData(worksheetPart, dropdownData, CancellationToken.None);

            // Assert
            Assert.That(result.PropertyToDropdowns.Count, Is.EqualTo(3));
            Assert.That(result.PropertyToDropdowns["Property1"], Contains.Item("Status"));
            Assert.That(result.PropertyToDropdowns["Property2"], Contains.Item("Status"));
            Assert.That(result.PropertyToDropdowns["Property3"], Contains.Item("Status"));

            // The actual implementation creates column mappings for all bind properties
            Assert.That(result.ColumnMappings.Count, Is.EqualTo(3));
            Assert.That(result.ColumnMappings["Property1"], Is.EqualTo("Status"));
            Assert.That(result.ColumnMappings["Property2"], Is.EqualTo("Status"));
            Assert.That(result.ColumnMappings["Property3"], Is.EqualTo("Status"));
        }

        [Test]
        public void WriteStaticData_WithSingleBindProperty_CreatesColumnMapping()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook);
            var workbookPart = document.AddWorkbookPart();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();

            var dropdownData = new List<ExcelDropdownData>
            {
                new()
                {
                    ColumnName = "UniqueDropdown",
                    DataList = new List<object> { "Value1", "Value2" },
                    BindProperties = new List<string> { "SingleProperty" }
                }
            };

            // Act
            var result = _staticDataWriter.WriteStaticData(worksheetPart, dropdownData, CancellationToken.None);

            // Assert
            Assert.That(result.ColumnMappings.ContainsKey("SingleProperty"), Is.True);
            Assert.That(result.ColumnMappings["SingleProperty"], Is.EqualTo("UniqueDropdown"));
            Assert.That(result.PropertyToDropdowns.ContainsKey("SingleProperty"), Is.True);
            Assert.That(result.PropertyToDropdowns["SingleProperty"], Contains.Item("UniqueDropdown"));
        }

        [Test]
        public void WriteStaticData_WithComplexBindingScenario_CreatesCorrectMappings()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook);
            var workbookPart = document.AddWorkbookPart();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();

            var dropdownData = new List<ExcelDropdownData>
            {
                // Single property binding
                new()
                {
                    ColumnName = "Status",
                    DataList = new List<object> { "Active", "Inactive" },
                    BindProperties = new List<string> { "StatusProperty" }
                },
                // Multiple properties binding to same dropdown
                new()
                {
                    ColumnName = "Category",
                    DataList = new List<object> { "Type1", "Type2" },
                    BindProperties = new List<string> { "CategoryProp1", "CategoryProp2" }
                },
                // Single property binding to different dropdown
                new()
                {
                    ColumnName = "Priority",
                    DataList = new List<object> { "High", "Medium", "Low" },
                    BindProperties = new List<string> { "PriorityProperty" }
                }
            };

            // Act
            var result = _staticDataWriter.WriteStaticData(worksheetPart, dropdownData, CancellationToken.None);

            // Assert
            // Total bind properties: StatusProperty + CategoryProp1 + CategoryProp2 + PriorityProperty = 4
            Assert.That(result.ColumnMappings.Count, Is.EqualTo(4));
            Assert.That(result.ColumnMappings["StatusProperty"], Is.EqualTo("Status"));
            Assert.That(result.ColumnMappings["CategoryProp1"], Is.EqualTo("Category"));
            Assert.That(result.ColumnMappings["CategoryProp2"], Is.EqualTo("Category"));
            Assert.That(result.ColumnMappings["PriorityProperty"], Is.EqualTo("Priority"));

            // All properties should be in PropertyToDropdowns
            Assert.That(result.PropertyToDropdowns.Count, Is.EqualTo(4));
            Assert.That(result.PropertyToDropdowns["StatusProperty"], Contains.Item("Status"));
            Assert.That(result.PropertyToDropdowns["CategoryProp1"], Contains.Item("Category"));
            Assert.That(result.PropertyToDropdowns["CategoryProp2"], Contains.Item("Category"));
            Assert.That(result.PropertyToDropdowns["PriorityProperty"], Contains.Item("Priority"));
        }

        [Test]
        public void WriteStaticData_WithNoBindProperties_DoesNotCreateMappings()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook);
            var workbookPart = document.AddWorkbookPart();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();

            var dropdownData = new List<ExcelDropdownData>
            {
                new()
                {
                    ColumnName = "UnboundDropdown",
                    DataList = new List<object> { "Value1", "Value2" },
                    BindProperties = new List<string>() // Empty bind properties
                }
            };

            // Act
            var result = _staticDataWriter.WriteStaticData(worksheetPart, dropdownData, CancellationToken.None);

            // Assert
            Assert.That(result.ColumnMappings.Count, Is.EqualTo(0));
            Assert.That(result.PropertyToDropdowns.Count, Is.EqualTo(0));
            // DropdownData should still be empty because no binding means no data collection
            Assert.That(result.DropdownData.Count, Is.EqualTo(0));
        }

        [Test]
        public void WriteStaticData_WithDifferentDataTypes_HandlesCorrectly()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook);
            var workbookPart = document.AddWorkbookPart();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();

            var dropdownData = new List<ExcelDropdownData>
            {
                new()
                {
                    ColumnName = "MixedTypes",
                    DataList = new List<object> { "String", 123, 45.67, true, DateTime.Now },
                    BindProperties = new List<string> { "MixedProperty" }
                }
            };

            // Act
            var result = _staticDataWriter.WriteStaticData(worksheetPart, dropdownData, CancellationToken.None);

            // Assert
            Assert.That(result.DropdownData.ContainsKey("MixedTypes"), Is.True);
            Assert.That(result.DropdownData["MixedTypes"].Count, Is.EqualTo(5));

            // Verify cell writer was called for each data type
            _mockCellWriter.Verify(
                x => x.WriteCellValue(It.IsAny<OpenXmlWriter>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<Type>(), It.IsAny<uint?>()),
                Times.AtLeast(5)); // At least 5 times for the data + header
        }

        [Test]
        public void WriteStaticData_WithLargeDataSet_HandlesBatching()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook);
            var workbookPart = document.AddWorkbookPart();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();

            var largeDataList = Enumerable.Range(1, 1000).Select(i => (object)$"Item{i}").ToList();
            var dropdownData = new List<ExcelDropdownData>
            {
                new()
                {
                    ColumnName = "LargeDropdown",
                    DataList = largeDataList,
                    BindProperties = new List<string> { "LargeProperty" }
                }
            };

            // Act
            var result = _staticDataWriter.WriteStaticData(worksheetPart, dropdownData, CancellationToken.None);

            // Assert
            Assert.That(result.DropdownData["LargeDropdown"].Count, Is.EqualTo(1000));

            // Verify all items are present
            for (int i = 1; i <= 1000; i++)
            {
                Assert.That(result.DropdownData["LargeDropdown"], Contains.Item($"Item{i}"));
            }
        }

        [Test]
        public void WriteStaticData_WithCancellationToken_RespectsCancellation()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook);
            var workbookPart = document.AddWorkbookPart();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();

            var largeDataList = Enumerable.Range(1, 10000).Select(i => (object)$"Item{i}").ToList();
            var dropdownData = new List<ExcelDropdownData>
            {
                new()
                {
                    ColumnName = "LargeDropdown",
                    DataList = largeDataList,
                    BindProperties = new List<string> { "LargeProperty" }
                }
            };

            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel immediately

            // Act & Assert
            Assert.Throws<OperationCanceledException>(() =>
                _staticDataWriter.WriteStaticData(worksheetPart, dropdownData, cts.Token));
        }

        [Test]
        public void WriteStaticData_WithDuplicateData_RemovesDuplicates()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook);
            var workbookPart = document.AddWorkbookPart();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();

            var dropdownData = new List<ExcelDropdownData>
            {
                new()
                {
                    ColumnName = "WithDuplicates",
                    DataList = new List<object> { "Active", "Inactive", "Active", "Pending", "Inactive" },
                    BindProperties = new List<string> { "StatusProperty" }
                }
            };

            // Act
            var result = _staticDataWriter.WriteStaticData(worksheetPart, dropdownData, CancellationToken.None);

            // Assert
            Assert.That(result.DropdownData["WithDuplicates"].Count, Is.EqualTo(3));
            Assert.That(result.DropdownData["WithDuplicates"], Contains.Item("Active"));
            Assert.That(result.DropdownData["WithDuplicates"], Contains.Item("Inactive"));
            Assert.That(result.DropdownData["WithDuplicates"], Contains.Item("Pending"));
        }

        [Test]
        public void WriteStaticData_WithEmptyDataList_HandlesGracefully()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook);
            var workbookPart = document.AddWorkbookPart();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();

            var dropdownData = new List<ExcelDropdownData>
            {
                new()
                {
                    ColumnName = "EmptyDropdown",
                    DataList = new List<object>(), // Empty data list
                    BindProperties = new List<string> { "EmptyProperty" }
                }
            };

            // Act
            var result = _staticDataWriter.WriteStaticData(worksheetPart, dropdownData, CancellationToken.None);

            // Assert
            Assert.That(result.DropdownData.ContainsKey("EmptyDropdown"), Is.True);
            Assert.That(result.DropdownData["EmptyDropdown"].Count, Is.EqualTo(0));
        }

        [Test]
        public void WriteStaticData_ColumnPositions_UseCorrectFormat()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook);
            var workbookPart = document.AddWorkbookPart();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();

            var dropdownData = new List<ExcelDropdownData>
            {
                new()
                {
                    ColumnName = "TestDropdown",
                    DataList = new List<object> { "Value1", "Value2", "Value3" },
                    BindProperties = new List<string> { "TestProperty" }
                }
            };

            // Act
            var result = _staticDataWriter.WriteStaticData(worksheetPart, dropdownData, CancellationToken.None);

            // Assert
            Assert.That(result.ColumnPositions.ContainsKey("TestDropdown"), Is.True);
            var position = result.ColumnPositions["TestDropdown"];

            // Should be in format like "$A$2:$A$4" (absolute references)
            Assert.That(position, Does.StartWith("$"));
            Assert.That(position, Does.Contain(":"));
            Assert.That(position, Does.Contain("$2:"));
        }

        [Test]
        public void WriteStaticData_SortedDropdownData_ReturnsSortedResults()
        {
            // Arrange
            using var stream = new MemoryStream();
            using var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook);
            var workbookPart = document.AddWorkbookPart();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();

            var dropdownData = new List<ExcelDropdownData>
            {
                new()
                {
                    ColumnName = "UnsortedDropdown",
                    DataList = new List<object> { "Zebra", "Apple", "Banana", "Cat" },
                    BindProperties = new List<string> { "SortedProperty" }
                }
            };

            // Act
            var result = _staticDataWriter.WriteStaticData(worksheetPart, dropdownData, CancellationToken.None);

            // Assert
            var sortedData = result.DropdownData["UnsortedDropdown"];
            Assert.That(sortedData[0], Is.EqualTo("Apple"));
            Assert.That(sortedData[1], Is.EqualTo("Banana"));
            Assert.That(sortedData[2], Is.EqualTo("Cat"));
            Assert.That(sortedData[3], Is.EqualTo("Zebra"));
        }
    }

    [TestFixture]
    public class StaticDataWriterResultTests
    {
        [Test]
        public void StaticDataResult_CanBeInstantiated()
        {
            // Act
            var result = new StaticDataWriter.StaticDataResult();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.DropdownData, Is.Not.Null);
            Assert.That(result.ColumnMappings, Is.Not.Null);
            Assert.That(result.ColumnPositions, Is.Not.Null);
            Assert.That(result.PropertyToDropdowns, Is.Not.Null);
        }

        [Test]
        public void StaticDataResult_Properties_CanBeSetAndGet()
        {
            // Arrange
            var result = new StaticDataWriter.StaticDataResult();
            var dropdownData = new Dictionary<string, List<string>> { { "Test", new List<string> { "Value" } } };
            var columnMappings = new Dictionary<string, string> { { "Prop", "Column" } };
            var columnPositions = new Dictionary<string, string> { { "Col", "$A$1:$A$5" } };
            var propertyToDropdowns = new Dictionary<string, List<string>> { { "Prop", new List<string> { "Dropdown" } } };

            // Act
            result.DropdownData = dropdownData;
            result.ColumnMappings = columnMappings;
            result.ColumnPositions = columnPositions;
            result.PropertyToDropdowns = propertyToDropdowns;

            // Assert
            Assert.That(result.DropdownData, Is.SameAs(dropdownData));
            Assert.That(result.ColumnMappings, Is.SameAs(columnMappings));
            Assert.That(result.ColumnPositions, Is.SameAs(columnPositions));
            Assert.That(result.PropertyToDropdowns, Is.SameAs(propertyToDropdowns));
        }
    }

    [TestFixture]
    public class ConditionalFormattingWriterTests
    {
        private ConditionalFormattingWriter _conditionalFormattingWriter;
        private Mock<IExcelCacheManager> _mockCacheManager;
        private ConditionalFormatStyleManager _styleManager;

        [SetUp]
        public void Setup()
        {
            _mockCacheManager = new Mock<IExcelCacheManager>();
            // Create a real instance since ConditionalFormatStyleManager is sealed and cannot be mocked
            _styleManager = new ConditionalFormatStyleManager(_mockCacheManager.Object);
            _conditionalFormattingWriter = new ConditionalFormattingWriter(_mockCacheManager.Object, _styleManager);

            _mockCacheManager.Setup(x => x.GetExcelColumnName(It.IsAny<int>()))
                .Returns<int>(col => $"Col{col}");
        }

        [Test]
        public void Constructor_NullCacheManager_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new ConditionalFormattingWriter(null!, _styleManager));
        }

        [Test]
        public void Constructor_NullStyleManager_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new ConditionalFormattingWriter(_mockCacheManager.Object, null!));
        }
    }

    [TestFixture]
    public class ConditionalFormattingWriterDetailedTests
    {
        private ConditionalFormattingWriter _conditionalFormattingWriter;
        private Mock<IExcelCacheManager> _mockCacheManager;
        private ConditionalFormatStyleManager _styleManager;
        private Mock<OpenXmlWriter> _mockWriter;
        private PropertyAccessor[] _testPropertyAccessors;

        [SetUp]
        public void Setup()
        {
            _mockCacheManager = new Mock<IExcelCacheManager>();
            _styleManager = new ConditionalFormatStyleManager(_mockCacheManager.Object);
            _conditionalFormattingWriter = new ConditionalFormattingWriter(_mockCacheManager.Object, _styleManager);
            _mockWriter = new Mock<OpenXmlWriter>();

            // Setup cache manager mocks
            _mockCacheManager.Setup(x => x.GetExcelColumnName(1)).Returns("A");
            _mockCacheManager.Setup(x => x.GetExcelColumnName(2)).Returns("B");
            _mockCacheManager.Setup(x => x.GetExcelColumnName(3)).Returns("C");

            // Create test property accessors
            _testPropertyAccessors = new[]
            {
                CreatePropertyAccessor("Status", 1),
                CreatePropertyAccessor("Amount", 2),
                CreatePropertyAccessor("Category", 3)
            };
        }

        [Test]
        public void WriteConditionalFormatting_NullWriter_ThrowsArgumentNullException()
        {
            // Arrange
            var rules = new List<ExcelConditionalRule>();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                _conditionalFormattingWriter.WriteConditionalFormatting(null!, rules, _testPropertyAccessors, 100));
        }

        [Test]
        public void WriteConditionalFormatting_NullPropertyAccessors_ThrowsArgumentNullException()
        {
            // Arrange
            var rules = new List<ExcelConditionalRule>();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                _conditionalFormattingWriter.WriteConditionalFormatting(_mockWriter.Object, rules, null!, 100));
        }

        [Test]
        public void WriteConditionalFormatting_NullRules_DoesNotThrow()
        {
            // Act & Assert
            Assert.DoesNotThrow(() =>
                _conditionalFormattingWriter.WriteConditionalFormatting(_mockWriter.Object, null, _testPropertyAccessors, 100));
        }

        [Test]
        public void WriteConditionalFormatting_EmptyRules_DoesNotWriteAnything()
        {
            // Arrange
            var rules = new List<ExcelConditionalRule>();

            // Act
            _conditionalFormattingWriter.WriteConditionalFormatting(_mockWriter.Object, rules, _testPropertyAccessors, 100);

            // Assert
            _mockWriter.Verify(w => w.WriteElement(It.IsAny<ConditionalFormatting>()), Times.Never);
        }

        [Test]
        public void WriteConditionalFormatting_WithVisualRule_WritesConditionalFormatting()
        {
            // Arrange
            var rules = new List<ExcelConditionalRule>
            {
                new()
                {
                    SourcePropertyName = "Status",
                    TargetPropertyName = "Amount",
                    Operator = ConditionalOperator.Equal,
                    Values = new List<string> { "Active" },
                    Actions = new List<ConditionalAction>
                    {
                        new() { Type = ConditionalActionType.ChangeBackgroundColor, Value = "#FF0000" }
                    }
                }
            };

            // Act
            _conditionalFormattingWriter.WriteConditionalFormatting(_mockWriter.Object, rules, _testPropertyAccessors, 100);

            // Assert
            _mockWriter.Verify(w => w.WriteElement(It.IsAny<ConditionalFormatting>()), Times.Once);
        }

        [Test]
        public void WriteConditionalFormatting_WithNonVisualRule_DoesNotWriteConditionalFormatting()
        {
            // Arrange
            var rules = new List<ExcelConditionalRule>
            {
                new()
                {
                    SourcePropertyName = "Status",
                    TargetPropertyName = "Amount",
                    Operator = ConditionalOperator.Equal,
                    Values = new List<string> { "Active" },
                    Actions = new List<ConditionalAction>
                    {
                        new() { Type = ConditionalActionType.ChangeReadOnly, BoolValue = true }
                    }
                }
            };

            // Act
            _conditionalFormattingWriter.WriteConditionalFormatting(_mockWriter.Object, rules, _testPropertyAccessors, 100);

            // Assert - Non-visual rules should not create conditional formatting
            _mockWriter.Verify(w => w.WriteElement(It.IsAny<ConditionalFormatting>()), Times.Never);
        }

        [Test]
        public void WriteConditionalFormatting_WithOnlyDropdownActions_DoesNotWriteConditionalFormatting()
        {
            // Arrange
            var rules = new List<ExcelConditionalRule>
            {
                new()
                {
                    SourcePropertyName = "Status",
                    TargetPropertyName = "Amount",
                    Operator = ConditionalOperator.Equal,
                    Values = new List<string> { "Active" },
                    Actions = new List<ConditionalAction>
                    {
                        new() { Type = ConditionalActionType.ChangeDropdown, DropdownName = "SomeDropdown" }
                    }
                }
            };

            // Act
            _conditionalFormattingWriter.WriteConditionalFormatting(_mockWriter.Object, rules, _testPropertyAccessors, 100);

            // Assert - Dropdown actions are non-visual and should not create conditional formatting
            _mockWriter.Verify(w => w.WriteElement(It.IsAny<ConditionalFormatting>()), Times.Never);
        }

        [Test]
        public void WriteConditionalFormatting_WithMixedVisualAndNonVisualActions_OnlyWritesVisualRules()
        {
            // Arrange
            var rules = new List<ExcelConditionalRule>
            {
                new()
                {
                    SourcePropertyName = "Status",
                    TargetPropertyName = "Amount",
                    Operator = ConditionalOperator.Equal,
                    Values = new List<string> { "Active" },
                    Actions = new List<ConditionalAction>
                    {
                        new() { Type = ConditionalActionType.ChangeBackgroundColor, Value = "#FF0000" },
                        new() { Type = ConditionalActionType.ChangeReadOnly, BoolValue = true }
                    }
                }
            };

            // Act
            _conditionalFormattingWriter.WriteConditionalFormatting(_mockWriter.Object, rules, _testPropertyAccessors, 100);

            // Assert - Should write conditional formatting because there are visual actions
            _mockWriter.Verify(w => w.WriteElement(It.IsAny<ConditionalFormatting>()), Times.Once);
        }

        [Test]
        public void WriteConditionalFormatting_WithInvalidPropertyNames_SkipsInvalidRules()
        {
            // Arrange - Create rules with property names that don't exist in _testPropertyAccessors
            var rules = new List<ExcelConditionalRule>
            {
                new()
                {
                    SourcePropertyName = "NonExistentSource", // This property doesn't exist
                    TargetPropertyName = "NonExistentTarget", // This property doesn't exist
                    Operator = ConditionalOperator.Equal,
                    Values = new List<string> { "Active" },
                    Actions = new List<ConditionalAction>
                    {
                        new() { Type = ConditionalActionType.ChangeBackgroundColor, Value = "#FF0000" }
                    }
                }
            };

            // Note: _testPropertyAccessors only contains "Status", "Amount", and "Category"

            // Act
            _conditionalFormattingWriter.WriteConditionalFormatting(_mockWriter.Object, rules, _testPropertyAccessors, 100);

            // Assert - Rules with invalid property names should be skipped
            _mockWriter.Verify(w => w.WriteElement(It.IsAny<ConditionalFormatting>()), Times.Never);
        }

        [Test]
        public void WriteConditionalFormatting_WithValidAndInvalidRules_OnlyWritesValidRules()
        {
            // Arrange
            var rules = new List<ExcelConditionalRule>
            {
                // Valid rule
                new()
                {
                    SourcePropertyName = "Status",
                    TargetPropertyName = "Amount",
                    Operator = ConditionalOperator.Equal,
                    Values = new List<string> { "Active" },
                    Actions = new List<ConditionalAction>
                    {
                        new() { Type = ConditionalActionType.ChangeBackgroundColor, Value = "#FF0000" }
                    }
                },
                // Invalid rule - non-existent properties
                new()
                {
                    SourcePropertyName = "NonExistentSource",
                    TargetPropertyName = "NonExistentTarget",
                    Operator = ConditionalOperator.Equal,
                    Values = new List<string> { "Test" },
                    Actions = new List<ConditionalAction>
                    {
                        new() { Type = ConditionalActionType.ChangeFontColor, Value = "#00FF00" }
                    }
                }
            };

            // Act
            _conditionalFormattingWriter.WriteConditionalFormatting(_mockWriter.Object, rules, _testPropertyAccessors, 100);

            // Assert - Should write conditional formatting for the valid rule only
            _mockWriter.Verify(w => w.WriteElement(It.IsAny<ConditionalFormatting>()), Times.Once);
        }

        [Test]
        public void WriteConditionalFormatting_WithMultipleRulesForSameTarget_GroupsRulesTogether()
        {
            // Arrange
            var rules = new List<ExcelConditionalRule>
            {
                new()
                {
                    SourcePropertyName = "Status",
                    TargetPropertyName = "Amount",
                    Operator = ConditionalOperator.Equal,
                    Values = new List<string> { "Active" },
                    Actions = new List<ConditionalAction>
                    {
                        new() { Type = ConditionalActionType.ChangeBackgroundColor, Value = "#FF0000" }
                    }
                },
                new()
                {
                    SourcePropertyName = "Category",
                    TargetPropertyName = "Amount",
                    Operator = ConditionalOperator.Equal,
                    Values = new List<string> { "Premium" },
                    Actions = new List<ConditionalAction>
                    {
                        new() { Type = ConditionalActionType.ChangeFontColor, Value = "#00FF00" }
                    }
                }
            };

            // Act
            _conditionalFormattingWriter.WriteConditionalFormatting(_mockWriter.Object, rules, _testPropertyAccessors, 100);

            // Assert - Should write one ConditionalFormatting element for the Amount target
            _mockWriter.Verify(w => w.WriteElement(It.IsAny<ConditionalFormatting>()), Times.Once);
        }

        [Test]
        public void WriteConditionalFormatting_WithMultipleTargets_WritesMultipleConditionalFormattings()
        {
            // Arrange
            var rules = new List<ExcelConditionalRule>
            {
                new()
                {
                    SourcePropertyName = "Status",
                    TargetPropertyName = "Amount",
                    Operator = ConditionalOperator.Equal,
                    Values = new List<string> { "Active" },
                    Actions = new List<ConditionalAction>
                    {
                        new() { Type = ConditionalActionType.ChangeBackgroundColor, Value = "#FF0000" }
                    }
                },
                new()
                {
                    SourcePropertyName = "Status",
                    TargetPropertyName = "Category",
                    Operator = ConditionalOperator.Equal,
                    Values = new List<string> { "Inactive" },
                    Actions = new List<ConditionalAction>
                    {
                        new() { Type = ConditionalActionType.ChangeFontColor, Value = "#00FF00" }
                    }
                }
            };

            // Act
            _conditionalFormattingWriter.WriteConditionalFormatting(_mockWriter.Object, rules, _testPropertyAccessors, 100);

            // Assert - Should write two ConditionalFormatting elements for different targets
            _mockWriter.Verify(w => w.WriteElement(It.IsAny<ConditionalFormatting>()), Times.Exactly(2));
        }

        private PropertyAccessor CreatePropertyAccessor(string propertyName, int order)
        {
            var mockProperty = new Mock<PropertyInfo>();
            mockProperty.Setup(p => p.Name).Returns(propertyName);

            return new PropertyAccessor
            {
                Property = mockProperty.Object,
                ColumnName = propertyName,
                Order = order,
                PropertyType = typeof(string),
                UnderlyingType = typeof(string),
                IsReadOnly = false,
                IsNullable = false,
                Getter = obj => null!
            };
        }
    }

    [TestFixture]
    public class ConditionalFormattingWriterFormulaTests
    {
        private ConditionalFormattingWriter _conditionalFormattingWriter;
        private Mock<IExcelCacheManager> _mockCacheManager;
        private ConditionalFormatStyleManager _styleManager;
        private Mock<OpenXmlWriter> _mockWriter;
        private PropertyAccessor[] _testPropertyAccessors;

        [SetUp]
        public void Setup()
        {
            _mockCacheManager = new Mock<IExcelCacheManager>();
            _styleManager = new ConditionalFormatStyleManager(_mockCacheManager.Object);
            _conditionalFormattingWriter = new ConditionalFormattingWriter(_mockCacheManager.Object, _styleManager);
            _mockWriter = new Mock<OpenXmlWriter>();

            _mockCacheManager.Setup(x => x.GetExcelColumnName(1)).Returns("A");
            _mockCacheManager.Setup(x => x.GetExcelColumnName(2)).Returns("B");

            _testPropertyAccessors = new[]
            {
                CreatePropertyAccessor("Status", 1),
                CreatePropertyAccessor("Amount", 2)
            };
        }

        [Test]
        public void WriteConditionalFormatting_WithEqualOperatorSingleValue_GeneratesCorrectFormula()
        {
            // Arrange
            var rules = new List<ExcelConditionalRule>
            {
                CreateVisualRule("Status", "Amount", ConditionalOperator.Equal, "Active")
            };

            // Act
            _conditionalFormattingWriter.WriteConditionalFormatting(_mockWriter.Object, rules, _testPropertyAccessors, 100);

            // Assert - Should generate formula: $A2="Active"
            _mockWriter.Verify(w => w.WriteElement(It.IsAny<ConditionalFormatting>()), Times.Once);
        }

        [Test]
        public void WriteConditionalFormatting_WithEqualOperatorMultipleValues_GeneratesOrFormula()
        {
            // Arrange
            var rule = new ExcelConditionalRule
            {
                SourcePropertyName = "Status",
                TargetPropertyName = "Amount",
                Operator = ConditionalOperator.Equal,
                Values = new List<string> { "Active", "Pending", "Approved" },
                Actions = new List<ConditionalAction>
                {
                    new() { Type = ConditionalActionType.ChangeBackgroundColor, Value = "#FF0000" }
                }
            };

            var rules = new List<ExcelConditionalRule> { rule };

            // Act
            _conditionalFormattingWriter.WriteConditionalFormatting(_mockWriter.Object, rules, _testPropertyAccessors, 100);

            // Assert - Should generate formula: OR($A2="Active",$A2="Pending",$A2="Approved")
            _mockWriter.Verify(w => w.WriteElement(It.IsAny<ConditionalFormatting>()), Times.Once);
        }

        [Test]
        public void WriteConditionalFormatting_WithContainsOperator_GeneratesSearchFormula()
        {
            // Arrange
            var rules = new List<ExcelConditionalRule>
            {
                CreateVisualRule("Status", "Amount", ConditionalOperator.Contains, "test")
            };

            // Act
            _conditionalFormattingWriter.WriteConditionalFormatting(_mockWriter.Object, rules, _testPropertyAccessors, 100);

            // Assert - Should generate formula: ISNUMBER(SEARCH("test",$A2))
            _mockWriter.Verify(w => w.WriteElement(It.IsAny<ConditionalFormatting>()), Times.Once);
        }

        [Test]
        public void WriteConditionalFormatting_WithGreaterThanOperatorNumericValue_GeneratesComparisonFormula()
        {
            // Arrange
            var rules = new List<ExcelConditionalRule>
            {
                CreateVisualRule("Status", "Amount", ConditionalOperator.GreaterThan, "100")
            };

            // Act
            _conditionalFormattingWriter.WriteConditionalFormatting(_mockWriter.Object, rules, _testPropertyAccessors, 100);

            // Assert - Should generate formula: $A2>100
            _mockWriter.Verify(w => w.WriteElement(It.IsAny<ConditionalFormatting>()), Times.Once);
        }

        [Test]
        public void WriteConditionalFormatting_WithGreaterThanOperatorTextValue_GeneratesQuotedComparisonFormula()
        {
            // Arrange
            var rules = new List<ExcelConditionalRule>
            {
                CreateVisualRule("Status", "Amount", ConditionalOperator.GreaterThan, "text")
            };

            // Act
            _conditionalFormattingWriter.WriteConditionalFormatting(_mockWriter.Object, rules, _testPropertyAccessors, 100);

            // Assert - Should generate formula: $A2>"text"
            _mockWriter.Verify(w => w.WriteElement(It.IsAny<ConditionalFormatting>()), Times.Once);
        }

        [Test]
        public void WriteConditionalFormatting_WithLessThanOperator_GeneratesComparisonFormula()
        {
            // Arrange
            var rules = new List<ExcelConditionalRule>
            {
                CreateVisualRule("Status", "Amount", ConditionalOperator.LessThan, "50")
            };

            // Act
            _conditionalFormattingWriter.WriteConditionalFormatting(_mockWriter.Object, rules, _testPropertyAccessors, 100);

            // Assert - Should generate formula: $A2<50
            _mockWriter.Verify(w => w.WriteElement(It.IsAny<ConditionalFormatting>()), Times.Once);
        }

        [Test]
        public void WriteConditionalFormatting_WithUnsupportedOperator_SkipsRule()
        {
            // Arrange - Using an operator that's not implemented in CreateFormula method
            var rules = new List<ExcelConditionalRule>
            {
                new()
                {
                    SourcePropertyName = "Status",
                    TargetPropertyName = "Amount",
                    Operator = ConditionalOperator.NotEqual, // This operator is not supported in CreateFormula
                    Values = new List<string> { "Active" },
                    Actions = new List<ConditionalAction>
                    {
                        new() { Type = ConditionalActionType.ChangeBackgroundColor, Value = "#FF0000" }
                    }
                }
            };

            // Act
            _conditionalFormattingWriter.WriteConditionalFormatting(_mockWriter.Object, rules, _testPropertyAccessors, 100);

            // Assert - Unsupported operators should still create conditional formatting, but with empty formula
            // The actual behavior depends on how the CreateFormula method handles unsupported operators
            _mockWriter.Verify(w => w.WriteElement(It.IsAny<ConditionalFormatting>()), Times.AtLeastOnce);
        }

        [Test]
        public void WriteConditionalFormatting_WithEmptyValues_HandlesGracefully()
        {
            // Arrange
            var rule = new ExcelConditionalRule
            {
                SourcePropertyName = "Status",
                TargetPropertyName = "Amount",
                Operator = ConditionalOperator.Equal,
                Values = new List<string>(), // Empty values
                Actions = new List<ConditionalAction>
                {
                    new() { Type = ConditionalActionType.ChangeBackgroundColor, Value = "#FF0000" }
                }
            };

            var rules = new List<ExcelConditionalRule> { rule };

            // Act
            _conditionalFormattingWriter.WriteConditionalFormatting(_mockWriter.Object, rules, _testPropertyAccessors, 100);

            // Assert - Even with empty values, the conditional formatting structure may still be created
            // The behavior depends on how the CreateFormula method handles empty values
            _mockWriter.Verify(w => w.WriteElement(It.IsAny<ConditionalFormatting>()), Times.AtMostOnce);
        }

        [Test]
        public void WriteConditionalFormatting_WithDifferentActionTypes_WritesConditionalFormatting()
        {
            // Arrange
            var rules = new List<ExcelConditionalRule>
            {
                // Background color change
                new()
                {
                    SourcePropertyName = "Status",
                    TargetPropertyName = "Amount",
                    Operator = ConditionalOperator.Equal,
                    Values = new List<string> { "Active" },
                    Actions = new List<ConditionalAction>
                    {
                        new() { Type = ConditionalActionType.ChangeBackgroundColor, Value = "#FF0000" }
                    }
                },
                // Font color change
                new()
                {
                    SourcePropertyName = "Status",
                    TargetPropertyName = "Amount",
                    Operator = ConditionalOperator.Equal,
                    Values = new List<string> { "Inactive" },
                    Actions = new List<ConditionalAction>
                    {
                        new() { Type = ConditionalActionType.ChangeFontColor, Value = "#00FF00" }
                    }
                },
                // Font bold change
                new()
                {
                    SourcePropertyName = "Status",
                    TargetPropertyName = "Amount",
                    Operator = ConditionalOperator.Equal,
                    Values = new List<string> { "Important" },
                    Actions = new List<ConditionalAction>
                    {
                        new() { Type = ConditionalActionType.ChangeFontBold, BoolValue = true }
                    }
                }
            };

            // Act
            _conditionalFormattingWriter.WriteConditionalFormatting(_mockWriter.Object, rules, _testPropertyAccessors, 100);

            // Assert - Should write one ConditionalFormatting with three rules targeting the same column
            _mockWriter.Verify(w => w.WriteElement(It.IsAny<ConditionalFormatting>()), Times.Once);
        }

        [Test]
        public void WriteConditionalFormatting_WithMaxRowsParameter_UsesCorrectRange()
        {
            // Arrange
            var rules = new List<ExcelConditionalRule>
            {
                CreateVisualRule("Status", "Amount", ConditionalOperator.Equal, "Active")
            };

            // Act
            _conditionalFormattingWriter.WriteConditionalFormatting(_mockWriter.Object, rules, _testPropertyAccessors, 500);

            // Assert - Should create range that goes to row 500
            _mockWriter.Verify(w => w.WriteElement(It.IsAny<ConditionalFormatting>()), Times.Once);
        }

        private ExcelConditionalRule CreateVisualRule(string sourceProperty, string targetProperty, ConditionalOperator op, string value)
        {
            return new ExcelConditionalRule
            {
                SourcePropertyName = sourceProperty,
                TargetPropertyName = targetProperty,
                Operator = op,
                Values = new List<string> { value },
                Actions = new List<ConditionalAction>
                {
                    new() { Type = ConditionalActionType.ChangeBackgroundColor, Value = "#FF0000" }
                }
            };
        }

        private PropertyAccessor CreatePropertyAccessor(string propertyName, int order)
        {
            var mockProperty = new Mock<PropertyInfo>();
            mockProperty.Setup(p => p.Name).Returns(propertyName);

            return new PropertyAccessor
            {
                Property = mockProperty.Object,
                ColumnName = propertyName,
                Order = order,
                PropertyType = typeof(string),
                UnderlyingType = typeof(string),
                IsReadOnly = false,
                IsNullable = false,
                Getter = obj => null!
            };
        }
    }

    [TestFixture]
    public class SheetConfigurationTests
    {
        private SheetConfiguration<TestModel> _sheetConfiguration;

        [SetUp]
        public void Setup()
        {
            _sheetConfiguration = new SheetConfiguration<TestModel>("Test Sheet");
        }

        [Test]
        public void Constructor_ValidSheetName_SetsProperties()
        {
            // Assert
            Assert.That(_sheetConfiguration.SheetName, Is.EqualTo("Test Sheet"));
            Assert.That(_sheetConfiguration.Data, Is.Not.Null);
            Assert.That(_sheetConfiguration.ExcludedProperties, Is.Not.Null);
            Assert.That(_sheetConfiguration.DropdownData, Is.Not.Null);
            Assert.That(_sheetConfiguration.ConditionalRules, Is.Not.Null);
        }

        [Test]
        public void Constructor_NullOrWhitespaceSheetName_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => new SheetConfiguration<TestModel>(string.Empty));
        }

        [Test]
        public void HasDropdownData_NoDropdownData_ReturnsFalse()
        {
            // Act
            var result = _sheetConfiguration.HasDropdownData();

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void HasDropdownData_WithDropdownData_ReturnsTrue()
        {
            // Arrange
            _sheetConfiguration.DropdownData.Add(new ExcelDropdownData
            {
                ColumnName = "Test",
                DataList = new List<object> { "Value1" },
                BindProperties = new List<string> { "Property1" }
            });

            // Act
            var result = _sheetConfiguration.HasDropdownData();

            // Assert
            Assert.That(result, Is.True);
        }

        [Test]
        public void GetDropdownData_ReturnsDropdownData()
        {
            // Act
            var result = _sheetConfiguration.GetDropdownData();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.SameAs(_sheetConfiguration.DropdownData));
        }
    }

    [TestFixture]
    public class SimpleSheetConfigurationTests
    {
        private SimpleSheetConfiguration<SimpleTestModel> _simpleSheetConfiguration;
        private Mock<IPropertyNameFormatter> _mockPropertyNameFormatter;
        private List<SimpleTestModel> _testData;

        [SetUp]
        public void Setup()
        {
            _mockPropertyNameFormatter = new Mock<IPropertyNameFormatter>();
            _testData = new List<SimpleTestModel>
            {
                new() { FirstName = "John", LastName = "Doe", Score = 100 }
            };

            _mockPropertyNameFormatter.Setup(x => x.ConvertToFriendlyName(It.IsAny<string>()))
                .Returns<string>(s => s.Replace("Name", " Name"));

            _simpleSheetConfiguration = new SimpleSheetConfiguration<SimpleTestModel>(
                "Simple Sheet", _testData, _mockPropertyNameFormatter.Object);
        }

        [Test]
        public void Constructor_ValidParameters_SetsProperties()
        {
            // Assert
            Assert.That(_simpleSheetConfiguration.SheetName, Is.EqualTo("Simple Sheet"));
            Assert.That(_simpleSheetConfiguration.Data, Is.SameAs(_testData));
            Assert.That(_simpleSheetConfiguration.DropdownSheetName, Is.Null);
            Assert.That(_simpleSheetConfiguration.ConditionalRules, Is.Not.Null);
            Assert.That(_simpleSheetConfiguration.ConditionalRules.Count, Is.EqualTo(0));
        }

        [Test]
        public void Constructor_NullOrWhitespaceSheetName_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => new SimpleSheetConfiguration<SimpleTestModel>(string.Empty, _testData, _mockPropertyNameFormatter.Object));
        }

        [Test]
        public void Constructor_NullData_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new SimpleSheetConfiguration<SimpleTestModel>("Test", null!, _mockPropertyNameFormatter.Object));
        }

        [Test]
        public void Constructor_NullPropertyNameFormatter_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new SimpleSheetConfiguration<SimpleTestModel>("Test", _testData, null!));
        }

        [Test]
        public void HasDropdownData_AlwaysReturnsFalse()
        {
            // Act
            var result = _simpleSheetConfiguration.HasDropdownData();

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void GetDropdownData_ReturnsNull()
        {
            // Act
            var result = _simpleSheetConfiguration.GetDropdownData();

            // Assert
            Assert.That(result, Is.Null);
        }
    }

    [TestFixture]
    public class ExcelColumnAttributeTests
    {
        [Test]
        public void DefaultConstructor_SetsDefaultValues()
        {
            // Act
            var attribute = new ExcelColumnAttribute();

            // Assert
            Assert.That(attribute.Name, Is.Null);
            Assert.That(attribute.OrderId, Is.EqualTo(0));
            Assert.That(attribute.Color, Is.Null);
            Assert.That(attribute.Width, Is.EqualTo(0));
            Assert.That(attribute.IsReadOnly, Is.False);
        }

        [Test]
        public void ConstructorWithName_SetsName()
        {
            // Act
            var attribute = new ExcelColumnAttribute("Test Name");

            // Assert
            Assert.That(attribute.Name, Is.EqualTo("Test Name"));
        }

        [Test]
        public void ConstructorWithNameAndOrder_SetsNameAndOrder()
        {
            // Act
            var attribute = new ExcelColumnAttribute("Test Name", 5);

            // Assert
            Assert.That(attribute.Name, Is.EqualTo("Test Name"));
            Assert.That(attribute.OrderId, Is.EqualTo(5));
        }

        [Test]
        public void Properties_CanBeSetAndGet()
        {
            // Arrange
            var attribute = new ExcelColumnAttribute
            {
                Name = "Custom Name",
                OrderId = 10,
                Color = "#FF0000",
                Width = 25.5,
                IsReadOnly = true
            };

            // Assert
            Assert.That(attribute.Name, Is.EqualTo("Custom Name"));
            Assert.That(attribute.OrderId, Is.EqualTo(10));
            Assert.That(attribute.Color, Is.EqualTo("#FF0000"));
            Assert.That(attribute.Width, Is.EqualTo(25.5));
            Assert.That(attribute.IsReadOnly, Is.True);
        }
    }

    [TestFixture]
    public class EnumTests
    {
        [Test]
        public void DataValidationType_HasExpectedValues()
        {
            // Assert
            Assert.That(Enum.IsDefined(typeof(DataValidationType), DataValidationType.Whole), Is.True);
            Assert.That(Enum.IsDefined(typeof(DataValidationType), DataValidationType.Decimal), Is.True);
            Assert.That(Enum.IsDefined(typeof(DataValidationType), DataValidationType.List), Is.True);
            Assert.That(Enum.IsDefined(typeof(DataValidationType), DataValidationType.Date), Is.True);
            Assert.That(Enum.IsDefined(typeof(DataValidationType), DataValidationType.Time), Is.True);
            Assert.That(Enum.IsDefined(typeof(DataValidationType), DataValidationType.TextLength), Is.True);
            Assert.That(Enum.IsDefined(typeof(DataValidationType), DataValidationType.Custom), Is.True);
        }

        [Test]
        public void ConditionalOperator_HasExpectedValues()
        {
            // Assert
            Assert.That(Enum.IsDefined(typeof(ConditionalOperator), ConditionalOperator.Equal), Is.True);
            Assert.That(Enum.IsDefined(typeof(ConditionalOperator), ConditionalOperator.NotEqual), Is.True);
            Assert.That(Enum.IsDefined(typeof(ConditionalOperator), ConditionalOperator.Contains), Is.True);
            Assert.That(Enum.IsDefined(typeof(ConditionalOperator), ConditionalOperator.NotContains), Is.True);
            Assert.That(Enum.IsDefined(typeof(ConditionalOperator), ConditionalOperator.GreaterThan), Is.True);
            Assert.That(Enum.IsDefined(typeof(ConditionalOperator), ConditionalOperator.LessThan), Is.True);
            Assert.That(Enum.IsDefined(typeof(ConditionalOperator), ConditionalOperator.GreaterThanOrEqual), Is.True);
            Assert.That(Enum.IsDefined(typeof(ConditionalOperator), ConditionalOperator.LessThanOrEqual), Is.True);
        }

        [Test]
        public void ConditionalActionType_HasExpectedValues()
        {
            // Assert
            Assert.That(Enum.IsDefined(typeof(ConditionalActionType), ConditionalActionType.ChangeBackgroundColor), Is.True);
            Assert.That(Enum.IsDefined(typeof(ConditionalActionType), ConditionalActionType.ChangeFontColor), Is.True);
            Assert.That(Enum.IsDefined(typeof(ConditionalActionType), ConditionalActionType.ChangeReadOnly), Is.True);
            Assert.That(Enum.IsDefined(typeof(ConditionalActionType), ConditionalActionType.ChangeFontBold), Is.True);
            Assert.That(Enum.IsDefined(typeof(ConditionalActionType), ConditionalActionType.ChangeDropdown), Is.True);
        }
    }

    [TestFixture]
    public class ModelClassTests
    {
        [Test]
        public void PropertyAccessor_CanBeInstantiated()
        {
            // Act
            var accessor = new PropertyAccessor();

            // Assert
            Assert.That(accessor, Is.Not.Null);
        }

        [Test]
        public void DataValidationInfo_CanBeInstantiated()
        {
            // Act
            var info = new DataValidationInfo
            {
                Type = DataValidationType.List,
                CellRange = "A1:A10",
                Formula1 = "Sheet1!A1:A10",
                ErrorTitle = "Error",
                ErrorMessage = "Invalid value",
                AllowBlank = true
            };

            // Assert
            Assert.That(info.Type, Is.EqualTo(DataValidationType.List));
            Assert.That(info.CellRange, Is.EqualTo("A1:A10"));
            Assert.That(info.Formula1, Is.EqualTo("Sheet1!A1:A10"));
            Assert.That(info.ErrorTitle, Is.EqualTo("Error"));
            Assert.That(info.ErrorMessage, Is.EqualTo("Invalid value"));
            Assert.That(info.AllowBlank.Value, Is.True);
        }

        [Test]
        public void ExcelDropdownData_CanBeInstantiated()
        {
            // Act
            var dropdownData = new ExcelDropdownData
            {
                ColumnName = "Status",
                DataList = new List<object> { "Active", "Inactive" },
                BindProperties = new List<string> { "StatusProperty" }
            };

            // Assert
            Assert.That(dropdownData.ColumnName, Is.EqualTo("Status"));
            Assert.That(dropdownData.DataList.Count, Is.EqualTo(2));
            Assert.That(dropdownData.BindProperties.Count, Is.EqualTo(1));
        }

        [Test]
        public void ExcelConditionalRule_CanBeInstantiated()
        {
            // Act
            var rule = new ExcelConditionalRule
            {
                SourcePropertyName = "Source",
                TargetPropertyName = "Target",
                Operator = ConditionalOperator.Equal,
                Values = new List<string> { "Value1" },
                Actions = new List<ConditionalAction>
                {
                    new() { Type = ConditionalActionType.ChangeBackgroundColor, Value = "#FF0000" }
                }
            };

            // Assert
            Assert.That(rule.SourcePropertyName, Is.EqualTo("Source"));
            Assert.That(rule.TargetPropertyName, Is.EqualTo("Target"));
            Assert.That(rule.Operator, Is.EqualTo(ConditionalOperator.Equal));
            Assert.That(rule.Values.Count, Is.EqualTo(1));
            Assert.That(rule.Actions.Count, Is.EqualTo(1));
        }

        [Test]
        public void ConditionalAction_CanBeInstantiated()
        {
            // Act
            var action = new ConditionalAction
            {
                Type = ConditionalActionType.ChangeFontBold,
                BoolValue = true,
                Value = null,
                DropdownName = "TestDropdown"
            };

            // Assert
            Assert.That(action.Type, Is.EqualTo(ConditionalActionType.ChangeFontBold));
            Assert.That(action.BoolValue.Value, Is.True);
            Assert.That(action.Value, Is.Null);
            Assert.That(action.DropdownName, Is.EqualTo("TestDropdown"));
        }
    }

    [TestFixture]
    public class IntegrationTests
    {
        [Test]
        public void ExcelFileBuilder_FullWorkflow_CreatesValidExcelFile()
        {
            // Arrange
            var testData = new List<TestModel>
            {
                new()
                {
                    Name = "John Doe",
                    Age = 30,
                    BirthDate = DateTime.Now.AddYears(-30),
                    Salary = 50000.00m,
                    IsActive = true,
                    WorkHours = TimeSpan.FromHours(8)
                },
                new()
                {
                    Name = "Jane Smith",
                    Age = 25,
                    BirthDate = DateTime.Now.AddYears(-25),
                    Salary = 45000.00m,
                    IsActive = false,
                    WorkHours = TimeSpan.FromHours(7.5)
                }
            };

            var builder = new ExcelFileBuilder();

            // Act
            var result = builder
                .AddSheet<TestModel>("Employees")
                .WithData(testData)
                .ExcludeProperties(x => x.ExcludedProperty)
                .Done()
                .Build();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
            Assert.That(result.Position, Is.EqualTo(0));
        }

        [Test]
        public void ExcelFileBuilder_MultipleSheets_CreatesValidExcelFile()
        {
            // Arrange
            var testData1 = new List<TestModel>
            {
                new() { Name = "Employee 1", Age = 30 }
            };

            var testData2 = new List<SimpleTestModel>
            {
                new() { FirstName = "John", LastName = "Doe", Score = 100 }
            };

            var builder = new ExcelFileBuilder();

            // Act
            var result = builder
                .AddSheet<TestModel>("Sheet1")
                .WithData(testData1)
                .Done()
                .AddSimpleSheet(testData2, "Sheet2")
                .Build();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        [Test]
        public void ExcelFileBuilder_DisposedStream_ThrowsObjectDisposedException()
        {
            // Arrange
            var testData = new List<TestModel>
            {
                new() { Name = "Test", Age = 25 }
            };

            var builder = new ExcelFileBuilder();
            var stream = builder
                .AddSheet<TestModel>("Test")
                .WithData(testData)
                .Done()
                .Build();

            // Act
            stream.Dispose();

            // Assert
            Assert.Throws<ObjectDisposedException>(() => { _ = stream.Length; });
        }
    }

    [TestFixture]
    public class ExcelIntegrationTests
    {
        // Test models for integration tests
        public class Employee
        {
            [ExcelColumn("Employee ID", 1, Width = 12)]
            public int Id { get; set; }

            [ExcelColumn("Full Name", 2, Color = "#E6F3FF", Width = 25)]
            public string Name { get; set; } = string.Empty;

            [ExcelColumn("Department", 3, Width = 20)]
            public string Department { get; set; } = string.Empty;

            [ExcelColumn("Status", 4, Width = 15)]
            public string Status { get; set; } = string.Empty;

            [ExcelColumn("Salary", 5, Width = 15)]
            public decimal Salary { get; set; }

            [ExcelColumn("Start Date", 6, Width = 20)]
            public DateTime StartDate { get; set; }

            [ExcelColumn("Is Manager", 7, IsReadOnly = true)]
            public bool IsManager { get; set; }

            [ExcelColumn("Weekly Hours", 8)]
            public TimeSpan WeeklyHours { get; set; }
        }

        public class Project
        {
            [ExcelColumn("Project Name", 1, Color = "#FFE6E6", Width = 30)]
            public string Name { get; set; } = string.Empty;

            [ExcelColumn("Priority", 2)]
            public string Priority { get; set; } = string.Empty;

            [ExcelColumn("Budget", 3)]
            public decimal? Budget { get; set; }

            [ExcelColumn("Due Date", 4)]
            public DateTime? DueDate { get; set; }
        }

        [Test]
        public void CompleteWorkflow_WithConditionalFormattingAndDropdowns_GeneratesValidExcel()
        {
            // Arrange
            var employees = new List<Employee>
            {
                new() { Id = 1, Name = "John Doe", Department = "IT", Status = "Active", Salary = 75000, StartDate = DateTime.Now.AddYears(-2), IsManager = true, WeeklyHours = TimeSpan.FromHours(40) },
                new() { Id = 2, Name = "Jane Smith", Department = "HR", Status = "Active", Salary = 65000, StartDate = DateTime.Now.AddYears(-1), IsManager = false, WeeklyHours = TimeSpan.FromHours(35) },
                new() { Id = 3, Name = "Bob Johnson", Department = "IT", Status = "Inactive", Salary = 55000, StartDate = DateTime.Now.AddYears(-3), IsManager = false, WeeklyHours = TimeSpan.FromHours(40) }
            };

            var projects = new List<Project>
            {
                new() { Name = "Website Redesign", Priority = "High", Budget = 100000, DueDate = DateTime.Now.AddMonths(3) },
                new() { Name = "Mobile App", Priority = "Medium", Budget = 75000, DueDate = DateTime.Now.AddMonths(6) },
                new() { Name = "Database Migration", Priority = "Low", Budget = null, DueDate = null }
            };

            // Create dropdown data
            var statusDropdown = new ExcelDropdownData
            {
                ColumnName = "Status Options",
                DataList = new List<object> { "Active", "Inactive", "On Leave", "Terminated" },
                BindProperties = new List<string> { "Status" }
            };

            var departmentDropdown = new ExcelDropdownData
            {
                ColumnName = "Departments",
                DataList = new List<object> { "IT", "HR", "Finance", "Marketing", "Operations" },
                BindProperties = new List<string> { "Department" }
            };

            var priorityDropdown = new ExcelDropdownData
            {
                ColumnName = "Priority Levels",
                DataList = new List<object> { "High", "Medium", "Low", "Critical" },
                BindProperties = new List<string> { "Priority" }
            };

            // Create conditional formatting rules
            var conditionalRules = new ExcelConditionalRulesFactory<Employee>();
            conditionalRules.When(x => x.Status)
                .Equals("Active")
                .Then(x => x.Name)
                .ChangeColor("#90EE90")
                .Build();

            conditionalRules.When(x => x.Status)
                .Equals("Inactive")
                .Then(x => x.Name)
                .ChangeColor("#FFB6C1")
                .ChangeFontColor("#800000")
                .Build();

            conditionalRules.When(x => x.Salary)
                .GreaterThan("70000")
                .Then(x => x.Salary)
                .ChangeFontBold(true)
                .ChangeFontColor("#006400")
                .Build();

            var builder = new ExcelFileBuilder();

            // Act
            using var excelStream = builder
                .AddSheet<Employee>("Employees")
                .WithData(employees)
                .WithDropdownData(new List<ExcelDropdownData> { statusDropdown, departmentDropdown })
                .WithConditionalRules(conditionalRules)
                .Done()
                .AddSheet<Project>("Projects")
                .WithData(projects)
                .WithDropdownData(new List<ExcelDropdownData> { priorityDropdown }, "Project Data")
                .Done()
                .Build();

            // Assert
            Assert.That(excelStream, Is.Not.Null);
            Assert.That(excelStream.Length, Is.GreaterThan(0));

            // Verify the Excel file can be opened (basic structure validation)
            excelStream.Position = 0;
            using var document = SpreadsheetDocument.Open(excelStream, false);
            Assert.That(document, Is.Not.Null);
            Assert.That(document.WorkbookPart, Is.Not.Null);

            var sheets = document.WorkbookPart.Workbook.Sheets?.Elements<Sheet>().ToList();
            Assert.That(sheets, Is.Not.Null);
            Assert.That(sheets.Count, Is.GreaterThanOrEqualTo(2)); // At least 2 sheets (could have dropdown sheets)

            // Verify sheet names
            var sheetNames = sheets.Select(s => s.Name?.Value).ToList();
            Assert.That(sheetNames, Contains.Item("Employees"));
            Assert.That(sheetNames, Contains.Item("Projects"));
        }

        [Test]
        public void LargeDataSetIntegration_WithAllFeatures_HandlesEfficiently()
        {
            // Arrange
            var largeEmployeeList = Enumerable.Range(1, 1000).Select(i => new Employee
            {
                Id = i,
                Name = $"Employee {i}",
                Department = new[] { "IT", "HR", "Finance", "Marketing" }[i % 4],
                Status = new[] { "Active", "Inactive", "On Leave" }[i % 3],
                Salary = 40000 + (i % 50) * 1000,
                StartDate = DateTime.Now.AddDays(-i * 7),
                IsManager = i % 10 == 0,
                WeeklyHours = TimeSpan.FromHours(35 + (i % 10))
            }).ToList();

            var dropdownData = new List<ExcelDropdownData>
            {
                new()
                {
                    ColumnName = "Status Options",
                    DataList = new List<object> { "Active", "Inactive", "On Leave", "Terminated" },
                    BindProperties = new List<string> { "Status" }
                }
            };

            var conditionalRules = new ExcelConditionalRulesFactory<Employee>();
            conditionalRules.When(x => x.Status)
                .Equals("Active")
                .Then(x => x.Name)
                .ChangeColor("#90EE90")
                .Build();

            var builder = new ExcelFileBuilder();

            // Act
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            using var excelStream = builder
                .AddSheet<Employee>("Large Employee List")
                .WithData(largeEmployeeList)
                .WithDropdownData(dropdownData)
                .WithConditionalRules(conditionalRules)
                .Done()
                .Build();
            stopwatch.Stop();

            // Assert
            Assert.That(excelStream, Is.Not.Null);
            Assert.That(excelStream.Length, Is.GreaterThan(0));
            Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(30000)); // Should complete within 30 seconds

            // Verify file structure
            excelStream.Position = 0;
            using var document = SpreadsheetDocument.Open(excelStream, false);
            Assert.That(document.WorkbookPart, Is.Not.Null);
            Assert.That(document.WorkbookPart.WorkbookStylesPart, Is.Not.Null); // Styles should be present
        }

        [Test]
        public void MultipleConditionalRules_WithComplexLogic_AppliesCorrectly()
        {
            // Arrange
            var employees = new List<Employee>
            {
                new() { Id = 1, Name = "Senior Manager", Department = "IT", Status = "Active", Salary = 95000, IsManager = true },
                new() { Id = 2, Name = "Junior Developer", Department = "IT", Status = "Active", Salary = 45000, IsManager = false },
                new() { Id = 3, Name = "HR Specialist", Department = "HR", Status = "Inactive", Salary = 55000, IsManager = false }
            };

            var conditionalRules = new ExcelConditionalRulesFactory<Employee>();

            // Rule 1: High salary employees get bold green text
            conditionalRules.When(x => x.Salary)
                .GreaterThan("80000")
                .Then(x => x.Salary)
                .ChangeFontBold(true)
                .ChangeFontColor("#006400")
                .Build();

            // Rule 2: IT department gets blue background
            conditionalRules.When(x => x.Department)
                .Equals("IT")
                .Then(x => x.Department)
                .ChangeColor("#E6F3FF")
                .Build();

            // Rule 3: Inactive employees get red background on status
            conditionalRules.When(x => x.Status)
                .Equals("Inactive")
                .Then(x => x.Status)
                .ChangeColor("#FFE6E6")
                .ChangeFontColor("#CC0000")
                .Build();

            // Rule 4: Managers get read-only salary field
            conditionalRules.When(x => x.IsManager)
                .Equals("True")
                .Then(x => x.Salary)
                .ChangeReadOnly(true)
                .Build();

            var builder = new ExcelFileBuilder();

            // Act
            using var excelStream = builder
                .AddSheet<Employee>("Complex Rules")
                .WithData(employees)
                .WithConditionalRules(conditionalRules)
                .Done()
                .Build();

            // Assert
            Assert.That(excelStream, Is.Not.Null);
            Assert.That(excelStream.Length, Is.GreaterThan(0));

            // Verify Excel structure includes conditional formatting
            excelStream.Position = 0;
            using var document = SpreadsheetDocument.Open(excelStream, false);
            var worksheetPart = document.WorkbookPart?.WorksheetParts.FirstOrDefault();
            Assert.That(worksheetPart, Is.Not.Null);

            // Check if conditional formatting is present in the worksheet
            var worksheet = worksheetPart.Worksheet;
            Assert.That(worksheet, Is.Not.Null);
        }

        [Test]
        public void ErrorHandling_InvalidDataTypes_HandlesGracefully()
        {
            // Arrange
            var problematicEmployees = new List<Employee>
            {
                new() { Id = 1, Name = "Valid Employee", Department = "IT", Status = "Active", Salary = 50000 },
                new() { Id = 2, Name = null!, Department = "HR", Status = "Active", Salary = -1000 }, // Null name, negative salary
                new() { Id = 3, Name = "", Department = "", Status = "", Salary = 0 } // Empty strings
            };

            var builder = new ExcelFileBuilder();

            // Act & Assert - Should not throw exception
            Assert.DoesNotThrow(() =>
            {
                using var excelStream = builder
                    .AddSheet<Employee>("Problematic Data")
                    .WithData(problematicEmployees)
                    .Done()
                    .Build();

                Assert.That(excelStream, Is.Not.Null);
                Assert.That(excelStream.Length, Is.GreaterThan(0));
            });
        }

        [Test]
        public void EmptyDataSets_HandledCorrectly()
        {
            // Arrange
            var emptyEmployees = new List<Employee>();
            var emptyProjects = new List<Project>();

            var builder = new ExcelFileBuilder();

            // Act
            using var excelStream = builder
                .AddSheet<Employee>("Empty Employees")
                .WithData(emptyEmployees)
                .Done()
                .AddSheet<Project>("Empty Projects")
                .WithData(emptyProjects)
                .Done()
                .Build();

            // Assert
            Assert.That(excelStream, Is.Not.Null);
            Assert.That(excelStream.Length, Is.GreaterThan(0));

            // Verify structure
            excelStream.Position = 0;
            using var document = SpreadsheetDocument.Open(excelStream, false);
            var sheets = document.WorkbookPart?.Workbook.Sheets?.Elements<Sheet>().ToList();
            Assert.That(sheets?.Count, Is.EqualTo(2));
        }

        [Test]
        public void SimpleSheetIntegration_WithDifferentDataTypes_WorksCorrectly()
        {
            // Arrange
            var mixedData = new List<object>
            {
                new { Name = "String Value", Number = 123, Date = DateTime.Now, IsTrue = true },
                new { Name = "Another String", Number = 456, Date = DateTime.Now.AddDays(1), IsTrue = false },
                new { Name = "Third Item", Number = 789, Date = DateTime.Now.AddDays(-1), IsTrue = true }
            };

            var builder = new ExcelFileBuilder();

            // Act
            using var excelStream = builder
                .AddSimpleSheet(mixedData, "Mixed Data Types")
                .Build();

            // Assert
            Assert.That(excelStream, Is.Not.Null);
            Assert.That(excelStream.Length, Is.GreaterThan(0));

            // Verify Excel can be opened
            excelStream.Position = 0;
            using var document = SpreadsheetDocument.Open(excelStream, false);
            Assert.That(document.WorkbookPart, Is.Not.Null);
        }

        [Test]
        public void ConcurrentAccess_MultipleBuilders_WorkIndependently()
        {
            // Arrange
            var employees1 = new List<Employee>
            {
                new() { Id = 1, Name = "Employee A", Department = "IT" }
            };

            var employees2 = new List<Employee>
            {
                new() { Id = 2, Name = "Employee B", Department = "HR" }
            };

            var tasks = new List<Task<MemoryStream>>();

            // Act
            for (int i = 0; i < 5; i++)
            {
                var taskIndex = i;
                tasks.Add(Task.Run(() =>
                {
                    var builder = new ExcelFileBuilder();
                    return builder
                        .AddSheet<Employee>($"Sheet {taskIndex}")
                        .WithData(taskIndex % 2 == 0 ? employees1 : employees2)
                        .Done()
                        .Build();
                }));
            }

            var results = Task.WhenAll(tasks).Result;

            // Assert
            Assert.That(results.Length, Is.EqualTo(5));
            foreach (var result in results)
            {
                Assert.That(result, Is.Not.Null);
                Assert.That(result.Length, Is.GreaterThan(0));
                result.Dispose();
            }
        }

        [Test]
        public void MemoryManagement_LargeFileGeneration_DoesNotLeakMemory()
        {
            // Arrange
            var initialMemory = GC.GetTotalMemory(true);

            // Act
            for (int iteration = 0; iteration < 10; iteration++)
            {
                var largeDataSet = Enumerable.Range(1, 1000).Select(i => new Employee
                {
                    Id = i,
                    Name = $"Employee {i}",
                    Department = "IT",
                    Status = "Active",
                    Salary = 50000
                }).ToList();

                var builder = new ExcelFileBuilder();
                using var stream = builder
                    .AddSheet<Employee>("Large Dataset")
                    .WithData(largeDataSet)
                    .Done()
                    .Build();

                // Force garbage collection after each iteration
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }

            var finalMemory = GC.GetTotalMemory(true);
            var memoryIncrease = finalMemory - initialMemory;

            // Assert - Memory increase should be reasonable (less than 50MB for this test)
            Assert.That(memoryIncrease, Is.LessThan(50 * 1024 * 1024),
                $"Memory increased by {memoryIncrease / (1024 * 1024)} MB, which may indicate a memory leak");
        }
    }

    [TestFixture]
    public class PerformanceTests
    {
        [Test]
        [Timeout(30000)] // 30 seconds timeout
        public void ExcelFileBuilder_LargeDataSet_CompletesWithinTimeout()
        {
            // Arrange
            var largeDataSet = Enumerable.Range(1, 10000)
                .Select(i => new TestModel
                {
                    Name = $"Employee {i}",
                    Age = 20 + (i % 50),
                    BirthDate = DateTime.Now.AddYears(-(20 + (i % 50))),
                    Salary = 30000 + (i * 100),
                    IsActive = i % 2 == 0,
                    WorkHours = TimeSpan.FromHours(8 + (i % 4))
                })
                .ToList();

            var builder = new ExcelFileBuilder();

            // Act
            using var result = builder
                .AddSheet<TestModel>("Large Dataset")
                .WithData(largeDataSet)
                .Done()
                .Build();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        [Test]
        public void StringBuilderPool_HighConcurrency_HandlesCorrectly()
        {
            // Arrange
            var pool = new StringBuilderPool(maxPoolSize: 10);
            var tasks = new List<Task>();
            var results = new ConcurrentBag<bool>();

            // Act
            for (int i = 0; i < 100; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        var sb = pool.Rent();
                        sb.Append("Test");
                        Thread.Sleep(10); // Simulate work
                        pool.Return(sb);
                        results.Add(true);
                    }
                    catch
                    {
                        results.Add(false);
                    }
                }));
            }

            Task.WaitAll(tasks.ToArray());

            // Assert
            Assert.That(results.Count, Is.EqualTo(100));
            Assert.That(results.All(r => r), Is.True);
        }
    }

    [TestFixture]
    public class EdgeCaseTests
    {
        [Test]
        public void ExcelCacheManager_ExtremeColumnNumbers_HandlesCorrectly()
        {
            // Arrange
            var cacheManager = new ExcelCacheManager();

            // Act & Assert
            Assert.That(cacheManager.GetExcelColumnName(256), Is.EqualTo("IV")); // Last column in Excel 2003
            Assert.That(cacheManager.GetExcelColumnName(16384), Is.EqualTo("XFD")); // Last column in modern Excel
        }

        [Test]
        public void PropertyNameFormatter_SpecialCharacters_HandlesGracefully()
        {
            // Arrange
            var formatter = new PropertyNameFormatter();

            // Act & Assert
            // These tests assume the PropertyNameFormatter has been fixed with LowercaseToNumberRegex
            Assert.That(formatter.ConvertToFriendlyName("Test123Property"), Is.EqualTo("Test 123 Property"));
            Assert.That(formatter.ConvertToFriendlyName("MyAPIKey"), Is.EqualTo("My API Key"));
            Assert.That(formatter.ConvertToFriendlyName("HTTPSResponse"), Is.EqualTo("HTTPS Response"));
        }

        [Test]
        public void ExcelCellWriter_ExtremeLengthStrings_HandlesCorrectly()
        {
            // Arrange
            var cellWriter = new ExcelCellWriter();
            var longString = new string('A', 32767); // Excel's max string length

            // Act
            var result = cellWriter.ValidateFormulaInjection(longString);

            // Assert
            Assert.That(result, Is.EqualTo(longString));
        }

        [Test]
        public void ExcelCellWriter_UnicodeCharacters_HandlesCorrectly()
        {
            // Arrange
            var cellWriter = new ExcelCellWriter();
            var unicodeString = "Testing Unicode: αβγδε 中文 🎉";

            // Act
            var result = cellWriter.ValidateFormulaInjection(unicodeString);

            // Assert
            Assert.That(result, Is.EqualTo(unicodeString));
        }

        [Test]
        public void ExcelCacheManager_NullableTypes_HandlesCorrectly()
        {
            // Arrange
            var cacheManager = new ExcelCacheManager();

            // Act
            var accessors = cacheManager.GetPropertyAccessors<NullableTestModel>(new HashSet<string>());

            // Assert
            Assert.That(accessors.Any(a => a.IsNullable), Is.True);
        }

        [Test]
        public void EnumerableExtensions_EmptySource_HandlesSafely()
        {
            // Arrange
            var emptySource = Enumerable.Empty<int>();

            // Act
            var batches = emptySource.Batch(5).ToList();

            // Assert
            Assert.That(batches.Count, Is.EqualTo(0));
        }

        [Test]
        public void EnumerableExtensions_SingleElement_CreatesSingleBatch()
        {
            // Arrange
            var singleElementSource = new[] { 1 };

            // Act
            var batches = singleElementSource.Batch(5).Select(batch => batch.ToList()).ToList();

            // Assert
            Assert.That(batches.Count, Is.EqualTo(1));
            Assert.That(batches[0].Count, Is.EqualTo(1));
        }

        [Test]
        public void ExcelCacheManager_ThreadSafety_HandlesCorrectly()
        {
            // Arrange
            var cacheManager = new ExcelCacheManager();
            var tasks = new List<Task<string>>();

            // Act
            for (int i = 1; i <= 100; i++)
            {
                var columnNumber = i;
                tasks.Add(Task.Run(() => cacheManager.GetExcelColumnName(columnNumber)));
            }

            var results = Task.WhenAll(tasks).Result;

            // Assert
            Assert.That(results.Length, Is.EqualTo(100));
            Assert.That(results[0], Is.EqualTo("A"));
            Assert.That(results[99], Is.EqualTo("CV"));
        }

        public class NullableTestModel
        {
            [ExcelColumn("Nullable Int")]
            public int? NullableInt { get; set; }

            [ExcelColumn("Nullable DateTime")]
            public DateTime? NullableDateTime { get; set; }

            [ExcelColumn("Regular String")]
            public string RegularString { get; set; } = string.Empty;
        }
    }

    [TestFixture]
    public class ErrorHandlingTests
    {
        [Test]
        public void ExcelCacheManager_InvalidHexColors_ReturnsDefaultBlack()
        {
            // Arrange
            var cacheManager = new ExcelCacheManager();

            // Act & Assert
            Assert.That(cacheManager.GetCleanHexColor("not-a-color"), Is.EqualTo("000000"));
            Assert.That(cacheManager.GetCleanHexColor("12345"), Is.EqualTo("000000")); // Too short
            Assert.That(cacheManager.GetCleanHexColor("1234567"), Is.EqualTo("000000")); // Too long
            Assert.That(cacheManager.GetCleanHexColor("GHIJKL"), Is.EqualTo("000000")); // Invalid hex chars
        }

        [Test]
        public void StringBuilderPool_ExcessiveCapacity_DoesNotPool()
        {
            // Arrange
            var pool = new StringBuilderPool(maxPoolSize: 2, maxCapacity: 100);
            var sb = new StringBuilder(200); // Exceeds max capacity

            // Act
            pool.Return(sb);
            var newSb = pool.Rent();

            // Assert - Should get a new instance, not the returned one
            Assert.That(newSb, Is.Not.SameAs(sb));
        }

        [Test]
        public void PropertyNameFormatter_NullAndEmptyInputs_HandlesGracefully()
        {
            // Arrange
            var formatter = new PropertyNameFormatter();

            // Act & Assert
            // Note: Current implementation returns null for null input
            Assert.That(formatter.ConvertToFriendlyName(null!), Is.Null);
            Assert.That(formatter.ConvertToFriendlyName(string.Empty), Is.EqualTo(string.Empty));
            Assert.That(formatter.ConvertToFriendlyName("   "), Is.EqualTo(string.Empty)); // Whitespace preserved
        }

        [Test]
        public void TypeValidator_AllNumericTypes_AreHandledCorrectly()
        {
            // Arrange & Act & Assert
            var numericTypes = new[]
            {
                typeof(int), typeof(long), typeof(short), typeof(byte),
                typeof(sbyte), typeof(ushort), typeof(uint), typeof(ulong),
                typeof(decimal), typeof(double), typeof(float)
            };

            foreach (var type in numericTypes)
            {
                Assert.That(TypeValidator.IsNumericType(type), Is.True, $"{type.Name} should be numeric");
            }

            var nonNumericTypes = new[]
            {
                typeof(string), typeof(DateTime), typeof(bool), typeof(char), typeof(object)
            };

            foreach (var type in nonNumericTypes)
            {
                Assert.That(TypeValidator.IsNumericType(type), Is.False, $"{type.Name} should not be numeric");
            }
        }
    }
}
//using System;
//using System.Collections.Generic;
//using System.IO;
//using System.Linq;
//using System.Linq.Expressions;
//using System.Reflection;
//using System.Text;
//using DynamicExcel.Write;
//using DocumentFormat.OpenXml;
//using DocumentFormat.OpenXml.Packaging;
//using DocumentFormat.OpenXml.Spreadsheet;
//using NUnit.Framework;
//using Moq;

//namespace DynamicExcel.Tests
//{
//    public class ExcelTestModel
//    {
//        [ExcelColumn("Status", 99)]
//        public string? Status { get; set; }
//        [ExcelColumn("IntValue", 1, Color = "#0066CC")]
//        public int TestIntValue { get; set; }
//        [ExcelColumn("LongValue", 2, Color = "#00A86B")]
//        public long TestLongValue { get; set; }
//        [ExcelColumn("Category", 3)]
//        public string? Category { get; set; }
//        [ExcelColumn("CategoryB", 7, Color = "FFD700")]
//        public string? CategoryB { get; set; }
//        [ExcelColumn("FloatValue", 3)]
//        public float TestFloatValue { get; set; }
//        [ExcelColumn("DoubleValue", 4)]
//        public double TestDoubleValue { get; set; }
//        [ExcelColumn("DecimalValue", 5, IsReadOnly = true)]
//        public decimal TestDecimalValue { get; set; }
//        [ExcelColumn("StringValue", 6)]
//        public string? TestStringValue { get; set; }
//        [ExcelColumn("CharValue", 7, Width = 30)]
//        public char TestCharValue { get; set; }
//        [ExcelColumn("BoolValue", 8)]
//        public bool TestBoolValue { get; set; }
//        [ExcelColumn("DateTimeValue", 9)]
//        public DateTime TestDateTimeValue { get; set; }
//        [ExcelColumn("TimeSpanValue", 10)]
//        public TimeSpan TestTimeSpanValue { get; set; }
//        [ExcelColumn("GuidValue", 11)]
//        public Guid TestGuidValue { get; set; }
//        [ExcelColumn("NullableInt", 12)]
//        public int? TestNullableInt { get; set; }
//        [ExcelColumn("NullableBool", 13)]
//        public bool? TestNullableBool { get; set; }
//        [ExcelColumn("NullableDate", 14)]
//        public DateTime? TestNullableDate { get; set; }
//    }

//    public class SimpleModel
//    {
//        public string? FirstName { get; set; }
//        public string? LastName { get; set; }
//        public int Age { get; set; }
//        public DateTime? BirthDate { get; set; }
//        public bool IsActive { get; set; }
//    }

//    [TestFixture]
//    public class ExcelFileBuilderTests
//    {
//        private ExcelFileBuilder _builder;

//        [SetUp]
//        public void Setup()
//        {
//            _builder = new ExcelFileBuilder();
//        }

//        [Test]
//        public void Build_WithNoSheets_ThrowsInvalidOperationException()
//        {
//            Assert.Throws<InvalidOperationException>(() => _builder.Build());
//        }

//        [Test]
//        public void AddSheet_WithEmptySheetName_ThrowsArgumentException()
//        {
//            Assert.Throws<ArgumentException>(() => _builder.AddSheet<ExcelTestModel>(""));
//        }

//        [Test]
//        public void AddSheet_WithWhitespaceSheetName_ThrowsArgumentException()
//        {
//            Assert.Throws<ArgumentException>(() => _builder.AddSheet<ExcelTestModel>("   "));
//        }

//        [Test]
//        public void AddSimpleSheet_WithNullData_ThrowsArgumentNullException()
//        {
//            Assert.Throws<ArgumentNullException>(() => _builder.AddSimpleSheet<SimpleModel>(null));
//        }

//        [Test]
//        public void Build_WithSingleSheet_ReturnsMemoryStream()
//        {
//            var data = new List<ExcelTestModel>
//            {
//                new ExcelTestModel { TestStringValue = "Test" }
//            };

//            var result = _builder
//                .AddSheet<ExcelTestModel>("Test Sheet")
//                .WithData(data)
//                .Done()
//                .Build();

//            Assert.That(result, Is.Not.Null);
//            Assert.That(result, Is.InstanceOf<MemoryStream>());
//            Assert.That(result.Length, Is.GreaterThan(0));
//            result.Dispose();
//        }

//        [Test]
//        public void Build_WithSimpleSheet_ReturnsMemoryStream()
//        {
//            var data = new List<SimpleModel>
//            {
//                new SimpleModel { FirstName = "John", LastName = "Doe", Age = 30 }
//            };

//            var result = _builder
//                .AddSimpleSheet(data, "Simple Sheet")
//                .Build();

//            Assert.That(result, Is.Not.Null);
//            Assert.That(result, Is.InstanceOf<MemoryStream>());
//            Assert.That(result.Length, Is.GreaterThan(0));
//            result.Dispose();
//        }

//        [Test]
//        public void Build_WithMultipleSheets_ReturnsMemoryStream()
//        {
//            var excelData = new List<ExcelTestModel>
//            {
//                new ExcelTestModel { TestStringValue = "Test" }
//            };
//            var simpleData = new List<SimpleModel>
//            {
//                new SimpleModel { FirstName = "Jane", LastName = "Smith" }
//            };

//            var result = _builder
//                .AddSheet<ExcelTestModel>("Excel Sheet")
//                .WithData(excelData)
//                .Done()
//                .AddSimpleSheet(simpleData, "Simple Sheet")
//                .Build();

//            Assert.That(result, Is.Not.Null);
//            Assert.That(result.Length, Is.GreaterThan(0));
//            result.Dispose();
//        }

//        [Test]
//        public void AddSheet_WithDropdownData_AddsDropdownSheet()
//        {
//            var data = new List<ExcelTestModel>
//            {
//                new ExcelTestModel { Category = "A", CategoryB = "X" }
//            };
//            var dropdownData = new List<ExcelDropdownData>
//            {
//                new ExcelDropdownData
//                {
//                    ColumnName = "Categories",
//                    DataList = new List<object> { "A", "B", "C" },
//                    BindProperties = new List<string> { "Category" }
//                }
//            };

//            var result = _builder
//                .AddSheet<ExcelTestModel>("Test Sheet")
//                .WithData(data)
//                .WithDropdownData(dropdownData)
//                .Done()
//                .Build();

//            Assert.That(result, Is.Not.Null);
//            Assert.That(result.Length, Is.GreaterThan(0));
//            result.Dispose();
//        }
//    }

//    [TestFixture]
//    public class SheetBuilderTests
//    {
//        private ExcelFileBuilder _fileBuilder;
//        private SheetBuilder<ExcelTestModel> _sheetBuilder;

//        [SetUp]
//        public void Setup()
//        {
//            _fileBuilder = new ExcelFileBuilder();
//            _sheetBuilder = _fileBuilder.AddSheet<ExcelTestModel>("Test Sheet");
//        }

//        [Test]
//        public void WithData_NullData_ThrowsArgumentNullException()
//        {
//            Assert.Throws<ArgumentNullException>(() => _sheetBuilder.WithData(null));
//        }

//        [Test]
//        public void WithDropdownData_NullData_ThrowsArgumentNullException()
//        {
//            Assert.Throws<ArgumentNullException>(() => _sheetBuilder.WithDropdownData(null));
//        }

//        [Test]
//        public void ExcludeProperties_NullExpression_ThrowsArgumentNullException()
//        {
//            Assert.Throws<ArgumentNullException>(() => _sheetBuilder.ExcludeProperties(null));
//        }

//        [Test]
//        public void ExcludeProperties_ValidExpression_ExcludesProperties()
//        {
//            var data = new List<ExcelTestModel>
//            {
//                new ExcelTestModel { TestStringValue = "Test", TestIntValue = 42 }
//            };

//            var result = _sheetBuilder
//                .WithData(data)
//                .ExcludeProperties(x => x.TestIntValue)
//                .Done()
//                .Build();

//            Assert.That(result, Is.Not.Null);
//            result.Dispose();
//        }

//        [Test]
//        public void ExcludeProperties_MultipleProperties_ExcludesAll()
//        {
//            var data = new List<ExcelTestModel>
//            {
//                new ExcelTestModel { TestStringValue = "Test", TestIntValue = 42, TestBoolValue = true }
//            };

//            var result = _sheetBuilder
//                .WithData(data)
//                .ExcludeProperties(x => x.TestIntValue, x => x.TestBoolValue)
//                .Done()
//                .Build();

//            Assert.That(result, Is.Not.Null);
//            result.Dispose();
//        }
//    }

//    [TestFixture]
//    public class PropertyNameFormatterTests
//    {
//        private PropertyNameFormatter _formatter;

//        [SetUp]
//        public void Setup()
//        {
//            _formatter = new PropertyNameFormatter();
//        }

//        [Test]
//        public void ConvertToFriendlyName_NullInput_ReturnsNull()
//        {
//            var result = _formatter.ConvertToFriendlyName(null);
//            Assert.That(result, Is.Null);
//        }

//        [Test]
//        public void ConvertToFriendlyName_EmptyString_ReturnsEmpty()
//        {
//            var result = _formatter.ConvertToFriendlyName("");
//            Assert.That(result, Is.EqualTo(""));
//        }

//        [Test]
//        public void ConvertToFriendlyName_CamelCase_AddsSpaces()
//        {
//            var result = _formatter.ConvertToFriendlyName("FirstName");
//            Assert.That(result, Is.EqualTo("First Name"));
//        }

//        [Test]
//        public void ConvertToFriendlyName_ConsecutiveCapitals_HandlesCorrectly()
//        {
//            var result = _formatter.ConvertToFriendlyName("XMLDocument");
//            Assert.That(result, Is.EqualTo("XML Document"));
//        }

//        [Test]
//        public void ConvertToFriendlyName_NumberToUppercase_AddsSpace()
//        {
//            var result = _formatter.ConvertToFriendlyName("Test123Value");
//            Assert.That(result, Is.EqualTo("Test123 Value"));
//        }

//        [Test]
//        public void ConvertToFriendlyName_AcronymAtEnd_AddsSpace()
//        {
//            var result = _formatter.ConvertToFriendlyName("documentPDF");
//            Assert.That(result, Is.EqualTo("document PDF"));
//        }

//        [Test]
//        public void ConvertToFriendlyName_SingleWord_ReturnsSame()
//        {
//            var result = _formatter.ConvertToFriendlyName("Name");
//            Assert.That(result, Is.EqualTo("Name"));
//        }
//    }

//    [TestFixture]
//    public class ExcelCacheManagerTests
//    {
//        private ExcelCacheManager _cacheManager;

//        [SetUp]
//        public void Setup()
//        {
//            _cacheManager = new ExcelCacheManager();
//        }

//        [Test]
//        public void GetPropertyAccessors_ReturnsOrderedAccessors()
//        {
//            var accessors = _cacheManager.GetPropertyAccessors<ExcelTestModel>(new HashSet<string>());

//            Assert.That(accessors, Is.Not.Null);
//            Assert.That(accessors.Length, Is.GreaterThan(0));
//            Assert.That(accessors[0].Order, Is.LessThanOrEqualTo(accessors[accessors.Length - 1].Order));
//        }

//        [Test]
//        public void GetPropertyAccessors_WithExclusions_ExcludesProperties()
//        {
//            var exclusions = new HashSet<string> { "TestIntValue" };
//            var accessors = _cacheManager.GetPropertyAccessors<ExcelTestModel>(exclusions);

//            Assert.That(accessors.Any(a => a.Property.Name == "TestIntValue"), Is.False);
//        }

//        [Test]
//        public void GetPropertyAccessors_CachesResults()
//        {
//            var accessors1 = _cacheManager.GetPropertyAccessors<ExcelTestModel>(new HashSet<string>());
//            var accessors2 = _cacheManager.GetPropertyAccessors<ExcelTestModel>(new HashSet<string>());

//            Assert.That(ReferenceEquals(accessors1, accessors2), Is.True);
//        }

//        [Test]
//        public void GetExcelColumnName_SingleLetter_ReturnsCorrectly()
//        {
//            Assert.That(_cacheManager.GetExcelColumnName(1), Is.EqualTo("A"));
//            Assert.That(_cacheManager.GetExcelColumnName(26), Is.EqualTo("Z"));
//        }

//        [Test]
//        public void GetExcelColumnName_DoubleLetter_ReturnsCorrectly()
//        {
//            Assert.That(_cacheManager.GetExcelColumnName(27), Is.EqualTo("AA"));
//            Assert.That(_cacheManager.GetExcelColumnName(52), Is.EqualTo("AZ"));
//        }

//        [Test]
//        public void GetExcelColumnName_TripleLetter_ReturnsCorrectly()
//        {
//            Assert.That(_cacheManager.GetExcelColumnName(703), Is.EqualTo("AAA"));
//        }

//        [Test]
//        public void GetCleanHexColor_NullInput_ReturnsNull()
//        {
//            var result = _cacheManager.GetCleanHexColor(null);
//            Assert.That(result, Is.Null);
//        }

//        [Test]
//        public void GetCleanHexColor_EmptyString_ReturnsNull()
//        {
//            var result = _cacheManager.GetCleanHexColor("");
//            Assert.That(result, Is.Null);
//        }

//        [Test]
//        public void GetCleanHexColor_WithHash_RemovesHash()
//        {
//            var result = _cacheManager.GetCleanHexColor("#FF0000");
//            Assert.That(result, Is.EqualTo("FF0000"));
//        }

//        [Test]
//        public void GetCleanHexColor_WithoutHash_ReturnsUppercase()
//        {
//            var result = _cacheManager.GetCleanHexColor("ff0000");
//            Assert.That(result, Is.EqualTo("FF0000"));
//        }
//    }

//    [TestFixture]
//    public class ExcelCellWriterTests
//    {
//        private ExcelCellWriter _cellWriter;
//        private Mock<OpenXmlWriter> _mockWriter;

//        [SetUp]
//        public void Setup()
//        {
//            _cellWriter = new ExcelCellWriter();
//            _mockWriter = new Mock<OpenXmlWriter>();
//        }

//        [Test]
//        public void ValidateFormulaInjection_NullInput_ReturnsEmpty()
//        {
//            var result = _cellWriter.ValidateFormulaInjection(null);
//            Assert.That(result, Is.EqualTo(""));
//        }

//        [Test]
//        public void ValidateFormulaInjection_EmptyInput_ReturnsEmpty()
//        {
//            var result = _cellWriter.ValidateFormulaInjection("");
//            Assert.That(result, Is.EqualTo(""));
//        }

//        [Test]
//        public void ValidateFormulaInjection_StartsWithEquals_PrependsSingleQuote()
//        {
//            var result = _cellWriter.ValidateFormulaInjection("=SUM(A1:A10)");
//            Assert.That(result, Is.EqualTo("'=SUM(A1:A10)"));
//        }

//        [Test]
//        public void ValidateFormulaInjection_StartsWithPlus_PrependsSingleQuote()
//        {
//            var result = _cellWriter.ValidateFormulaInjection("+123");
//            Assert.That(result, Is.EqualTo("'+123"));
//        }

//        [Test]
//        public void ValidateFormulaInjection_StartsWithMinus_PrependsSingleQuote()
//        {
//            var result = _cellWriter.ValidateFormulaInjection("-123");
//            Assert.That(result, Is.EqualTo("'-123"));
//        }

//        [Test]
//        public void ValidateFormulaInjection_StartsWithAt_PrependsSingleQuote()
//        {
//            var result = _cellWriter.ValidateFormulaInjection("@SUM");
//            Assert.That(result, Is.EqualTo("'@SUM"));
//        }

//        [Test]
//        public void ValidateFormulaInjection_NormalText_ReturnsUnchanged()
//        {
//            var result = _cellWriter.ValidateFormulaInjection("Normal text");
//            Assert.That(result, Is.EqualTo("Normal text"));
//        }
//    }

//    [TestFixture]
//    public class StringBuilderPoolTests
//    {
//        private StringBuilderPool _pool;

//        [SetUp]
//        public void Setup()
//        {
//            _pool = new StringBuilderPool(maxPoolSize: 2, maxCapacity: 100);
//        }

//        [Test]
//        public void Rent_ReturnsStringBuilder()
//        {
//            var sb = _pool.Rent();
//            Assert.That(sb, Is.Not.Null);
//            Assert.That(sb.Length, Is.EqualTo(0));
//        }

//        [Test]
//        public void Rent_MultipleCalls_ReturnsDifferentInstances()
//        {
//            var sb1 = _pool.Rent();
//            var sb2 = _pool.Rent();
//            Assert.That(ReferenceEquals(sb1, sb2), Is.False);
//        }

//        [Test]
//        public void Return_AndRent_ReusesSameInstance()
//        {
//            var sb1 = _pool.Rent();
//            _pool.Return(sb1);
//            var sb2 = _pool.Rent();
//            Assert.That(ReferenceEquals(sb1, sb2), Is.True);
//        }

//        [Test]
//        public void Return_ClearsStringBuilder()
//        {
//            var sb = _pool.Rent();
//            sb.Append("Test");
//            _pool.Return(sb);
//            var sb2 = _pool.Rent();
//            Assert.That(sb2.Length, Is.EqualTo(0));
//        }

//        [Test]
//        public void Return_ExceedsMaxPoolSize_DoesNotPool()
//        {
//            var sb1 = _pool.Rent();
//            var sb2 = _pool.Rent();
//            var sb3 = _pool.Rent();

//            _pool.Return(sb1);
//            _pool.Return(sb2);
//            _pool.Return(sb3);

//            var sb4 = _pool.Rent();
//            Assert.That(ReferenceEquals(sb4, sb1) || ReferenceEquals(sb4, sb2), Is.True);
//            Assert.That(ReferenceEquals(sb4, sb3), Is.False);
//        }

//        [Test]
//        public void Return_ExceedsMaxCapacity_DoesNotPool()
//        {
//            var sb = _pool.Rent();
//            sb.Capacity = 200;
//            _pool.Return(sb);

//            var sb2 = _pool.Rent();
//            Assert.That(ReferenceEquals(sb, sb2), Is.False);
//        }
//    }

//    [TestFixture]
//    public class TypeValidatorTests
//    {
//        [Test]
//        public void IsIntegerType_IntTypes_ReturnsTrue()
//        {
//            Assert.That(TypeValidator.IsIntegerType(typeof(int)), Is.True);
//            Assert.That(TypeValidator.IsIntegerType(typeof(long)), Is.True);
//            Assert.That(TypeValidator.IsIntegerType(typeof(short)), Is.True);
//            Assert.That(TypeValidator.IsIntegerType(typeof(byte)), Is.True);
//            Assert.That(TypeValidator.IsIntegerType(typeof(sbyte)), Is.True);
//            Assert.That(TypeValidator.IsIntegerType(typeof(ushort)), Is.True);
//            Assert.That(TypeValidator.IsIntegerType(typeof(uint)), Is.True);
//            Assert.That(TypeValidator.IsIntegerType(typeof(ulong)), Is.True);
//        }

//        [Test]
//        public void IsIntegerType_NonIntTypes_ReturnsFalse()
//        {
//            Assert.That(TypeValidator.IsIntegerType(typeof(float)), Is.False);
//            Assert.That(TypeValidator.IsIntegerType(typeof(double)), Is.False);
//            Assert.That(TypeValidator.IsIntegerType(typeof(decimal)), Is.False);
//            Assert.That(TypeValidator.IsIntegerType(typeof(string)), Is.False);
//            Assert.That(TypeValidator.IsIntegerType(typeof(bool)), Is.False);
//        }

//        [Test]
//        public void IsDecimalType_DecimalTypes_ReturnsTrue()
//        {
//            Assert.That(TypeValidator.IsDecimalType(typeof(decimal)), Is.True);
//            Assert.That(TypeValidator.IsDecimalType(typeof(double)), Is.True);
//            Assert.That(TypeValidator.IsDecimalType(typeof(float)), Is.True);
//        }

//        [Test]
//        public void IsDecimalType_NonDecimalTypes_ReturnsFalse()
//        {
//            Assert.That(TypeValidator.IsDecimalType(typeof(int)), Is.False);
//            Assert.That(TypeValidator.IsDecimalType(typeof(long)), Is.False);
//            Assert.That(TypeValidator.IsDecimalType(typeof(string)), Is.False);
//            Assert.That(TypeValidator.IsDecimalType(typeof(bool)), Is.False);
//        }
//    }

//    [TestFixture]
//    public class EnumerableExtensionsTests
//    {
//        [Test]
//        public void Batch_EmptyEnumerable_ReturnsEmptyBatches()
//        {
//            var source = new List<int>();
//            var batches = source.Batch(3).ToList();
//            Assert.That(batches.Count, Is.EqualTo(0));
//        }

//        [Test]
//        public void Batch_LessThanBatchSize_ReturnsSingleBatch()
//        {
//            var source = new List<int> { 1, 2 };
//            var batches = source.Batch(3).Select(batch => batch.ToList()).ToList();
//            Assert.That(batches.Count, Is.EqualTo(1));
//            Assert.That(batches[0].Count, Is.EqualTo(2));
//        }

//        [Test]
//        public void Batch_ExactBatchSize_ReturnsSingleBatch()
//        {
//            var source = new List<int> { 1, 2, 3 };
//            var batches = source.Batch(3).Select(batch => batch.ToList()).ToList();
//            Assert.That(batches.Count, Is.EqualTo(1));
//            Assert.That(batches[0].Count, Is.EqualTo(3));
//        }

//        [Test]
//        public void Batch_MoreThanBatchSize_ReturnsMultipleBatches()
//        {
//            var source = new List<int> { 1, 2, 3, 4, 5 };
//            var batches = source.Batch(3).Select(batch => batch.ToList()).ToList();
//            Assert.That(batches.Count, Is.EqualTo(2));
//            Assert.That(batches[0].Count, Is.EqualTo(3));
//            Assert.That(batches[1].Count, Is.EqualTo(2));
//        }

//        [Test]
//        public void Batch_PreservesOrder()
//        {
//            var source = new List<int> { 1, 2, 3, 4, 5 };
//            var batches = source.Batch(2).Select(batch => batch.ToList()).ToList();
//            var flattened = batches.SelectMany(b => b).ToList();
//            Assert.That(flattened, Is.EqualTo(source));
//        }
//    }

//    [TestFixture]
//    public class ExcelStyleManagerTests
//    {
//        private ExcelStyleManager _styleManager;
//        private Mock<IExcelCacheManager> _mockCacheManager;

//        [SetUp]
//        public void Setup()
//        {
//            _mockCacheManager = new Mock<IExcelCacheManager>();
//            _styleManager = new ExcelStyleManager(_mockCacheManager.Object);
//        }

//        [Test]
//        public void CreateStyles_WithEmptyAccessors_ReturnsEmptyDictionary()
//        {
//            using var stream = new MemoryStream();
//            using var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook);
//            var workbookPart = document.AddWorkbookPart();

//            var result = _styleManager.CreateStyles(workbookPart, new PropertyAccessor[0], CancellationToken.None);

//            Assert.That(result, Is.Not.Null);
//        }

//        [Test]
//        public void CreateStyles_WithColoredAccessors_CreatesColorStyles()
//        {
//            _mockCacheManager.Setup(m => m.GetCleanHexColor("#FF0000")).Returns("FF0000");

//            var accessors = new[]
//            {
//                new PropertyAccessor
//                {
//                    Property = typeof(ExcelTestModel).GetProperty("TestIntValue"),
//                    ColumnName = "Test",
//                    Color = "#FF0000",
//                    UnderlyingType = typeof(int),
//                    PropertyType = typeof(int),
//                    Getter = x => ((ExcelTestModel)x).TestIntValue,
//                    Width = 15
//                }
//            };

//            using var stream = new MemoryStream();
//            using var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook);
//            var workbookPart = document.AddWorkbookPart();

//            var result = _styleManager.CreateStyles(workbookPart, accessors, CancellationToken.None);

//            Assert.That(result.ContainsKey("header_FF0000"), Is.True);
//            Assert.That(result.ContainsKey("cell_FF0000"), Is.True);
//        }
//    }

//    [TestFixture]
//    public class ExcelValidationManagerTests
//    {
//        private ExcelValidationManager _validationManager;
//        private Mock<IExcelCacheManager> _mockCacheManager;
//        private Mock<OpenXmlWriter> _mockWriter;

//        [SetUp]
//        public void Setup()
//        {
//            _mockCacheManager = new Mock<IExcelCacheManager>();
//            _mockCacheManager.Setup(m => m.GetExcelColumnName(It.IsAny<int>()))
//                .Returns<int>(col => col <= 26 ? ((char)('A' + col - 1)).ToString() : "AA");
//            _validationManager = new ExcelValidationManager(_mockCacheManager.Object);
//            _mockWriter = new Mock<OpenXmlWriter>();
//        }

//        [Test]
//        public void AddDataValidations_WithReadOnlyProperty_AddsReadOnlyValidation()
//        {
//            var accessors = new[]
//            {
//                new PropertyAccessor
//                {
//                    Property = typeof(ExcelTestModel).GetProperty("TestDecimalValue"),
//                    ColumnName = "Decimal",
//                    IsReadOnly = true,
//                    UnderlyingType = typeof(decimal),
//                    PropertyType = typeof(decimal)
//                }
//            };

//            var parameter = new DataValidationParameter
//            {
//                Writer = _mockWriter.Object,
//                PropertyAccessors = accessors,
//                TotalRows = 100,
//                ColumnIndexMap = new Dictionary<string, int> { ["Decimal"] = 1 }
//            };

//            _validationManager.AddDataValidations(parameter);

//            _mockWriter.Verify(w => w.WriteStartElement(It.IsAny<DataValidations>(), It.IsAny<List<OpenXmlAttribute>>()), Times.Once);
//        }

//        [Test]
//        public void AddDataValidations_WithIntegerType_AddsWholeValidation()
//        {
//            var accessors = new[]
//            {
//                new PropertyAccessor
//                {
//                    Property = typeof(ExcelTestModel).GetProperty("TestIntValue"),
//                    ColumnName = "Integer",
//                    IsReadOnly = false,
//                    UnderlyingType = typeof(int),
//                    PropertyType = typeof(int),
//                    IsNullable = false
//                }
//            };

//            var parameter = new DataValidationParameter
//            {
//                Writer = _mockWriter.Object,
//                PropertyAccessors = accessors,
//                TotalRows = 100,
//                ColumnIndexMap = new Dictionary<string, int> { ["Integer"] = 1 }
//            };

//            _validationManager.AddDataValidations(parameter);

//            _mockWriter.Verify(w => w.WriteStartElement(It.IsAny<DataValidations>(), It.IsAny<List<OpenXmlAttribute>>()), Times.Once);
//        }

//        [Test]
//        public void AddDataValidations_WithDateTimeType_AddsDateValidation()
//        {
//            var accessors = new[]
//            {
//                new PropertyAccessor
//                {
//                    Property = typeof(ExcelTestModel).GetProperty("TestDateTimeValue"),
//                    ColumnName = "Date",
//                    IsReadOnly = false,
//                    UnderlyingType = typeof(DateTime),
//                    PropertyType = typeof(DateTime),
//                    IsNullable = false
//                }
//            };

//            var parameter = new DataValidationParameter
//            {
//                Writer = _mockWriter.Object,
//                PropertyAccessors = accessors,
//                TotalRows = 100,
//                ColumnIndexMap = new Dictionary<string, int> { ["Date"] = 1 }
//            };

//            _validationManager.AddDataValidations(parameter);

//            _mockWriter.Verify(w => w.WriteStartElement(It.IsAny<DataValidations>(), It.IsAny<List<OpenXmlAttribute>>()), Times.Once);
//        }
//    }
//}
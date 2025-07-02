using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace ExcelWrite.ExcelWriter;

public interface IWriteExcelService<TExcelModel>
    where TExcelModel : new()
{
    Stream Write(string filePath, string? sheetName, List<TExcelModel> data, List<ExcelDropdownData>? dropdownData, CancellationToken cancellationToken = default);
}

public interface IExcelCacheManager
{
    PropertyAccessor[] GetPropertyAccessors<TExcelModel>();

    string GetExcelColumnName(int columnNumber);

    string? GetCleanHexColor(string? hexColor);
}

public interface IExcelCellWriter
{
    void WriteCellValue(OpenXmlWriter writer, string cellReference, object value, Type underlyingType, uint? styleIndex);

    string ValidateFormulaInjection(string? valueString);
}

public interface IExcelStyleManager
{
    Dictionary<string, uint> CreateStyles(WorkbookPart workbookPart, PropertyAccessor[] propertyAccessors, CancellationToken cancellationToken);
}

public interface IExcelValidationManager
{
    void AddDataValidations(DataValidationParameter validationParameter);
}

public class ExcelCacheManager : IExcelCacheManager
{
    private readonly ConcurrentDictionary<Type, PropertyAccessor[]> _propertyAccessorCache = new();
    private readonly ConcurrentDictionary<string, string> _excelColumnNameCache = new();
    private readonly ConcurrentDictionary<string, string> _cleanHexColorCache = new();

    public PropertyAccessor[] GetPropertyAccessors<TExcelModel>()
    {
        return _propertyAccessorCache.GetOrAdd(typeof(TExcelModel), type =>
        {
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var accessors = new List<PropertyAccessor>();

            foreach (var property in properties)
            {
                var attribute = property.GetCustomAttribute<ExcelColumnAttribute>();
                if (attribute != null)
                {
                    var accessor = CreatePropertyAccessor<TExcelModel>(property, attribute);
                    accessors.Add(accessor);
                }
            }

            return accessors.OrderBy(m => m.Order).ThenBy(m => m.Property.Name).ToArray();
        });
    }

    private PropertyAccessor CreatePropertyAccessor<TExcelModel>(PropertyInfo property, ExcelColumnAttribute attribute)
    {
        var parameter = Expression.Parameter(typeof(object), "x");
        var cast = Expression.Convert(parameter, typeof(TExcelModel));
        var propertyAccess = Expression.Property(cast, property);
        var convert = Expression.Convert(propertyAccess, typeof(object));
        var lambda = Expression.Lambda<Func<object, object>>(convert, parameter);
        var getter = lambda.Compile();

        var underlyingType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        return new PropertyAccessor
        {
            Property = property,
            ColumnName = attribute.Name,
            Order = attribute.OrderId,
            Color = attribute.Color,
            Width = attribute.Width > 0 ? attribute.Width : ExcelWriterConstants.DefaultColumnWidth,
            Getter = getter,
            PropertyType = property.PropertyType,
            UnderlyingType = underlyingType,
            IsNullable = Nullable.GetUnderlyingType(property.PropertyType) != null
        };
    }

    public string GetExcelColumnName(int columnNumber)
    {
        return _excelColumnNameCache.GetOrAdd(columnNumber.ToString(), _ => GetExcelColumnNameInternal(columnNumber));
    }

    private string GetExcelColumnNameInternal(int columnNumber)
    {
        Span<char> buffer = stackalloc char[10];
        var position = buffer.Length - 1;

        while (columnNumber > 0)
        {
            var modulo = (columnNumber - 1) % 26;
            buffer[position--] = (char)(65 + modulo);
            columnNumber = (columnNumber - modulo) / 26;
        }

        return new string(buffer.Slice(position + 1));
    }

    public string? GetCleanHexColor(string? hexColor)
    {
        if (string.IsNullOrEmpty(hexColor))
        {
            return null;
        }

        return _cleanHexColorCache.GetOrAdd(hexColor, color => color.Replace("#", string.Empty).ToUpper());
    }
}

public class ExcelCellWriter : IExcelCellWriter
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteCellValue(OpenXmlWriter writer, string cellReference, object value, Type underlyingType, uint? styleIndex)
    {
        WriteValueByType(writer, cellReference, value, underlyingType, styleIndex);
    }

    private void WriteValueByType(OpenXmlWriter writer, string cellReference, object value, Type underlyingType, uint? styleIndex)
    {
        switch (value)
        {
            case string stringValue:
                WriteStringValue(writer, cellReference, stringValue, styleIndex);
                break;
            case DateTime dateValue:
                WriteDateValue(writer, cellReference, dateValue, styleIndex);
                break;
            case TimeSpan timeSpanValue:
                WriteTimeSpanValue(writer, cellReference, timeSpanValue, styleIndex);
                break;
            case bool boolValue:
                WriteBooleanValue(writer, cellReference, boolValue, styleIndex);
                break;
            default:
                if (IsNumericType(underlyingType))
                {
                    WriteNumericValue(writer, cellReference, value, styleIndex);
                }
                else
                {
                    WriteGenericValue(writer, cellReference, value, styleIndex);
                }
                break;
        }
    }

    private void WriteStringValue(OpenXmlWriter writer, string cellReference, string stringValue, uint? styleIndex)
    {
        stringValue = SanitizeForXml(ValidateFormulaInjection(stringValue));

        var cell = new Cell
        {
            CellReference = cellReference,
            DataType = CellValues.InlineString,
        };

        if (styleIndex.HasValue)
        {
            cell.StyleIndex = styleIndex.Value;
        }

        writer.WriteStartElement(cell);
        writer.WriteStartElement(new InlineString());
        writer.WriteElement(new Text(stringValue));
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private string SanitizeForXml(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var sanitized = new StringBuilder(input.Length);

        foreach (var c in input)
        {
            if (IsValidXmlChar(c))
            {
                sanitized.Append(c);
            }
        }

        return sanitized.ToString();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsValidXmlChar(char c)
    {
        return c == 0x9 || c == 0xA || c == 0xD ||
               (c >= 0x20 && c <= 0xD7FF) ||
               (c >= 0xE000 && c <= 0xFFFD);
    }

    private void WriteDateValue(OpenXmlWriter writer, string cellReference, DateTime dateValue, uint? styleIndex)
    {
        var cell = new Cell
        {
            CellReference = cellReference,
            StyleIndex = styleIndex ?? 2,
        };

        writer.WriteStartElement(cell);
        writer.WriteElement(new CellValue(dateValue.ToOADate().ToString(CultureInfo.InvariantCulture)));
        writer.WriteEndElement();
    }

    private void WriteTimeSpanValue(OpenXmlWriter writer, string cellReference, TimeSpan timeSpanValue, uint? styleIndex)
    {
        var totalDays = timeSpanValue.TotalDays;

        var cell = new Cell
        {
            CellReference = cellReference,
            StyleIndex = styleIndex ?? 3,
        };

        writer.WriteStartElement(cell);
        writer.WriteElement(new CellValue(totalDays.ToString(CultureInfo.InvariantCulture)));
        writer.WriteEndElement();
    }

    private void WriteBooleanValue(OpenXmlWriter writer, string cellReference, bool boolValue, uint? styleIndex)
    {
        var cell = new Cell
        {
            CellReference = cellReference,
            DataType = CellValues.Boolean
        };

        if (styleIndex.HasValue)
        {
            cell.StyleIndex = styleIndex.Value;
        }

        writer.WriteStartElement(cell);
        writer.WriteElement(new CellValue(boolValue ? "1" : "0"));
        writer.WriteEndElement();
    }

    private void WriteNumericValue(OpenXmlWriter writer, string cellReference, object value, uint? styleIndex)
    {
        var cell = new Cell { CellReference = cellReference };

        if (styleIndex.HasValue)
        {
            cell.StyleIndex = styleIndex.Value;
        }

        writer.WriteStartElement(cell);
        writer.WriteElement(new CellValue(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty));
        writer.WriteEndElement();
    }

    private void WriteGenericValue(OpenXmlWriter writer, string cellReference, object value, uint? styleIndex)
    {
        var valueString = SanitizeForXml(ValidateFormulaInjection(value.ToString()));

        var cell = new Cell
        {
            CellReference = cellReference,
            DataType = CellValues.InlineString
        };

        if (styleIndex.HasValue)
        {
            cell.StyleIndex = styleIndex.Value;
        }

        writer.WriteStartElement(cell);
        writer.WriteStartElement(new InlineString());
        writer.WriteElement(new Text(valueString));
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string ValidateFormulaInjection(string? valueString)
    {
        if (string.IsNullOrEmpty(valueString))
        {
            return string.Empty;
        }

        var firstChar = valueString[0];
        if (firstChar == '=' || firstChar == '+' || firstChar == '-' || firstChar == '@')
        {
            return "'" + valueString;
        }

        return valueString;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsNumericType(Type type)
    {
        return type == typeof(sbyte) || type == typeof(byte) ||
               type == typeof(short) || type == typeof(ushort) ||
               type == typeof(int) || type == typeof(uint) ||
               type == typeof(long) || type == typeof(ulong) ||
               type == typeof(float) || type == typeof(double) ||
               type == typeof(decimal);
    }
}

public class ExcelStyleManager : IExcelStyleManager
{
    private readonly IExcelCacheManager _cacheManager;

    public ExcelStyleManager(IExcelCacheManager cacheManager)
    {
        _cacheManager = cacheManager;
    }

    public Dictionary<string, uint> CreateStyles(WorkbookPart workbookPart, PropertyAccessor[] propertyAccessors, CancellationToken cancellationToken)
    {
        var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
        var styleMappings = new Dictionary<string, uint>();

        using var writer = OpenXmlWriter.Create(stylesPart);
        writer.WriteStartElement(new Stylesheet());

        WriteNumberFormats(writer);
        WriteFonts(writer);
        var uniqueColors = ExtractUniqueColors(propertyAccessors);
        WriteFills(writer, uniqueColors, styleMappings);
        WriteBorders(writer);
        WriteCellFormats(writer, uniqueColors, styleMappings);

        writer.WriteEndElement();
        return styleMappings;
    }

    private void WriteNumberFormats(OpenXmlWriter writer)
    {
        writer.WriteStartElement(new NumberingFormats { Count = 2 });

        writer.WriteElement(new NumberingFormat
        {
            NumberFormatId = 164,
            FormatCode = "dd-mm-yyyy",
        });

        writer.WriteElement(new NumberingFormat
        {
            NumberFormatId = 165,
            FormatCode = "[h]:mm:ss",
        });

        writer.WriteEndElement();
    }

    private void WriteFonts(OpenXmlWriter writer)
    {
        writer.WriteStartElement(new Fonts { Count = 2 });
        writer.WriteElement(new Font());
        writer.WriteElement(new Font(new Bold()));
        writer.WriteEndElement();
    }

    private HashSet<string> ExtractUniqueColors(PropertyAccessor[] propertyAccessors)
    {
        var uniqueColors = new HashSet<string>();
        foreach (var accessor in propertyAccessors)
        {
            if (!string.IsNullOrEmpty(accessor.Color))
            {
                var cleanHex = _cacheManager.GetCleanHexColor(accessor.Color);
                if (cleanHex != null)
                {
                    uniqueColors.Add(cleanHex);
                }
            }
        }
        return uniqueColors;
    }

    private void WriteFills(OpenXmlWriter writer, HashSet<string> uniqueColors, Dictionary<string, uint> styleMappings)
    {
        var fills = new List<Fill>
        {
            new(new PatternFill { PatternType = PatternValues.None }),
            new(new PatternFill { PatternType = PatternValues.Gray125 }),
        };

        uint colorIndex = 2;
        foreach (var cleanHex in uniqueColors)
        {
            fills.Add(new Fill(new PatternFill
            {
                PatternType = PatternValues.Solid,
                ForegroundColor = new ForegroundColor { Rgb = cleanHex },
            }));

            styleMappings[$"fill_header_{cleanHex}"] = colorIndex++;

            fills.Add(new Fill(new PatternFill
            {
                PatternType = PatternValues.Solid,
                ForegroundColor = new ForegroundColor { Rgb = cleanHex },
            }));

            styleMappings[$"fill_cell_{cleanHex}"] = colorIndex++;
        }

        writer.WriteStartElement(new Fills { Count = (uint)fills.Count });

        foreach (var fill in fills)
        {
            writer.WriteElement(fill);
        }

        writer.WriteEndElement();
    }

    private void WriteBorders(OpenXmlWriter writer)
    {
        writer.WriteStartElement(new Borders { Count = 3 });

        writer.WriteElement(new Border());

        WriteBorderWithStyle(writer, "FFD0D0D0", BorderStyleValues.Thin, BorderStyleValues.Thin);
        WriteBorderWithStyle(writer, "FF808080", BorderStyleValues.Thin, BorderStyleValues.Medium);

        writer.WriteEndElement();
    }

    private void WriteBorderWithStyle(OpenXmlWriter writer, string color, BorderStyleValues sideStyle, BorderStyleValues topBottomStyle)
    {
        writer.WriteStartElement(new Border());
        writer.WriteElement(new LeftBorder(new Color { Rgb = color }) { Style = sideStyle });
        writer.WriteElement(new RightBorder(new Color { Rgb = color }) { Style = sideStyle });
        writer.WriteElement(new TopBorder(new Color { Rgb = color }) { Style = topBottomStyle });
        writer.WriteElement(new BottomBorder(new Color { Rgb = color }) { Style = topBottomStyle });
        writer.WriteElement(new DiagonalBorder());
        writer.WriteEndElement();
    }

    private void WriteCellFormats(OpenXmlWriter writer, HashSet<string> uniqueColors, Dictionary<string, uint> styleMappings)
    {
        var cellFormats = new List<CellFormat>
        {
            new() { FontId = 0, FillId = 0, BorderId = 0 },
            new() { FontId = 1, FillId = 0, BorderId = 2, ApplyFont = true, ApplyBorder = true },
            new() { FontId = 0, FillId = 0, BorderId = 1, NumberFormatId = 164, ApplyNumberFormat = true, ApplyBorder = true },
            new() { FontId = 0, FillId = 0, BorderId = 1, NumberFormatId = 165, ApplyNumberFormat = true, ApplyBorder = true },
        };

        foreach (var cleanHex in uniqueColors)
        {
            AddColorFormats(cellFormats, styleMappings, cleanHex);
        }

        writer.WriteStartElement(new CellFormats { Count = (uint)cellFormats.Count });
        foreach (var format in cellFormats)
        {
            writer.WriteElement(format);
        }

        writer.WriteEndElement();
    }

    private void AddColorFormats(List<CellFormat> cellFormats, Dictionary<string, uint> styleMappings, string cleanHex)
    {
        if (styleMappings.ContainsKey($"fill_header_{cleanHex}"))
        {
            var headerFillId = styleMappings[$"fill_header_{cleanHex}"];
            cellFormats.Add(new CellFormat
            {
                FontId = 1,
                FillId = headerFillId,
                BorderId = 2,
                ApplyFont = true,
                ApplyFill = true,
                ApplyBorder = true,
            });
            styleMappings[$"header_{cleanHex}"] = (uint)cellFormats.Count - 1;
        }

        if (styleMappings.ContainsKey($"fill_cell_{cleanHex}"))
        {
            var cellFillId = styleMappings[$"fill_cell_{cleanHex}"];
            cellFormats.Add(new CellFormat
            {
                FontId = 0,
                FillId = cellFillId,
                BorderId = 1,
                ApplyFill = true,
                ApplyBorder = true,
            });
            styleMappings[$"cell_{cleanHex}"] = (uint)cellFormats.Count - 1;

            cellFormats.Add(new CellFormat
            {
                FontId = 0,
                FillId = cellFillId,
                BorderId = 1,
                NumberFormatId = 164,
                ApplyNumberFormat = true,
                ApplyFill = true,
                ApplyBorder = true,
            });
            styleMappings[$"cell_date_{cleanHex}"] = (uint)cellFormats.Count - 1;

            cellFormats.Add(new CellFormat
            {
                FontId = 0,
                FillId = cellFillId,
                BorderId = 1,
                NumberFormatId = 165,
                ApplyNumberFormat = true,
                ApplyFill = true,
                ApplyBorder = true,
            });
            styleMappings[$"cell_time_{cleanHex}"] = (uint)cellFormats.Count - 1;
        }
    }
}

public class ExcelValidationManager : IExcelValidationManager
{
    private readonly IExcelCacheManager _cacheManager;
    private readonly DateTime _minExcelDate = new(1900, 1, 1);
    private readonly DateTime _maxExcelDate = new(2100, 12, 31);
    private readonly string _dropdownDataSheetName = "Static Data";

    public ExcelValidationManager(IExcelCacheManager cacheManager)
    {
        _cacheManager = cacheManager;
    }

    public void AddDataValidations(DataValidationParameter validationParameter)
    {
        var validations = new List<DataValidationInfo>();

        AddDropdownValidations(validations, validationParameter);
        AddTypeBasedValidations(validations, validationParameter);

        if (validations.Any())
        {
            WriteDataValidations(validationParameter.Writer, validations);
        }
    }

    private void AddDropdownValidations(List<DataValidationInfo> validations, DataValidationParameter validationParameter)
    {
        if (validationParameter.DropdownData != null
            && validationParameter.DropdownData.Any()
            && validationParameter.ColumnMappings != null
            && validationParameter.ColumnPositions != null)
        {
            foreach (var mapping in validationParameter.ColumnMappings)
            {
                if (validationParameter.ColumnIndexMap.TryGetValue(mapping.Key, out var columnIndex) &&
                    validationParameter.ColumnPositions.TryGetValue(mapping.Value, out var sourceRange))
                {
                    validations.Add(new DataValidationInfo
                    {
                        Type = DataValidationType.List,
                        CellRange = $"{_cacheManager.GetExcelColumnName(columnIndex)}2:{_cacheManager.GetExcelColumnName(columnIndex)}{validationParameter.TotalRows}",
                        Formula1 = $"'{_dropdownDataSheetName}'!{sourceRange}",
                        AllowBlank = true,
                    });
                }
            }
        }
    }

    private void AddTypeBasedValidations(List<DataValidationInfo> validations, DataValidationParameter validationParameter)
    {
        for (var i = 0; i < validationParameter.PropertyAccessors.Length; i++)
        {
            var accessor = validationParameter.PropertyAccessors[i];

            if (validationParameter.ColumnMappings?.ContainsKey(accessor.ColumnName) == true)
            {
                continue;
            }

            var validation = CreateTypeValidation(accessor, i + 1, validationParameter.TotalRows);
            if (validation != null)
            {
                validations.Add(validation);
            }
        }
    }

    private DataValidationInfo? CreateTypeValidation(PropertyAccessor accessor, int columnIndex, int totalRows)
    {
        var cellRange = $"{_cacheManager.GetExcelColumnName(columnIndex)}2:{_cacheManager.GetExcelColumnName(columnIndex)}{totalRows}";

        if (IsIntegerType(accessor.UnderlyingType))
        {
            return new DataValidationInfo
            {
                Type = DataValidationType.Whole,
                CellRange = cellRange,
                ErrorTitle = "Invalid Integer",
                ErrorMessage = $"{accessor.ColumnName} must be a whole number",
                AllowBlank = accessor.IsNullable,
            };
        }

        if (IsDecimalType(accessor.UnderlyingType))
        {
            return new DataValidationInfo
            {
                Type = DataValidationType.Decimal,
                CellRange = cellRange,
                ErrorTitle = "Invalid Number",
                ErrorMessage = $"{accessor.ColumnName} must be a valid number",
                AllowBlank = accessor.IsNullable,
            };
        }

        if (accessor.UnderlyingType == typeof(DateTime))
        {
            return new DataValidationInfo
            {
                Type = DataValidationType.Date,
                CellRange = cellRange,
                Formula1 = $"DATE({_minExcelDate.Year},{_minExcelDate.Month},{_minExcelDate.Day})",
                Formula2 = $"DATE({_maxExcelDate.Year},{_maxExcelDate.Month},{_maxExcelDate.Day})",
                ErrorTitle = "Invalid Date",
                ErrorMessage = $"{accessor.ColumnName} must be a valid date between {_minExcelDate:yyyy-MM-dd} and {_maxExcelDate:yyyy-MM-dd}",
                AllowBlank = accessor.IsNullable,
            };
        }

        if (accessor.UnderlyingType == typeof(TimeSpan))
        {
            return new DataValidationInfo
            {
                Type = DataValidationType.Decimal,
                CellRange = cellRange,
                Formula1 = "0", 
                Formula2 = "999",
                ErrorTitle = "Invalid Time",
                ErrorMessage = $"{accessor.ColumnName} must be a valid time value",
                AllowBlank = accessor.IsNullable,
            };
        }

        return null;
    }

    private void WriteDataValidations(OpenXmlWriter writer, List<DataValidationInfo> validations)
    {
        var dataValidationsAttributes = new List<OpenXmlAttribute>
        {
            new("count", null!, validations.Count.ToString())
        };

        writer.WriteStartElement(new DataValidations(), dataValidationsAttributes);

        foreach (var validation in validations)
        {
            WriteDataValidation(writer, validation);
        }

        writer.WriteEndElement();
    }

    private void WriteDataValidation(OpenXmlWriter writer, DataValidationInfo validation)
    {
        var attributes = new List<OpenXmlAttribute>
        {
            new("type", null!, GetValidationTypeString(validation.Type)),
            new("sqref", null!, validation.CellRange),
            new("allowBlank", null!, validation.Type == DataValidationType.List || (validation.AllowBlank ?? false) ? "1" : "0"),
            new("showInputMessage", null!, "1"),
            new("showErrorMessage", null!, "1"),
        };

        if (!string.IsNullOrEmpty(validation.ErrorTitle))
        {
            attributes.Add(new OpenXmlAttribute("errorTitle", null!, validation.ErrorTitle));
        }

        if (!string.IsNullOrEmpty(validation.ErrorMessage))
        {
            attributes.Add(new OpenXmlAttribute("error", null!, validation.ErrorMessage));
        }

        if ((validation.Type == DataValidationType.Whole
             || validation.Type == DataValidationType.Decimal
             || validation.Type == DataValidationType.Date
             || validation.Type == DataValidationType.Time)
            && !string.IsNullOrEmpty(validation.Formula2))
        {
            attributes.Add(new OpenXmlAttribute("operator", null!, "between"));
        }

        writer.WriteStartElement(new DataValidation(), attributes);

        if (!string.IsNullOrEmpty(validation.Formula1))
        {
            writer.WriteStartElement(new Formula1());
            writer.WriteString(validation.Formula1);
            writer.WriteEndElement();
        }

        if (!string.IsNullOrEmpty(validation.Formula2))
        {
            writer.WriteStartElement(new Formula2());
            writer.WriteString(validation.Formula2);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private string GetValidationTypeString(DataValidationType type)
    {
        return type switch
        {
            DataValidationType.Whole => "whole",
            DataValidationType.Decimal => "decimal",
            DataValidationType.List => "list",
            DataValidationType.Date => "date",
            DataValidationType.Time => "time",
            DataValidationType.TextLength => "textLength",
            DataValidationType.Custom => "custom",
            _ => "none"
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIntegerType(Type type)
    {
        return type == typeof(int) || type == typeof(long) || type == typeof(short) ||
               type == typeof(byte) || type == typeof(sbyte) || type == typeof(ushort) ||
               type == typeof(uint) || type == typeof(ulong);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsDecimalType(Type type)
    {
        return type == typeof(decimal) || type == typeof(double) || type == typeof(float);
    }
}

public class StringBuilderPool
{
    private readonly ConcurrentBag<StringBuilder> _pool = new();
    private readonly int _maxPoolSize;
    private readonly int _maxCapacity;

    public StringBuilderPool(int maxPoolSize = 16, int maxCapacity = 1024)
    {
        _maxPoolSize = maxPoolSize;
        _maxCapacity = maxCapacity;
    }

    public StringBuilder Rent()
    {
        if (!_pool.TryTake(out var sb))
        {
            sb = new StringBuilder(64);
        }
        sb.Clear();
        return sb;
    }

    public void Return(StringBuilder sb)
    {
        if (sb.Capacity <= _maxCapacity && _pool.Count < _maxPoolSize)
        {
            sb.Clear();
            _pool.Add(sb);
        }
    }
}

public class WriteExcelService<TExcelModel> : IWriteExcelService<TExcelModel>
    where TExcelModel : new()
{
    private readonly IExcelCacheManager _cacheManager;
    private readonly IExcelCellWriter _cellWriter;
    private readonly IExcelStyleManager _styleManager;
    private readonly IExcelValidationManager _validationManager;
    private readonly StringBuilderPool _stringBuilderPool;
    private readonly string _dropdownDataSheetName = "Static Data";

    public WriteExcelService()
        : this(new ExcelCacheManager(), new ExcelCellWriter(), null, null, new StringBuilderPool())
    {
    }

    public WriteExcelService(
        IExcelCacheManager cacheManager,
        IExcelCellWriter cellWriter,
        IExcelStyleManager? styleManager,
        IExcelValidationManager? validationManager,
        StringBuilderPool stringBuilderPool)
    {
        _cacheManager = cacheManager;
        _cellWriter = cellWriter;
        _styleManager = styleManager ?? new ExcelStyleManager(cacheManager);
        _validationManager = validationManager ?? new ExcelValidationManager(cacheManager);
        _stringBuilderPool = stringBuilderPool;
    }

    public Stream Write(string filePath, string? sheetName, List<TExcelModel> data, List<ExcelDropdownData>? dropdownData, CancellationToken cancellationToken = default)
    {
        ValidateInputs(filePath, data);

        sheetName ??= "Adjustments";

        EnsureDirectoryExists(filePath);

        try
        {
            CreateExcelFile(filePath, data, sheetName, dropdownData, cancellationToken);
            return new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (OperationCanceledException)
        {
            CleanupFile(filePath);
            throw;
        }
        catch (Exception ex)
        {
            CleanupFile(filePath);
            throw new InvalidOperationException($"Failed to create Excel file: {ex.Message}", ex);
        }
    }

    private void ValidateInputs(string filePath, List<TExcelModel> data)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
        }

        if (data == null)
        {
            throw new ArgumentException("Data cannot be null", nameof(data));
        }
    }

    private void EnsureDirectoryExists(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);

        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private void CleanupFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    protected void CreateExcelFile(string filePath, IEnumerable<TExcelModel> data, string sheetName, List<ExcelDropdownData>? staticData, CancellationToken cancellationToken)
    {
        using var document = SpreadsheetDocument.Create(filePath, SpreadsheetDocumentType.Workbook);
        cancellationToken.ThrowIfCancellationRequested();

        var workbookPart = document.AddWorkbookPart();
        var propertyAccessors = _cacheManager.GetPropertyAccessors<TExcelModel>();
        ValidatePropertyAccessors(propertyAccessors);

        var stylesPart = _styleManager.CreateStyles(workbookPart, propertyAccessors, cancellationToken);
        var sheets = CreateWorksheets(workbookPart, sheetName, data, staticData, propertyAccessors, stylesPart, cancellationToken);
        WriteWorkbook(workbookPart, sheets);
    }

    private void ValidatePropertyAccessors(PropertyAccessor[] propertyAccessors)
    {
        if (propertyAccessors.Length == 0)
        {
            throw new InvalidOperationException($"Type {typeof(TExcelModel).Name} has no properties with ExcelColumnAttribute");
        }
    }

    private List<(string SheetName, string RelationshipId)> CreateWorksheets(
        WorkbookPart workbookPart,
        string sheetName,
        IEnumerable<TExcelModel> data,
        List<ExcelDropdownData>? staticData,
        PropertyAccessor[] propertyAccessors,
        Dictionary<string, uint> stylesPart,
        CancellationToken cancellationToken)
    {
        var sheets = new List<(string SheetName, string RelationshipId)>();

        var mainWorksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var mainRelationshipId = workbookPart.GetIdOfPart(mainWorksheetPart);
        sheets.Add((sheetName, mainRelationshipId));

        Dictionary<string, List<string>>? dropdownDataDict = null;
        Dictionary<string, string>? columnMappings = null;
        Dictionary<string, string>? columnPositions = null;

        if (staticData != null && staticData.Any())
        {
            var staticSheetResult = CreateStaticDataWorksheet(workbookPart, staticData, cancellationToken);
            sheets.Add(staticSheetResult.SheetInfo);
            dropdownDataDict = staticSheetResult.DropdownData;
            columnMappings = staticSheetResult.ColumnMappings;
            columnPositions = staticSheetResult.ColumnPositions;
        }

        WriteModelDataWorksheet(mainWorksheetPart, data, propertyAccessors, stylesPart, dropdownDataDict, columnMappings, columnPositions, sheetName, cancellationToken);

        return sheets;
    }

    private ((string SheetName, string RelationshipId) SheetInfo, Dictionary<string, List<string>> DropdownData,
        Dictionary<string, string> ColumnMappings, Dictionary<string, string> ColumnPositions)
        CreateStaticDataWorksheet(WorkbookPart workbookPart, List<ExcelDropdownData> staticData, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var staticWorksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var staticRelationshipId = workbookPart.GetIdOfPart(staticWorksheetPart);

        var result = WriteStaticDataWorksheet(staticWorksheetPart, staticData, cancellationToken);
        return ((_dropdownDataSheetName, staticRelationshipId), result.DropdownData, result.ColumnMappings, result.ColumnPositions);
    }

    protected void WriteModelDataWorksheet(
        WorksheetPart worksheetPart,
        IEnumerable<TExcelModel> data,
        PropertyAccessor[] propertyAccessors,
        Dictionary<string, uint> styleMappings,
        Dictionary<string, List<string>>? dropdownData,
        Dictionary<string, string>? columnMappings,
        Dictionary<string, string>? columnPositions,
        string sheetName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var columnIndexMap = CreateColumnIndexMap(propertyAccessors);

        using var writer = OpenXmlWriter.Create(worksheetPart);
        writer.WriteStartElement(new Worksheet());

        WriteColumnsElement(writer, propertyAccessors);
        writer.WriteStartElement(new SheetData());

        WriteHeaderRow(writer, propertyAccessors, styleMappings, 1);
        var rowCount = WriteDataRows(writer, data, propertyAccessors, styleMappings, 2, cancellationToken);

        writer.WriteEndElement();

        _validationManager.AddDataValidations(new DataValidationParameter
        {
            Writer = writer,
            PropertyAccessors = propertyAccessors,
            ColumnMappings = columnMappings,
            ColumnPositions = columnPositions,
            DropdownData = dropdownData,
            TotalRows = rowCount,
            ColumnIndexMap = columnIndexMap,
        });

        writer.WriteEndElement();
    }

    private Dictionary<string, int> CreateColumnIndexMap(PropertyAccessor[] propertyAccessors)
    {
        var columnIndexMap = new Dictionary<string, int>(propertyAccessors.Length);
        for (var i = 0; i < propertyAccessors.Length; i++)
        {
            columnIndexMap[propertyAccessors[i].ColumnName] = i + 1;
        }
        return columnIndexMap;
    }

    protected int WriteDataRows(
        OpenXmlWriter writer,
        IEnumerable<TExcelModel> data,
        PropertyAccessor[] propertyAccessors,
        Dictionary<string, uint> styleMappings,
        uint startRowIndex,
        CancellationToken cancellationToken)
    {
        var rowWriter = new DataRowWriter<TExcelModel>(_cacheManager, _cellWriter, _stringBuilderPool);

        return rowWriter.WriteRows(writer, data, propertyAccessors, styleMappings, startRowIndex, cancellationToken);
    }

    protected void WriteColumnsElement(OpenXmlWriter writer, PropertyAccessor[] propertyAccessors)
    {
        writer.WriteStartElement(new Columns());

        for (uint i = 0; i < propertyAccessors.Length; i++)
        {
            writer.WriteElement(new Column
            {
                Min = i + 1,
                Max = i + 1,
                Width = propertyAccessors[i].Width,
                CustomWidth = true,
            });
        }

        writer.WriteEndElement();
    }

    protected void WriteHeaderRow(OpenXmlWriter writer, PropertyAccessor[] propertyAccessors, Dictionary<string, uint> styleMappings, uint rowIndex)
    {
        var headerWriter = new HeaderRowWriter(_cacheManager, _stringBuilderPool);
        headerWriter.WriteHeader(writer, propertyAccessors, styleMappings, rowIndex);
    }

    protected void WriteWorkbook(WorkbookPart workbookPart, List<(string sheetName, string relationshipId)> sheets)
    {
        using var writer = OpenXmlWriter.Create(workbookPart);
        writer.WriteStartElement(new Workbook());
        writer.WriteStartElement(new Sheets());

        uint sheetId = 1;
        foreach (var (sheetName, relationshipId) in sheets)
        {
            writer.WriteElement(new Sheet
            {
                Name = sheetName,
                SheetId = sheetId++,
                Id = relationshipId,
            });
        }

        writer.WriteEndElement(); // Sheets
        writer.WriteEndElement(); // Workbook
    }

    protected (Dictionary<string, List<string>> DropdownData, Dictionary<string, string> ColumnMappings, Dictionary<string, string> ColumnPositions)
        WriteStaticDataWorksheet(WorksheetPart worksheetPart, List<ExcelDropdownData> dropdownDataList, CancellationToken cancellationToken)
    {
        var staticDataWriter = new StaticDataWriter(_cacheManager, _cellWriter, _stringBuilderPool);
        return staticDataWriter.WriteStaticData(worksheetPart, dropdownDataList, cancellationToken);
    }
}

internal class DataRowWriter<TExcelModel>
{
    private readonly IExcelCacheManager _cacheManager;
    private readonly IExcelCellWriter _cellWriter;
    private readonly StringBuilderPool _stringBuilderPool;

    public DataRowWriter(IExcelCacheManager cacheManager, IExcelCellWriter cellWriter, StringBuilderPool stringBuilderPool)
    {
        _cacheManager = cacheManager;
        _cellWriter = cellWriter;
        _stringBuilderPool = stringBuilderPool;
    }

    public int WriteRows(
        OpenXmlWriter writer,
        IEnumerable<TExcelModel> data,
        PropertyAccessor[] propertyAccessors,
        Dictionary<string, uint> styleMappings,
        uint startRowIndex,
        CancellationToken cancellationToken)
    {
        var rowCount = 0;
        var currentRowIndex = startRowIndex;
        var columnStyles = PrepareColumnStyles(propertyAccessors, styleMappings);
        var stringBuilder = _stringBuilderPool.Rent();

        try
        {
            foreach (var batch in data.Batch(ExcelWriterConstants.BatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                rowCount += WriteBatch(writer, batch, propertyAccessors, columnStyles, ref currentRowIndex, stringBuilder);
            }
        }
        finally
        {
            _stringBuilderPool.Return(stringBuilder);
        }

        return rowCount + 1;
    }

    private (uint?[] CellStyles, uint?[] DateStyles, uint?[] TimeStyles) PrepareColumnStyles(PropertyAccessor[] propertyAccessors, Dictionary<string, uint> styleMappings)
    {
        var columnStyles = new uint?[propertyAccessors.Length];
        var dateColumnStyles = new uint?[propertyAccessors.Length];
        var timeColumnStyles = new uint?[propertyAccessors.Length];

        for (var i = 0; i < propertyAccessors.Length; i++)
        {
            var accessor = propertyAccessors[i];
            if (!string.IsNullOrEmpty(accessor.Color))
            {
                var cleanHex = _cacheManager.GetCleanHexColor(accessor.Color);
                if (cleanHex != null)
                {
                    if (styleMappings.TryGetValue($"cell_{cleanHex}", out var cellStyle))
                    {
                        columnStyles[i] = cellStyle;
                    }

                    if (styleMappings.TryGetValue($"cell_date_{cleanHex}", out var dateStyle))
                    {
                        dateColumnStyles[i] = dateStyle;
                    }

                    if (styleMappings.TryGetValue($"cell_time_{cleanHex}", out var timeStyle))
                    {
                        timeColumnStyles[i] = timeStyle;
                    }
                }
            }
        }

        return (columnStyles, dateColumnStyles, timeColumnStyles);
    }

    private int WriteBatch(
        OpenXmlWriter writer,
        IEnumerable<TExcelModel> batch,
        PropertyAccessor[] propertyAccessors,
        (uint?[] CellStyles, uint?[] DateStyles, uint?[] TimeStyles) columnStyles,
        ref uint currentRowIndex,
        StringBuilder stringBuilder)
    {
        var count = 0;
        foreach (var item in batch)
        {
            if (item == null)
            {
                currentRowIndex++;
                count++;
                continue;
            }

            WriteDataRow(writer, item, propertyAccessors, columnStyles, currentRowIndex, stringBuilder);
            currentRowIndex++;
            count++;
        }
        return count;
    }

    private void WriteDataRow(
        OpenXmlWriter writer,
        TExcelModel item,
        PropertyAccessor[] propertyAccessors,
        (uint?[] CellStyles, uint?[] DateStyles, uint?[] TimeStyles) columnStyles,
        uint rowIndex,
        StringBuilder stringBuilder)
    {
        writer.WriteStartElement(new Row { RowIndex = rowIndex });

        for (var colIndex = 0; colIndex < propertyAccessors.Length; colIndex++)
        {
            var accessor = propertyAccessors[colIndex];
            var cellReference = GetCellReference(colIndex + 1, rowIndex, stringBuilder);

            var value = GetPropertyValue(accessor, item);
            var styleIndex = GetStyleIndexForValue(value, columnStyles, colIndex);

            if (value != null)
            {
                _cellWriter.WriteCellValue(writer, cellReference, value, accessor.UnderlyingType, styleIndex);
            }
            else if (styleIndex.HasValue)
            {
                writer.WriteStartElement(new Cell { CellReference = cellReference, StyleIndex = styleIndex.Value });
                writer.WriteEndElement();
            }
        }

        writer.WriteEndElement();
    }

    private uint? GetStyleIndexForValue(
        object? value,
        (uint?[] CellStyles, uint?[] DateStyles, uint?[] TimeStyles) columnStyles,
        int colIndex)
    {
        return value switch
        {
            DateTime => columnStyles.DateStyles[colIndex],
            TimeSpan => columnStyles.TimeStyles[colIndex],
            _ => columnStyles.CellStyles[colIndex]
        };
    }

    private object? GetPropertyValue(PropertyAccessor accessor, TExcelModel item)
    {
        try
        {
            return item == null ? null : accessor.Getter(item);
        }
        catch
        {
            // Log will be added
            return null;
        }
    }

    private string GetCellReference(int columnNumber, uint rowNumber, StringBuilder sb)
    {
        sb.Clear();
        sb.Append(_cacheManager.GetExcelColumnName(columnNumber));
        sb.Append(rowNumber);
        return sb.ToString();
    }
}

internal class HeaderRowWriter
{
    private readonly IExcelCacheManager _cacheManager;
    private readonly StringBuilderPool _stringBuilderPool;

    public HeaderRowWriter(IExcelCacheManager cacheManager, StringBuilderPool stringBuilderPool)
    {
        _cacheManager = cacheManager;
        _stringBuilderPool = stringBuilderPool;
    }

    public void WriteHeader(OpenXmlWriter writer, PropertyAccessor[] propertyAccessors, Dictionary<string, uint> styleMappings, uint rowIndex)
    {
        writer.WriteStartElement(new Row { RowIndex = rowIndex });

        var sb = _stringBuilderPool.Rent();
        try
        {
            for (var i = 0; i < propertyAccessors.Length; i++)
            {
                var accessor = propertyAccessors[i];
                var cellReference = GetCellReference(i + 1, rowIndex, sb);
                var styleIndex = GetHeaderStyleIndex(accessor, styleMappings);

                WriteHeaderCell(writer, cellReference, accessor.ColumnName, styleIndex);
            }
        }
        finally
        {
            _stringBuilderPool.Return(sb);
        }

        writer.WriteEndElement();
    }

    private uint GetHeaderStyleIndex(PropertyAccessor accessor, Dictionary<string, uint> styleMappings)
    {
        uint styleIndex = 1;

        if (!string.IsNullOrEmpty(accessor.Color))
        {
            var cleanHex = _cacheManager.GetCleanHexColor(accessor.Color);
            if (cleanHex != null && styleMappings.TryGetValue($"header_{cleanHex}", out var headerStyle))
            {
                styleIndex = headerStyle;
            }
        }

        return styleIndex;
    }

    private void WriteHeaderCell(OpenXmlWriter writer, string cellReference, string columnName, uint styleIndex)
    {
        var cell = new Cell
        {
            CellReference = cellReference,
            DataType = CellValues.InlineString,
            StyleIndex = styleIndex,
        };

        writer.WriteStartElement(cell);
        writer.WriteStartElement(new InlineString());
        writer.WriteElement(new Text(columnName));
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private string GetCellReference(int columnNumber, uint rowNumber, StringBuilder sb)
    {
        sb.Clear();
        sb.Append(_cacheManager.GetExcelColumnName(columnNumber));
        sb.Append(rowNumber);
        return sb.ToString();
    }
}

internal class StaticDataWriter
{
    private readonly IExcelCacheManager _cacheManager;
    private readonly IExcelCellWriter _cellWriter;
    private readonly StringBuilderPool _stringBuilderPool;

    public StaticDataWriter(IExcelCacheManager cacheManager, IExcelCellWriter cellWriter, StringBuilderPool stringBuilderPool)
    {
        _cacheManager = cacheManager;
        _cellWriter = cellWriter;
        _stringBuilderPool = stringBuilderPool;
    }

    public (Dictionary<string, List<string>> DropdownData, Dictionary<string, string> ColumnMappings, Dictionary<string, string> ColumnPositions)
        WriteStaticData(WorksheetPart worksheetPart, List<ExcelDropdownData> dropdownDataList, CancellationToken cancellationToken)
    {
        var dropdownData = new Dictionary<string, List<string>>();
        var columnMappings = new Dictionary<string, string>();
        var columnPositions = new Dictionary<string, string>();
        var columnDataCollectors = InitializeColumnDataCollectors(dropdownDataList, columnMappings);

        using (var writer = OpenXmlWriter.Create(worksheetPart))
        {
            writer.WriteStartElement(new Worksheet());
            WriteStaticColumnsElement(writer, dropdownDataList);
            writer.WriteStartElement(new SheetData());

            WriteStaticHeaderRow(writer, dropdownDataList, 1);
            var rowCount = WriteStaticDataRows(writer, dropdownDataList, 2, columnDataCollectors, columnPositions, cancellationToken);

            UpdateColumnPositions(columnPositions, rowCount);

            writer.WriteEndElement(); // SheetData
            writer.WriteEndElement(); // Worksheet
        }

        PopulateDropdownData(dropdownData, columnDataCollectors);

        return (dropdownData, columnMappings, columnPositions);
    }

    private Dictionary<string, HashSet<string>> InitializeColumnDataCollectors(List<ExcelDropdownData> dropdownDataList, Dictionary<string, string> columnMappings)
    {
        var columnDataCollectors = new Dictionary<string, HashSet<string>>();

        foreach (var column in dropdownDataList)
        {
            if (column.BindProperties.Any())
            {
                foreach (var bindTo in column.BindProperties)
                {
                    columnMappings[bindTo] = column.ColumnName;
                    if (!columnDataCollectors.ContainsKey(column.ColumnName))
                    {
                        columnDataCollectors[column.ColumnName] = new HashSet<string>();
                    }
                }
            }
        }

        return columnDataCollectors;
    }

    private void WriteStaticColumnsElement(OpenXmlWriter writer, List<ExcelDropdownData> dropdownDataList)
    {
        writer.WriteStartElement(new Columns());

        var currentColumn = 1;
        for (var i = 0; i < dropdownDataList.Count; i++)
        {
            writer.WriteElement(new Column
            {
                Min = (uint)currentColumn,
                Max = (uint)currentColumn,
                Width = ExcelWriterConstants.DefaultColumnWidth,
                CustomWidth = true,
            });

            currentColumn++;

            if (i < dropdownDataList.Count - 1)
            {
                writer.WriteElement(new Column
                {
                    Min = (uint)currentColumn,
                    Max = (uint)currentColumn,
                    Width = 5,
                    CustomWidth = true,
                });
                currentColumn++;
            }
        }

        writer.WriteEndElement();
    }

    private void WriteStaticHeaderRow(OpenXmlWriter writer, List<ExcelDropdownData> dropdownDataList, uint rowIndex)
    {
        writer.WriteStartElement(new Row { RowIndex = rowIndex });
        var sb = _stringBuilderPool.Rent();

        try
        {
            for (var colIndex = 0; colIndex < dropdownDataList.Count; colIndex++)
            {
                var column = dropdownDataList[colIndex];
                var actualColumnIndex = GetActualColumnIndex(colIndex);
                var cellReference = GetCellReference(actualColumnIndex, rowIndex, sb);

                var cell = new Cell
                {
                    CellReference = cellReference,
                    DataType = CellValues.InlineString,
                    StyleIndex = 1,
                };

                writer.WriteStartElement(cell);
                writer.WriteStartElement(new InlineString());
                writer.WriteElement(new Text(column.ColumnName));
                writer.WriteEndElement();
                writer.WriteEndElement();
            }
        }
        finally
        {
            _stringBuilderPool.Return(sb);
        }

        writer.WriteEndElement();
    }

    private int WriteStaticDataRows(
        OpenXmlWriter writer,
        List<ExcelDropdownData> dropdownDataList,
        uint startRowIndex,
        Dictionary<string, HashSet<string>> columnDataCollectors,
        Dictionary<string, string> columnPositions,
        CancellationToken cancellationToken)
    {
        var maxRows = dropdownDataList.Max(col => col.DataList.Count);

        if (maxRows == 0)
        {
            return (int)startRowIndex;
        }

        var columnDataArrays = dropdownDataList
            .Select(column => column.DataList.ToArray())
            .ToArray();

        var sb = _stringBuilderPool.Rent();

        try
        {
            for (var rowOffset = 0; rowOffset < maxRows; rowOffset++)
            {
                if (rowOffset % 100 == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                WriteStaticDataRow(writer, dropdownDataList, columnDataArrays, startRowIndex + (uint)rowOffset,
                    rowOffset, columnDataCollectors, columnPositions, sb);
            }

            return maxRows > 0 ? (int)(startRowIndex + maxRows - 1) : (int)startRowIndex;
        }
        finally
        {
            _stringBuilderPool.Return(sb);
        }
    }

    private void WriteStaticDataRow(
        OpenXmlWriter writer,
        List<ExcelDropdownData> dropdownDataList,
        object[][] columnDataArrays,
        uint currentRowIndex,
        int rowOffset,
        Dictionary<string, HashSet<string>> columnDataCollectors,
        Dictionary<string, string> columnPositions,
        StringBuilder stringBuilder)
    {
        writer.WriteStartElement(new Row { RowIndex = currentRowIndex });

        for (var colIndex = 0; colIndex < dropdownDataList.Count; colIndex++)
        {
            var column = dropdownDataList[colIndex];
            var columnData = columnDataArrays[colIndex];

            var actualColumnIndex = GetActualColumnIndex(colIndex);
            var cellReference = GetCellReference(actualColumnIndex, currentRowIndex, stringBuilder);

            if (rowOffset < columnData.Length)
            {
                var value = columnData[rowOffset];
                _cellWriter.WriteCellValue(writer, cellReference, value, value.GetType(), null);

                if (columnDataCollectors.TryGetValue(column.ColumnName, out var collector))
                {
                    collector.Add(value.ToString() ?? string.Empty);
                }
            }

            if (rowOffset == 0 && columnDataCollectors.ContainsKey(column.ColumnName))
            {
                columnPositions[column.ColumnName] = _cacheManager.GetExcelColumnName(actualColumnIndex);
            }
        }

        writer.WriteEndElement();
    }

    private void UpdateColumnPositions(Dictionary<string, string> columnPositions, int rowCount)
    {
        foreach (var kvp in columnPositions.ToList())
        {
            columnPositions[kvp.Key] = $"${kvp.Value}$2:${kvp.Value}${rowCount}";
        }
    }

    private void PopulateDropdownData(Dictionary<string, List<string>> dropdownData, Dictionary<string, HashSet<string>> columnDataCollectors)
    {
        foreach (var collector in columnDataCollectors)
        {
            dropdownData[collector.Key] = collector.Value.OrderBy(v => v).ToList();
        }
    }

    private int GetActualColumnIndex(int columnIndex)
    {
        // Each column takes 1 position, plus separator after each column
        const int columnSeparator = 1;
        return columnIndex * (1 + columnSeparator) + 1;
    }

    private string GetCellReference(int columnNumber, uint rowNumber, StringBuilder sb)
    {
        sb.Clear();
        sb.Append(_cacheManager.GetExcelColumnName(columnNumber));
        sb.Append(rowNumber);
        return sb.ToString();
    }
}

public class DataValidationParameter
{
    public OpenXmlWriter Writer { get; set; } = null!;

    public PropertyAccessor[] PropertyAccessors { get; set; } = null!;

    public Dictionary<string, string>? ColumnMappings { get; set; }

    public Dictionary<string, string>? ColumnPositions { get; set; }

    public Dictionary<string, List<string>>? DropdownData { get; set; }

    public Dictionary<string, int> ColumnIndexMap { get; set; } = null!;

    public int TotalRows { get; set; }
}

public static class ExcelWriterConstants
{
    public const int DefaultColumnWidth = 15;

    public const int BatchSize = 10000;
}

public class PropertyAccessor
{
    public PropertyInfo Property { get; set; } = null!;

    public string ColumnName { get; set; } = null!;

    public int Order { get; set; }

    public string? Color { get; set; }

    public double Width { get; set; }

    public Func<object, object> Getter { get; set; } = null!;

    public Type PropertyType { get; set; } = null!;

    public Type UnderlyingType { get; set; } = null!;

    public bool IsNullable { get; set; }
}

public class DataValidationInfo
{
    public DataValidationType Type { get; init; }

    public string CellRange { get; init; } = null!;

    public string Formula1 { get; init; } = null!;

    public string Formula2 { get; init; } = null!;

    public string ErrorTitle { get; init; } = null!;

    public string ErrorMessage { get; init; } = null!;

    public bool? AllowBlank { get; init; }
}

public enum DataValidationType
{
    Whole,
    Decimal,
    List,
    Date,
    Time,
    TextLength,
    Custom
}

public static class EnumerableExtensions
{
    public static IEnumerable<IEnumerable<T>> Batch<T>(this IEnumerable<T> source, int batchSize)
    {
        using var enumerator = source.GetEnumerator();
        while (enumerator.MoveNext())
        {
            yield return YieldBatchElements(enumerator, batchSize - 1);
        }
    }

    private static IEnumerable<T> YieldBatchElements<T>(IEnumerator<T> enumerator, int batchSize)
    {
        yield return enumerator.Current;
        for (var i = 0; i < batchSize && enumerator.MoveNext(); i++)
        {
            yield return enumerator.Current;
        }
    }
}

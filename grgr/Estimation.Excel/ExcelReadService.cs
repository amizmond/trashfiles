using Estimation.Excel.Models;

namespace Estimation.Excel;

public interface IExcelReadService<out TExcelModel>
    where TExcelModel : new()
{
    IEnumerable<TExcelModel> ReadSheet(Stream fileStream, string? sheetName = null, CancellationToken cancellationToken = default);
    List<ExcelColumnInfo> GetColumnInfo();
    List<string> ReadHeaders(Stream fileStream, string? sheetName = null);
    IEnumerable<TExcelModel> ReadSheetWithMapping(Stream fileStream, Dictionary<string, string> propertyToColumnMapping, string? sheetName = null, CancellationToken cancellationToken = default);
}

public class ExcelReadService<TExcelModel> : IExcelReadService<TExcelModel>
    where TExcelModel : new()
{
    private readonly Dictionary<PropertyInfo, ExcelColumnAttribute> _propertyMappings;
    private readonly Dictionary<PropertyInfo, string> _propertyColumnNames;

    public ExcelReadService() : this(new PropertyNameFormatter())
    {
    }

    public ExcelReadService(IPropertyNameFormatter propertyNameFormatter)
    {
        var propertyNameFormatter1 = propertyNameFormatter ?? throw new ArgumentNullException(nameof(propertyNameFormatter));

        var properties = typeof(TExcelModel)
            .GetProperties()
            .Where(p => p.GetCustomAttribute<ExcelColumnAttribute>() != null)
            .ToArray();

        _propertyMappings = properties.ToDictionary(
            p => p,
            p => p.GetCustomAttribute<ExcelColumnAttribute>()!);

        _propertyColumnNames = new Dictionary<PropertyInfo, string>();
        foreach (var property in properties)
        {
            var attribute = _propertyMappings[property];
            var columnName = !string.IsNullOrEmpty(attribute.Name)
                ? attribute.Name
                : propertyNameFormatter1.ConvertToFriendlyName(property.Name);
            _propertyColumnNames[property] = columnName;
        }
    }

    public IEnumerable<TExcelModel> ReadSheet(Stream fileStream, string? sheetName = null, CancellationToken cancellationToken = default)
    {
        return ReadSheetInternal(fileStream, _propertyColumnNames, sheetName, cancellationToken);
    }

    public List<ExcelColumnInfo> GetColumnInfo()
    {
        return _propertyMappings.Select(pm => new ExcelColumnInfo(
            pm.Key.Name,
            _propertyColumnNames[pm.Key],
            pm.Key.PropertyType,
            !pm.Value.AllowMissing
        )).ToList();
    }

    public List<string> ReadHeaders(Stream fileStream, string? sheetName = null)
    {
        using var document = SpreadsheetDocument.Open(fileStream, false);
        var workbookPart = document.WorkbookPart!;
        var worksheetPart = GetWorksheetPart(workbookPart, sheetName);
        var sharedStrings = GetSharedStrings(workbookPart, CancellationToken.None);

        using var reader = OpenXmlReader.Create(worksheetPart);
        while (reader.Read())
        {
            if (reader.ElementType == typeof(Row))
            {
                var row = (Row)reader.LoadCurrentElement()!;
                var headers = ReadHeaderRow(row, sharedStrings, CancellationToken.None);
                return headers.OrderBy(h => h.Value).Select(h => h.Key).ToList();
            }
        }

        return new List<string>();
    }

    public IEnumerable<TExcelModel> ReadSheetWithMapping(Stream fileStream, Dictionary<string, string> propertyToColumnMapping, string? sheetName = null, CancellationToken cancellationToken = default)
    {
        var customColumnNames = new Dictionary<PropertyInfo, string>(_propertyColumnNames.Count);
        foreach (var kvp in _propertyColumnNames)
        {
            customColumnNames[kvp.Key] = propertyToColumnMapping.TryGetValue(kvp.Key.Name, out var customName)
                ? customName
                : kvp.Value;
        }

        return ReadSheetInternal(fileStream, customColumnNames, sheetName, cancellationToken);
    }

    private IEnumerable<TExcelModel> ReadSheetInternal(Stream fileStream, Dictionary<PropertyInfo, string> propertyColumnNames, string? sheetName, CancellationToken cancellationToken)
    {
        using var document = SpreadsheetDocument.Open(fileStream, false);
        var workbookPart = document.WorkbookPart!;
        var worksheetPart = GetWorksheetPart(workbookPart, sheetName);

        var sharedStrings = GetSharedStrings(workbookPart, cancellationToken);

        var batchSize = 1000;
        var rowBatch = new List<Row>();
        var columnMapping = new Dictionary<string, int>();
        var isHeaderRead = false;

        using var reader = OpenXmlReader.Create(worksheetPart);

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.ElementType == typeof(Row))
            {
                var row = (Row)reader.LoadCurrentElement()!;

                if (!isHeaderRead)
                {
                    columnMapping = ReadHeaderRow(row, sharedStrings, cancellationToken);
                    ValidateRequiredColumns(columnMapping, propertyColumnNames);
                    isHeaderRead = true;
                    continue;
                }

                rowBatch.Add(row);

                if (rowBatch.Count >= batchSize)
                {
                    foreach (var model in ProcessRowBatch(rowBatch, columnMapping, sharedStrings, propertyColumnNames, cancellationToken))
                    {
                        yield return model;
                    }

                    rowBatch.Clear();
                }
            }
        }

        if (rowBatch.Count > 0)
        {
            foreach (var model in ProcessRowBatch(rowBatch, columnMapping, sharedStrings, propertyColumnNames, cancellationToken))
            {
                yield return model;
            }
        }
    }

    protected WorksheetPart GetWorksheetPart(WorkbookPart workbookPart, string? sheetName)
    {
        var sheets = workbookPart.Workbook.GetFirstChild<Sheets>()?.Elements<Sheet>().ToList();

        if (sheets == null || !sheets.Any())
        {
            throw new InvalidOperationException("No sheets found in the excel file.");
        }

        Sheet? sheet;

        if (string.IsNullOrEmpty(sheetName))
        {
            sheet = sheets.First();
        }
        else
        {
            sheet = sheets.FirstOrDefault(s => s.Name?.Value?.Equals(sheetName, StringComparison.OrdinalIgnoreCase) ?? false);
        }

        if (string.IsNullOrEmpty(sheet?.Id?.Value))
        {
            throw new InvalidOperationException($"Sheet {sheetName} not found in the Excel file.");
        }

        return (WorksheetPart)workbookPart.GetPartById(sheet.Id.Value);
    }

    protected Dictionary<string, string> GetSharedStrings(WorkbookPart workbookPart, CancellationToken cancellationToken)
    {
        var sharedStrings = new Dictionary<string, string>();
        var sharedStringTablePart = workbookPart.SharedStringTablePart;

        if (sharedStringTablePart == null)
        {
            return sharedStrings;
        }

        using var reader = OpenXmlReader.Create(sharedStringTablePart);
        var index = 0;

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.ElementType == typeof(SharedStringItem))
            {
                var sharedStringItem = (SharedStringItem)reader.LoadCurrentElement()!;

                sharedStrings[index.ToString()] = sharedStringItem.Text?.Text ?? string.Empty;
                index++;
            }
        }

        return sharedStrings;
    }

    protected Dictionary<string, int> ReadHeaderRow(Row headerRow, Dictionary<string, string> sharedStrings, CancellationToken cancellationToken)
    {
        var columnMapping = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var cell in headerRow.Elements<Cell>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var columnIndex = GetColumnIndex(cell.CellReference!);
            var cellValue = GetCellValue(cell, sharedStrings, false);

            if (!string.IsNullOrWhiteSpace(cellValue))
            {
                columnMapping[cellValue.Trim()] = columnIndex;
            }
        }

        return columnMapping;
    }

    protected void ValidateRequiredColumns(Dictionary<string, int> columnMapping, Dictionary<PropertyInfo, string> propertyColumnNames)
    {
        var missingColumns = new List<string>();

        foreach (var propertyMapping in _propertyMappings)
        {
            var property = propertyMapping.Key;
            var attribute = propertyMapping.Value;
            var columnName = propertyColumnNames[property];

            if (!attribute.AllowMissing && !columnMapping.ContainsKey(columnName))
            {
                missingColumns.Add(columnName);
            }
        }

        if (missingColumns.Any())
        {
            throw new InvalidOperationException($"Required columns are missing in the Excel file: {string.Join(", ", missingColumns)}");
        }
    }

    protected IEnumerable<TExcelModel> ProcessRowBatch(List<Row> rows, Dictionary<string, int> columnMapping, Dictionary<string, string> sharedStrings, Dictionary<PropertyInfo, string> propertyColumnNames, CancellationToken cancellationToken)
    {
        var results = new ConcurrentBag<(int Index, TExcelModel Model)>();

        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Environment.ProcessorCount,
        };

        Parallel.ForEach(rows.Select((row, index) => new { Row = row, Index = index }), parallelOptions, item =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var (isValid, model) = ProcessRow(item.Row, columnMapping, sharedStrings, propertyColumnNames);
                if (isValid)
                {
                    results.Add((item.Index, model));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error during batch processing Row -> {item.Row.RowIndex} Error -> {ex.Message}", ex);
            }
        });

        return results.OrderBy(r => r.Index).Select(r => r.Model);
    }

    private (bool IsValid, TExcelModel Model) ProcessRow(Row row, Dictionary<string, int> columnMapping, Dictionary<string, string> sharedStrings, Dictionary<PropertyInfo, string> propertyColumnNames)
    {
        var model = new TExcelModel();
        var cells = row.Elements<Cell>().ToList();

        if (!cells.Any() || cells.All(c => string.IsNullOrWhiteSpace(GetCellValue(c, sharedStrings, false))))
        {
            return (false, model);
        }

        var hasAnyValue = false;

        foreach (var propertyMapping in _propertyMappings)
        {
            var property = propertyMapping.Key;
            var columnName = propertyColumnNames[property];

            if (columnMapping.TryGetValue(columnName, out var columnIndex))
            {
                var cell = cells.FirstOrDefault(c => GetColumnIndex(c.CellReference!) == columnIndex);

                if (cell != null)
                {
                    var shouldCleanNumeric = property.PropertyType == typeof(string);
                    var cellValue = GetCellValue(cell, sharedStrings, shouldCleanNumeric);

                    if (!string.IsNullOrWhiteSpace(cellValue))
                    {
                        hasAnyValue = true;
                    }

                    SetPropertyValue(model, property, cellValue);
                }
                else if (property.PropertyType.IsValueType && Nullable.GetUnderlyingType(property.PropertyType) == null)
                {
                    SetPropertyValue(model, property, null);
                }
            }
        }

        return (hasAnyValue, model);
    }

    protected string GetCellValue(Cell cell, Dictionary<string, string> sharedStrings, bool cleanNumericValues = false)
    {
        if (cell.CellValue == null && cell.InlineString == null)
        {
            return string.Empty;
        }

        if (cell.DataType != null)
        {
            if (cell.DataType.Value == CellValues.SharedString)
            {
                if (cell.CellValue != null && sharedStrings.TryGetValue(cell.CellValue.Text, out var sharedString))
                {
                    return sharedString.Trim();
                }
            }
            else if (cell.DataType.Value == CellValues.InlineString)
            {
                if (cell.InlineString != null)
                {
                    if (cell.InlineString.Text != null)
                    {
                        return cell.InlineString.Text.Text.Trim() ?? string.Empty;
                    }

                    var runs = cell.InlineString.Elements<Run>().ToList();
                    if (runs.Any())
                    {
                        var textBuilder = new System.Text.StringBuilder();
                        foreach (var run in runs)
                        {
                            if (run.Text != null)
                            {
                                textBuilder.Append(run.Text.Text);
                            }
                        }
                        return textBuilder.ToString().Trim();
                    }
                }
            }
            else if (cell.DataType.Value == CellValues.String && cell.CellValue != null)
            {
                return cell.CellValue.Text.Trim();
            }
        }

        if (cell.CellValue != null)
        {
            var cellText = cell.CellValue.Text.Trim();

            if (cleanNumericValues && double.TryParse(cellText, NumberStyles.Any, CultureInfo.InvariantCulture, out var numericValue))
            {
                return FormatNumericValueForString(numericValue, cellText);
            }

            return cellText;
        }

        return string.Empty;
    }

    protected string FormatNumericValueForString(double numericValue, string originalText)
    {
        if (double.IsNaN(numericValue) || double.IsInfinity(numericValue))
        {
            return originalText;
        }

        var isInteger = Math.Abs(numericValue - Math.Round(numericValue)) < double.Epsilon;

        if (isInteger)
        {
            return numericValue.ToString("0", CultureInfo.InvariantCulture);
        }
        else
        {
            return FormatDoubleNumber(numericValue, originalText);
        }
    }

    protected string FormatDoubleNumber(double numericValue, string originalText)
    {
        var formatted = numericValue.ToString("G15", CultureInfo.InvariantCulture);

        if (formatted.Contains('.'))
        {
            formatted = formatted.TrimEnd('0').TrimEnd('.');
        }

        return formatted;
    }

    protected int GetColumnIndex(string cellReference)
    {
        var columnIndex = 0;
        var multiplier = 1;
        var englishAlphabet = 26;

        for (var i = cellReference.Length - 1; i >= 0; i--)
        {
            var ch = cellReference[i];
            if (!char.IsLetter(ch))
            {
                continue;
            }

            columnIndex += (ch - 'A' + 1) * multiplier;
            multiplier *= englishAlphabet;
        }

        return columnIndex - 1;
    }

    protected void SetPropertyValue(TExcelModel model, PropertyInfo property, string? value)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (property.PropertyType.IsValueType && Nullable.GetUnderlyingType(property.PropertyType) == null)
                {
                    property.SetValue(model, Activator.CreateInstance(property.PropertyType));
                }
                else
                {
                    property.SetValue(model, null);
                }

                return;
            }

            var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

            var convertedValue = targetType switch
            {
                _ when targetType == typeof(string) => value,
                _ when targetType == typeof(int) => int.Parse(value, CultureInfo.InvariantCulture),
                _ when targetType == typeof(long) => ParseLong(value),
                _ when targetType == typeof(decimal) => decimal.Parse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture),
                _ when targetType == typeof(double) => double.Parse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture),
                _ when targetType == typeof(float) => float.Parse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture),
                _ when targetType == typeof(bool) => ParseBoolean(value),
                _ when targetType == typeof(DateTime) => ParseDateTime(value),
                _ when targetType == typeof(TimeSpan) => ParseTimeSpan(value),
                _ when targetType == typeof(Guid) => Guid.Parse(value),
                _ => Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture),
            };

            property.SetValue(model, convertedValue);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to set property '{property.Name}' with value '{value}': {ex.Message}", ex);
        }
    }

    protected long ParseLong(string value)
    {
        if (long.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var decimalValue))
        {
            return (long)decimalValue;
        }

        throw new FormatException($"Unable to parse '{value}' as Long");
    }

    protected bool ParseBoolean(string value)
    {
        value = value.Trim().ToLowerInvariant();

        return value.ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "y" or "t" => true,
            "0" or "false" or "no" or "n" or "f" => false,
            _ => bool.Parse(value),
        };
    }

    protected DateTime ParseDateTime(string value)
    {
        if (double.TryParse(value, out var serialDate))
        {
            return DateTime.FromOADate(serialDate);
        }

        return DateTime.Parse(value, CultureInfo.InvariantCulture);
    }

    protected TimeSpan ParseTimeSpan(string value)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var timeAsFraction)
            && timeAsFraction >= 0 && timeAsFraction <= 1)
        {
            var time = TimeSpan.FromDays(timeAsFraction);
            return TimeSpan.FromSeconds(Math.Round(time.TotalSeconds));
        }

        if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var timeSpan))
        {
            return timeSpan;
        }

        throw new FormatException($"Unable to parse '{value}' as TimeSpan");
    }
}
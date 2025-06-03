using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace ExcelTest;

public class ExcelReadService_3<TExcelModel> : IReadExcelService<TExcelModel> where TExcelModel : new()
{
    private readonly Dictionary<string, PropertySetter> _propertySetters;
    private readonly Dictionary<string, Type> _propertyTypes;
    private readonly HashSet<string> _requiredColumns;
    private readonly int _degreeOfParallelism;

    private class PropertySetter
    {
        public Action<TExcelModel, object?> Setter { get; set; }
        public PropertyInfo PropertyInfo { get; set; }
    }

    public ExcelReadService_3()
    {
        _degreeOfParallelism = Environment.ProcessorCount;
        (_propertySetters, _propertyTypes, _requiredColumns) = BuildPropertyMappings();
    }

    public IEnumerable<TExcelModel> ReadSheetStreaming(Stream fileStream, string? sheetName = null)
    {
        using var document = SpreadsheetDocument.Open(fileStream, false);
        var workbookPart = document.WorkbookPart;
        if (workbookPart == null)
            throw new InvalidOperationException("Workbook part not found");

        var worksheetPart = GetWorksheetPart(workbookPart, sheetName);
        var sharedStringTable = workbookPart.SharedStringTablePart?.SharedStringTable;

        // Set up parallel processing pipeline
        var parseOptions = new ExecutionDataflowBlockOptions
        {
            MaxDegreeOfParallelism = _degreeOfParallelism,
            BoundedCapacity = _degreeOfParallelism * 100
        };

        var outputOptions = new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = 1000
        };

        // Create pipeline blocks
        var rowBuffer = new BufferBlock<RowData>(new DataflowBlockOptions { BoundedCapacity = 1000 });
        var parseBlock = new TransformBlock<RowData, TExcelModel?>(
            rowData => ParseRow(rowData, sharedStringTable),
            parseOptions);
        var outputBuffer = new BufferBlock<TExcelModel?>(outputOptions);

        // Link pipeline
        rowBuffer.LinkTo(parseBlock, new DataflowLinkOptions { PropagateCompletion = true });
        parseBlock.LinkTo(outputBuffer, new DataflowLinkOptions { PropagateCompletion = true },
            model => model != null);
        parseBlock.LinkTo(DataflowBlock.NullTarget<TExcelModel?>());

        // Start streaming rows
        var streamingTask = Task.Run(async () =>
        {
            try
            {
                await StreamRowsAsync(worksheetPart, sharedStringTable, rowBuffer);
            }
            finally
            {
                rowBuffer.Complete();
            }
        });

        // Yield results as they become available
        while (!outputBuffer.Completion.IsCompleted)
        {
            if (outputBuffer.TryReceive(out var model) && model != null)
            {
                yield return model;
            }
            else if (!outputBuffer.Completion.IsCompleted)
            {
                Task.Delay(1).Wait();
            }
        }
    }

    private async Task StreamRowsAsync(WorksheetPart worksheetPart, SharedStringTable? sharedStringTable,
        ITargetBlock<RowData> targetBlock)
    {
        Dictionary<int, string>? columnMapping = null;
        var rowIndex = 0;

        using var reader = OpenXmlReader.Create(worksheetPart);
        while (reader.Read())
        {
            if (reader.ElementType == typeof(Row))
            {
                if (reader.IsStartElement)
                {
                    var row = (Row)reader.LoadCurrentElement();

                    if (rowIndex == 0)
                    {
                        // First row - build column mapping
                        columnMapping = BuildColumnMapping(row, sharedStringTable);
                        ValidateRequiredColumns(columnMapping);
                    }
                    else if (columnMapping != null)
                    {
                        // Data rows
                        var rowData = new RowData
                        {
                            Cells = row.Elements<Cell>().ToList(),
                            ColumnMapping = columnMapping,
                            RowIndex = rowIndex
                        };

                        await targetBlock.SendAsync(rowData);
                    }

                    rowIndex++;
                }
            }
        }
    }

    private TExcelModel? ParseRow(RowData rowData, SharedStringTable? sharedStringTable)
    {
        try
        {
            var model = new TExcelModel();
            var cellsByColumn = rowData.Cells.ToDictionary(c => GetColumnIndex(c.CellReference), c => c);

            foreach (var kvp in rowData.ColumnMapping)
            {
                var columnIndex = kvp.Key;
                var columnName = kvp.Value;

                if (_propertySetters.TryGetValue(columnName, out var propertySetter))
                {
                    if (cellsByColumn.TryGetValue(columnIndex, out var cell))
                    {
                        var value = GetCellValue(cell, sharedStringTable);
                        if (!string.IsNullOrEmpty(value))
                        {
                            var convertedValue = ConvertValue(value, _propertyTypes[columnName]);
                            propertySetter.Setter(model, convertedValue);
                        }
                    }
                }
            }

            return model;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error parsing row {rowData.RowIndex}: {ex.Message}", ex);
        }
    }

    private object? ConvertValue(string value, Type targetType)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null
                ? Activator.CreateInstance(targetType)
                : null;
        }

        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (underlyingType == typeof(string))
            return value;

        if (underlyingType == typeof(int))
            return int.Parse(value);

        if (underlyingType == typeof(long))
            return long.Parse(value);

        if (underlyingType == typeof(decimal))
            return decimal.Parse(value, CultureInfo.InvariantCulture);

        if (underlyingType == typeof(double))
            return double.Parse(value, CultureInfo.InvariantCulture);

        if (underlyingType == typeof(float))
            return float.Parse(value, CultureInfo.InvariantCulture);

        if (underlyingType == typeof(bool))
        {
            if (bool.TryParse(value, out var boolResult))
                return boolResult;

            // Handle Excel boolean representations
            value = value.ToLowerInvariant();
            return value == "1" || value == "true" || value == "yes";
        }

        if (underlyingType == typeof(DateTime))
        {
            // Try parsing as date
            if (DateTime.TryParse(value, out var dateResult))
                return dateResult;

            // Try parsing as OLE Automation date (Excel serial date)
            if (double.TryParse(value, out var oleDate))
                return DateTime.FromOADate(oleDate);
        }

        if (underlyingType == typeof(Guid))
            return Guid.Parse(value);

        // Fallback to Convert.ChangeType
        return Convert.ChangeType(value, underlyingType, CultureInfo.InvariantCulture);
    }

    private static bool ParseBool(string value)
    {
        if (bool.TryParse(value, out var result))
            return result;

        var lower = value.ToLowerInvariant();
        return lower == "1" || lower == "true" || lower == "yes" || lower == "y";
    }

    private static string GetCellValue(Cell cell, SharedStringTable? sharedStringTable)
    {
        if (cell.CellValue == null)
            return string.Empty;

        var value = cell.CellValue.Text;

        if (cell.DataType != null && cell.DataType.Value == CellValues.SharedString)
        {
            if (sharedStringTable != null && int.TryParse(value, out var index))
            {
                var sharedStringItem = sharedStringTable.Elements<SharedStringItem>()
                    .ElementAtOrDefault(index);
                if (sharedStringItem != null)
                {
                    return sharedStringItem.Text?.Text ?? sharedStringItem.InnerText ?? string.Empty;
                }
            }
        }

        return value;
    }

    private Dictionary<int, string> BuildColumnMapping(Row headerRow, SharedStringTable? sharedStringTable)
    {
        var mapping = new Dictionary<int, string>();

        foreach (var cell in headerRow.Elements<Cell>())
        {
            var columnIndex = GetColumnIndex(cell.CellReference);
            var columnName = GetCellValue(cell, sharedStringTable);

            if (!string.IsNullOrWhiteSpace(columnName))
            {
                mapping[columnIndex] = columnName.Trim();
            }
        }

        return mapping;
    }

    private void ValidateRequiredColumns(Dictionary<int, string> columnMapping)
    {
        var foundColumns = new HashSet<string>(columnMapping.Values, StringComparer.OrdinalIgnoreCase);
        var missingColumns = _requiredColumns.Where(c => !foundColumns.Contains(c)).ToList();

        if (missingColumns.Any())
        {
            throw new InvalidOperationException(
                $"Missing required columns: {string.Join(", ", missingColumns)}");
        }
    }

    private static int GetColumnIndex(string? cellReference)
    {
        if (string.IsNullOrEmpty(cellReference))
            return 0;

        var columnName = string.Empty;
        foreach (var ch in cellReference)
        {
            if (char.IsLetter(ch))
                columnName += ch;
            else
                break;
        }

        var index = 0;
        for (var i = 0; i < columnName.Length; i++)
        {
            index = index * 26 + (columnName[i] - 'A' + 1);
        }

        return index - 1;
    }

    private static WorksheetPart GetWorksheetPart(WorkbookPart workbookPart, string? sheetName)
    {
        var sheets = workbookPart.Workbook.Descendants<Sheet>();

        Sheet? sheet;
        if (string.IsNullOrEmpty(sheetName))
        {
            sheet = sheets.FirstOrDefault();
        }
        else
        {
            sheet = sheets.FirstOrDefault(s =>
                string.Equals(s.Name, sheetName, StringComparison.OrdinalIgnoreCase));
        }

        if (sheet == null)
            throw new InvalidOperationException(
                $"Sheet '{sheetName ?? "default"}' not found in workbook");

        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!);
        return worksheetPart;
    }

    private static (Dictionary<string, PropertySetter>, Dictionary<string, Type>, HashSet<string>) BuildPropertyMappings()
    {
        var setters = new Dictionary<string, PropertySetter>(StringComparer.OrdinalIgnoreCase);
        var types = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var properties = typeof(TExcelModel).GetProperties();

        foreach (var property in properties)
        {
            var attribute = property.GetCustomAttribute<ExcelColumnAttribute>();
            if (attribute != null)
            {
                var columnName = attribute.Name;

                // Create compiled setter for performance
                var parameter = Expression.Parameter(typeof(TExcelModel), "model");
                var valueParam = Expression.Parameter(typeof(object), "value");
                var propertyAccess = Expression.Property(parameter, property);
                var convertedValue = Expression.Convert(valueParam, property.PropertyType);
                var assign = Expression.Assign(propertyAccess, convertedValue);
                var lambda = Expression.Lambda<Action<TExcelModel, object?>>(assign, parameter, valueParam);

                setters[columnName] = new PropertySetter
                {
                    Setter = lambda.Compile(),
                    PropertyInfo = property
                };

                types[columnName] = property.PropertyType;

                // Consider column required if it's not nullable
                var underlyingType = Nullable.GetUnderlyingType(property.PropertyType);
                if (underlyingType == null && property.PropertyType.IsValueType)
                {
                    required.Add(columnName);
                }
            }
        }

        return (setters, types, required);
    }

    private class RowData
    {
        public List<Cell> Cells { get; set; } = new();
        public Dictionary<int, string> ColumnMapping { get; set; } = new();
        public int RowIndex { get; set; }
    }
}
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace DynamicExcel.Write;

public sealed class ConditionalFormatStyleManager
{
    private readonly IExcelCacheManager _cacheManager;
    private readonly Dictionary<string, uint> _conditionalStyleCache = new();
    private uint _nextDifferentialFormat;

    public ConditionalFormatStyleManager(IExcelCacheManager cacheManager)
    {
        _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
    }

    public Dictionary<string, uint> CreateConditionalStyles(WorkbookPart workbookPart, List<ISheetConfiguration> sheets)
    {
        ArgumentNullException.ThrowIfNull(workbookPart);
        ArgumentNullException.ThrowIfNull(sheets);

        var stylesPart = workbookPart.WorkbookStylesPart;
        if (stylesPart?.Stylesheet == null)
        {
            return _conditionalStyleCache;
        }

        EnsureDifferentialFormatsExists(stylesPart);

        var uniqueStyles = CollectUniqueStyles(sheets);
        CreateDifferentialFormats(stylesPart, uniqueStyles);

        return _conditionalStyleCache;
    }

    private void EnsureDifferentialFormatsExists(WorkbookStylesPart stylesPart)
    {
        stylesPart.Stylesheet.DifferentialFormats ??= new DifferentialFormats { Count = 0 };
    }

    private HashSet<string> CollectUniqueStyles(List<ISheetConfiguration> sheets)
    {
        var uniqueStyles = new HashSet<string>(StringComparer.Ordinal);

        foreach (var sheet in sheets)
        {
            foreach (var rule in sheet.ConditionalRules)
            {
                var visualActions = GetVisualActions(rule.Actions);

                if (visualActions.Count > 0)
                {
                    var styleKey = GetStyleKey(visualActions);
                    uniqueStyles.Add(styleKey);
                }
            }
        }

        return uniqueStyles;
    }

    private List<ConditionalAction> GetVisualActions(List<ConditionalAction> actions)
    {
        return actions
            .Where(a => a.Type is not ConditionalActionType.ChangeReadOnly and not ConditionalActionType.ChangeDropdown)
            .ToList();
    }

    private void CreateDifferentialFormats(WorkbookStylesPart stylesPart, HashSet<string> uniqueStyles)
    {
        foreach (var styleKey in uniqueStyles)
        {
            CreateDifferentialFormat(stylesPart, styleKey);
        }

        stylesPart.Stylesheet.DifferentialFormats!.Count = _nextDifferentialFormat;
    }

    private string GetStyleKey(List<ConditionalAction> actions)
    {
        if (actions.Count == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>(actions.Count);

        foreach (var action in actions.OrderBy(a => a.Type))
        {
            var part = CreateStyleKeyPart(action);
            if (!string.IsNullOrEmpty(part))
            {
                parts.Add(part);
            }
        }

        return string.Join("|", parts);
    }

    private string CreateStyleKeyPart(ConditionalAction action)
    {
        return action.Type switch
        {
            ConditionalActionType.ChangeBackgroundColor when !string.IsNullOrEmpty(action.Value) => $"bg:{action.Value}",
            ConditionalActionType.ChangeFontColor when !string.IsNullOrEmpty(action.Value) => $"fc:{action.Value}",
            ConditionalActionType.ChangeFontBold when action.BoolValue.HasValue => $"fb:{action.BoolValue}",
            _ => string.Empty,
        };
    }

    private void CreateDifferentialFormat(WorkbookStylesPart stylesPart, string styleKey)
    {
        if (_conditionalStyleCache.ContainsKey(styleKey))
        {
            return;
        }

        var differentialFormat = new DifferentialFormat();
        var parts = styleKey.Split('|', StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            ApplyStylePart(differentialFormat, part);
        }

        stylesPart.Stylesheet.DifferentialFormats?.Append(differentialFormat);
        _conditionalStyleCache[styleKey] = _nextDifferentialFormat++;
    }

    private void ApplyStylePart(DifferentialFormat dxf, string part)
    {
        var colonIndex = part.IndexOf(':', StringComparison.Ordinal);
        if (colonIndex < 0)
        {
            return;
        }

        var type = part[..colonIndex];
        var value = part[(colonIndex + 1)..];

        switch (type)
        {
            case "bg":
                ApplyBackgroundColor(dxf, value);
                break;
            case "fc":
                ApplyFontColor(dxf, value);
                break;
            case "fb":
                ApplyFontBold(dxf, value);
                break;
        }
    }

    private void ApplyBackgroundColor(DifferentialFormat dxf, string colorValue)
    {
        var bgColor = _cacheManager.GetCleanHexColor(colorValue) ?? colorValue.Replace("#", "", StringComparison.Ordinal);

        dxf.Fill = new Fill(new PatternFill
        {
            PatternType = PatternValues.Solid,
            ForegroundColor = new ForegroundColor { Rgb = "FF" + bgColor },
            BackgroundColor = new BackgroundColor { Rgb = "FF" + bgColor },
        });
    }

    private void ApplyFontColor(DifferentialFormat dxf, string colorValue)
    {
        var fontColor = _cacheManager.GetCleanHexColor(colorValue) ?? colorValue.Replace("#", "", StringComparison.Ordinal);

        dxf.Font ??= new Font();
        dxf.Font.Append(new Color { Rgb = "FF" + fontColor });
    }

    private void ApplyFontBold(DifferentialFormat dxf, string boolValue)
    {
        if (string.Equals(boolValue, "True", StringComparison.Ordinal))
        {
            dxf.Font ??= new Font();
            dxf.Font.Append(new Bold());
        }
    }

    public uint? GetStyleId(List<ConditionalAction>? actions)
    {
        if (actions?.Count is null or 0)
        {
            return null;
        }

        var visualActions = GetVisualActions(actions);

        if (visualActions.Count == 0)
        {
            return null;
        }

        var styleKey = GetStyleKey(visualActions);
        return _conditionalStyleCache.TryGetValue(styleKey, out var styleId) ? styleId : null;
    }
}

public sealed class ConditionalFormattingWriter
{
    private readonly IExcelCacheManager _cacheManager;
    private readonly ConditionalFormatStyleManager _styleManager;

    public ConditionalFormattingWriter(IExcelCacheManager cacheManager, ConditionalFormatStyleManager styleManager)
    {
        _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
        _styleManager = styleManager ?? throw new ArgumentNullException(nameof(styleManager));
    }

    public void WriteConditionalFormatting(OpenXmlWriter writer, List<ExcelConditionalRule>? rules, PropertyAccessor[] propertyAccessors, int maxRows)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(propertyAccessors);

        if (rules?.Count is null or 0)
        {
            return;
        }

        var propertyIndexMap = CreatePropertyIndexMap(propertyAccessors);
        var rulesByTarget = GroupRulesByTarget(rules, propertyIndexMap);

        foreach (var targetGroup in rulesByTarget)
        {
            if (propertyIndexMap.TryGetValue(targetGroup.Key, out var targetIndex))
            {
                WriteConditionalFormattingForTarget(writer, targetGroup.Value, targetIndex, maxRows);
            }
        }
    }

    private Dictionary<string, List<RuleInfo>> GroupRulesByTarget(List<ExcelConditionalRule> rules, Dictionary<string, int> propertyIndexMap)
    {
        var rulesByTarget = new Dictionary<string, List<RuleInfo>>(StringComparer.Ordinal);

        foreach (var rule in rules)
        {
            if (!IsValidRule(rule, propertyIndexMap, out var sourceIndex))
            {
                continue;
            }

            if (!rulesByTarget.TryGetValue(rule.TargetPropertyName, out var rulesList))
            {
                rulesList = new List<RuleInfo>();
                rulesByTarget[rule.TargetPropertyName] = rulesList;
            }

            rulesList.Add(new RuleInfo(rule, sourceIndex));
        }

        return rulesByTarget;
    }

    private static bool IsValidRule(ExcelConditionalRule rule, Dictionary<string, int> propertyIndexMap, out int sourceIndex)
    {
        sourceIndex = 0;
        return propertyIndexMap.TryGetValue(rule.SourcePropertyName, out sourceIndex) &&
               propertyIndexMap.ContainsKey(rule.TargetPropertyName);
    }

    private void WriteConditionalFormattingForTarget(OpenXmlWriter writer, List<RuleInfo> rules, int targetColumnIndex, int maxRows)
    {
        var targetRange = $"{_cacheManager.GetExcelColumnName(targetColumnIndex)}2:" +
                         $"{_cacheManager.GetExcelColumnName(targetColumnIndex)}{maxRows}";

        var visualRules = GetVisualRules(rules);

        if (visualRules.Count == 0)
        {
            return;
        }

        var conditionalFormatting = new ConditionalFormatting
        {
            SequenceOfReferences = new ListValue<StringValue> { InnerText = targetRange },
        };

        var priority = 1;
        foreach (var ruleInfo in visualRules)
        {
            var cfRule = CreateConditionalFormattingRule(ruleInfo.Rule, ruleInfo.SourceIndex, priority++);
            if (cfRule != null)
            {
                conditionalFormatting.Append(cfRule);
            }
        }

        writer.WriteElement(conditionalFormatting);
    }

    private List<RuleInfo> GetVisualRules(List<RuleInfo> rules)
    {
        return rules
            .Where(r => r.Rule.Actions.Exists(a =>
                a.Type is not ConditionalActionType.ChangeReadOnly and not ConditionalActionType.ChangeDropdown))
            .ToList();
    }

    private ConditionalFormattingRule? CreateConditionalFormattingRule(ExcelConditionalRule rule, int sourceColumnIndex, int priority)
    {
        var formatStyleId = _styleManager.GetStyleId(rule.Actions);

        if (!formatStyleId.HasValue)
        {
            return null;
        }

        var conditionalRule = new ConditionalFormattingRule
        {
            Type = ConditionalFormatValues.Expression,
            FormatId = formatStyleId.Value,
            Priority = priority,
        };

        var formula = CreateFormula(rule, sourceColumnIndex);

        if (!string.IsNullOrEmpty(formula))
        {
            var formulaElement = new Formula { Text = formula };
            conditionalRule.Append(formulaElement);
        }

        return conditionalRule;
    }

    private Dictionary<string, int> CreatePropertyIndexMap(PropertyAccessor[] propertyAccessors)
    {
        var map = new Dictionary<string, int>(propertyAccessors.Length, StringComparer.Ordinal);

        for (var i = 0; i < propertyAccessors.Length; i++)
        {
            map[propertyAccessors[i].Property.Name] = i + 1;
        }

        return map;
    }

    private string CreateFormula(ExcelConditionalRule rule, int sourceColumnIndex)
    {
        var sourceColumn = _cacheManager.GetExcelColumnName(sourceColumnIndex);

        return rule.Operator switch
        {
            ConditionalOperator.Equal => CreateEqualFormula(rule, sourceColumn),
            ConditionalOperator.Contains => CreateContainsFormula(rule, sourceColumn),
            ConditionalOperator.GreaterThan => CreateComparisonFormula(rule, sourceColumn, ">"),
            ConditionalOperator.LessThan => CreateComparisonFormula(rule, sourceColumn, "<"),
            _ => string.Empty
        };
    }

    private string CreateEqualFormula(ExcelConditionalRule rule, string sourceColumn)
    {
        return rule.Values.Count switch
        {
            1 => $"${sourceColumn}2=\"{rule.Values[0]}\"",
            > 1 => $"OR({string.Join(",", rule.Values.Select(v => $"${sourceColumn}2=\"{v}\""))})",
            _ => string.Empty
        };
    }

    private string CreateContainsFormula(ExcelConditionalRule rule, string sourceColumn)
    {
        return rule.Values.Count > 0 ? $"ISNUMBER(SEARCH(\"{rule.Values[0]}\",${sourceColumn}2))" : string.Empty;
    }

    private string CreateComparisonFormula(ExcelConditionalRule rule, string sourceColumn, string operatorSymbol)
    {
        if (rule.Values.Count == 0)
        {
            return string.Empty;
        }

        var value = rule.Values[0];
        return double.TryParse(value, out _) ? $"${sourceColumn}2{operatorSymbol}{value}" : $"${sourceColumn}2{operatorSymbol}\"{value}\"";
    }

    private readonly struct RuleInfo
    {
        public ExcelConditionalRule Rule { get; }
        public int SourceIndex { get; }

        public RuleInfo(ExcelConditionalRule rule, int sourceIndex)
        {
            Rule = rule;
            SourceIndex = sourceIndex;
        }
    }
}

public sealed class DataRowWriter<TExcelModel> where TExcelModel : class
{
    private readonly IExcelCacheManager _cacheManager;
    private readonly IExcelCellWriter _cellWriter;
    private readonly StringBuilderPool _stringBuilderPool;

    public DataRowWriter(IExcelCacheManager cacheManager, IExcelCellWriter cellWriter, StringBuilderPool stringBuilderPool)
    {
        _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
        _cellWriter = cellWriter ?? throw new ArgumentNullException(nameof(cellWriter));
        _stringBuilderPool = stringBuilderPool ?? throw new ArgumentNullException(nameof(stringBuilderPool));
    }

    public int WriteRows(
        OpenXmlWriter writer,
        IEnumerable<TExcelModel> data,
        PropertyAccessor[] propertyAccessors,
        Dictionary<string, uint> styleMappings,
        uint startRowIndex,
        CancellationToken cancellationToken)
    {
        var excelModels = data.ToList();
        ValidateParameters(writer, excelModels, propertyAccessors, styleMappings);

        var rowCount = 0;
        var currentRowIndex = startRowIndex;
        var columnStyles = PrepareColumnStyles(propertyAccessors, styleMappings);
        var stringBuilder = _stringBuilderPool.Rent();

        try
        {
            foreach (var batch in excelModels.Batch(ExcelWriterConstants.BatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                rowCount += WriteBatch(writer, batch, propertyAccessors, columnStyles, ref currentRowIndex, stringBuilder);
            }
        }
        finally
        {
            _stringBuilderPool.Return(stringBuilder);
        }

        return rowCount;
    }

    private static void ValidateParameters(
        OpenXmlWriter writer,
        IEnumerable<TExcelModel> data,
        PropertyAccessor[] propertyAccessors,
        Dictionary<string, uint> styleMappings)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(propertyAccessors);
        ArgumentNullException.ThrowIfNull(styleMappings);
    }

    private ColumnStyleInfo PrepareColumnStyles(PropertyAccessor[] propertyAccessors, Dictionary<string, uint> styleMappings)
    {
        var columnStyles = new uint?[propertyAccessors.Length];
        var dateColumnStyles = new uint?[propertyAccessors.Length];
        var timeColumnStyles = new uint?[propertyAccessors.Length];

        for (var i = 0; i < propertyAccessors.Length; i++)
        {
            var accessor = propertyAccessors[i];
            if (string.IsNullOrEmpty(accessor.Color))
            {
                continue;
            }

            var cleanHex = _cacheManager.GetCleanHexColor(accessor.Color);
            if (cleanHex == null)
            {
                continue;
            }

            TrySetColumnStyle(styleMappings, $"{ExcelWriterConstants.CellStylePrefix}{cleanHex}", ref columnStyles[i]);
            TrySetColumnStyle(styleMappings, $"{ExcelWriterConstants.CellDateStylePrefix}{cleanHex}", ref dateColumnStyles[i]);
            TrySetColumnStyle(styleMappings, $"{ExcelWriterConstants.CellTimeStylePrefix}{cleanHex}", ref timeColumnStyles[i]);
        }

        return new ColumnStyleInfo(columnStyles, dateColumnStyles, timeColumnStyles);
    }

    private static void TrySetColumnStyle(Dictionary<string, uint> styleMappings, string key, ref uint? style)
    {
        if (styleMappings.TryGetValue(key, out var styleValue))
        {
            style = styleValue;
        }
    }

    private int WriteBatch(
        OpenXmlWriter writer,
        IEnumerable<TExcelModel?> batch,
        PropertyAccessor[] propertyAccessors,
        ColumnStyleInfo columnStyles,
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
        ColumnStyleInfo columnStyles,
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

    private static uint? GetStyleIndexForValue(object? value, ColumnStyleInfo columnStyles, int colIndex)
    {
        return value switch
        {
            DateTime => columnStyles.DateStyles[colIndex] ?? ExcelWriterConstants.DefaultDateStyle,
            TimeSpan => columnStyles.TimeStyles[colIndex] ?? ExcelWriterConstants.DefaultTimeStyle,
            _ => columnStyles.CellStyles[colIndex],
        };
    }

    private static object? GetPropertyValue(PropertyAccessor accessor, TExcelModel? item)
    {
        if (item == null)
        {
            return null;
        }

        try
        {
            return accessor.Getter(item);
        }
        catch (Exception ex)
        {
            // Log the exception
            return null;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string GetCellReference(int columnNumber, uint rowNumber, StringBuilder sb)
    {
        sb.Clear();
        sb.Append(_cacheManager.GetExcelColumnName(columnNumber));
        sb.Append(rowNumber);
        return sb.ToString();
    }

    private readonly struct ColumnStyleInfo
    {
        public uint?[] CellStyles { get; }
        public uint?[] DateStyles { get; }
        public uint?[] TimeStyles { get; }

        public ColumnStyleInfo(uint?[] cellStyles, uint?[] dateStyles, uint?[] timeStyles)
        {
            CellStyles = cellStyles;
            DateStyles = dateStyles;
            TimeStyles = timeStyles;
        }
    }
}

public sealed class HeaderRowWriter
{
    private readonly IExcelCacheManager _cacheManager;
    private readonly StringBuilderPool _stringBuilderPool;

    public HeaderRowWriter(IExcelCacheManager cacheManager, StringBuilderPool stringBuilderPool)
    {
        _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
        _stringBuilderPool = stringBuilderPool ?? throw new ArgumentNullException(nameof(stringBuilderPool));
    }

    public void WriteHeader(OpenXmlWriter writer, PropertyAccessor[] propertyAccessors, Dictionary<string, uint> styleMappings, uint rowIndex)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(propertyAccessors);
        ArgumentNullException.ThrowIfNull(styleMappings);

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
            if (cleanHex != null &&
                styleMappings.TryGetValue($"{ExcelWriterConstants.HeaderStylePrefix}{cleanHex}", out var headerStyle))
            {
                styleIndex = headerStyle;
            }
        }

        return styleIndex;
    }

    private static void WriteHeaderCell(OpenXmlWriter writer, string cellReference, string columnName, uint styleIndex)
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string GetCellReference(int columnNumber, uint rowNumber, StringBuilder sb)
    {
        sb.Clear();
        sb.Append(_cacheManager.GetExcelColumnName(columnNumber));
        sb.Append(rowNumber);
        return sb.ToString();
    }
}

public sealed class StaticDataWriter
{
    private readonly IExcelCacheManager _cacheManager;
    private readonly IExcelCellWriter _cellWriter;
    private readonly StringBuilderPool _stringBuilderPool;

    public sealed class StaticDataResult
    {
        public Dictionary<string, List<string>> DropdownData { get; set; } = new();
        public Dictionary<string, string> ColumnMappings { get; set; } = new();
        public Dictionary<string, string> ColumnPositions { get; set; } = new();
        public Dictionary<string, List<string>> PropertyToDropdowns { get; set; } = new();
    }

    public StaticDataWriter(IExcelCacheManager cacheManager, IExcelCellWriter cellWriter, StringBuilderPool stringBuilderPool)
    {
        _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
        _cellWriter = cellWriter ?? throw new ArgumentNullException(nameof(cellWriter));
        _stringBuilderPool = stringBuilderPool ?? throw new ArgumentNullException(nameof(stringBuilderPool));
    }

    public StaticDataResult WriteStaticData(WorksheetPart worksheetPart, List<ExcelDropdownData> dropdownDataList, CancellationToken cancellationToken)
    {
        ValidateParameters(worksheetPart, dropdownDataList);

        var result = new StaticDataResult();
        var columnDataCollectors = InitializeColumnDataCollectors(dropdownDataList, result.ColumnMappings);

        BuildPropertyToDropdownsMapping(dropdownDataList, result.PropertyToDropdowns);

        using (var writer = OpenXmlWriter.Create(worksheetPart))
        {
            writer.WriteStartElement(new Worksheet());
            WriteStaticColumnsElement(writer, dropdownDataList);
            writer.WriteStartElement(new SheetData());

            WriteStaticHeaderRow(writer, dropdownDataList, 1);
            var rowCount = WriteStaticDataRows(writer, dropdownDataList, 2, columnDataCollectors, result.ColumnPositions, cancellationToken);

            UpdateColumnPositions(result.ColumnPositions, rowCount);

            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        PopulateDropdownData(result.DropdownData, columnDataCollectors);

        return result;
    }

    private static void ValidateParameters(WorksheetPart worksheetPart, List<ExcelDropdownData> dropdownDataList)
    {
        ArgumentNullException.ThrowIfNull(worksheetPart);
        ArgumentNullException.ThrowIfNull(dropdownDataList);
    }

    private static void BuildPropertyToDropdownsMapping(List<ExcelDropdownData> dropdownDataList, Dictionary<string, List<string>> propertyToDropdowns)
    {
        foreach (var dropdown in dropdownDataList)
        {
            foreach (var bindProperty in dropdown.BindProperties)
            {
                if (!propertyToDropdowns.TryGetValue(bindProperty, out var dropdownList))
                {
                    dropdownList = new List<string>();
                    propertyToDropdowns[bindProperty] = dropdownList;
                }
                dropdownList.Add(dropdown.ColumnName);
            }
        }
    }

    private static Dictionary<string, HashSet<string>> InitializeColumnDataCollectors(List<ExcelDropdownData> dropdownDataList, Dictionary<string, string> columnMappings)
    {
        var columnDataCollectors = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var propertyToDropdowns = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var column in dropdownDataList)
        {
            if (column.BindProperties.Count == 0)
            {
                continue;
            }

            foreach (var bindTo in column.BindProperties)
            {
                if (!propertyToDropdowns.TryGetValue(bindTo, out var dropdownList))
                {
                    dropdownList = new List<string>();
                    propertyToDropdowns[bindTo] = dropdownList;
                }
                dropdownList.Add(column.ColumnName);

                if (!columnDataCollectors.ContainsKey(column.ColumnName))
                {
                    columnDataCollectors[column.ColumnName] = new HashSet<string>(StringComparer.Ordinal);
                }
            }
        }

        foreach (var kvp in propertyToDropdowns)
        {
            if (kvp.Value.Count == 1)
            {
                columnMappings[kvp.Key] = kvp.Value[0];
            }
        }

        return columnDataCollectors;
    }

    private static void WriteStaticColumnsElement(OpenXmlWriter writer, List<ExcelDropdownData> dropdownDataList)
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
        var maxRows = dropdownDataList.Count > 0 ? dropdownDataList.Max(col => col.DataList.Count) : 0;

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

    private static void UpdateColumnPositions(Dictionary<string, string> columnPositions, int rowCount)
    {
        var keys = columnPositions.Keys.ToList();
        foreach (var key in keys)
        {
            columnPositions[key] = $"${columnPositions[key]}$2:${columnPositions[key]}${rowCount}";
        }
    }

    private static void PopulateDropdownData(Dictionary<string, List<string>> dropdownData, Dictionary<string, HashSet<string>> columnDataCollectors)
    {
        foreach (var collector in columnDataCollectors)
        {
            dropdownData[collector.Key] = collector.Value.OrderBy(v => v, StringComparer.Ordinal).ToList();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetActualColumnIndex(int columnIndex)
    {
        const int columnSeparator = 1;
        return columnIndex * (1 + columnSeparator) + 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string GetCellReference(int columnNumber, uint rowNumber, StringBuilder sb)
    {
        sb.Clear();
        sb.Append(_cacheManager.GetExcelColumnName(columnNumber));
        sb.Append(rowNumber);
        return sb.ToString();
    }
}

public sealed class SheetConfiguration<T> : ISheetConfiguration where T : class, new()
{
    public string SheetName { get; }
    public List<T> Data { get; set; }
    public HashSet<string> ExcludedProperties { get; }
    public List<ExcelDropdownData> DropdownData { get; set; }
    public string? DropdownSheetName { get; set; }
    public List<ExcelConditionalRule> ConditionalRules { get; set; }

    public SheetConfiguration(string sheetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetName);

        SheetName = sheetName;
        Data = new List<T>();
        ExcludedProperties = new HashSet<string>(StringComparer.Ordinal);
        DropdownData = new List<ExcelDropdownData>();
        ConditionalRules = new List<ExcelConditionalRule>();
    }

    public PropertyAccessor[] GetPropertyAccessors(IExcelCacheManager cacheManager)
    {
        ArgumentNullException.ThrowIfNull(cacheManager);
        return cacheManager.GetPropertyAccessors<T>(ExcludedProperties);
    }

    public bool HasDropdownData() => DropdownData.Count > 0;

    public List<ExcelDropdownData> GetDropdownData() => DropdownData;

    public void WriteToWorksheet(
        WorksheetPart worksheetPart,
        IExcelCacheManager cacheManager,
        IExcelCellWriter cellWriter,
        IExcelValidationManager validationManager,
        StringBuilderPool stringBuilderPool,
        Dictionary<string, uint> styleMappings,
        Dictionary<string, List<string>>? dropdownDataDict,
        Dictionary<string, string>? columnMappings,
        Dictionary<string, string>? columnPositions,
        string dropdownSheetName, WorkbookPart workbookPart,
        ConditionalFormatStyleManager conditionalStyleManager,
        CancellationToken cancellationToken,
        Dictionary<string, List<string>>? propertyToDropdowns)
    {
        ArgumentNullException.ThrowIfNull(worksheetPart);
        ArgumentNullException.ThrowIfNull(cacheManager);
        ArgumentNullException.ThrowIfNull(cellWriter);
        ArgumentNullException.ThrowIfNull(validationManager);
        ArgumentNullException.ThrowIfNull(stringBuilderPool);
        ArgumentNullException.ThrowIfNull(styleMappings);
        ArgumentNullException.ThrowIfNull(workbookPart);
        ArgumentNullException.ThrowIfNull(conditionalStyleManager);

        var propertyAccessors = GetPropertyAccessors(cacheManager);

        using var writer = OpenXmlWriter.Create(worksheetPart);
        writer.WriteStartElement(new Worksheet());

        WriteColumnsElement(writer, propertyAccessors, styleMappings, cacheManager);
        WriteSheetData(writer, propertyAccessors, styleMappings, cacheManager, cellWriter, stringBuilderPool, cancellationToken);

        if (ConditionalRules.Count > 0)
        {
            WriteConditionalFormatting(writer, propertyAccessors, cacheManager, conditionalStyleManager);
        }

        WriteDataValidations(
            writer,
            propertyAccessors,
            columnMappings,
            columnPositions,
            dropdownDataDict,
            dropdownSheetName,
            validationManager,
            propertyToDropdowns);

        writer.WriteEndElement();
    }

    private void WriteColumnsElement(OpenXmlWriter writer, PropertyAccessor[] propertyAccessors, Dictionary<string, uint> styleMappings, IExcelCacheManager cacheManager)
    {
        writer.WriteStartElement(new Columns());

        for (uint i = 0; i < propertyAccessors.Length; i++)
        {
            var accessor = propertyAccessors[i];
            var column = new Column
            {
                Min = i + 1,
                Max = i + 1,
                Width = accessor.Width,
                CustomWidth = true,
                BestFit = false,
            };

            if (!string.IsNullOrEmpty(accessor.Color))
            {
                var cleanHex = cacheManager.GetCleanHexColor(accessor.Color);
                if (cleanHex != null && styleMappings.TryGetValue($"{ExcelWriterConstants.CellStylePrefix}{cleanHex}", out var styleIndex))
                {
                    column.Style = styleIndex;
                }
            }

            writer.WriteElement(column);
        }

        writer.WriteEndElement();
    }

    private void WriteSheetData(OpenXmlWriter writer, PropertyAccessor[] propertyAccessors, Dictionary<string, uint> styleMappings,
        IExcelCacheManager cacheManager, IExcelCellWriter cellWriter, StringBuilderPool stringBuilderPool, CancellationToken cancellationToken)
    {
        writer.WriteStartElement(new SheetData());

        var headerWriter = new HeaderRowWriter(cacheManager, stringBuilderPool);
        headerWriter.WriteHeader(writer, propertyAccessors, styleMappings, 1);

        var dataRowWriter = new DataRowWriter<T>(cacheManager, cellWriter, stringBuilderPool);
        dataRowWriter.WriteRows(writer, Data, propertyAccessors, styleMappings, 2, cancellationToken);

        writer.WriteEndElement();
    }

    private void WriteConditionalFormatting(OpenXmlWriter writer, PropertyAccessor[] propertyAccessors,
        IExcelCacheManager cacheManager, ConditionalFormatStyleManager conditionalStyleManager)
    {
        var conditionalWriter = new ConditionalFormattingWriter(cacheManager, conditionalStyleManager);
        conditionalWriter.WriteConditionalFormatting(writer, ConditionalRules, propertyAccessors, ExcelWriterConstants.MaxFormattingRows);
    }

    private void WriteDataValidations(
        OpenXmlWriter writer,
        PropertyAccessor[] propertyAccessors,
        Dictionary<string, string>? columnMappings,
        Dictionary<string, string>? columnPositions,
        Dictionary<string, List<string>>? dropdownDataDict,
        string dropdownSheetName,
        IExcelValidationManager validationManager,
        Dictionary<string, List<string>>? propertyToDropdowns)
    {
        if (ShouldAddValidations(propertyAccessors))
        {
            var propertyNameToColumnIndexMap = CreatePropertyNameToColumnIndexMap(propertyAccessors);

            var extendedParameter = new ExtendedDataValidationParameter
            {
                Writer = writer,
                PropertyAccessors = propertyAccessors,
                ColumnMappings = columnMappings,
                ColumnPositions = columnPositions,
                DropdownData = dropdownDataDict,
                ColumnIndexMap = propertyNameToColumnIndexMap,
                TotalRows = ExcelWriterConstants.MaxFormattingRows,
                DropdownSheetName = dropdownSheetName,
                ConditionalRules = ConditionalRules,
                PropertyToDropdowns = propertyToDropdowns,
            };

            validationManager.AddDataValidations(extendedParameter);
        }
    }

    private Dictionary<string, int> CreatePropertyNameToColumnIndexMap(PropertyAccessor[] propertyAccessors)
    {
        var map = new Dictionary<string, int>(propertyAccessors.Length, StringComparer.Ordinal);

        for (var i = 0; i < propertyAccessors.Length; i++)
        {
            map[propertyAccessors[i].Property.Name] = i + 1;
        }

        return map;
    }

    private bool ShouldAddValidations(PropertyAccessor[] propertyAccessors)
    {
        return propertyAccessors.Any(p => p.IsReadOnly) ||
               HasDropdownData() ||
               HasTypeValidations(propertyAccessors) ||
               ConditionalRules.Exists(r => r.Actions.Exists(a => a.Type == ConditionalActionType.ChangeReadOnly));
    }

    private static bool HasTypeValidations(PropertyAccessor[] propertyAccessors)
    {
        return propertyAccessors.Any(p =>
            TypeValidator.IsIntegerType(p.UnderlyingType) ||
            TypeValidator.IsDecimalType(p.UnderlyingType) ||
            p.UnderlyingType == typeof(DateTime) ||
            p.UnderlyingType == typeof(TimeSpan));
    }
}

public sealed class SimpleSheetConfiguration<T> : ISheetConfiguration where T : class
{
    public string SheetName { get; }
    public List<T> Data { get; }
    public string? DropdownSheetName => null;
    public List<ExcelConditionalRule> ConditionalRules => new();

    private const BindingFlags PropertyBindingFlags = BindingFlags.Public | BindingFlags.Instance;
    private readonly IPropertyNameFormatter _propertyNameFormatter;
    private PropertyAccessor[]? _propertyAccessors;

    public SimpleSheetConfiguration(string sheetName, List<T> data, IPropertyNameFormatter propertyNameFormatter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetName);

        SheetName = sheetName;
        Data = data ?? throw new ArgumentNullException(nameof(data));
        _propertyNameFormatter = propertyNameFormatter ?? throw new ArgumentNullException(nameof(propertyNameFormatter));
    }

    public PropertyAccessor[] GetPropertyAccessors(IExcelCacheManager cacheManager)
    {
        return _propertyAccessors ??= CreatePropertyAccessors();
    }

    private PropertyAccessor[] CreatePropertyAccessors()
    {
        var properties = typeof(T)
            .GetProperties(PropertyBindingFlags)
            .Where(p => p.CanRead)
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToArray();

        var propertyAccessors = new PropertyAccessor[properties.Length];

        for (var i = 0; i < properties.Length; i++)
        {
            propertyAccessors[i] = CreatePropertyAccessor(properties[i], i);
        }

        return propertyAccessors;
    }

    private PropertyAccessor CreatePropertyAccessor(PropertyInfo property, int order)
    {
        var getter = CreatePropertyGetter(property);
        var underlyingType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        var columnName = _propertyNameFormatter.ConvertToFriendlyName(property.Name);

        return new PropertyAccessor
        {
            Property = property,
            ColumnName = columnName,
            Order = order,
            Color = null,
            Width = ExcelWriterConstants.DefaultColumnWidth,
            Getter = getter,
            PropertyType = property.PropertyType,
            UnderlyingType = underlyingType,
            IsNullable = Nullable.GetUnderlyingType(property.PropertyType) != null,
            IsReadOnly = false,
        };
    }

    private static Func<object, object> CreatePropertyGetter(PropertyInfo property)
    {
        var parameter = Expression.Parameter(typeof(object), "x");
        var cast = Expression.Convert(parameter, typeof(T));
        var propertyAccess = Expression.Property(cast, property);
        var convert = Expression.Convert(propertyAccess, typeof(object));
        var lambda = Expression.Lambda<Func<object, object>>(convert, parameter);

        return lambda.Compile();
    }

    public bool HasDropdownData() => false;

    public List<ExcelDropdownData>? GetDropdownData() => null;

    public void WriteToWorksheet(
        WorksheetPart worksheetPart,
        IExcelCacheManager cacheManager,
        IExcelCellWriter cellWriter,
        IExcelValidationManager validationManager,
        StringBuilderPool stringBuilderPool,
        Dictionary<string, uint> styleMappings,
        Dictionary<string, List<string>>? dropdownDataDict,
        Dictionary<string, string>? columnMappings,
        Dictionary<string, string>? columnPositions,
        string dropdownSheetName,
        WorkbookPart workbookPart,
        ConditionalFormatStyleManager conditionalStyleManager,
        CancellationToken cancellationToken,
        Dictionary<string, List<string>>? propertyToDropdowns)
    {
        ArgumentNullException.ThrowIfNull(worksheetPart);
        ArgumentNullException.ThrowIfNull(cacheManager);
        ArgumentNullException.ThrowIfNull(cellWriter);
        ArgumentNullException.ThrowIfNull(stringBuilderPool);
        ArgumentNullException.ThrowIfNull(styleMappings);

        var propertyAccessors = GetPropertyAccessors(cacheManager);

        using var writer = OpenXmlWriter.Create(worksheetPart);
        writer.WriteStartElement(new Worksheet());

        WriteColumnsElement(writer, propertyAccessors);
        WriteSheetData(writer, propertyAccessors, styleMappings, cacheManager, cellWriter, stringBuilderPool, cancellationToken);

        writer.WriteEndElement();
    }

    private void WriteSheetData(
        OpenXmlWriter writer,
        PropertyAccessor[] propertyAccessors,
        Dictionary<string, uint> styleMappings,
        IExcelCacheManager cacheManager,
        IExcelCellWriter cellWriter,
        StringBuilderPool stringBuilderPool,
        CancellationToken cancellationToken)
    {
        writer.WriteStartElement(new SheetData());

        var headerWriter = new HeaderRowWriter(cacheManager, stringBuilderPool);
        headerWriter.WriteHeader(writer, propertyAccessors, styleMappings, 1);

        var dataRowWriter = new DataRowWriter<T>(cacheManager, cellWriter, stringBuilderPool);
        dataRowWriter.WriteRows(writer, Data, propertyAccessors, styleMappings, 2, cancellationToken);

        writer.WriteEndElement();
    }

    private static void WriteColumnsElement(OpenXmlWriter writer, PropertyAccessor[] propertyAccessors)
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
}

public sealed class ExcelFileBuilder
{
    private readonly List<ISheetConfiguration> _sheets = new();
    private readonly IExcelCacheManager _cacheManager;
    private readonly IExcelCellWriter _cellWriter;
    private readonly IExcelStyleManager _styleManager;
    private readonly IExcelValidationManager _validationManager;
    private readonly StringBuilderPool _stringBuilderPool;
    private readonly IPropertyNameFormatter _propertyNameFormatter;
    private readonly IExcelWorkbookWriter _workbookWriter;
    private readonly string _dropdownSheetName = ExcelWriterConstants.DefaultDropdownSheetName;

    public ExcelFileBuilder() : this(
        new ExcelCacheManager(),
        new ExcelCellWriter(),
        new ExcelStyleManager(new ExcelCacheManager()),
        new ExcelValidationManager(new ExcelCacheManager()),
        new StringBuilderPool(),
        new PropertyNameFormatter(),
        new ExcelWorkbookWriter())
    {
    }

    public ExcelFileBuilder(
        IExcelCacheManager cacheManager,
        IExcelCellWriter cellWriter,
        IExcelStyleManager styleManager,
        IExcelValidationManager validationManager,
        StringBuilderPool stringBuilderPool,
        IPropertyNameFormatter propertyNameFormatter,
        IExcelWorkbookWriter workbookWriter)
    {
        _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
        _cellWriter = cellWriter ?? throw new ArgumentNullException(nameof(cellWriter));
        _styleManager = styleManager ?? throw new ArgumentNullException(nameof(styleManager));
        _validationManager = validationManager ?? throw new ArgumentNullException(nameof(validationManager));
        _stringBuilderPool = stringBuilderPool ?? throw new ArgumentNullException(nameof(stringBuilderPool));
        _propertyNameFormatter = propertyNameFormatter ?? throw new ArgumentNullException(nameof(propertyNameFormatter));
        _workbookWriter = workbookWriter ?? throw new ArgumentNullException(nameof(workbookWriter));
    }

    public SheetBuilder<T> AddSheet<T>(string sheetName) where T : class, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetName);
        return new SheetBuilder<T>(this, sheetName);
    }

    public ExcelFileBuilder AddSimpleSheet<T>(List<T> data, string? sheetName = null) where T : class
    {
        ArgumentNullException.ThrowIfNull(data);

        var actualSheetName = sheetName ?? GenerateSheetName<T>();
        var configuration = new SimpleSheetConfiguration<T>(actualSheetName, data, _propertyNameFormatter);
        _sheets.Add(configuration);

        return this;
    }

    internal void AddSheetConfiguration(ISheetConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _sheets.Add(configuration);
    }

    public MemoryStream Build(CancellationToken cancellationToken = default)
    {
        if (_sheets.Count == 0)
        {
            throw new InvalidOperationException("At least one sheet must be added before building the Excel file.");
        }

        var memoryStream = new MemoryStream();

        try
        {
            BuildExcelDocument(memoryStream, cancellationToken);
            memoryStream.Position = 0;
            return memoryStream;
        }
        catch
        {
            memoryStream.Dispose();
            throw;
        }
    }

    private void BuildExcelDocument(MemoryStream memoryStream, CancellationToken cancellationToken)
    {
        using var document = SpreadsheetDocument.Create(memoryStream, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();

        var styleMappings = CreateStyleMappings(workbookPart, cancellationToken);
        var conditionalStyleManager = CreateConditionalFormatStyles(workbookPart);

        var dropdownSheets = CreateDropdownSheets(workbookPart, cancellationToken);
        var sheets = CreateDataSheets(workbookPart, styleMappings, dropdownSheets, conditionalStyleManager, cancellationToken);

        AddDropdownSheetsToSheetList(sheets, dropdownSheets);
        _workbookWriter.WriteWorkbook(workbookPart, sheets);
    }

    private Dictionary<string, uint> CreateStyleMappings(WorkbookPart workbookPart, CancellationToken cancellationToken)
    {
        var allPropertyAccessors = new List<PropertyAccessor>();

        foreach (var sheet in _sheets)
        {
            allPropertyAccessors.AddRange(sheet.GetPropertyAccessors(_cacheManager));
        }

        return _styleManager.CreateStyles(workbookPart, allPropertyAccessors.ToArray(), cancellationToken);
    }

    private ConditionalFormatStyleManager CreateConditionalFormatStyles(WorkbookPart workbookPart)
    {
        var conditionalStyleManager = new ConditionalFormatStyleManager(_cacheManager);
        conditionalStyleManager.CreateConditionalStyles(workbookPart, _sheets);
        return conditionalStyleManager;
    }

    private Dictionary<string, DropdownSheetInfo> CreateDropdownSheets(WorkbookPart workbookPart, CancellationToken cancellationToken)
    {
        var dropdownSheets = new Dictionary<string, DropdownSheetInfo>(StringComparer.Ordinal);

        if (!_sheets.Any(s => s.HasDropdownData()))
        {
            return dropdownSheets;
        }

        var groupedDropdowns = _sheets
            .Where(s => s.HasDropdownData())
            .GroupBy(s => s.DropdownSheetName ?? _dropdownSheetName)
            .ToList();

        foreach (var group in groupedDropdowns)
        {
            var dropdownSheetInfo = CreateDropdownSheetForGroup(workbookPart, group, cancellationToken);
            if (dropdownSheetInfo != null)
            {
                dropdownSheets[group.Key] = dropdownSheetInfo;
            }
        }

        return dropdownSheets;
    }

    private DropdownSheetInfo? CreateDropdownSheetForGroup(WorkbookPart workbookPart, IGrouping<string, ISheetConfiguration> configGroup, CancellationToken cancellationToken)
    {
        var allDropdownData = MergeDropdownData(configGroup);

        if (allDropdownData.Count == 0)
        {
            return null;
        }

        var staticWorksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var dropdownSheetRelationshipId = workbookPart.GetIdOfPart(staticWorksheetPart);

        var staticDataWriter = new StaticDataWriter(_cacheManager, _cellWriter, _stringBuilderPool);
        var result = staticDataWriter.WriteStaticData(staticWorksheetPart, allDropdownData, cancellationToken);

        return new DropdownSheetInfo(dropdownSheetRelationshipId, result.DropdownData,
            result.ColumnMappings, result.ColumnPositions, result.PropertyToDropdowns);
    }

    private static List<ExcelDropdownData> MergeDropdownData(IGrouping<string, ISheetConfiguration> configGroup)
    {
        return configGroup
            .SelectMany(s => s.GetDropdownData() ?? Enumerable.Empty<ExcelDropdownData>())
            .GroupBy(d => d.ColumnName, StringComparer.Ordinal)
            .Select(g => new ExcelDropdownData
            {
                ColumnName = g.Key,
                DataList = g.SelectMany(d => d.DataList).Distinct().ToList(),
                BindProperties = g.SelectMany(d => d.BindProperties).Distinct().ToList()
            })
            .ToList();
    }

    private List<(string SheetName, string RelationshipId)> CreateDataSheets(
        WorkbookPart workbookPart,
        Dictionary<string, uint> styleMappings,
        Dictionary<string, DropdownSheetInfo> dropdownSheets,
        ConditionalFormatStyleManager conditionalStyleManager,
        CancellationToken cancellationToken)
    {
        var sheets = new List<(string SheetName, string RelationshipId)>();

        foreach (var sheetConfig in _sheets)
        {
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var relationshipId = workbookPart.GetIdOfPart(worksheetPart);
            sheets.Add((sheetConfig.SheetName, relationshipId));

            WriteSheetData(worksheetPart, sheetConfig, styleMappings, dropdownSheets, workbookPart, conditionalStyleManager, cancellationToken);
        }

        return sheets;
    }

    private void WriteSheetData(
        WorksheetPart worksheetPart,
        ISheetConfiguration sheetConfig,
        Dictionary<string, uint> styleMappings,
        Dictionary<string, DropdownSheetInfo> dropdownSheets,
        WorkbookPart workbookPart,
        ConditionalFormatStyleManager conditionalStyleManager,
        CancellationToken cancellationToken)
    {
        var dropdownSheetName = sheetConfig.DropdownSheetName ?? _dropdownSheetName;

        dropdownSheets.TryGetValue(dropdownSheetName, out var dropdownInfo);

        sheetConfig.WriteToWorksheet(
            worksheetPart,
            _cacheManager,
            _cellWriter,
            _validationManager,
            _stringBuilderPool,
            styleMappings,
            dropdownInfo?.Data,
            dropdownInfo?.ColumnMappings,
            dropdownInfo?.ColumnPositions,
            dropdownSheetName,
            workbookPart,
            conditionalStyleManager,
            cancellationToken,
            dropdownInfo?.PropertyToDropdowns);
    }

    private static void AddDropdownSheetsToSheetList(List<(string SheetName, string RelationshipId)> sheets, Dictionary<string, DropdownSheetInfo> dropdownSheets)
    {
        foreach (var dropdownSheet in dropdownSheets.OrderBy(ds => ds.Key, StringComparer.Ordinal))
        {
            sheets.Add((dropdownSheet.Key, dropdownSheet.Value.RelationshipId));
        }
    }

    private string GenerateSheetName<T>()
    {
        var typeName = typeof(T).Name;
        typeName = RemoveCommonSuffixes(typeName);

        return _propertyNameFormatter.ConvertToFriendlyName(typeName);
    }

    private static string RemoveCommonSuffixes(string typeName)
    {
        var suffixes = new[] { "Model", "Dto", "Entity" };

        foreach (var suffix in suffixes)
        {
            if (typeName.EndsWith(suffix, StringComparison.Ordinal))
            {
                return typeName[..^suffix.Length];
            }
        }

        return typeName;
    }

    private sealed class DropdownSheetInfo
    {
        public string RelationshipId { get; }
        public Dictionary<string, List<string>> Data { get; }
        public Dictionary<string, string> ColumnMappings { get; }
        public Dictionary<string, string> ColumnPositions { get; }
        public Dictionary<string, List<string>> PropertyToDropdowns { get; }

        public DropdownSheetInfo(
            string relationshipId,
            Dictionary<string, List<string>> data,
            Dictionary<string, string> columnMappings,
            Dictionary<string, string> columnPositions,
            Dictionary<string, List<string>> propertyToDropdowns)
        {
            RelationshipId = relationshipId;
            Data = data;
            ColumnMappings = columnMappings;
            ColumnPositions = columnPositions;
            PropertyToDropdowns = propertyToDropdowns;
        }
    }
}

public sealed class SheetBuilder<T> where T : class, new()
{
    private readonly ExcelFileBuilder _fileBuilder;
    private readonly SheetConfiguration<T> _configuration;

    public SheetBuilder(ExcelFileBuilder fileBuilder, string sheetName)
    {
        _fileBuilder = fileBuilder ?? throw new ArgumentNullException(nameof(fileBuilder));
        _configuration = new SheetConfiguration<T>(sheetName);
    }

    public SheetBuilder<T> WithData(List<T> data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _configuration.Data = data;
        return this;
    }

    public SheetBuilder<T> ExcludeProperties(params Expression<Func<T, object>>[] excludeProperties)
    {
        ArgumentNullException.ThrowIfNull(excludeProperties);

        var propertyNames = ExtractPropertyNames(excludeProperties);
        foreach (var propertyName in propertyNames)
        {
            _configuration.ExcludedProperties.Add(propertyName);
        }

        return this;
    }

    public SheetBuilder<T> WithDropdownData(List<ExcelDropdownData> dropdownData, string dropdownSheetName = "Static Data")
    {
        ArgumentNullException.ThrowIfNull(dropdownData);

        _configuration.DropdownData = dropdownData;
        _configuration.DropdownSheetName = dropdownSheetName;
        return this;
    }

    public SheetBuilder<T> WithConditionalRules(IExcelConditionalRulesFactory<T> conditionalFactory)
    {
        ArgumentNullException.ThrowIfNull(conditionalFactory);
        _configuration.ConditionalRules = conditionalFactory.Rules;
        return this;
    }

    public ExcelFileBuilder Done()
    {
        _fileBuilder.AddSheetConfiguration(_configuration);
        return _fileBuilder;
    }

    private HashSet<string> ExtractPropertyNames(Expression<Func<T, object>>[] expressions)
    {
        var propertyNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var expression in expressions)
        {
            var propertyName = ExtractPropertyName(expression);
            propertyNames.Add(propertyName);
        }

        return propertyNames;
    }

    private static string ExtractPropertyName(Expression<Func<T, object>> expression)
    {
        var body = expression.Body;

        if (body is UnaryExpression unaryExpression && unaryExpression.NodeType == ExpressionType.Convert)
        {
            body = unaryExpression.Operand;
        }

        if (body is MemberExpression memberExpression && memberExpression.Member is PropertyInfo property)
        {
            return property.Name;
        }

        throw new ArgumentException($"Expression '{expression}' does not refer to a property.");
    }
}

public interface IExcelWorkbookWriter
{
    void WriteWorkbook(WorkbookPart workbookPart, List<(string sheetName, string relationshipId)> sheets);
}

public sealed class ExcelWorkbookWriter : IExcelWorkbookWriter
{
    public void WriteWorkbook(WorkbookPart workbookPart, List<(string sheetName, string relationshipId)>? sheets)
    {
        ArgumentNullException.ThrowIfNull(workbookPart);

        if (sheets?.Count is null or 0)
        {
            throw new ArgumentException("Sheets list cannot be null or empty", nameof(sheets));
        }

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
                Id = relationshipId
            });
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
    }
}

public interface ISheetConfiguration
{
    string SheetName { get; }
    string? DropdownSheetName { get; }
    List<ExcelConditionalRule> ConditionalRules { get; }
    PropertyAccessor[] GetPropertyAccessors(IExcelCacheManager cacheManager);
    bool HasDropdownData();
    List<ExcelDropdownData>? GetDropdownData();

    void WriteToWorksheet(
        WorksheetPart worksheetPart,
        IExcelCacheManager cacheManager,
        IExcelCellWriter cellWriter,
        IExcelValidationManager validationManager,
        StringBuilderPool stringBuilderPool,
        Dictionary<string, uint> styleMappings,
        Dictionary<string, List<string>>? dropdownDataDict,
        Dictionary<string, string>? columnMappings,
        Dictionary<string, string>? columnPositions,
        string dropdownSheetName, WorkbookPart workbookPart,
        ConditionalFormatStyleManager conditionalStyleManager,
        CancellationToken cancellationToken,
        Dictionary<string, List<string>>? propertyToDropdowns);
}

#region Conditional Formatting Interfaces

public interface IExcelConditionalRulesFactory<TExcelModel> where TExcelModel : class
{
    IExcelConditionalWhenBuilder<TExcelModel> When<TProperty>(Expression<Func<TExcelModel, TProperty>> propertyExpression);
    List<ExcelConditionalRule> Rules { get; }
}

public interface IExcelConditionalWhenBuilder<TExcelModel> where TExcelModel : class
{
    IExcelConditionalThenBuilder<TExcelModel> Equals(params string[] values);
    IExcelConditionalThenBuilder<TExcelModel> Contains(string value);
    IExcelConditionalThenBuilder<TExcelModel> GreaterThan(string value);
    IExcelConditionalThenBuilder<TExcelModel> LessThan(string value);
}

public interface IExcelConditionalThenBuilder<TExcelModel> where TExcelModel : class
{
    IExcelConditionalActionBuilder<TExcelModel> Then<TProperty>(Expression<Func<TExcelModel, TProperty>> propertyExpression);
}

public interface IExcelConditionalActionBuilder<TExcelModel> where TExcelModel : class
{
    IExcelConditionalActionBuilder<TExcelModel> ChangeColor(string hexColor);
    IExcelConditionalActionBuilder<TExcelModel> ChangeReadOnly(bool isReadOnly);
    IExcelConditionalActionBuilder<TExcelModel> ChangeFontColor(string hexColor);
    IExcelConditionalActionBuilder<TExcelModel> ChangeFontBold(bool isBold);
    IExcelConditionalActionBuilder<TExcelModel> ChangeDropdown(string dropdownName);
    void Build();
}

#endregion

#region Dropdown Interfaces

public interface IExcelDropdownsFactory<TExcelModel> where TExcelModel : class
{
    IExcelDropdownBuilder<TExcelModel> Create(string sourceColumnName, IReadOnlyCollection<object> dropdownDataList);
    List<ExcelDropdownData> DropdownsList { get; }
}

public interface IExcelDropdownBuilder<TExcelModel> where TExcelModel : class
{
    IExcelDropdownBuilder<TExcelModel> Bind<TResult>(Expression<Func<TExcelModel, TResult>> propertyExpression);
    void Build();
}

#endregion

#region Conditional Formatting Implementation

public sealed class ExcelConditionalRulesFactory<TExcelModel> : IExcelConditionalRulesFactory<TExcelModel>
    where TExcelModel : class
{
    private readonly List<ExcelConditionalRule> _rules = new();

    public List<ExcelConditionalRule> Rules => _rules;

    public IExcelConditionalWhenBuilder<TExcelModel> When<TProperty>(Expression<Func<TExcelModel, TProperty>> propertyExpression)
    {
        ArgumentNullException.ThrowIfNull(propertyExpression);

        var propertyInfo = GetPropertyInfo(propertyExpression);
        return new ExcelConditionalWhenBuilder<TExcelModel>(propertyInfo.Name, _rules);
    }

    private static PropertyInfo GetPropertyInfo<TProperty>(Expression<Func<TExcelModel, TProperty>> expression)
    {
        if (expression.Body is not MemberExpression memberExpression)
        {
            throw new ArgumentException("Expression must be a member expression");
        }

        if (memberExpression.Member is not PropertyInfo propertyInfo)
        {
            throw new ArgumentException("Expression must refer to a property");
        }

        return propertyInfo;
    }
}

public sealed class ExcelConditionalWhenBuilder<TExcelModel> : IExcelConditionalWhenBuilder<TExcelModel>
    where TExcelModel : class
{
    private readonly string _sourcePropertyName;
    private readonly List<ExcelConditionalRule> _targetCollection;

    public ExcelConditionalWhenBuilder(string sourcePropertyName, List<ExcelConditionalRule> targetCollection)
    {
        _sourcePropertyName = sourcePropertyName ?? throw new ArgumentNullException(nameof(sourcePropertyName));
        _targetCollection = targetCollection ?? throw new ArgumentNullException(nameof(targetCollection));
    }

    public IExcelConditionalThenBuilder<TExcelModel> Equals(params string[]? values)
    {
        if (values?.Length is null or 0)
        {
            throw new ArgumentException("At least one value must be provided", nameof(values));
        }

        var rule = new ExcelConditionalRule
        {
            SourcePropertyName = _sourcePropertyName,
            Operator = ConditionalOperator.Equal,
            Values = values.ToList(),
        };

        return new ExcelConditionalThenBuilder<TExcelModel>(rule, _targetCollection);
    }

    public IExcelConditionalThenBuilder<TExcelModel> Contains(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);

        var rule = new ExcelConditionalRule
        {
            SourcePropertyName = _sourcePropertyName,
            Operator = ConditionalOperator.Contains,
            Values = [value],
        };

        return new ExcelConditionalThenBuilder<TExcelModel>(rule, _targetCollection);
    }

    public IExcelConditionalThenBuilder<TExcelModel> GreaterThan(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);

        var rule = new ExcelConditionalRule
        {
            SourcePropertyName = _sourcePropertyName,
            Operator = ConditionalOperator.GreaterThan,
            Values = [value],
        };

        return new ExcelConditionalThenBuilder<TExcelModel>(rule, _targetCollection);
    }

    public IExcelConditionalThenBuilder<TExcelModel> LessThan(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);

        var rule = new ExcelConditionalRule
        {
            SourcePropertyName = _sourcePropertyName,
            Operator = ConditionalOperator.LessThan,
            Values = [value],
        };

        return new ExcelConditionalThenBuilder<TExcelModel>(rule, _targetCollection);
    }
}

public sealed class ExcelConditionalThenBuilder<TExcelModel> : IExcelConditionalThenBuilder<TExcelModel>
    where TExcelModel : class
{
    private readonly ExcelConditionalRule _rule;
    private readonly List<ExcelConditionalRule> _targetCollection;

    public ExcelConditionalThenBuilder(ExcelConditionalRule rule, List<ExcelConditionalRule> targetCollection)
    {
        _rule = rule ?? throw new ArgumentNullException(nameof(rule));
        _targetCollection = targetCollection ?? throw new ArgumentNullException(nameof(targetCollection));
    }

    public IExcelConditionalActionBuilder<TExcelModel> Then<TProperty>(Expression<Func<TExcelModel, TProperty>> propertyExpression)
    {
        ArgumentNullException.ThrowIfNull(propertyExpression);

        var propertyInfo = GetPropertyInfo(propertyExpression);
        _rule.TargetPropertyName = propertyInfo.Name;

        return new ExcelConditionalActionBuilder<TExcelModel>(_rule, _targetCollection);
    }

    private static PropertyInfo GetPropertyInfo<TProperty>(Expression<Func<TExcelModel, TProperty>> expression)
    {
        if (expression.Body is not MemberExpression memberExpression)
        {
            throw new ArgumentException("Expression must be a member expression");
        }

        if (memberExpression.Member is not PropertyInfo propertyInfo)
        {
            throw new ArgumentException("Expression must refer to a property");
        }

        return propertyInfo;
    }
}

public sealed class ExcelConditionalActionBuilder<TExcelModel> : IExcelConditionalActionBuilder<TExcelModel>
    where TExcelModel : class
{
    private readonly ExcelConditionalRule _rule;
    private readonly List<ExcelConditionalRule> _targetCollection;
    private bool _isBuilt;

    public ExcelConditionalActionBuilder(ExcelConditionalRule rule, List<ExcelConditionalRule> targetCollection)
    {
        _rule = rule ?? throw new ArgumentNullException(nameof(rule));
        _targetCollection = targetCollection ?? throw new ArgumentNullException(nameof(targetCollection));
    }

    public IExcelConditionalActionBuilder<TExcelModel> ChangeColor(string hexColor)
    {
        ThrowIfBuilt();
        ArgumentException.ThrowIfNullOrEmpty(hexColor);

        _rule.Actions.Add(new ConditionalAction
        {
            Type = ConditionalActionType.ChangeBackgroundColor,
            Value = hexColor,
        });

        return this;
    }

    public IExcelConditionalActionBuilder<TExcelModel> ChangeReadOnly(bool isReadOnly)
    {
        ThrowIfBuilt();

        _rule.Actions.Add(new ConditionalAction
        {
            Type = ConditionalActionType.ChangeReadOnly,
            BoolValue = isReadOnly,
        });

        return this;
    }

    public IExcelConditionalActionBuilder<TExcelModel> ChangeFontColor(string hexColor)
    {
        ThrowIfBuilt();
        ArgumentException.ThrowIfNullOrEmpty(hexColor);

        _rule.Actions.Add(new ConditionalAction
        {
            Type = ConditionalActionType.ChangeFontColor,
            Value = hexColor,
        });

        return this;
    }

    public IExcelConditionalActionBuilder<TExcelModel> ChangeFontBold(bool isBold)
    {
        ThrowIfBuilt();

        _rule.Actions.Add(new ConditionalAction
        {
            Type = ConditionalActionType.ChangeFontBold,
            BoolValue = isBold,
        });

        return this;
    }

    public IExcelConditionalActionBuilder<TExcelModel> ChangeDropdown(string dropdownName)
    {
        ThrowIfBuilt();
        ArgumentException.ThrowIfNullOrEmpty(dropdownName);

        _rule.Actions.Add(new ConditionalAction
        {
            Type = ConditionalActionType.ChangeDropdown,
            DropdownName = dropdownName,
        });

        return this;
    }

    public void Build()
    {
        ThrowIfBuilt();

        if (_rule.Actions.Count == 0)
        {
            throw new InvalidOperationException("At least one action must be specified");
        }

        _targetCollection.Add(_rule);
        _isBuilt = true;
    }

    private void ThrowIfBuilt()
    {
        if (_isBuilt)
        {
            throw new InvalidOperationException("This rule has already been built");
        }
    }
}

#endregion

#region Dropdown Implementation

public sealed class ExcelDropdownsFactory<TExcelModel> : IExcelDropdownsFactory<TExcelModel>
    where TExcelModel : class
{
    private readonly List<ExcelDropdownData> _dropdownsList = new();

    public List<ExcelDropdownData> DropdownsList => _dropdownsList.ToList();

    public IExcelDropdownBuilder<TExcelModel> Create(string sourceColumnName, IReadOnlyCollection<object>? dropdownDataList)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceColumnName);

        if (dropdownDataList?.Count is null or 0)
        {
            throw new ArgumentException("Dropdown data list cannot be null or empty", nameof(dropdownDataList));
        }

        return new ExcelDropdownBuilder<TExcelModel>(sourceColumnName, dropdownDataList, _dropdownsList);
    }
}

public sealed class ExcelDropdownBuilder<TExcelModel> : IExcelDropdownBuilder<TExcelModel>
    where TExcelModel : class
{
    private readonly string _columnName;
    private readonly IReadOnlyCollection<object> _columnDataList;
    private readonly List<ExcelDropdownData> _targetCollection;
    private readonly HashSet<string> _bindings = new(StringComparer.Ordinal);
    private bool _isCompleted;

    public ExcelDropdownBuilder(string columnName, IReadOnlyCollection<object> columnDataList, List<ExcelDropdownData> targetCollection)
    {
        _columnName = columnName;
        _columnDataList = columnDataList;
        _targetCollection = targetCollection;
    }

    public IExcelDropdownBuilder<TExcelModel> Bind<TResult>(Expression<Func<TExcelModel, TResult>> propertyExpression)
    {
        ArgumentNullException.ThrowIfNull(propertyExpression);
        ThrowIfCompleted();

        var propertyInfo = GetProperty(propertyExpression);
        var attribute = propertyInfo.GetCustomAttribute<ExcelColumnAttribute>();

        if (attribute == null)
        {
            throw new InvalidOperationException($"Property '{propertyInfo.Name}' must be decorated with {nameof(ExcelColumnAttribute)}.");
        }

        if (!_bindings.Add(propertyInfo.Name))
        {
            throw new InvalidOperationException($"Property '{propertyInfo.Name}' has already been bound.");
        }

        return this;
    }

    public void Build()
    {
        ThrowIfCompleted();

        if (_bindings.Count == 0)
        {
            throw new InvalidOperationException("At least one property must be bound before adding static data.");
        }

        var staticData = new ExcelDropdownData
        {
            ColumnName = _columnName,
            BindProperties = _bindings.ToList(),
            DataList = _columnDataList,
        };

        _targetCollection.Add(staticData);
        _isCompleted = true;
    }

    private void ThrowIfCompleted()
    {
        if (_isCompleted)
        {
            throw new InvalidOperationException("This builder instance has already been completed. Create a new builder for additional static data.");
        }
    }

    private static PropertyInfo GetProperty<T, TResult>(Expression<Func<T, TResult>> expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        if (expression.Body is not MemberExpression memberExpression)
        {
            throw new ArgumentException($"Body of expression needs to be {typeof(MemberExpression)}");
        }

        if (memberExpression.Member is not PropertyInfo property)
        {
            throw new ArgumentException("Must be a property");
        }

        return property;
    }
}

#endregion

public sealed class ExcelValidationManager : IExcelValidationManager
{
    private readonly IExcelCacheManager _cacheManager;
    private readonly DateTime _minExcelDate = new(1900, 1, 1);
    private readonly DateTime _maxExcelDate = new(9999, 12, 31);

    public ExcelValidationManager(IExcelCacheManager cacheManager)
    {
        _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
    }

    public void AddDataValidations(DataValidationParameter validationParameter)
    {
        ArgumentNullException.ThrowIfNull(validationParameter);

        var validations = new List<DataValidationInfo>();

        AddReadOnlyValidations(validations, validationParameter);
        AddDropdownValidations(validations, validationParameter);
        AddTypeBasedValidations(validations, validationParameter);

        if (validationParameter is ExtendedDataValidationParameter extendedParam)
        {
            AddConditionalReadOnlyValidations(validations, extendedParam);
        }

        if (validations.Count > 0)
        {
            WriteDataValidations(validationParameter.Writer, validations);
        }
    }

    private void AddConditionalReadOnlyValidations(List<DataValidationInfo> validations, ExtendedDataValidationParameter? parameter)
    {
        if (parameter?.ConditionalRules.Count is null or 0)
        {
            return;
        }

        var propertyIndexMap = CreatePropertyIndexMap(parameter.PropertyAccessors);

        foreach (var rule in parameter.ConditionalRules)
        {
            var readOnlyAction = rule.Actions.Find(a => a.Type == ConditionalActionType.ChangeReadOnly && a.BoolValue == true);

            if (readOnlyAction == null)
            {
                continue;
            }

            if (!propertyIndexMap.TryGetValue(rule.SourcePropertyName, out var sourceIndex) ||
                !propertyIndexMap.TryGetValue(rule.TargetPropertyName, out var targetIndex))
            {
                continue;
            }

            var targetColumnName = _cacheManager.GetExcelColumnName(targetIndex);
            var sourceColumnName = _cacheManager.GetExcelColumnName(sourceIndex);
            var cellRange = $"{targetColumnName}2:{targetColumnName}{parameter.TotalRows}";

            var formula = CreateConditionalFormula(rule, sourceColumnName);

            if (!string.IsNullOrEmpty(formula))
            {
                validations.Add(new DataValidationInfo
                {
                    Type = DataValidationType.Custom,
                    CellRange = cellRange,
                    Formula1 = $"NOT({formula})",
                    ErrorTitle = ExcelWriterConstants.ConditionalReadOnlyTitle,
                    ErrorMessage = $"This field is read-only when {GetConditionDescription(rule)}",
                    AllowBlank = true,
                });
            }
        }
    }

    private static string CreateConditionalFormula(ExcelConditionalRule rule, string sourceColumn)
    {
        return rule.Operator switch
        {
            ConditionalOperator.Equal when rule.Values.Count == 1 => $"{sourceColumn}2=\"{rule.Values[0]}\"",
            ConditionalOperator.Equal when rule.Values.Count > 1 => $"OR({string.Join(",", rule.Values.Select(v => $"{sourceColumn}2=\"{v}\""))})",
            ConditionalOperator.Contains when rule.Values.Count > 0 => $"ISNUMBER(SEARCH(\"{rule.Values[0]}\",{sourceColumn}2))",
            ConditionalOperator.GreaterThan when rule.Values.Count > 0 => $"{sourceColumn}2>{rule.Values[0]}",
            ConditionalOperator.LessThan when rule.Values.Count > 0 => $"{sourceColumn}2<{rule.Values[0]}",
            _ => string.Empty
        };
    }

    private static string GetConditionDescription(ExcelConditionalRule rule)
    {
        var values = string.Join(" or ", rule.Values.Select(v => $"'{v}'"));

        return rule.Operator switch
        {
            ConditionalOperator.Equal => $"{rule.SourcePropertyName} is {values}",
            ConditionalOperator.Contains => $"{rule.SourcePropertyName} contains '{rule.Values.FirstOrDefault()}'",
            ConditionalOperator.GreaterThan => $"{rule.SourcePropertyName} is greater than {rule.Values.FirstOrDefault()}",
            ConditionalOperator.LessThan => $"{rule.SourcePropertyName} is less than {rule.Values.FirstOrDefault()}",
            _ => "condition is met"
        };
    }

    private Dictionary<string, int> CreatePropertyIndexMap(PropertyAccessor[] propertyAccessors)
    {
        var map = new Dictionary<string, int>(propertyAccessors.Length, StringComparer.Ordinal);

        for (var i = 0; i < propertyAccessors.Length; i++)
        {
            map[propertyAccessors[i].Property.Name] = i + 1;
        }

        return map;
    }

    private void AddReadOnlyValidations(List<DataValidationInfo> validations, DataValidationParameter validationParameter)
    {
        for (var i = 0; i < validationParameter.PropertyAccessors.Length; i++)
        {
            var accessor = validationParameter.PropertyAccessors[i];

            if (accessor.IsReadOnly)
            {
                var columnIndex = i + 1;
                var cellRange = $"{_cacheManager.GetExcelColumnName(columnIndex)}2:" + $"{_cacheManager.GetExcelColumnName(columnIndex)}{validationParameter.TotalRows}";

                validations.Add(new DataValidationInfo
                {
                    Type = DataValidationType.Custom,
                    CellRange = cellRange,
                    Formula1 = "FALSE",
                    ErrorTitle = ExcelWriterConstants.ReadOnlyValidationTitle,
                    ErrorMessage = $"The column '{accessor.ColumnName}' is read-only and cannot be modified.",
                    AllowBlank = true,
                });
            }
        }
    }

    private void AddDropdownValidations(List<DataValidationInfo> validations, DataValidationParameter validationParameter)
    {
        if (validationParameter.DropdownData?.Count is null or 0 || validationParameter.ColumnPositions == null)
        {
            return;
        }

        var propertyToDropdowns = validationParameter.PropertyToDropdowns ?? new Dictionary<string, List<string>>();

        Dictionary<string, List<ConditionalDropdownRule>>? conditionalDropdowns = null;
        if (validationParameter is ExtendedDataValidationParameter extendedParam)
        {
            conditionalDropdowns = BuildConditionalDropdownRules(extendedParam.ConditionalRules, validationParameter.PropertyAccessors, validationParameter.ColumnPositions);
        }

        foreach (var propertyDropdown in propertyToDropdowns)
        {
            var propertyName = propertyDropdown.Key;
            var dropdownNames = propertyDropdown.Value;

            if (!validationParameter.ColumnIndexMap.TryGetValue(propertyName, out var columnIndex))
            {
                continue;
            }

            var accessor = validationParameter.PropertyAccessors.FirstOrDefault(a => a.Property.Name == propertyName);

            if (accessor?.IsReadOnly == true)
            {
                continue;
            }

            var cellRange = $"{_cacheManager.GetExcelColumnName(columnIndex)}2:" + $"{_cacheManager.GetExcelColumnName(columnIndex)}{validationParameter.TotalRows}";

            if (conditionalDropdowns?.ContainsKey(propertyName) == true)
            {
                ProcessConditionalDropdown(validations, validationParameter, propertyName, dropdownNames, conditionalDropdowns, cellRange);
            }
            else
            {
                ProcessStandardDropdown(validations, validationParameter, dropdownNames, cellRange);
            }
        }
    }

    private void ProcessConditionalDropdown(
        List<DataValidationInfo> validations,
        DataValidationParameter validationParameter,
        string propertyName, List<string> dropdownNames,
        Dictionary<string, List<ConditionalDropdownRule>> conditionalDropdowns,
        string cellRange)
    {
        string? primarySourceRange = null;

        var usedInConditions = conditionalDropdowns[propertyName]
            .Select(r => r.AlternativeDropdownName)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var dropdownName in dropdownNames)
        {
            if (!usedInConditions.Contains(dropdownName) && validationParameter.ColumnPositions != null && validationParameter.ColumnPositions.TryGetValue(dropdownName, out var range))
            {
                primarySourceRange = range;
                break;
            }
        }

        if (primarySourceRange == null && dropdownNames.Count > 0)
        {
            var firstDropdown = dropdownNames[0];
            if (validationParameter.ColumnPositions != null && validationParameter.ColumnPositions.TryGetValue(firstDropdown, out var range))
            {
                primarySourceRange = range;
            }
        }

        if (primarySourceRange != null)
        {
            var dropdownRules = conditionalDropdowns[propertyName];
            var formula = BuildConditionalDropdownFormula(dropdownRules, primarySourceRange, validationParameter.DropdownSheetName);

            validations.Add(new DataValidationInfo
            {
                Type = DataValidationType.List,
                CellRange = cellRange,
                Formula1 = formula,
                AllowBlank = true,
            });
        }
    }

    private static void ProcessStandardDropdown(List<DataValidationInfo> validations, DataValidationParameter validationParameter, List<string> dropdownNames, string cellRange)
    {
        if (dropdownNames.Count > 0)
        {
            var dropdownName = dropdownNames[0];
            if (validationParameter.ColumnPositions != null && validationParameter.ColumnPositions.TryGetValue(dropdownName, out var sourceRange))
            {
                validations.Add(new DataValidationInfo
                {
                    Type = DataValidationType.List,
                    CellRange = cellRange,
                    Formula1 = $"'{validationParameter.DropdownSheetName}'!{sourceRange}",
                    AllowBlank = true,
                });
            }
        }
    }

    private Dictionary<string, List<ConditionalDropdownRule>> BuildConditionalDropdownRules(
        List<ExcelConditionalRule> conditionalRules,
        PropertyAccessor[] propertyAccessors,
        Dictionary<string, string> columnPositions)
    {
        var result = new Dictionary<string, List<ConditionalDropdownRule>>(StringComparer.Ordinal);
        var propertyIndexMap = CreatePropertyIndexMap(propertyAccessors);

        foreach (var rule in conditionalRules)
        {
            var dropdownAction = rule.Actions.Find(a => a.Type == ConditionalActionType.ChangeDropdown);
            if (dropdownAction?.DropdownName == null)
            {
                continue;
            }

            if (!propertyIndexMap.TryGetValue(rule.SourcePropertyName, out var sourceIndex) ||
                !propertyIndexMap.TryGetValue(rule.TargetPropertyName, out _))
            {
                continue;
            }

            if (!columnPositions.TryGetValue(dropdownAction.DropdownName, out var alternativeRange))
            {
                continue;
            }

            var sourceColumnName = _cacheManager.GetExcelColumnName(sourceIndex);

            if (!result.TryGetValue(rule.TargetPropertyName, out var rulesList))
            {
                rulesList = new List<ConditionalDropdownRule>();
                result[rule.TargetPropertyName] = rulesList;
            }

            rulesList.Add(new ConditionalDropdownRule
            {
                SourceColumn = sourceColumnName,
                Operator = rule.Operator,
                Values = rule.Values,
                AlternativeRange = alternativeRange,
                AlternativeDropdownName = dropdownAction.DropdownName,
            });
        }

        return result;
    }

    private static string BuildConditionalDropdownFormula(List<ConditionalDropdownRule> rules, string defaultRange, string dropdownSheetName)
    {
        if (rules.Count == 0)
        {
            return $"'{dropdownSheetName}'!{defaultRange}";
        }

        var formula = new StringBuilder();

        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            var condition = CreateDropdownCondition(rule);

            if (i == 0)
            {
                formula.Append($"IF({condition},'{dropdownSheetName}'!{rule.AlternativeRange},");
            }
            else
            {
                formula.Append($"IF({condition},'{dropdownSheetName}'!{rule.AlternativeRange},");
            }
        }

        formula.Append($"'{dropdownSheetName}'!{defaultRange}");
        formula.Append(new string(')', rules.Count));

        return formula.ToString();
    }

    private static string CreateDropdownCondition(ConditionalDropdownRule rule)
    {
        var sourceRef = $"INDIRECT(\"{rule.SourceColumn}\"&ROW())";

        return rule.Operator switch
        {
            ConditionalOperator.Equal when rule.Values.Count == 1 => $"{sourceRef}=\"{rule.Values[0]}\"",
            ConditionalOperator.Equal when rule.Values.Count > 1 => $"OR({string.Join(",", rule.Values.Select(v => $"{sourceRef}=\"{v}\""))})",
            ConditionalOperator.Contains when rule.Values.Count > 0 => $"ISNUMBER(SEARCH(\"{rule.Values[0]}\",{sourceRef}))",
            ConditionalOperator.GreaterThan when rule.Values.Count > 0 => $"{sourceRef}>{rule.Values[0]}",
            ConditionalOperator.LessThan when rule.Values.Count > 0 => $"{sourceRef}<{rule.Values[0]}",
            _ => "FALSE",
        };
    }

    private sealed class ConditionalDropdownRule
    {
        public string SourceColumn { get; init; } = null!;
        public ConditionalOperator Operator { get; init; }
        public List<string> Values { get; init; } = new();
        public string AlternativeRange { get; init; } = null!;
        public string AlternativeDropdownName { get; init; } = null!;
    }

    private void AddTypeBasedValidations(List<DataValidationInfo> validations, DataValidationParameter validationParameter)
    {
        for (var i = 0; i < validationParameter.PropertyAccessors.Length; i++)
        {
            var accessor = validationParameter.PropertyAccessors[i];

            if (accessor.IsReadOnly ||
                validationParameter.ColumnMappings?.ContainsKey(accessor.Property.Name) == true)
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
        var cellRange = $"{_cacheManager.GetExcelColumnName(columnIndex)}2:" + $"{_cacheManager.GetExcelColumnName(columnIndex)}{totalRows}";

        if (TypeValidator.IsIntegerType(accessor.UnderlyingType))
        {
            return new DataValidationInfo
            {
                Type = DataValidationType.Whole,
                CellRange = cellRange,
                ErrorTitle = ExcelWriterConstants.InvalidIntegerTitle,
                ErrorMessage = $"{accessor.ColumnName} must be a whole number",
                AllowBlank = accessor.IsNullable,
            };
        }

        if (TypeValidator.IsDecimalType(accessor.UnderlyingType))
        {
            return new DataValidationInfo
            {
                Type = DataValidationType.Decimal,
                CellRange = cellRange,
                ErrorTitle = ExcelWriterConstants.InvalidNumberTitle,
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
                ErrorTitle = ExcelWriterConstants.InvalidDateTitle,
                ErrorMessage = $"{accessor.ColumnName} must be a valid date between " + $"{_minExcelDate:yyyy-MM-dd} and {_maxExcelDate:yyyy-MM-dd}",
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
                ErrorTitle = ExcelWriterConstants.InvalidTimeTitle,
                ErrorMessage = $"{accessor.ColumnName} must be a valid time value",
                AllowBlank = accessor.IsNullable,
            };
        }

        return null;
    }

    private static void WriteDataValidations(OpenXmlWriter writer, List<DataValidationInfo> validations)
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

    private static void WriteDataValidation(OpenXmlWriter writer, DataValidationInfo validation)
    {
        var attributes = CreateValidationAttributes(validation);

        writer.WriteStartElement(new DataValidation(), attributes);

        if (!string.IsNullOrEmpty(validation.Formula1))
        {
            writer.WriteElement(new Formula1 { Text = validation.Formula1 });
        }

        if (!string.IsNullOrEmpty(validation.Formula2))
        {
            writer.WriteElement(new Formula2 { Text = validation.Formula2 });
        }

        writer.WriteEndElement();
    }

    private static List<OpenXmlAttribute> CreateValidationAttributes(DataValidationInfo validation)
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

        if (RequiresBetweenOperator(validation))
        {
            attributes.Add(new OpenXmlAttribute("operator", null!, "between"));
        }

        return attributes;
    }

    private static bool RequiresBetweenOperator(DataValidationInfo validation)
    {
        return (validation.Type is DataValidationType.Whole or DataValidationType.Decimal or DataValidationType.Date or DataValidationType.Time) &&
               !string.IsNullOrEmpty(validation.Formula2);
    }

    private static string GetValidationTypeString(DataValidationType type)
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
}

#region Constants

public static class ExcelWriterConstants
{
    public const int DefaultColumnWidth = 15;
    public const int BatchSize = 10000;
    public const int MaxFormattingRows = 1_048_576;
    public const uint DefaultDateStyle = 2;
    public const uint DefaultTimeStyle = 3;
    public const string DateFormatCode = "dd-mm-yyyy";
    public const string TimeFormatCode = "[h]:mm:ss";
    public const string DefaultDropdownSheetName = "Static Data";

    public const string HeaderStylePrefix = "header_";
    public const string CellStylePrefix = "cell_";
    public const string CellDateStylePrefix = "cell_date_";
    public const string CellTimeStylePrefix = "cell_time_";
    public const string FillHeaderStylePrefix = "fill_header_";
    public const string FillCellStylePrefix = "fill_cell_";

    public const string ReadOnlyValidationTitle = "Read-Only Column";
    public const string ConditionalReadOnlyTitle = "Conditionally Read-Only";
    public const string InvalidIntegerTitle = "Invalid Integer";
    public const string InvalidNumberTitle = "Invalid Number";
    public const string InvalidDateTitle = "Invalid Date";
    public const string InvalidTimeTitle = "Invalid Time";
}

#endregion

#region Attributes

[AttributeUsage(AttributeTargets.Property)]
public sealed class ExcelColumnAttribute : Attribute
{
    public string? Name { get; set; }
    public int OrderId { get; set; }
    public string? Color { get; set; }
    public double Width { get; set; }
    public bool IsReadOnly { get; set; }

    public ExcelColumnAttribute()
    {
    }

    public ExcelColumnAttribute(string name)
    {
        Name = name;
    }

    public ExcelColumnAttribute(string name, int orderId) : this(name)
    {
        OrderId = orderId;
    }
}

#endregion

#region Enums

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

public enum ConditionalOperator
{
    Equal,
    NotEqual,
    Contains,
    NotContains,
    GreaterThan,
    LessThan,
    GreaterThanOrEqual,
    LessThanOrEqual
}

public enum ConditionalActionType
{
    ChangeBackgroundColor,
    ChangeFontColor,
    ChangeReadOnly,
    ChangeFontBold,
    ChangeDropdown
}

#endregion

#region Core Models

public sealed class PropertyAccessor
{
    public PropertyInfo Property { get; set; } = null!;
    public string ColumnName { get; set; } = null!;
    public int? Order { get; set; }
    public string? Color { get; set; }
    public double Width { get; set; }
    public Func<object, object> Getter { get; set; } = null!;
    public Type PropertyType { get; set; } = null!;
    public Type UnderlyingType { get; set; } = null!;
    public bool IsNullable { get; set; }
    public bool IsReadOnly { get; set; }
}

public sealed class DataValidationInfo
{
    public DataValidationType Type { get; init; }
    public string CellRange { get; init; } = null!;
    public string Formula1 { get; init; } = null!;
    public string Formula2 { get; init; } = null!;
    public string ErrorTitle { get; init; } = null!;
    public string ErrorMessage { get; init; } = null!;
    public bool? AllowBlank { get; init; }
}

public class DataValidationParameter
{
    public OpenXmlWriter Writer { get; set; } = null!;
    public PropertyAccessor[] PropertyAccessors { get; set; } = null!;
    public Dictionary<string, string>? ColumnMappings { get; set; }
    public Dictionary<string, string>? ColumnPositions { get; set; }
    public Dictionary<string, List<string>>? DropdownData { get; set; }
    public Dictionary<string, int> ColumnIndexMap { get; set; } = null!;
    public string DropdownSheetName { get; set; } = ExcelWriterConstants.DefaultDropdownSheetName;
    public int TotalRows { get; set; }
    public Dictionary<string, List<string>>? PropertyToDropdowns { get; set; }
}

public sealed class ExtendedDataValidationParameter : DataValidationParameter
{
    public List<ExcelConditionalRule> ConditionalRules { get; set; } = new();
}

public sealed class ExcelDropdownData
{
    public string ColumnName { get; set; } = null!;
    public IReadOnlyCollection<object> DataList { get; set; } = null!;
    public IReadOnlyCollection<string> BindProperties { get; set; } = null!;
}

public sealed class ExcelConditionalRule
{
    public string SourcePropertyName { get; set; } = null!;
    public string TargetPropertyName { get; set; } = null!;
    public ConditionalOperator Operator { get; set; }
    public List<string> Values { get; set; } = new();
    public List<ConditionalAction> Actions { get; set; } = new();
}

public sealed class ConditionalAction
{
    public ConditionalActionType Type { get; set; }
    public string? Value { get; set; }
    public bool? BoolValue { get; set; }
    public string? DropdownName { get; set; }
}

#endregion

#region Interfaces

public interface IPropertyNameFormatter
{
    string ConvertToFriendlyName(string name);
}

public interface IExcelCacheManager
{
    PropertyAccessor[] GetPropertyAccessors<TExcelModel>(HashSet<string> excludedProperties);
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

#endregion

#region Helper Classes

public static class TypeValidator
{
    private static readonly HashSet<Type> IntegerTypes = new()
        {
            typeof(int), typeof(long), typeof(short),
            typeof(byte), typeof(sbyte), typeof(ushort),
            typeof(uint), typeof(ulong),
        };

    private static readonly HashSet<Type> DecimalTypes = new()
        {
            typeof(decimal), typeof(double), typeof(float),
        };

    public static bool IsIntegerType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return IntegerTypes.Contains(type);
    }

    public static bool IsDecimalType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return DecimalTypes.Contains(type);
    }

    public static bool IsNumericType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return IsIntegerType(type) || IsDecimalType(type);
    }
}

public static class EnumerableExtensions
{
    public static IEnumerable<IEnumerable<T>> Batch<T>(this IEnumerable<T> source, int batchSize)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

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

public sealed class PropertyNameFormatter : IPropertyNameFormatter
{
    private static readonly Regex LowercaseToUppercaseRegex = new("([a-z])([A-Z])", RegexOptions.Compiled);
    private static readonly Regex LowercaseToNumberRegex = new("([a-z])([0-9])", RegexOptions.Compiled);
    private static readonly Regex NumberToUppercaseRegex = new("([0-9])([A-Z])", RegexOptions.Compiled);
    private static readonly Regex ConsecutiveCapitalsRegex = new("([A-Z]+)([A-Z][a-z])", RegexOptions.Compiled);
    private static readonly Regex AcronymAtEndRegex = new("([a-z])([A-Z]{2,}$)", RegexOptions.Compiled);

    public string ConvertToFriendlyName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var result = LowercaseToUppercaseRegex.Replace(name, "$1 $2");
        result = LowercaseToNumberRegex.Replace(result, "$1 $2");
        result = NumberToUppercaseRegex.Replace(result, "$1 $2");
        result = ConsecutiveCapitalsRegex.Replace(result, "$1 $2");
        result = AcronymAtEndRegex.Replace(result, "$1 $2");

        return result.Trim();
    }
}

public sealed class StringBuilderPool
{
    private readonly ConcurrentBag<StringBuilder> _pool = new();
    private readonly int _maxPoolSize;
    private readonly int _maxCapacity;
    private int _currentPoolSize;

    public StringBuilderPool(int maxPoolSize = 16, int maxCapacity = 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPoolSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCapacity);

        _maxPoolSize = maxPoolSize;
        _maxCapacity = maxCapacity;
    }

    public StringBuilder Rent()
    {
        if (_pool.TryTake(out var sb))
        {
            Interlocked.Decrement(ref _currentPoolSize);
            sb.Clear();
            return sb;
        }

        return new StringBuilder(64);
    }

    public void Return(StringBuilder sb)
    {
        ArgumentNullException.ThrowIfNull(sb);

        if (sb.Capacity <= _maxCapacity && _currentPoolSize < _maxPoolSize)
        {
            sb.Clear();
            _pool.Add(sb);
            Interlocked.Increment(ref _currentPoolSize);
        }
    }
}

#endregion

#region Cache Manager

public sealed class ExcelCacheManager : IExcelCacheManager
{
    private readonly ConcurrentDictionary<Type, PropertyAccessor[]> _propertyAccessorCache = new();
    private readonly ConcurrentDictionary<string, string> _excelColumnNameCache = new();
    private readonly ConcurrentDictionary<string, string> _cleanHexColorCache = new();
    private readonly IPropertyNameFormatter _propertyNameFormatter;

    public ExcelCacheManager() : this(new PropertyNameFormatter())
    {
    }

    public ExcelCacheManager(IPropertyNameFormatter propertyNameFormatter)
    {
        _propertyNameFormatter = propertyNameFormatter ?? throw new ArgumentNullException(nameof(propertyNameFormatter));
    }

    public PropertyAccessor[] GetPropertyAccessors<TExcelModel>(HashSet<string> excludedProperties)
    {
        ArgumentNullException.ThrowIfNull(excludedProperties);

        var allAccessors = _propertyAccessorCache.GetOrAdd(typeof(TExcelModel), type =>
        {
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var accessors = new List<PropertyAccessor>();

            foreach (var property in properties)
            {
                var attribute = property.GetCustomAttribute<ExcelColumnAttribute>();
                if (attribute != null)
                {
                    var accessor = CreatePropertyAccessor(type, property, attribute);
                    accessors.Add(accessor);
                }
            }

            return accessors
                .OrderBy(m => m.Order ?? int.MaxValue)
                .ThenBy(m => m.Property.Name, StringComparer.Ordinal)
                .ToArray();
        });

        if (excludedProperties.Count > 0)
        {
            return allAccessors
                .Where(a => !excludedProperties.Contains(a.Property.Name))
                .ToArray();
        }

        return allAccessors;
    }

    private PropertyAccessor CreatePropertyAccessor(Type modelType, PropertyInfo property, ExcelColumnAttribute attribute)
    {
        var parameter = Expression.Parameter(typeof(object), "x");
        var cast = Expression.Convert(parameter, modelType);
        var propertyAccess = Expression.Property(cast, property);
        var convert = Expression.Convert(propertyAccess, typeof(object));
        var lambda = Expression.Lambda<Func<object, object>>(convert, parameter);
        var getter = lambda.Compile();

        var underlyingType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        var columnName = !string.IsNullOrEmpty(attribute.Name)
            ? attribute.Name
            : _propertyNameFormatter.ConvertToFriendlyName(property.Name);

        return new PropertyAccessor
        {
            Property = property,
            ColumnName = columnName,
            Order = attribute.OrderId,
            Color = attribute.Color,
            Width = attribute.Width > 0 ? attribute.Width : ExcelWriterConstants.DefaultColumnWidth,
            Getter = getter,
            PropertyType = property.PropertyType,
            UnderlyingType = underlyingType,
            IsNullable = Nullable.GetUnderlyingType(property.PropertyType) != null,
            IsReadOnly = attribute.IsReadOnly,
        };
    }

    public string GetExcelColumnName(int columnNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columnNumber);

        return _excelColumnNameCache.GetOrAdd(columnNumber.ToString(), _ => GetExcelColumnNameInternal(columnNumber));
    }

    private static string GetExcelColumnNameInternal(int columnNumber)
    {
        Span<char> buffer = stackalloc char[10];
        var position = buffer.Length - 1;

        while (columnNumber > 0)
        {
            var modulo = (columnNumber - 1) % 26;
            buffer[position--] = (char)(65 + modulo);
            columnNumber = (columnNumber - modulo) / 26;
        }

        return new string(buffer[(position + 1)..]);
    }

    public string? GetCleanHexColor(string? hexColor)
    {
        if (string.IsNullOrEmpty(hexColor))
        {
            return null;
        }

        return _cleanHexColorCache.GetOrAdd(hexColor, color =>
        {
            var cleaned = color.Replace("#", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();

            return cleaned.Length == 6 && Regex.IsMatch(cleaned, "^[0-9A-F]{6}$") ? cleaned : "000000";
        });
    }
}

#endregion

#region Cell Writer

public sealed class ExcelCellWriter : IExcelCellWriter
{
    private const string FormulaInjectionCharacters = "=+-@";

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteCellValue(OpenXmlWriter writer, string cellReference, object value, Type underlyingType, uint? styleIndex)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentException.ThrowIfNullOrEmpty(cellReference);
        ArgumentNullException.ThrowIfNull(underlyingType);

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

    private static void WriteDateValue(OpenXmlWriter writer, string cellReference, DateTime dateValue, uint? styleIndex)
    {
        var cell = new Cell
        {
            CellReference = cellReference,
            StyleIndex = styleIndex ?? ExcelWriterConstants.DefaultDateStyle,
        };

        writer.WriteStartElement(cell);
        writer.WriteElement(new CellValue(dateValue.ToOADate().ToString(CultureInfo.InvariantCulture)));
        writer.WriteEndElement();
    }

    private static void WriteTimeSpanValue(OpenXmlWriter writer, string cellReference, TimeSpan timeSpanValue, uint? styleIndex)
    {
        var totalDays = timeSpanValue.TotalDays;

        var cell = new Cell
        {
            CellReference = cellReference,
            StyleIndex = styleIndex ?? ExcelWriterConstants.DefaultTimeStyle,
        };

        writer.WriteStartElement(cell);
        writer.WriteElement(new CellValue(totalDays.ToString(CultureInfo.InvariantCulture)));
        writer.WriteEndElement();
    }

    private static void WriteBooleanValue(OpenXmlWriter writer, string cellReference, bool boolValue, uint? styleIndex)
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

    private static void WriteNumericValue(OpenXmlWriter writer, string cellReference, object value, uint? styleIndex)
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
            DataType = CellValues.InlineString,
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

    private static string SanitizeForXml(string input)
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
    private static bool IsValidXmlChar(char c)
    {
        return c is '\x9' or '\xA' or '\xD' or
               (>= '\x20' and <= '\xD7FF') or
               (>= '\xE000' and <= '\xFFFD');
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string ValidateFormulaInjection(string? valueString)
    {
        if (string.IsNullOrEmpty(valueString))
        {
            return string.Empty;
        }

        var firstChar = valueString[0];
        return FormulaInjectionCharacters.Contains(firstChar) ? "'" + valueString : valueString;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsNumericType(Type type)
    {
        return TypeValidator.IsNumericType(type);
    }
}

#endregion

#region Style Manager

public sealed class ExcelStyleManager : IExcelStyleManager
{
    private readonly IExcelCacheManager _cacheManager;

    public ExcelStyleManager(IExcelCacheManager cacheManager)
    {
        _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
    }

    public Dictionary<string, uint> CreateStyles(WorkbookPart workbookPart, PropertyAccessor[] propertyAccessors, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workbookPart);
        ArgumentNullException.ThrowIfNull(propertyAccessors);

        var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
        var styleMappings = new Dictionary<string, uint>(StringComparer.Ordinal);

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

    private static void WriteNumberFormats(OpenXmlWriter writer)
    {
        writer.WriteStartElement(new NumberingFormats { Count = 2 });

        writer.WriteElement(new NumberingFormat
        {
            NumberFormatId = 164,
            FormatCode = ExcelWriterConstants.DateFormatCode,
        });

        writer.WriteElement(new NumberingFormat
        {
            NumberFormatId = 165,
            FormatCode = ExcelWriterConstants.TimeFormatCode,
        });

        writer.WriteEndElement();
    }

    private static void WriteFonts(OpenXmlWriter writer)
    {
        writer.WriteStartElement(new Fonts { Count = 2 });
        writer.WriteElement(new Font());
        writer.WriteElement(new Font(new Bold()));
        writer.WriteEndElement();
    }

    private HashSet<string> ExtractUniqueColors(PropertyAccessor[] propertyAccessors)
    {
        var uniqueColors = new HashSet<string>(StringComparer.Ordinal);

        foreach (var accessor in propertyAccessors)
        {
            if (string.IsNullOrEmpty(accessor.Color))
            {
                continue;
            }

            var cleanHex = _cacheManager.GetCleanHexColor(accessor.Color);
            if (cleanHex != null)
            {
                uniqueColors.Add(cleanHex);
            }
        }

        return uniqueColors;
    }

    private static void WriteFills(OpenXmlWriter writer, HashSet<string> uniqueColors, Dictionary<string, uint> styleMappings)
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

            styleMappings[$"{ExcelWriterConstants.FillHeaderStylePrefix}{cleanHex}"] = colorIndex++;

            fills.Add(new Fill(new PatternFill
            {
                PatternType = PatternValues.Solid,
                ForegroundColor = new ForegroundColor { Rgb = cleanHex },
            }));

            styleMappings[$"{ExcelWriterConstants.FillCellStylePrefix}{cleanHex}"] = colorIndex++;
        }

        writer.WriteStartElement(new Fills { Count = (uint)fills.Count });

        foreach (var fill in fills)
        {
            writer.WriteElement(fill);
        }

        writer.WriteEndElement();
    }

    private static void WriteBorders(OpenXmlWriter writer)
    {
        writer.WriteStartElement(new Borders { Count = 3 });

        writer.WriteElement(new Border());

        WriteBorderWithStyle(writer, "FFD0D0D0", BorderStyleValues.Thin, BorderStyleValues.Thin);
        WriteBorderWithStyle(writer, "FF808080", BorderStyleValues.Thin, BorderStyleValues.Medium);

        writer.WriteEndElement();
    }

    private static void WriteBorderWithStyle(OpenXmlWriter writer, string color, BorderStyleValues sideStyle, BorderStyleValues topBottomStyle)
    {
        writer.WriteStartElement(new Border());
        writer.WriteElement(new LeftBorder(new Color { Rgb = color }) { Style = sideStyle });
        writer.WriteElement(new RightBorder(new Color { Rgb = color }) { Style = sideStyle });
        writer.WriteElement(new TopBorder(new Color { Rgb = color }) { Style = topBottomStyle });
        writer.WriteElement(new BottomBorder(new Color { Rgb = color }) { Style = topBottomStyle });
        writer.WriteElement(new DiagonalBorder());
        writer.WriteEndElement();
    }

    private static void WriteCellFormats(OpenXmlWriter writer, HashSet<string> uniqueColors, Dictionary<string, uint> styleMappings)
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

    private static void AddColorFormats(List<CellFormat> cellFormats, Dictionary<string, uint> styleMappings, string cleanHex)
    {
        var fillHeaderKey = $"{ExcelWriterConstants.FillHeaderStylePrefix}{cleanHex}";
        var fillCellKey = $"{ExcelWriterConstants.FillCellStylePrefix}{cleanHex}";

        if (styleMappings.TryGetValue(fillHeaderKey, out var headerFillId))
        {
            cellFormats.Add(new CellFormat
            {
                FontId = 1,
                FillId = headerFillId,
                BorderId = 2,
                ApplyFont = true,
                ApplyFill = true,
                ApplyBorder = true,
            });
            styleMappings[$"{ExcelWriterConstants.HeaderStylePrefix}{cleanHex}"] = (uint)cellFormats.Count - 1;
        }

        if (styleMappings.TryGetValue(fillCellKey, out var cellFillId))
        {
            cellFormats.Add(new CellFormat
            {
                FontId = 0,
                FillId = cellFillId,
                BorderId = 1,
                ApplyFill = true,
                ApplyBorder = true,
            });
            styleMappings[$"{ExcelWriterConstants.CellStylePrefix}{cleanHex}"] = (uint)cellFormats.Count - 1;

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
            styleMappings[$"{ExcelWriterConstants.CellDateStylePrefix}{cleanHex}"] = (uint)cellFormats.Count - 1;

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
            styleMappings[$"{ExcelWriterConstants.CellTimeStylePrefix}{cleanHex}"] = (uint)cellFormats.Count - 1;
        }
    }
}

#endregion
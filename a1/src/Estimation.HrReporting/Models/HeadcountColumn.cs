using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;

namespace Estimation.HrReporting.Models;

public enum HeadcountValueKind
{
    Text,
    Date,
    Number
}

public enum HeadcountFilterGroup
{
    None,
    Agile,
    Employment,
    Organisation,
    Location,
    Job,
    Other
}

public sealed class HeadcountColumn
{
    public const string DateFormat = "yyyy-MM-dd";
    public const string NumberFormat = "0.####";

    private readonly Func<HeadcountRow, object?> _getter;
    private readonly Action<HeadcountRow, object?> _setter;

    internal HeadcountColumn(
        string key,
        string header,
        HeadcountValueKind kind,
        int maxLength,
        double width,
        bool visibleByDefault,
        HeadcountFilterGroup filterGroup)
    {
        Key = key;
        Header = header;
        Kind = kind;
        MaxLength = maxLength;
        Width = width;
        VisibleByDefault = visibleByDefault;
        FilterGroup = filterGroup;
        NormalizedHeader = NormalizeHeader(header);

        var property = typeof(HeadcountRow).GetProperty(key, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"HeadcountRow has no property '{key}'.");
        Property = property;

        var row = Expression.Parameter(typeof(HeadcountRow), "row");
        var value = Expression.Parameter(typeof(object), "value");
        var member = Expression.Property(row, property);

        _getter = Expression.Lambda<Func<HeadcountRow, object?>>(
            Expression.Convert(member, typeof(object)), row).Compile();
        _setter = Expression.Lambda<Action<HeadcountRow, object?>>(
            Expression.Assign(member, Expression.Convert(value, property.PropertyType)), row, value).Compile();
    }

    public string Key { get; }

    public string Header { get; }

    public HeadcountValueKind Kind { get; }

    public int MaxLength { get; }

    public double Width { get; }

    public bool VisibleByDefault { get; }

    public HeadcountFilterGroup FilterGroup { get; }

    public bool IsFilterable => FilterGroup != HeadcountFilterGroup.None;

    internal string NormalizedHeader { get; }

    internal PropertyInfo Property { get; }

    public object? GetValue(HeadcountRow row) => _getter(row);

    internal void SetValue(HeadcountRow row, object? value) => _setter(row, value);

    public string? Format(HeadcountRow? row) => row is null ? null : FormatValue(GetValue(row));

    public static string? FormatValue(object? value) => value switch
    {
        null => null,
        string text => text,
        DateTime date => date.ToString(DateFormat, CultureInfo.InvariantCulture),
        decimal number => number.ToString(NumberFormat, CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)
    };

    public override string ToString() => Header;

    internal static string NormalizeHeader(string? header) =>
        string.IsNullOrWhiteSpace(header)
            ? string.Empty
            : string.Concat(header.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
}

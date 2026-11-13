using System.Text.Json;
using System.Text.Json.Serialization;

namespace Estimation.HrReporting.Services;

public sealed class OrgHealthLayout
{
    public const string StorageKey = "hrReporting.orgHealth.layout";
    public const string TotalKey = "#total";

    private readonly HashSet<string> _kpisOn = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _kpisOff = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _hiddenColumns = new(StringComparer.OrdinalIgnoreCase);

    public bool IsDefaultKpiSet => _kpisOn.Count == 0 && _kpisOff.Count == 0;

    public bool IsShown(OrgHealthKpi kpi) =>
        kpi.ShownByDefault ? !_kpisOff.Contains(kpi.Key) : _kpisOn.Contains(kpi.Key);

    public void SetShown(OrgHealthKpi kpi, bool shown)
    {
        _kpisOn.Remove(kpi.Key);
        _kpisOff.Remove(kpi.Key);
        if (shown != kpi.ShownByDefault)
        {
            (shown ? _kpisOn : _kpisOff).Add(kpi.Key);
        }
    }

    public void ShowAll(IEnumerable<OrgHealthKpi> kpis)
    {
        foreach (var kpi in kpis)
        {
            SetShown(kpi, true);
        }
    }

    public void ResetKpis()
    {
        _kpisOn.Clear();
        _kpisOff.Clear();
    }

    public bool IsColumnShown(string? domain) => !_hiddenColumns.Contains(ColumnKey(domain));

    public void SetColumnShown(string? domain, bool shown)
    {
        if (shown)
        {
            _hiddenColumns.Remove(ColumnKey(domain));
        }
        else
        {
            _hiddenColumns.Add(ColumnKey(domain));
        }
    }

    public string Serialize() => JsonSerializer.Serialize(
        new Stored(
            [.. _kpisOn.Order(StringComparer.OrdinalIgnoreCase)],
            [.. _kpisOff.Order(StringComparer.OrdinalIgnoreCase)],
            [.. _hiddenColumns.Order(StringComparer.OrdinalIgnoreCase)]));

    public static OrgHealthLayout Parse(string? json)
    {
        var layout = new OrgHealthLayout();
        if (string.IsNullOrWhiteSpace(json))
        {
            return layout;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<Stored>(json);
            layout._kpisOn.UnionWith(Clean(stored?.KpisOn));
            layout._kpisOff.UnionWith(Clean(stored?.KpisOff));
            layout._hiddenColumns.UnionWith(Clean(stored?.HiddenColumns).Select(c => c.Trim()));
        }
        catch (JsonException)
        {
        }

        return layout;
    }

    private static string ColumnKey(string? domain) => domain?.Trim() ?? TotalKey;

    private static IEnumerable<string> Clean(IEnumerable<string?>? values) =>
        values?.Where(v => v is not null).Select(v => v!) ?? [];

    private sealed record Stored(
        [property: JsonPropertyName("kpisOn")] List<string?>? KpisOn,
        [property: JsonPropertyName("kpisOff")] List<string?>? KpisOff,
        [property: JsonPropertyName("hiddenColumns")] List<string?>? HiddenColumns);
}

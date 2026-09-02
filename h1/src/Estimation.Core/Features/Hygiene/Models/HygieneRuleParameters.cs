using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Estimation.Core.Features.Hygiene.Models;

public enum HygieneWordMode
{
    /// <summary>Every phrase must be present.</summary>
    And,

    /// <summary>At least one phrase must be present.</summary>
    Or
}

/// <summary>
/// The parameters of a rule. Which members matter depends on the check: phrases and mode for
/// ContainsWords, phrases and a minimum for NotOnlyWords, a number or a date for the range checks,
/// values for InValues and NotInValues. Serialised to <see cref="FeatureHygieneRule.ParametersJson"/>.
/// </summary>
public sealed class HygieneRuleParameters
{
    /// <summary>The value a user picks in a value list to mean "the field is empty".</summary>
    public const string EmptyValue = "(empty)";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public List<string> Words { get; set; } = [];

    public HygieneWordMode Mode { get; set; } = HygieneWordMode.And;

    public int MinOtherWords { get; set; } = 1;

    public decimal? Number { get; set; }

    public DateOnly? Date { get; set; }

    public List<string> Values { get; set; } = [];

    [JsonIgnore]
    public IReadOnlyList<string> CleanWords => Clean(Words);

    [JsonIgnore]
    public IReadOnlyList<string> CleanValues => Clean(Values);

    public static HygieneRuleParameters Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new HygieneRuleParameters();
        }

        try
        {
            return JsonSerializer.Deserialize<HygieneRuleParameters>(json, JsonOptions) ?? new HygieneRuleParameters();
        }
        catch (JsonException)
        {
            return new HygieneRuleParameters();
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public HygieneRuleParameters Clone() => Parse(ToJson());

    /// <summary>A copy holding only what the given check reads, with phrases and values tidied.</summary>
    public HygieneRuleParameters ForCheck(HygieneCheck check, HygieneFieldKind kind)
    {
        var result = new HygieneRuleParameters();

        switch (check)
        {
            case HygieneCheck.ContainsWords:
                result.Words = CleanWords.ToList();
                result.Mode = Mode;
                break;
            case HygieneCheck.NotOnlyWords:
                result.Words = CleanWords.ToList();
                result.MinOtherWords = Math.Max(1, MinOtherWords);
                break;
            case HygieneCheck.NotGreaterThan:
            case HygieneCheck.NotLessThan:
                if (kind == HygieneFieldKind.Date)
                {
                    result.Date = Date;
                }
                else
                {
                    result.Number = Number;
                }
                break;
            case HygieneCheck.InValues:
            case HygieneCheck.NotInValues:
                result.Values = CleanValues.ToList();
                break;
        }

        return result;
    }

    public static List<string> SplitPhrases(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : Clean(text.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries)).ToList();

    public static string JoinPhrases(IEnumerable<string> phrases) => string.Join(", ", phrases);

    public static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string FormatNumber(decimal number) => number.ToString("0.####", CultureInfo.InvariantCulture);

    private static IReadOnlyList<string> Clean(IEnumerable<string>? items) =>
        (items ?? [])
            .Where(i => i is not null)
            .Select(i => i.Trim())
            .Where(i => i.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

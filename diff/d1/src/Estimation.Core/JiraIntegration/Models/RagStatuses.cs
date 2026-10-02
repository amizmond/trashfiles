namespace Estimation.Core.JiraIntegration.Models;

public sealed record RagStatusOption(string Value, string Label);

public static class RagStatuses
{
    public const string Green = "Green";
    public const string Amber = "Amber";
    public const string Red = "Red";

    private static readonly string[] Colours = [Green, Amber, Red];

    public static readonly IReadOnlyList<string> All = Colours;

    public static readonly IReadOnlyList<RagStatusOption> Options =
    [
        new(Green, "Green – Everything going to plan"),
        new(Amber, "Amber – Still going to plan but issues"),
        new(Red, "Red – Issues have affected the plan for this request")
    ];

    public static string? Normalize(string? text)
    {
        var trimmed = text?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return Colour(trimmed) ?? trimmed;
    }

    public static bool IsKnown(string? value) => Order(value) < Colours.Length;

    public static bool Same(string? a, string? b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    public static string Label(string? value)
    {
        var normalized = Normalize(value);
        return Options.FirstOrDefault(o => o.Value == normalized)?.Label ?? normalized ?? string.Empty;
    }

    public static int Order(string? value)
    {
        var normalized = Normalize(value);

        if (normalized is null)
        {
            return Colours.Length + 1;
        }

        var index = Array.IndexOf(Colours, normalized);
        return index < 0 ? Colours.Length : index;
    }

    public static IReadOnlyList<string> Choices(IEnumerable<string?> present)
    {
        var others = present
            .Where(v => !IsKnown(v))
            .Select(Normalize)
            .Where(v => v is not null)
            .Select(v => v!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase);

        return Colours.Concat(others).ToList();
    }

    private static string? Colour(string text)
    {
        var length = 0;

        while (length < text.Length && char.IsLetter(text[length]))
        {
            length++;
        }

        var firstWord = text[..length];
        return Colours.FirstOrDefault(c => c.Equals(firstWord, StringComparison.OrdinalIgnoreCase));
    }
}

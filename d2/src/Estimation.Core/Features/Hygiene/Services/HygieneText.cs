using System.Text.RegularExpressions;

namespace Estimation.Core.Features.Hygiene.Services;

public static partial class HygieneText
{
    private const RegexOptions Common = RegexOptions.Compiled | RegexOptions.CultureInvariant;

    private static readonly Regex Monospace = new(@"\{\{(.*?)\}\}", Common);

    private static readonly Regex Macro = new(@"\{[A-Za-z][A-Za-z0-9-]*(?::(?<args>[^{}\r\n]*))?\}", Common);

    private static readonly Regex MacroTitle =
        new(@"(?:^|\|)\s*title\s*=(?<title>[^|]*)", Common | RegexOptions.IgnoreCase);

    private static readonly Regex Image = new(@"![^!\r\n]+!", Common);

    private static readonly Regex LinkWithText = new(@"\[([^\]|]*)\|[^\]]*\]", Common);
    private static readonly Regex BareLink = new(@"\[([^\]]*)\]", Common);

    private static readonly Regex Heading = new(@"^[ \t]*h[1-6]\.[ \t]*", Common | RegexOptions.Multiline);

    private static readonly Regex ListMarker = new(@"^[ \t]*(?:[*\-#]+|\d+\.)[ \t]+", Common | RegexOptions.Multiline);

    private static readonly Regex TablePipe = new(@"\|{1,2}", Common);

    private static readonly Regex LeadingEmphasis = new(@"(?<![\p{L}\p{N}])[*_+^~]+(?=[\p{L}\p{N}])", Common);
    private static readonly Regex TrailingEmphasis = new(@"(?<=[\p{L}\p{N}])[*_+^~]+(?![\p{L}\p{N}])", Common);

    private static readonly Regex HeadingLine = new(@"^[ \t]*h[1-6]\..*$", Common | RegexOptions.Multiline);

    private static readonly Regex EmphasisOnlyLine =
        new(@"^[ \t]*([*_+^~])[^*_+^~\r\n]*\1[ \t]*$", Common | RegexOptions.Multiline);

    private static readonly Regex Whitespace = new(@"\s+", Common);

    private static readonly Regex LetterOrDigit = new(@"[\p{L}\p{N}]", Common);

    public static string Normalize(string? text) => Normalize(text, keepMacroTitles: true);

    private static string Normalize(string? text, bool keepMacroTitles)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var result = text.Replace("\r\n", "\n");
        result = Monospace.Replace(result, "$1");
        result = keepMacroTitles ? Macro.Replace(result, MacroText) : Macro.Replace(result, " ");
        result = Image.Replace(result, " ");
        result = LinkWithText.Replace(result, "$1");
        result = BareLink.Replace(result, "$1");
        result = Heading.Replace(result, string.Empty);
        result = ListMarker.Replace(result, string.Empty);
        result = TablePipe.Replace(result, " ");
        result = LeadingEmphasis.Replace(result, string.Empty);
        result = TrailingEmphasis.Replace(result, string.Empty);
        result = Whitespace.Replace(result, " ");

        return result.Trim();
    }

    public static bool HasContent(string? text) => LetterOrDigit.IsMatch(Body(text));

    public static bool HasAnyText(string? text) => LetterOrDigit.IsMatch(Normalize(text));

    internal static bool HasLetterOrDigit(string text) => LetterOrDigit.IsMatch(text);

    public static string Body(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var result = text.Replace("\r\n", "\n");
        result = HeadingLine.Replace(result, string.Empty);
        result = EmphasisOnlyLine.Replace(result, string.Empty);

        return Normalize(result, keepMacroTitles: false);
    }

    public static bool ContainsPhrase(string normalizedText, string phrase, bool caseSensitive = false)
    {
        var regex = PhraseRegex(phrase, caseSensitive);
        return regex is not null && regex.IsMatch(normalizedText);
    }

    public static IReadOnlyList<string> MissingPhrases(string normalizedText, IEnumerable<string> phrases, bool caseSensitive = false) =>
        phrases.Where(p => !ContainsPhrase(normalizedText, p, caseSensitive)).ToList();

    public static IReadOnlyList<string> PresentPhrases(string normalizedText, IEnumerable<string> phrases, bool caseSensitive = false) =>
        phrases.Where(p => ContainsPhrase(normalizedText, p, caseSensitive)).ToList();

    public static int CountOtherWords(string normalizedText, IEnumerable<string> phrases, bool caseSensitive = false)
    {
        var remaining = normalizedText;

        foreach (var phrase in phrases)
        {
            var regex = PhraseRegex(phrase, caseSensitive);

            if (regex is not null)
            {
                remaining = regex.Replace(remaining, " ");
            }
        }

        return CountWords(remaining);
    }

    internal static IReadOnlyList<string> MissingPhrases(string normalizedText, IReadOnlyList<string> phrases, IReadOnlyList<Regex?> regexes)
    {
        var missing = new List<string>();

        for (var i = 0; i < phrases.Count; i++)
        {
            if (regexes[i] is not { } regex || !regex.IsMatch(normalizedText))
            {
                missing.Add(phrases[i]);
            }
        }

        return missing;
    }

    internal static int CountOtherWords(string normalizedText, IReadOnlyList<Regex?> regexes)
    {
        var remaining = normalizedText;

        foreach (var regex in regexes)
        {
            if (regex is not null)
            {
                remaining = regex.Replace(remaining, " ");
            }
        }

        return CountWords(remaining);
    }

    public static int CountWords(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? 0
            : Whitespace.Split(text).Count(token => LetterOrDigit.IsMatch(token));

    public static string? Excerpt(string? text, int maxLength = 160) => ExcerptOfNormalized(Normalize(text), maxLength);

    internal static string? ExcerptOfNormalized(string normalized, int maxLength = 160)
    {
        if (normalized.Length == 0)
        {
            return null;
        }

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength].TrimEnd() + "…";
    }

    private static string MacroText(Match macro)
    {
        var arguments = macro.Groups["args"];

        if (!arguments.Success)
        {
            return " ";
        }

        var title = MacroTitle.Match(arguments.Value);

        return title.Success ? $" {title.Groups["title"].Value} " : " ";
    }

    internal static Regex? PhraseRegex(string phrase, bool caseSensitive = false)
    {
        var words = Whitespace.Split(phrase.Trim()).Where(w => w.Length > 0).ToList();

        if (words.Count == 0)
        {
            return null;
        }

        var options = RegexOptions.CultureInvariant | (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
        var pattern = @"(?<![\p{L}\p{N}])" + string.Join(@"\s+", words.Select(Regex.Escape)) + @"(?![\p{L}\p{N}])";
        return new Regex(pattern, options);
    }
}

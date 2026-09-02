using System.Text.RegularExpressions;

namespace Estimation.Core.Features.Hygiene.Services;

/// <summary>
/// Text handling for the text checks. Feature descriptions arrive from Jira Server as wiki markup,
/// so headings, macros, emphasis markers, links and table pipes are removed before anything is
/// matched or counted. A word is a whitespace-separated token with at least one letter or digit.
/// </summary>
public static partial class HygieneText
{
    private const RegexOptions Common = RegexOptions.Compiled | RegexOptions.CultureInvariant;

    // {{monospace}} keeps its text.
    private static readonly Regex Monospace = new(@"\{\{(.*?)\}\}", Common);

    // {panel:title=Result}, {code:java}, {color:#ff0000}, {noformat}, {quote}, {anchor:x}, {toc} and their closers.
    private static readonly Regex Macro = new(@"\{[A-Za-z][A-Za-z0-9-]*(?::[^{}\r\n]*)?\}", Common);

    // !image.png|thumbnail!
    private static readonly Regex Image = new(@"![^!\r\n]+!", Common);

    // [text|http://...] and [http://...]
    private static readonly Regex LinkWithText = new(@"\[([^\]|]*)\|[^\]]*\]", Common);
    private static readonly Regex BareLink = new(@"\[([^\]]*)\]", Common);

    // h1. to h6. at the start of a line.
    private static readonly Regex Heading = new(@"^[ \t]*h[1-6]\.[ \t]*", Common | RegexOptions.Multiline);

    // * bullet, - bullet, # numbered, ** nested, 1. numbered
    private static readonly Regex ListMarker = new(@"^[ \t]*(?:[*\-#]+|\d+\.)[ \t]+", Common | RegexOptions.Multiline);

    // ||header|| and |cell|
    private static readonly Regex TablePipe = new(@"\|{1,2}", Common);

    // *bold*, _italic_, +underline+, ^super^, ~sub~ : the markers, not the words.
    private static readonly Regex LeadingEmphasis = new(@"(?<![\p{L}\p{N}])[*_+^~]+(?=[\p{L}\p{N}])", Common);
    private static readonly Regex TrailingEmphasis = new(@"(?<=[\p{L}\p{N}])[*_+^~]+(?![\p{L}\p{N}])", Common);

    private static readonly Regex Whitespace = new(@"\s+", Common);

    private static readonly Regex LetterOrDigit = new(@"[\p{L}\p{N}]", Common);

    /// <summary>Plain text: markup removed, whitespace collapsed, trimmed. Empty for null.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var result = text.Replace("\r\n", "\n");
        result = Monospace.Replace(result, "$1");
        result = Macro.Replace(result, " ");
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

    /// <summary>True when the text still says something once markup and symbols are set aside.</summary>
    public static bool HasContent(string? text) => LetterOrDigit.IsMatch(Normalize(text));

    /// <summary>Whole-word, case-insensitive phrase match on normalised text.</summary>
    public static bool ContainsPhrase(string normalizedText, string phrase)
    {
        var regex = PhraseRegex(phrase);
        return regex is not null && regex.IsMatch(normalizedText);
    }

    public static IReadOnlyList<string> MissingPhrases(string normalizedText, IEnumerable<string> phrases) =>
        phrases.Where(p => !ContainsPhrase(normalizedText, p)).ToList();

    public static IReadOnlyList<string> PresentPhrases(string normalizedText, IEnumerable<string> phrases) =>
        phrases.Where(p => ContainsPhrase(normalizedText, p)).ToList();

    /// <summary>How many words remain once the phrases are taken out.</summary>
    public static int CountOtherWords(string normalizedText, IEnumerable<string> phrases)
    {
        var remaining = normalizedText;

        foreach (var phrase in phrases)
        {
            var regex = PhraseRegex(phrase);

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

    /// <summary>A short excerpt for showing an actual value next to a failure.</summary>
    public static string? Excerpt(string? text, int maxLength = 160)
    {
        var normalized = Normalize(text);

        if (normalized.Length == 0)
        {
            return null;
        }

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength].TrimEnd() + "…";
    }

    private static Regex? PhraseRegex(string phrase)
    {
        var words = Whitespace.Split(phrase.Trim()).Where(w => w.Length > 0).ToList();

        if (words.Count == 0)
        {
            return null;
        }

        var pattern = @"(?<![\p{L}\p{N}])" + string.Join(@"\s+", words.Select(Regex.Escape)) + @"(?![\p{L}\p{N}])";
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}

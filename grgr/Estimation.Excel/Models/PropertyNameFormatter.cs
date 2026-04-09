using System.Text.RegularExpressions;

namespace Estimation.Excel.Models;

public interface IPropertyNameFormatter
{
    string ConvertToFriendlyName(string name);
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

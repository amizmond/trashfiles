using System.Globalization;
using System.Text;
using Estimation.Core.Resources.Models;

namespace Estimation.Core.JiraIntegration.Client;

public static class JiraPersonMatcher
{
    public static HumanResource? FindAssignee(JiraIssueResponse issue, IReadOnlyCollection<HumanResource> people)
    {
        var login = Login(issue.AssigneeUserName);
        var nameKey = NameKey(issue.AssigneeDisplayName);
        return OnlyOne(people, p => SameText(Login(p.AccountName), login))
               ?? OnlyOne(people, p => SameText(p.Email, issue.AssigneeEmail))
               ?? OnlyOne(people, p => nameKey is not null && NameKey(p.FullName) == nameKey);
    }

    private static HumanResource? OnlyOne(IReadOnlyCollection<HumanResource> people, Func<HumanResource, bool> match)
    {
        var found = people.Where(match).Take(2).ToList();
        return found.Count == 1 ? found[0] : null;
    }

    private static string? Login(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed[(trimmed.LastIndexOf('\\') + 1)..];
    }

    private static bool SameText(string? left, string? right)
    {
        return !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right)
               && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string? NameKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var withoutAccents = new string(name.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray());
        var words = withoutAccents
            .Replace(',', ' ')
            .ToUpperInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Order(StringComparer.Ordinal);
        return string.Join(' ', words);
    }
}

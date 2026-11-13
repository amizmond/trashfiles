namespace Estimation.HrReporting.Services;

public static class HeadcountRules
{
    public const string Operational = "Operational";

    public const string Art = "ART";

    public const string Engineering = "Engineering";

    public const string FixedPriceType = "Resource Augmentation (Fixed Price Non Headcount)";

    public const string India = "India";

    public const int SmallTeamLimit = 8;

    public const int LargeTeamLimit = 12;

    public static readonly IReadOnlyList<string> PermTypes = ["Apprentice", "Graduate", "Intern", "Permanent/Regular"];

    public static readonly IReadOnlyList<string> EmployeeTypes = [.. PermTypes, "Temp/Contractor Payroll (Fixed Term)"];

    public static readonly IReadOnlyList<string> NoTrainValues = ["N/A", "#N/A"];

    public static readonly IReadOnlyList<string> UsaNames = ["USA", "United States", "United States of America", "US"];

    public static bool IsOneOf(string? value, IEnumerable<string> values) =>
        value is not null && values.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);
}

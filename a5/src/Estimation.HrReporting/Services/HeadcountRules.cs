using Estimation.HrReporting.Models;

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

    public static readonly AgileRole LeanPortfolioManager = new(["LPM", "Lean Portfolio Manager"], "Lean Portfolio Manager");

    public static readonly AgileRole SolutionTrainEngineer = new(["STE", "Solution Train Engineer"], "Solution Train Engineer");

    public static readonly AgileRole ReleaseTrainEngineer = new(["RTE", "Release Train Engineer"], "Release Train Engineer");

    public static readonly AgileRole ScrumMaster = new(["Scrum Master"], "Scrum Master");

    private static readonly char[] RoleSeparators = [',', ';'];

    public static bool IsOneOf(string? value, IEnumerable<string> values) =>
        value is not null && values.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<AgileRoleName> AgileRoles(HeadcountRow row)
    {
        if (string.IsNullOrWhiteSpace(row.TargetAgileRole))
        {
            return string.IsNullOrWhiteSpace(row.Job) ? [] : [AgileRoleName.Parse(row.Job)];
        }

        return row.TargetAgileRole
            .Split(RoleSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(AgileRoleName.Parse)
            .Where(r => r.Name.Length > 0)
            .ToList();
    }

    public static bool IsFullTimeScrumMaster(IReadOnlyList<AgileRoleName> roles) =>
        roles.Count == 1
        && ScrumMaster.Matches(roles)
        && (roles[0].Qualifier is null || !roles[0].Qualifier!.Contains("part", StringComparison.OrdinalIgnoreCase));
}

public sealed record AgileRole(IReadOnlyList<string> Names, string WorkdayJob)
{
    public bool Matches(IReadOnlyList<AgileRoleName> roles) =>
        roles.Any(r => Names.Contains(r.Name, StringComparer.OrdinalIgnoreCase));

    public bool IsWorkdayJob(HeadcountRow row) =>
        string.Equals(row.Job?.Trim(), WorkdayJob, StringComparison.OrdinalIgnoreCase);
}

public readonly record struct AgileRoleName(string Name, string? Qualifier)
{
    public static AgileRoleName Parse(string text)
    {
        var value = text.Trim();
        var open = value.IndexOf('(');
        if (open < 0)
        {
            return new AgileRoleName(value, null);
        }

        var close = value.IndexOf(')', open + 1);
        var qualifier = (close < 0 ? value[(open + 1)..] : value[(open + 1)..close]).Trim();
        return new AgileRoleName(value[..open].Trim(), qualifier.Length == 0 ? null : qualifier);
    }
}

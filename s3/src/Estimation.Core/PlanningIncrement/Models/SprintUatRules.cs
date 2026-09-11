namespace Estimation.Core.PlanningIncrement.Models;

public static class SprintUatRules
{
    public const int FixVersionMaxLength = 50;

    public static List<string> Validate(DateTime sprintEnd, DateTime uatStart, DateTime uatEnd, string? fixVersion)
    {
        var errors = new List<string>();

        if (uatStart == default || uatEnd == default)
        {
            errors.Add("UAT start and UAT end are required.");
        }
        else
        {
            if (uatEnd.Date < uatStart.Date)
            {
                errors.Add("UAT end must be on or after UAT start.");
            }

            if (uatEnd.Date < sprintEnd.Date)
            {
                errors.Add("UAT cannot end before the sprint does.");
            }
        }

        if (fixVersion is not null && fixVersion.Trim().Length > FixVersionMaxLength)
        {
            errors.Add($"Fix version cannot be longer than {FixVersionMaxLength} characters.");
        }

        return errors;
    }
}

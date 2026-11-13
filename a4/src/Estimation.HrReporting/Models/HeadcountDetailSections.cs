namespace Estimation.HrReporting.Models;

public sealed record HeadcountDetailSection(string Title, IReadOnlyList<HeadcountColumn> Columns);

public static class HeadcountDetailSections
{
    public static readonly IReadOnlyList<HeadcountDetailSection> All =
    [
        new("Personal details",
        [
            HeadcountColumns.Brid,
            HeadcountColumns.EmployeeName,
            HeadcountColumns.EmployeeEmailAddress,
            HeadcountColumns.EmployeePositionId,
            HeadcountColumns.OperationalStatus,
            HeadcountColumns.ColleagueNonPermType,
            HeadcountColumns.CorporateGradeDescription,
            HeadcountColumns.Vendor,
            HeadcountColumns.LengthOfServiceDate,
            HeadcountColumns.LengthOfServiceInYears,
            HeadcountColumns.Hc,
            HeadcountColumns.Fte
        ]),
        new("Agile",
        [
            HeadcountColumns.Train,
            HeadcountColumns.Team,
            HeadcountColumns.ArtNonArt,
            HeadcountColumns.EngNonEng,
            HeadcountColumns.TargetAgileRole,
            HeadcountColumns.TargetLevel
        ]),
        new("Organisation",
        [
            HeadcountColumns.LineManagerName,
            HeadcountColumns.LineManagerBrid,
            HeadcountColumns.LineManagerEmailAddress,
            HeadcountColumns.ImhL07,
            HeadcountColumns.OrgUnitId,
            HeadcountColumns.CostCentre,
            HeadcountColumns.CostCentreName,
            HeadcountColumns.ControlTier,
            HeadcountColumns.ControlSpan
        ]),
        new("Location",
        [
            HeadcountColumns.Country,
            HeadcountColumns.City,
            HeadcountColumns.Building
        ]),
        new("Job",
        [
            HeadcountColumns.JobFamilyGroup,
            HeadcountColumns.JobFamilyDescription,
            HeadcountColumns.Job,
            HeadcountColumns.JobProfile,
            HeadcountColumns.JobDescription,
            HeadcountColumns.PositionDescription
        ]),
        new("Other",
        [
            HeadcountColumns.Actions
        ])
    ];

    public static readonly IReadOnlySet<HeadcountColumn> Wide = new HashSet<HeadcountColumn>
    {
        HeadcountColumns.JobDescription,
        HeadcountColumns.PositionDescription,
        HeadcountColumns.Actions
    };
}

namespace Estimation.HrReporting.Models;

public static class HeadcountColumns
{
    public static readonly HeadcountColumn Brid = Text(nameof(HeadcountRow.Brid), "BRID", 50, 14, true, HeadcountFilterGroup.None);
    public static readonly HeadcountColumn EmployeeName = Text(nameof(HeadcountRow.EmployeeName), "Employee Name", 200, 28, true, HeadcountFilterGroup.None);
    public static readonly HeadcountColumn OperationalStatus = Text(nameof(HeadcountRow.OperationalStatus), "Operational Status", 100, 18, true, HeadcountFilterGroup.Employment);
    public static readonly HeadcountColumn CorporateGradeDescription = Text(nameof(HeadcountRow.CorporateGradeDescription), "Corporate Grade Description", 100, 16, true, HeadcountFilterGroup.Employment);
    public static readonly HeadcountColumn ColleagueNonPermType = Text(nameof(HeadcountRow.ColleagueNonPermType), "Colleague / Non-Perm Type", 100, 22, true, HeadcountFilterGroup.Employment);
    public static readonly HeadcountColumn EmployeeEmailAddress = Text(nameof(HeadcountRow.EmployeeEmailAddress), "Employee Email Address", 320, 32, false, HeadcountFilterGroup.None);
    public static readonly HeadcountColumn EmployeePositionId = Text(nameof(HeadcountRow.EmployeePositionId), "Employee Position ID", 50, 16, false, HeadcountFilterGroup.None);
    public static readonly HeadcountColumn PositionDescription = Text(nameof(HeadcountRow.PositionDescription), "Position Description", 400, 32, false, HeadcountFilterGroup.Job);
    public static readonly HeadcountColumn JobDescription = Text(nameof(HeadcountRow.JobDescription), "Job Description", 400, 32, false, HeadcountFilterGroup.Job);
    public static readonly HeadcountColumn LengthOfServiceDate = new(nameof(HeadcountRow.LengthOfServiceDate), "Length of Service Date", HeadcountValueKind.Date, 0, 14, false, HeadcountFilterGroup.None);
    public static readonly HeadcountColumn CostCentre = Text(nameof(HeadcountRow.CostCentre), "Cost Centre", 50, 14, false, HeadcountFilterGroup.Organisation);
    public static readonly HeadcountColumn CostCentreName = Text(nameof(HeadcountRow.CostCentreName), "Cost Centre Name", 200, 28, false, HeadcountFilterGroup.Organisation);
    public static readonly HeadcountColumn Vendor = Text(nameof(HeadcountRow.Vendor), "Vendor", 200, 20, false, HeadcountFilterGroup.Employment);
    public static readonly HeadcountColumn LineManagerBrid = Text(nameof(HeadcountRow.LineManagerBrid), "Line Manager BRID", 50, 14, false, HeadcountFilterGroup.None);
    public static readonly HeadcountColumn LineManagerName = Text(nameof(HeadcountRow.LineManagerName), "Line Manager Name", 200, 26, true, HeadcountFilterGroup.Organisation);
    public static readonly HeadcountColumn LineManagerEmailAddress = Text(nameof(HeadcountRow.LineManagerEmailAddress), "Line Manager Email Address", 320, 32, false, HeadcountFilterGroup.None);
    public static readonly HeadcountColumn ImhL07 = Text(nameof(HeadcountRow.ImhL07), "IMH L07", 200, 22, false, HeadcountFilterGroup.Organisation);
    public static readonly HeadcountColumn OrgUnitId = Text(nameof(HeadcountRow.OrgUnitId), "Org Unit ID", 50, 14, false, HeadcountFilterGroup.Organisation);
    public static readonly HeadcountColumn Country = Text(nameof(HeadcountRow.Country), "Country", 100, 16, true, HeadcountFilterGroup.Location);
    public static readonly HeadcountColumn City = Text(nameof(HeadcountRow.City), "City", 100, 16, true, HeadcountFilterGroup.Location);
    public static readonly HeadcountColumn Building = Text(nameof(HeadcountRow.Building), "Building", 200, 22, false, HeadcountFilterGroup.Location);
    public static readonly HeadcountColumn LengthOfServiceInYears = Number(nameof(HeadcountRow.LengthOfServiceInYears), "Length of Service In Years", 12, false, HeadcountFilterGroup.Employment);
    public static readonly HeadcountColumn ControlSpan = Text(nameof(HeadcountRow.ControlSpan), "ControlSpan", 50, 12, false, HeadcountFilterGroup.Organisation);
    public static readonly HeadcountColumn ControlTier = Text(nameof(HeadcountRow.ControlTier), "ControlTier", 50, 12, false, HeadcountFilterGroup.Organisation);
    public static readonly HeadcountColumn JobFamilyGroup = Text(nameof(HeadcountRow.JobFamilyGroup), "Job Family Group", 200, 20, false, HeadcountFilterGroup.Job);
    public static readonly HeadcountColumn JobFamilyDescription = Text(nameof(HeadcountRow.JobFamilyDescription), "Job Family Description", 200, 24, false, HeadcountFilterGroup.Job);
    public static readonly HeadcountColumn Job = Text(nameof(HeadcountRow.Job), "Job", 200, 22, true, HeadcountFilterGroup.Job);
    public static readonly HeadcountColumn JobProfile = Text(nameof(HeadcountRow.JobProfile), "Job Profile", 300, 28, false, HeadcountFilterGroup.Job);
    public static readonly HeadcountColumn Hc = Number(nameof(HeadcountRow.Hc), "HC", 8, true, HeadcountFilterGroup.Employment);
    public static readonly HeadcountColumn Fte = Number(nameof(HeadcountRow.Fte), "FTE", 8, true, HeadcountFilterGroup.Employment);
    public static readonly HeadcountColumn Team = Text(nameof(HeadcountRow.Team), "Team", 200, 22, true, HeadcountFilterGroup.Agile);
    public static readonly HeadcountColumn Train = Text(nameof(HeadcountRow.Train), "Train", 200, 22, true, HeadcountFilterGroup.Agile);
    public static readonly HeadcountColumn ArtNonArt = Text(nameof(HeadcountRow.ArtNonArt), "ART / NonART", 50, 14, true, HeadcountFilterGroup.Agile);
    public static readonly HeadcountColumn EngNonEng = Text(nameof(HeadcountRow.EngNonEng), "Eng / NonEng", 50, 14, false, HeadcountFilterGroup.Agile);
    public static readonly HeadcountColumn TargetAgileRole = Text(nameof(HeadcountRow.TargetAgileRole), "Target Agile Role", 200, 24, false, HeadcountFilterGroup.Agile);
    public static readonly HeadcountColumn TargetLevel = Text(nameof(HeadcountRow.TargetLevel), "Target Level", 100, 16, false, HeadcountFilterGroup.Agile);
    public static readonly HeadcountColumn Actions = Text(nameof(HeadcountRow.Actions), "Actions", 4000, 40, false, HeadcountFilterGroup.Other);

    public static readonly IReadOnlyList<HeadcountColumn> All =
    [
        Brid,
        EmployeeName,
        OperationalStatus,
        CorporateGradeDescription,
        ColleagueNonPermType,
        EmployeeEmailAddress,
        EmployeePositionId,
        PositionDescription,
        JobDescription,
        LengthOfServiceDate,
        CostCentre,
        CostCentreName,
        Vendor,
        LineManagerBrid,
        LineManagerName,
        LineManagerEmailAddress,
        ImhL07,
        OrgUnitId,
        Country,
        City,
        Building,
        LengthOfServiceInYears,
        ControlSpan,
        ControlTier,
        JobFamilyGroup,
        JobFamilyDescription,
        Job,
        JobProfile,
        Hc,
        Fte,
        Team,
        Train,
        ArtNonArt,
        EngNonEng,
        TargetAgileRole,
        TargetLevel,
        Actions
    ];

    public static readonly IReadOnlyList<HeadcountColumn> Comparable = All.Where(c => c != Brid).ToList();

    public static readonly IReadOnlyList<HeadcountColumn> IgnoredByDefault = [LengthOfServiceInYears];

    public static readonly IReadOnlyList<HeadcountColumn> SearchColumns =
        [Brid, EmployeeName, EmployeeEmailAddress, EmployeePositionId];

    public static readonly IReadOnlyList<HeadcountFilterGroup> FilterGroupOrder =
    [
        HeadcountFilterGroup.Agile,
        HeadcountFilterGroup.Employment,
        HeadcountFilterGroup.Organisation,
        HeadcountFilterGroup.Location,
        HeadcountFilterGroup.Job,
        HeadcountFilterGroup.Other
    ];

    public static readonly IReadOnlyList<HeadcountColumn> Filterable = FilterGroupOrder
        .SelectMany(group => FilterOrder.Where(c => c.FilterGroup == group))
        .ToList();

    private static IEnumerable<HeadcountColumn> FilterOrder =>
    [
        Train,
        Team,
        ArtNonArt,
        EngNonEng,
        TargetAgileRole,
        TargetLevel,
        OperationalStatus,
        ColleagueNonPermType,
        CorporateGradeDescription,
        Vendor,
        Hc,
        Fte,
        LengthOfServiceInYears,
        ImhL07,
        OrgUnitId,
        CostCentre,
        CostCentreName,
        LineManagerName,
        ControlTier,
        ControlSpan,
        Country,
        City,
        Building,
        JobFamilyGroup,
        JobFamilyDescription,
        Job,
        JobProfile,
        JobDescription,
        PositionDescription,
        Actions
    ];

    private static readonly Dictionary<string, HeadcountColumn> ByKeyIndex =
        All.ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);

    public static HeadcountColumn? ByKey(string? key) =>
        key is not null && ByKeyIndex.TryGetValue(key, out var column) ? column : null;

    public static string GroupTitle(HeadcountFilterGroup group) => group switch
    {
        HeadcountFilterGroup.Agile => "Agile",
        HeadcountFilterGroup.Employment => "Employment",
        HeadcountFilterGroup.Organisation => "Organisation",
        HeadcountFilterGroup.Location => "Location",
        HeadcountFilterGroup.Job => "Job",
        HeadcountFilterGroup.Other => "Other",
        _ => string.Empty
    };

    private static HeadcountColumn Text(string key, string header, int maxLength, double width, bool visible, HeadcountFilterGroup group) =>
        new(key, header, HeadcountValueKind.Text, maxLength, width, visible, group);

    private static HeadcountColumn Number(string key, string header, double width, bool visible, HeadcountFilterGroup group) =>
        new(key, header, HeadcountValueKind.Number, 0, width, visible, group);
}

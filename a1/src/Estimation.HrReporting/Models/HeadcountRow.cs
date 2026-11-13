namespace Estimation.HrReporting.Models;

public class HeadcountRow
{
    public int Id { get; set; }

    public int UploadId { get; set; }

    public int RowNumber { get; set; }

    public string Brid { get; set; } = string.Empty;

    public string? EmployeeName { get; set; }

    public string? OperationalStatus { get; set; }

    public string? CorporateGradeDescription { get; set; }

    public string? ColleagueNonPermType { get; set; }

    public string? EmployeeEmailAddress { get; set; }

    public string? EmployeePositionId { get; set; }

    public string? PositionDescription { get; set; }

    public string? JobDescription { get; set; }

    public DateTime? LengthOfServiceDate { get; set; }

    public string? CostCentre { get; set; }

    public string? CostCentreName { get; set; }

    public string? Vendor { get; set; }

    public string? LineManagerBrid { get; set; }

    public string? LineManagerName { get; set; }

    public string? LineManagerEmailAddress { get; set; }

    public string? ImhL07 { get; set; }

    public string? OrgUnitId { get; set; }

    public string? Country { get; set; }

    public string? City { get; set; }

    public string? Building { get; set; }

    public decimal? LengthOfServiceInYears { get; set; }

    public string? ControlSpan { get; set; }

    public string? ControlTier { get; set; }

    public string? JobFamilyGroup { get; set; }

    public string? JobFamilyDescription { get; set; }

    public string? Job { get; set; }

    public string? JobProfile { get; set; }

    public decimal? Hc { get; set; }

    public decimal? Fte { get; set; }

    public string? Team { get; set; }

    public string? Train { get; set; }

    public string? ArtNonArt { get; set; }

    public string? EngNonEng { get; set; }

    public string? TargetAgileRole { get; set; }

    public string? TargetLevel { get; set; }

    public string? Actions { get; set; }

    public HeadcountUpload? Upload { get; set; }

    internal HeadcountRow CopyForInsert()
    {
        var copy = (HeadcountRow)MemberwiseClone();
        copy.Id = 0;
        copy.UploadId = 0;
        copy.Upload = null;
        return copy;
    }
}

namespace Estimation.Core.Models;

public class TeamMemberUploadRow
{
    public string EmployeeName { get; set; } = null!;
    public string? EmployeeNumber { get; set; }
    public bool IsNew { get; set; }
    public int? ExistingHrId { get; set; }
    public List<SkillUploadItem> Skills { get; set; } = new();
}

public class SkillUploadItem
{
    public int SkillId { get; set; }
    public string SkillName { get; set; } = null!;
    public string? NewLevelName { get; set; }
    public int? NewLevelId { get; set; }
    public string? OldLevelName { get; set; }
    public bool IsChanged { get; set; }
    public bool IsRemoved { get; set; }
}

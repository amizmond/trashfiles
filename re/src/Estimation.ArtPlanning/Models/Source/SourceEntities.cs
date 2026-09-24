
namespace Estimation.ArtPlanning.Models.Source;

public class SourceArt
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class SourceArtTeam
{
    public int CapitalProjectId { get; set; }
    public int TeamId { get; set; }
}

public class SourceTeam
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class SourceTeamMember
{
    public int TeamId { get; set; }
    public int HumanResourceId { get; set; }
    public int? TeamRoleId { get; set; }
}

public class SourceTeamRole
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class SourceHumanResource
{
    public int Id { get; set; }

    public string? EmployeeNumber { get; set; }

    public string EmployeeName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int? CityId { get; set; }
}

public class SourceHumanResourceSkill
{
    public int HumanResourceId { get; set; }
    public int SkillId { get; set; }
    public int? SkillLevelId { get; set; }
}

public class SourceSkill
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class SourceSkillLevel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? Value { get; set; }
}

public class SourcePi
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}

public class SourceHoliday
{
    public int Id { get; set; }
    public int HumanResourceId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int HalfDay { get; set; }
}

public class SourcePublicHoliday
{
    public int Id { get; set; }
    public int? CountryId { get; set; }
    public int? CityId { get; set; }
    public DateTime Date { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class SourceAppPage
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Group { get; set; }
    public bool IsAdminOnly { get; set; }
    public int SortOrder { get; set; }
    public int ScopeMode { get; set; }
}

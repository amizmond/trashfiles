using System.ComponentModel.DataAnnotations;

namespace Estimation.Core.Models;

public class Skill
{
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = null!;

    [MaxLength(150)]
    public string? Description { get; set; }

    [Required]
    public DateTime? Created { get; set; }

    public DateTime? Updated { get; set; }

    public virtual IList<SkillLevel> Levels { get; set; } = new List<SkillLevel>();
}

public class SkillLevel
{
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = null!;

    public int? Value { get; set; }

    [MaxLength(150)]
    public string? Description { get; set; }
}

public class HumanResource
{
    public int Id { get; set; }

    public bool IsActive { get; set; } = true;

    [MaxLength(50)]
    public string? Cio { get; set; }

    [MaxLength(50)]
    public string? Cio1 { get; set; }

    [MaxLength(50)]
    public string? Cio2 { get; set; }

    [MaxLength(30)]
    public string? EmployeeNumber { get; set; }

    [Required]
    [MaxLength(70)]
    public string EmployeeName { get; set; } = null!;

    [Required]
    [MaxLength(100)]
    public string FullName { get; set; } = null!;

    [MaxLength(100)]
    public string? LineManagerName { get; set; }

    public virtual IList<HumanResourceSkill> HumanResourceSkills { get; set; } = new List<HumanResourceSkill>();

    public virtual IList<TeamMember> TeamMembers { get; set; } = new List<TeamMember>();

    public int? EmployeeCategoryId { get; set; }
    public virtual EmployeeCategory? EmployeeCategory { get; set; }

    public int? EmployeeTypeId { get; set; }
    public virtual EmployeeType? EmployeeType { get; set; }

    public int? EmployeeRoleId { get; set; }
    public virtual EmployeeRole? EmployeeRole { get; set; }

    public int? EmployeeVendorId { get; set; }
    public virtual EmployeeVendor? EmployeeVendor { get; set; }

    public int? CityId { get; set; }
    public virtual City? City { get; set; }

    public int? CountryId { get; set; }
    public virtual Country? Country { get; set; }

    public int? CorporateGradeId { get; set; }
    public virtual CorporateGrade? CorporateGrade { get; set; }

    public int? TeamRoleId { get; set; }
    public virtual TeamRole? TeamRole { get; set; }
}

public class EmployeeCategory
{
    public int Id { get; set; }

    [Required]
    [MaxLength(30)]
    public string Name { get; set; } = null!;

    [MaxLength(100)]
    public string? Description { get; set; }
}

public class EmployeeType
{
    public int Id { get; set; }

    [Required]
    [MaxLength(30)]
    public string Name { get; set; } = null!;

    [MaxLength(100)]
    public string? Description { get; set; }
}

public class EmployeeVendor
{
    public int Id { get; set; }

    [Required]
    [MaxLength(30)]
    public string Name { get; set; } = null!;

    [MaxLength(100)]
    public string? Description { get; set; }
}

public class EmployeeRole{
    public int Id { get; set; }

    [Required]
    [MaxLength(30)]
    public string Name { get; set; } = null!;

    [MaxLength(100)]
    public string? Description { get; set; }
}

public class CorporateGrade
{
    public int Id { get; set; }

    [Required]
    [MaxLength(30)]
    public string Name { get; set; } = null!;

    [MaxLength(100)]
    public string? Description { get; set; }
}

public class City
{
    public int Id { get; set; }

    [Required]
    [MaxLength(70)]
    public string Name { get; set; } = null!;

    [MaxLength(100)]
    public string? Description { get; set; }
}

public class Country
{
    public int Id { get; set; }

    [Required]
    [MaxLength(70)]
    public string Name { get; set; } = null!;

    [MaxLength(100)]
    public string? Description { get; set; }
}

public class TeamRole
{
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = null!;

    [MaxLength(100)]
    public string? Description { get; set; }
}

public class Team
{
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = null!;

    [MaxLength(70)]
    public string? FullName { get; set; }
    [MaxLength(50)]
    public string? OptionalTeamTag { get; set; }
    [MaxLength(200)]
    public string? Description { get; set; }

    public virtual IList<TeamMember> TeamMembers { get; set; } = new List<TeamMember>();
    public virtual IList<FeatureTeam> FeatureTeams { get; set; } = new List<FeatureTeam>();
    public virtual IList<CapitalProjectTeam> CapitalProjectTeams { get; set; } = new List<CapitalProjectTeam>();
    public virtual IList<TeamTechnologyStack> TeamTechnologyStacks { get; set; } = new List<TeamTechnologyStack>();
}

public class HumanResourceSkill
{
    public int HumanResourceId { get; set; }
    public virtual HumanResource HumanResource { get; set; } = null!;

    public int SkillId { get; set; }
    public virtual Skill Skill { get; set; } = null!;

    public int? SkillLevelId { get; set; }
    public virtual SkillLevel? SkillLevel { get; set; }
}

public class CapitalProject
{
    public int Id { get; set; }

    [MaxLength(10)]
    public string? JiraKey { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    [MaxLength(500)]
    public string? Description { get; set; }

    public virtual IList<CapitalProjectTeam> CapitalProjectTeams { get; set; } = new List<CapitalProjectTeam>();

    public virtual IList<CapitalProjectStrategicObjective> CapitalProjectStrategicObjectives { get; set; } = new List<CapitalProjectStrategicObjective>();
}

public class StrategicObjective
{
    public int Id { get; set; }

    [MaxLength(100)] public string? JiraId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(250)]
    public string? Comments { get; set; }

    public virtual IList<CapitalProjectStrategicObjective> CapitalProjectStrategicObjectives { get; set; } = new List<CapitalProjectStrategicObjective>();

    public virtual IList<StrategicObjectivePortfolioEpic> StrategicObjectivePortfolioEpics { get; set; } = new List<StrategicObjectivePortfolioEpic>();
}

public class PortfolioEpic
{
    public int Id { get; set; }

    [MaxLength(100)]
    public string? JiraId { get; set; }

    [MaxLength(100)]
    public string? Name { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(250)]
    public string? Comments { get; set; }

    public virtual IList<StrategicObjectivePortfolioEpic> StrategicObjectivePortfolioEpics { get; set; } = new List<StrategicObjectivePortfolioEpic>();

    public virtual IList<BusinessOutcome> BusinessOutcomes { get; set; } = new List<BusinessOutcome>();
}

public class BusinessOutcome
{
    public int Id { get; set; }

    [MaxLength(100)]
    public string? JiraId { get; set; }

    [MaxLength(200)]
    public string? Name { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(250)]
    public string? Comments { get; set; }

    public int? Ranking { get; set; }

    [MaxLength(200)]
    public string? ArtName { get; set; }

    public int? PortfolioEpicId { get; set; }
    public virtual PortfolioEpic? PortfolioEpic { get; set; }

    public virtual IList<Feature> Features { get; set; } = new List<Feature>();
}

public class Feature
{
    public int Id { get; set; }

    [Required]
    [MaxLength(10)]
    public string ProjectKey { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? JiraId { get; set; }

    [Required]
    [MaxLength(255)]
    public string? Summary { get; set; }

    [MaxLength(200)]
    public string? Name { get; set; }

    [MaxLength(32767)]
    public string? Description { get; set; }

    [MaxLength(250)]
    public string? Comments { get; set; }

    public int? Ranking { get; set; }

    public int? UnfundedOptionId { get; set; }
    public virtual UnfundedOption? UnfundedOption { get; set; }

    public DateTime? DateExpected { get; set; }

    public bool? IsLinkedToTheJira { get; set; }

    public int? BusinessOutcomeId { get; set; }
    public virtual BusinessOutcome? BusinessOutcome { get; set; }

    public int? PiId { get; set; }

    public virtual Pi? Pi { get; set; }

    public virtual IList<FeatureTechnologyStack> FeatureTechnologyStacks { get; set; } = new List<FeatureTechnologyStack>();

    public virtual IList<FeatureTeam> FeatureTeams { get; set; } = new List<FeatureTeam>();

    public virtual IList<FeatureLabel> FeatureLabels { get; set; } = new List<FeatureLabel>();
}

public class UnfundedOption
{
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = null!;

    [MaxLength(150)]
    public string? Description { get; set; }

    [Range(0, int.MaxValue)]
    public int Order { get; set; } = 0;

    public virtual IList<Feature> Features { get; set; } = new List<Feature>();
}

public class Pi
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(50)]
    public string? Priority { get; set; }

    [MaxLength(250)]
    public string? Comments { get; set; }

    public DateTime? StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    public virtual IList<Feature> Features { get; set; } = new List<Feature>();
}

public class TechnologyStack
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    [MaxLength(200)]
    public string? Description { get; set; }

    public virtual IList<TechnologyStackSkill> TechnologyStackSkills { get; set; } = new List<TechnologyStackSkill>();
    public virtual IList<FeatureTechnologyStack> FeatureTechnologyStacks { get; set; } = new List<FeatureTechnologyStack>();
    public virtual IList<TeamTechnologyStack> TeamTechnologyStacks { get; set; } = new List<TeamTechnologyStack>();
}

public class TechnologyStackSkill
{
    public int TechnologyStackId { get; set; }
    public virtual TechnologyStack TechnologyStack { get; set; } = null!;

    public int SkillId { get; set; }
    public virtual Skill Skill { get; set; } = null!;
}

public class FeatureTechnologyStack
{
    public int Id { get; set; }

    public int FeatureId { get; set; }
    public virtual Feature Feature { get; set; } = null!;

    public int TechnologyStackId { get; set; }
    public virtual TechnologyStack TechnologyStack { get; set; } = null!;

    public int? EstimatedEffort { get; set; }
}

public class TeamTechnologyStack
{
    public int TeamId { get; set; }
    public virtual Team Team { get; set; } = null!;

    public int TechnologyStackId { get; set; }
    public virtual TechnologyStack TechnologyStack { get; set; } = null!;
}

public class CapitalProjectStrategicObjective
{
    public int CapitalProjectId { get; set; }
    public virtual CapitalProject CapitalProject { get; set; } = null!;

    public int StrategicObjectiveId { get; set; }
    public virtual StrategicObjective StrategicObjective { get; set; } = null!;
}

public class CapitalProjectTeam
{
    public int CapitalProjectId { get; set; }
    public virtual CapitalProject CapitalProject { get; set; } = null!;
    
    public int TeamId { get; set; }
    public virtual Team Team { get; set; } = null!;
}

public class StrategicObjectivePortfolioEpic
{
    public int StrategicObjectiveId { get; set; }
    public virtual StrategicObjective StrategicObjective { get; set; } = null!;

    public int PortfolioEpicId { get; set; }
    public virtual PortfolioEpic PortfolioEpic { get; set; } = null!;
}

public class FeatureTeam
{
    public int FeatureId { get; set; }
    public virtual Feature Feature { get; set; } = null!;

    public int TeamId { get; set; }
    public virtual Team Team { get; set; } = null!;
}

public class TeamMember
{
    public int TeamId { get; set; }
    public virtual Team Team { get; set; } = null!;

    public int HumanResourceId { get; set; }
    public virtual HumanResource HumanResource { get; set; } = null!;
}

public class Label
{
    public int Id { get; set; }

    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = null!;

    public virtual IList<FeatureLabel> FeatureLabels { get; set; } = new List<FeatureLabel>();
}

public class FeatureLabel
{
    public int FeatureId { get; set; }
    public virtual Feature Feature { get; set; } = null!;

    public int LabelId { get; set; }
    public virtual Label Label { get; set; } = null!;
}
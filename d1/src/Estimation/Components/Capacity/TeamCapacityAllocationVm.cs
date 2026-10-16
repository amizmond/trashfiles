namespace Estimation.Components.Capacity;

public class FeatureAllocVm
{
    public int FeatureId { get; set; }

    public bool IsValid { get; set; }

    public string Mode { get; set; } = "Team stacks";

    public decimal Required { get; set; }
    public decimal Reserved { get; set; }
    public bool IsFullyReserved { get; set; }
    public decimal Shortage => Required - Reserved > 0m ? Required - Reserved : 0m;

    public List<SkillAllocVm> Skills { get; set; } = new();
}

public class SkillAllocVm
{
    public int StackId { get; set; }
    public string Label { get; set; } = string.Empty;
    public decimal Required { get; set; }
    public decimal Reserved { get; set; }
    public bool IsReserved => Reserved >= Required;
    public List<MemberAllocVm> Members { get; set; } = new();
}

public class MemberAllocVm
{
    public int HumanResourceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SkillLevelValue { get; set; }
    public string? SkillLevelName { get; set; }
    public decimal AllocatedStoryPoints { get; set; }
}

public class MemberCapacityDetailVm
{
    public int HumanResourceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Role { get; set; }
    public decimal Capacity { get; set; }
    public decimal Allocated { get; set; }
    public decimal Remaining => Capacity - Allocated;
    public List<MemberAllocationLine> Lines { get; set; } = new();
}

public class MemberAllocationLine
{
    public string StackName { get; set; } = string.Empty;
    public string FeatureName { get; set; } = string.Empty;
    public string? JiraId { get; set; }
    public decimal StoryPoints { get; set; }
}

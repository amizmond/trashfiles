namespace Estimation.Core.Resources.Models;

public class TeamUploadBlockedProject
{
    public string Name { get; set; } = null!;

    public string Reason { get; set; } = null!;
}

public class TeamUploadRow
{
    public int? ExistingTeamId { get; set; }
    public string TeamName { get; set; } = null!;
    public string? ProjectNames { get; set; }
    public string? TechnologyStacks { get; set; }
    public bool IsNew { get; set; }

    public List<string> NewProjects { get; set; } = new();
    public List<string> RemovedProjects { get; set; } = new();
    public List<string> UnchangedProjects { get; set; } = new();
    public List<TeamUploadBlockedProject> BlockedProjects { get; set; } = new();

    public List<string> NewTechStacks { get; set; } = new();
    public List<string> RemovedTechStacks { get; set; } = new();
    public List<string> UnchangedTechStacks { get; set; } = new();

    public bool HasChanges => IsNew
        || NewProjects.Count > 0
        || RemovedProjects.Count > 0
        || NewTechStacks.Count > 0
        || RemovedTechStacks.Count > 0;
}

namespace Estimation.MasterData.Models;

public class MasterRow
{
    public int Id { get; set; }
    public string? JiraId { get; set; }
    public List<MasterRowJiraRef> StrategicObjective { get; set; } = new();
    public int? EpicRanking { get; set; }
    public string? Unfunded { get; set; }
    public int? UnfundedOptionId { get; set; }
    public string? EpicName { get; set; }
    public string? BoName { get; set; }
    public int? PiId { get; set; }
    public DateTime? TargetEnd { get; set; }
    public string? PiName { get; set; }
    public string? BoJiraId { get; set; }
    public string? FeatureName { get; set; }
    public List<MasterRowTeamRef> Teams { get; set; } = new();
    public int? EstimatedDays { get; set; }
    public List<MasterRowTechStackRef> TechStacks { get; set; } = new();
    public string? Dependencies { get; set; }
    public int? ConfidencePercentage { get; set; }
    public string? L6Owner { get; set; }
    public string? CapitalProjectName { get; set; }
    public int? ArtId { get; set; }
    public string? EpicJiraId { get; set; }
    public int? DevEstimate { get; set; }
    public int? BAEstimate { get; set; }
    public string? ProjectKey { get; set; }
    public int? BoId { get; set; }
    public int? EpicTableId { get; set; }
    public int? TotalEstimates { get; set; }
    public bool? IsNewRow { get; set; }
    public string? FeatureLabel { get; set; }
    public string? FeatureSummary { get; set; }

    public MasterRow()
    {

    }

    public MasterRow(
     int id,
     string? jiraId,
     List<MasterRowJiraRef> soJiraIds,
     int? epicRanking,
     string? unfunded,
     int? unfundedOptionId,
     string? epicName,
     string? boName,
     int? piId,
     DateTime? targetEnd,
     string? piName,
     string? boJiraId,
     string? featureName,
     string? capitalProjectName,
     List<MasterRowTeamRef> teams,
     int? estimatedDays,
     List<MasterRowTechStackRef> techStacks,
     string? dependencies,
     int? confidencePercenatge,
     string? l6Owner,
     string? epicJiraId,
     int? devEstimate,
     int? baEstimate,
     string? projectKey,
     int? businessOutcomeid,
     int? portfolioEpicId,
     int? totalEstimates,
     string? featureLabel,
     string? featureSummary,
     bool? isNewRow)
    {
        try
        {
            Id = id;
            JiraId = jiraId;
            StrategicObjective = soJiraIds ?? new List<MasterRowJiraRef>();
            EpicRanking = epicRanking;
            Unfunded = unfunded;
            UnfundedOptionId = unfundedOptionId;
            EpicName = epicName;
            BoName = boName;
            PiId = piId;
            TargetEnd = targetEnd;
            PiName = piName;
            BoJiraId = boJiraId;
            FeatureName = featureName;
            CapitalProjectName = capitalProjectName;
            Teams = teams ?? new List<MasterRowTeamRef>();
            EstimatedDays = estimatedDays;
            TechStacks = techStacks ?? new List<MasterRowTechStackRef>();
            Dependencies = dependencies;
            ConfidencePercentage = confidencePercenatge;
            L6Owner = l6Owner;
            EpicJiraId = epicJiraId;
            DevEstimate = devEstimate;
            BAEstimate = baEstimate;
            ProjectKey = projectKey;
            BoId = businessOutcomeid;
            EpicTableId = portfolioEpicId;
            TotalEstimates = totalEstimates;
            FeatureLabel = featureLabel;
            FeatureSummary = featureSummary;
            IsNewRow = isNewRow;
        }
        catch (Exception ex)
        {
            var exm = ex;
        }
    }
}
public class MasterRowJiraRef
{
    public int? SoTableId { get; set; }
    public string? JiraId { get; set; }
    public string? Description { get; set; }
    public string? Summary { get; set; }

    public MasterRowJiraRef() { }

    public MasterRowJiraRef(int? tableId, string? jiraId, string? summary, string? description = null)
    {
        SoTableId = tableId;
        JiraId = jiraId;
        Summary = summary;
        Description = description;
    }
}

public class MasterRowTeamRef
{
    public int? TeamId { get; set; }
    public string? Name { get; set; }

    public bool? IsPrimary { get; set; }

    public MasterRowTeamRef() { }

    public MasterRowTeamRef(int? teamId, string? name, bool? isPrimary)
    {
        TeamId = teamId;
        Name = name;
        IsPrimary = isPrimary;
    }
}

public class MasterRowTechStackRef
{
    public string? Name { get; set; }
    public int? Id { get; set; }
    public int? EstimatedEffort { get; set; }

    public MasterRowTechStackRef() { }

    public MasterRowTechStackRef(int? id, string name, int? estimatedEffort)
    {
        Id = id;
        Name = name;
        EstimatedEffort = estimatedEffort;
    }
}

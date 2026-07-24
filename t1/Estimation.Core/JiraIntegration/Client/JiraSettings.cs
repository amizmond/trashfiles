namespace Estimation.Core.JiraIntegration.Client;

public class JiraSettings
{
    public string Url { get; set; } = string.Empty;
    public string ConsumerKey { get; set; } = string.Empty;
    public string RsaKeyPath { get; set; } = string.Empty;
    public string FeatureNameCustomFieldId { get; set; } = "customfield_11703";
    public string TargetStartCustomFieldId { get; set; } = "customfield_19002";
    public string TargetEndCustomFieldId { get; set; } = "customfield_19003";
    public string StoryPointsCustomFieldId { get; set; } = "customfield_10003";
    public string GfedTeamCustomFieldId { get; set; } = "customfield_31702";
    public string PlanningIncrementCustomFieldId { get; set; } = "customfield_30302";
    public string ParentLinkCustomFieldId { get; set; } = "customfield_19001";
    public string RagExplainCustomFieldId { get; set; } = "customfield_30300";
    public string AcceptanceCriteriaCustomFieldId { get; set; } = "customfield_15900";
    public string NavigatorIdCustomFieldId { get; set; } = "customfield_19105";
    public string FeatureLinkCustomFieldId { get; set; } = "customfield_11702";

    /// <summary>
    /// Jira's Sprint custom field (greenhopper multi-sprint history). Optional: when empty,
    /// sprint snapshots skip carry-over detection. Instance-specific — find it via
    /// /rest/api/2/field on the server (name "Sprint").
    /// </summary>
    public string SprintCustomFieldId { get; set; } = string.Empty;

    public bool SkipSyncPreview { get; set; } = false;
}

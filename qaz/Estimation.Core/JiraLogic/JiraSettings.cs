namespace Estimation.Core.JiraLogic;

public class JiraSettings
{
    public string Url { get; set; } = string.Empty;
    public string ConsumerKey { get; set; } = string.Empty;
    public string RsaKeyPath { get; set; } = string.Empty;
    public string FeatureNameCustomFieldId { get; set; } = "customfield_11703";
    public string BusinessOutcomeCustomFieldId { get; set; } = "customfield_19001";
    public string TargetStartCustomFieldId { get; set; } = "customfield_19002";
    public string TargetEndCustomFieldId { get; set; } = "customfield_19003";
    public string StoryPointsCustomFieldId { get; set; } = "customfield_10003";
    public string GfedTeamCustomFieldId { get; set; } = "customfield_31720";
    public string PlanningIncrementCustomFieldId { get; set; } = "customfield_30302";
    public bool SkipSyncPreview { get; set; } = false;
}

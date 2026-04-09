namespace Estimation.Core.JiraLogic;

public class JiraOAuthSettings
{
    public string Url { get; set; } = string.Empty;
    public string ConsumerKey { get; set; } = string.Empty;
    public string RsaKeyPath { get; set; } = string.Empty;
    public string FeatureNameCustomFieldId { get; set; } = "customfield_11703";
    public string BusinessOutcomeCustomFieldId { get; set; } = "customfield_19001";
}

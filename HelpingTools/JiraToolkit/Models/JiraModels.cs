namespace JiraToolkit.Models;

public class JiraFieldRow
{
    public string Field { get; set; } = string.Empty;
    public string FieldId { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool IsCustomField { get; set; }
    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);
}

public class JiraProject
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
}

public class JiraIssueType
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Subtask { get; set; }
}

public class JiraLabel
{
    public string Name { get; set; } = string.Empty;
}

public class JiraCredentials
{
    public string BaseUrl { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

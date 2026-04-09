using JiraToolkit.Models;

namespace JiraToolkit.Services;

public class JiraSessionService
{
    public string JiraUrl { get; private set; } = string.Empty;
    public string Username { get; private set; } = string.Empty;
    public string Password { get; private set; } = string.Empty;

    public bool IsConnected => !string.IsNullOrWhiteSpace(JiraUrl)
        && !string.IsNullOrWhiteSpace(Username)
        && !string.IsNullOrWhiteSpace(Password)
        && Projects.Count > 0;

    public bool HasCredentials => !string.IsNullOrWhiteSpace(JiraUrl)
        && !string.IsNullOrWhiteSpace(Username)
        && !string.IsNullOrWhiteSpace(Password);

    public List<JiraProject> Projects { get; private set; } = new();

    public event Action? OnChange;

    public void SetCredentials(string url, string username, string password)
    {
        JiraUrl = url;
        Username = username;
        Password = password;
    }

    public JiraCredentials GetCredentials() => new()
    {
        BaseUrl = JiraUrl,
        Username = Username,
        Password = Password
    };

    public void SetProjects(List<JiraProject> projects)
    {
        Projects = projects;
        NotifyStateChanged();
    }

    public void NotifyStateChanged() => OnChange?.Invoke();
}

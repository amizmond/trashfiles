namespace Estimation.Components.Shared;

public record ArtRef(int Id, string Name, string? Description);

public static class ArtHelper
{
    public static ArtRef? ResolveArtByJiraId(
        string? projectKey,
        string? jiraId,
        IReadOnlyDictionary<string, ArtRef> lookup)
    {
        var key = projectKey;
        if (string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(jiraId))
        {
            var dash = jiraId.IndexOf('-');
            key = dash > 0 ? jiraId.Substring(0, dash) : jiraId;
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return lookup.TryGetValue(key, out var cp) ? cp : null;
    }
}

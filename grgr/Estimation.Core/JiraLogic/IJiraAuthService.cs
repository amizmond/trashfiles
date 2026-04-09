namespace Estimation.Core.JiraLogic;

public class JiraRequestToken
{
    public string Token { get; set; } = string.Empty;
    public string TokenSecret { get; set; } = string.Empty;
    public string AuthorizeUrl { get; set; } = string.Empty;
}

public interface IJiraAuthService
{
    bool IsConfigured { get; }
    Task<JiraRequestToken> GetRequestTokenAsync();
    Task<(string AccessToken, string AccessTokenSecret)> ExchangeAccessTokenAsync(
        string requestToken, string requestTokenSecret, string verifier);
    Task<bool> IsAuthenticatedAsync(string userName);
    Task SaveTokenAsync(string userName, string accessToken, string accessTokenSecret);
    Task<JiraToken?> GetStoredTokenAsync(string userName);
    Task LogoutAsync(string userName);
    Task<bool> TestConnectionAsync(string userName);
    string BuildOAuthHeader(string httpMethod, string url, string accessToken);
}

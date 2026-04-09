using System.Security.Cryptography;
using System.Text;
using System.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;

namespace Estimation.Core.JiraLogic;

public class JiraAuthService : IJiraAuthService, IDisposable
{
    private readonly IDbContextFactory<EstimationDbContext> _contextFactory;
    private readonly JiraOAuthSettings _settings;
    private readonly RSA? _rsa;

    public bool IsConfigured => !string.IsNullOrEmpty(_settings.Url)
                                && !string.IsNullOrEmpty(_settings.ConsumerKey)
                                && _rsa is not null;

    public JiraAuthService(
        IDbContextFactory<EstimationDbContext> contextFactory,
        IOptions<JiraOAuthSettings> settings)
    {
        _contextFactory = contextFactory;
        _settings = settings.Value;

        if (!string.IsNullOrEmpty(_settings.RsaKeyPath) && File.Exists(_settings.RsaKeyPath))
        {
            try
            {
                _rsa = RSA.Create();
                var pemText = File.ReadAllText(_settings.RsaKeyPath);
                _rsa.ImportFromPem(pemText);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load RSA key from {Path}", _settings.RsaKeyPath);
                _rsa?.Dispose();
                _rsa = null;
            }
        }
    }

    public async Task<JiraRequestToken> GetRequestTokenAsync()
    {
        EnsureConfigured();

        var url = $"{_settings.Url.TrimEnd('/')}/plugins/servlet/oauth/request-token";

        var oauthParams = new SortedDictionary<string, string>
        {
            ["oauth_consumer_key"] = _settings.ConsumerKey,
            ["oauth_signature_method"] = "RSA-SHA1",
            ["oauth_timestamp"] = GetTimestamp(),
            ["oauth_nonce"] = GetNonce(),
            ["oauth_version"] = "1.0",
            ["oauth_callback"] = "oob",
        };

        var signature = GenerateSignature("POST", url, oauthParams);
        oauthParams["oauth_signature"] = signature;

        using var httpClient = new HttpClient();
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Authorization", BuildAuthorizationHeader(oauthParams));

        var response = await httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Failed to get request token: {response.StatusCode} – {body}");

        var qs = HttpUtility.ParseQueryString(body);
        var token = qs["oauth_token"]
                    ?? throw new Exception("No oauth_token in response");
        var tokenSecret = qs["oauth_token_secret"]
                          ?? throw new Exception("No oauth_token_secret in response");

        return new JiraRequestToken
        {
            Token = token,
            TokenSecret = tokenSecret,
            AuthorizeUrl = $"{_settings.Url.TrimEnd('/')}/plugins/servlet/oauth/authorize?oauth_token={Uri.EscapeDataString(token)}",
        };
    }

    public async Task<(string AccessToken, string AccessTokenSecret)> ExchangeAccessTokenAsync(
        string requestToken, string requestTokenSecret, string verifier)
    {
        EnsureConfigured();

        var url = $"{_settings.Url.TrimEnd('/')}/plugins/servlet/oauth/access-token";

        var oauthParams = new SortedDictionary<string, string>
        {
            ["oauth_consumer_key"] = _settings.ConsumerKey,
            ["oauth_token"] = requestToken,
            ["oauth_signature_method"] = "RSA-SHA1",
            ["oauth_timestamp"] = GetTimestamp(),
            ["oauth_nonce"] = GetNonce(),
            ["oauth_version"] = "1.0",
            ["oauth_verifier"] = verifier,
        };

        var signature = GenerateSignature("POST", url, oauthParams);
        oauthParams["oauth_signature"] = signature;

        using var httpClient = new HttpClient();
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Authorization", BuildAuthorizationHeader(oauthParams));

        var response = await httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Failed to exchange access token: {response.StatusCode} – {body}");

        var qs = HttpUtility.ParseQueryString(body);
        var accessToken = qs["oauth_token"]
                          ?? throw new Exception("No oauth_token in response");
        var accessTokenSecret = qs["oauth_token_secret"]
                                ?? throw new Exception("No oauth_token_secret in response");

        return (accessToken, accessTokenSecret);
    }

    public async Task<bool> IsAuthenticatedAsync(string userName)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.JiraTokens.AnyAsync(t => t.UserName == userName);
    }

    public async Task SaveTokenAsync(string userName, string accessToken, string accessTokenSecret)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var existing = await context.JiraTokens.FirstOrDefaultAsync(t => t.UserName == userName);

        if (existing is not null)
        {
            existing.AccessToken = accessToken;
            existing.AccessTokenSecret = accessTokenSecret;
            existing.Updated = DateTime.Now;
        }
        else
        {
            context.JiraTokens.Add(new JiraToken
            {
                UserName = userName,
                AccessToken = accessToken,
                AccessTokenSecret = accessTokenSecret,
                Created = DateTime.Now,
            });
        }

        await context.SaveChangesAsync();
    }

    public async Task<JiraToken?> GetStoredTokenAsync(string userName)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.JiraTokens.AsNoTracking()
            .FirstOrDefaultAsync(t => t.UserName == userName);
    }

    public async Task LogoutAsync(string userName)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var token = await context.JiraTokens.FirstOrDefaultAsync(t => t.UserName == userName);
        if (token is not null)
        {
            context.JiraTokens.Remove(token);
            await context.SaveChangesAsync();
        }
    }

    public async Task<bool> TestConnectionAsync(string userName)
    {
        try
        {
            if (!IsConfigured) return false;

            var token = await GetStoredTokenAsync(userName);
            if (token is null) return false;

            var url = $"{_settings.Url.TrimEnd('/')}/rest/api/2/myself";

            var oauthParams = new SortedDictionary<string, string>
            {
                ["oauth_consumer_key"] = _settings.ConsumerKey,
                ["oauth_token"] = token.AccessToken,
                ["oauth_signature_method"] = "RSA-SHA1",
                ["oauth_timestamp"] = GetTimestamp(),
                ["oauth_nonce"] = GetNonce(),
                ["oauth_version"] = "1.0",
            };

            var signature = GenerateSignature("GET", url, oauthParams);
            oauthParams["oauth_signature"] = signature;

            using var httpClient = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", BuildAuthorizationHeader(oauthParams));

            var response = await httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to test Jira connection for {UserName}", userName);
            return false;
        }
    }

    public string BuildOAuthHeader(string httpMethod, string url, string accessToken)
    {
        EnsureConfigured();

        var oauthParams = new SortedDictionary<string, string>
        {
            ["oauth_consumer_key"] = _settings.ConsumerKey,
            ["oauth_token"] = accessToken,
            ["oauth_signature_method"] = "RSA-SHA1",
            ["oauth_timestamp"] = GetTimestamp(),
            ["oauth_nonce"] = GetNonce(),
            ["oauth_version"] = "1.0",
        };

        var signature = GenerateSignature(httpMethod, url, oauthParams);
        oauthParams["oauth_signature"] = signature;

        return BuildAuthorizationHeader(oauthParams);
    }

    public void Dispose()
    {
        _rsa?.Dispose();
    }

    // ── OAuth 1.0a helpers ──────────────────────────────────────

    private string GenerateSignature(string httpMethod, string url, SortedDictionary<string, string> parameters)
    {
        var uri = new Uri(url);
        var baseUrl = $"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? "" : $":{uri.Port}")}{uri.AbsolutePath}";

        // Merge query string parameters into the signing parameter set (OAuth 1.0a §3.4.1.3)
        var allParams = new SortedDictionary<string, string>(parameters);
        if (!string.IsNullOrEmpty(uri.Query))
        {
            var queryParams = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var qp in queryParams)
            {
                var parts = qp.Split('=', 2);
                var key = Uri.UnescapeDataString(parts[0]);
                var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "";
                allParams[key] = value;
            }
        }

        var paramString = string.Join("&",
            allParams.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

        var baseString = $"{httpMethod.ToUpperInvariant()}&{Uri.EscapeDataString(baseUrl)}&{Uri.EscapeDataString(paramString)}";

        var signatureBytes = _rsa!.SignData(
            Encoding.UTF8.GetBytes(baseString),
            HashAlgorithmName.SHA1,
            RSASignaturePadding.Pkcs1);

        return Convert.ToBase64String(signatureBytes);
    }

    private static string BuildAuthorizationHeader(SortedDictionary<string, string> parameters)
    {
        var pairs = parameters
            .Select(p => $"{Uri.EscapeDataString(p.Key)}=\"{Uri.EscapeDataString(p.Value)}\"");
        return $"OAuth {string.Join(", ", pairs)}";
    }

    private static string GetTimestamp() =>
        DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

    private static string GetNonce() =>
        Guid.NewGuid().ToString("N");

    private void EnsureConfigured()
    {
        if (!IsConfigured)
            throw new InvalidOperationException(
                "Jira OAuth is not configured. Check JiraInstance settings and RSA key file.");
    }
}

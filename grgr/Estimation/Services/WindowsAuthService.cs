using Microsoft.AspNetCore.Components.Authorization;

namespace Estimation.Services;

public interface IWindowsAuthService
{
    Task<string?> GetUserName();

    Task<bool> IsUserAuthorized();
}

public class WindowsAuthService : IWindowsAuthService
{
    private readonly HashSet<string> _allowedUsers;
    private readonly AuthenticationStateProvider _authStateProvider;

    private bool _isAuthDisabled;

    public WindowsAuthService(AuthenticationStateProvider authStateProvider, IConfiguration configuration)
    {
        _authStateProvider = authStateProvider;

        _isAuthDisabled = configuration.GetValue<bool>("Authorization:IsWindowsAuthDisabled");
        var users = configuration.GetSection("Authorization:AllowedUsers").Get<string[]>() ?? [];
        _allowedUsers = new HashSet<string>(users, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<string?> GetUserName()
    {
        var authState = await _authStateProvider.GetAuthenticationStateAsync();
        return authState.User.Identity?.Name;
    }

    public async Task<bool> IsUserAuthorized()
    {
        if (_isAuthDisabled)
        { 
            return true;
        }

        var userName = await GetUserName();

        if (string.IsNullOrWhiteSpace(userName))
        { 
            return false;
        }

        return _allowedUsers.Contains(userName);
    }
}

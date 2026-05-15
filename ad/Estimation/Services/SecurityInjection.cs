using Estimation.Core.Services;
using Microsoft.AspNetCore.Authentication.Negotiate;

namespace Estimation.Services;

public static class SecurityInjection
{
    public static void AddSecurity(this IServiceCollection service, IConfiguration configuration)
    {
        service.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();

        service.AddScoped<IWindowsAuthService, WindowsAuthService>();
        service.AddSingleton<IActiveDirectoryService, ActiveDirectoryService>();
        service.AddScoped<IAppAuthorizationService, AppAuthorizationService>();

        service.AddAuthorizationCore(option =>
        {
            option.FallbackPolicy = option.DefaultPolicy;
        });
    }
}

using Microsoft.AspNetCore.Authentication.Negotiate;

namespace Estimation.Services;

public static class SecurityInjection
{
    public static void AddSecurity(this IServiceCollection service, IConfiguration configuration)
    {
        service.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();

        service.AddScoped<IWindowsAuthService, WindowsAuthService>();

        service.AddAuthorizationCore(option =>
        { 
            option.FallbackPolicy = option.DefaultPolicy;
        });
    }
}

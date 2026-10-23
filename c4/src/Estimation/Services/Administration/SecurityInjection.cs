using Estimation.Core.Administration.Services;
using Estimation.Core.Shared.Services;
using Estimation.Services.TeamPlanning;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Estimation.Services.Administration;

public static class SecurityInjection
{
    public static void AddSecurity(this IServiceCollection service, IConfiguration configuration)
    {
        service.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();

        service.AddScoped<IWindowsAuthService, WindowsAuthService>();
        service.AddSingleton<IOnlineUserTracker, OnlineUserTracker>();
        service.AddScoped<OnlineUserCircuitHandler>();
        service.AddScoped<CircuitHandler>(sp => sp.GetRequiredService<OnlineUserCircuitHandler>());
        service.AddSingleton<IActiveDirectoryService, ActiveDirectoryService>();
        service.AddScoped<IAppAuthorizationService, AppAuthorizationService>();
        service.AddScoped<IProfileService, ProfileService>();
        service.AddScoped<IUserPermissionService, UserPermissionService>();
        service.AddScoped<ISolutionTrainResolver, SolutionTrainResolver>();
        service.AddScoped<IPermissionGuard, PermissionGuard>();
        service.AddScoped<ISelfServiceResolver, SelfServiceResolver>();

        service.AddAuthorizationCore(option =>
        {
            option.FallbackPolicy = option.DefaultPolicy;
        });
    }
}

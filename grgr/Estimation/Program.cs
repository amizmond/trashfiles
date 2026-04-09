using Estimation.Components;
using Estimation.Components.ModalDialog;
using Estimation.Core;
using Estimation.Core.JiraLogic;
using Estimation.Core.Services;
using Estimation.Services;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using Serilog;

namespace Estimation;

public class Program
{
    public static async Task Main(string[] args)
    {
        var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "estimation-log.txt");
        Log.Logger = new LoggerConfiguration()
            .WriteTo.File(logPath,
                rollingInterval: RollingInterval.Day,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 5)
            .CreateLogger();
        try
        {
            Log.Information("Start application");

            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();



            builder.Services.AddMudServices();
            builder.Services.AddMemoryCache();

            var connectionString = builder.Configuration.GetConnectionString("EstimationDbConnection")
                ?? throw new InvalidOperationException("Connection string 'EstimationDbConnection' not found.");
            builder.Services.AddDbContextFactory<EstimationDbContext>(options => options.UseSqlServer(connectionString));

            builder.Services.AddScoped<IModalDialogService, ModalDialogService>();
            builder.Services.AddScoped<ISkillService, SkillService>();
            builder.Services.AddScoped<IHumanResourceService, HumanResourceService>();

            builder.Services.AddScoped<ITeamService, TeamService>();
            builder.Services.AddScoped<ITeamMemberUploadService, TeamMemberUploadService>();
            builder.Services.AddScoped<IPiService, PiService>();
            builder.Services.AddScoped<ICapitalProjectService, CapitalProjectService>();
            builder.Services.AddScoped<IStrategicObjectiveService, StrategicObjectiveService>();
            builder.Services.AddScoped<IPortfolioEpicService, PortfolioEpicService>();
            builder.Services.AddScoped<IBusinessOutcomeService, BusinessOutcomeService>();
            builder.Services.AddScoped<IFeatureService, FeatureService>();
            builder.Services.AddScoped<IUnfundedOptionService, UnfundedOptionService>();
            builder.Services.AddScoped<IPiPrioritizationService, PiPrioritizationService>();
            builder.Services.AddScoped<IPiCapacityService, PiCapacityService>();
            builder.Services.AddScoped<IStaticSettingsService, StaticSettingsService>();
            builder.Services.AddScoped<IUploadLxlTeamDataService, UploadLxlTeamDataService>();
            builder.Services.AddScoped<IUploadMasterService, UploadMasterService>();
            builder.Services.AddScoped<IDashboardService, DashboardService>();
            builder.Services.AddScoped<ITechnologyStackService, TechnologyStackService>();

            builder.Services.Configure<JiraOAuthSettings>(builder.Configuration.GetSection("JiraInstance"));
            builder.Services.AddScoped<IJiraAuthService, JiraAuthService>();
            builder.Services.AddScoped<IJiraIssueService, JiraIssueService>();
            builder.Services.AddScoped<IJiraMetadataService, JiraMetadataService>();

            builder.Services.AddSecurity(builder.Configuration);

            var app = builder.Build();

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            app.UseStaticFiles();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseAntiforgery();

            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            app.Run();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unhandled exception");
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }
}

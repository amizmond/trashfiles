using Estimation.Components;
using Estimation.Core;
using Estimation.Core.Administration.Audit;
using Estimation.Core.Administration.Services;
using Estimation.Core.Calendar.Services;
using Estimation.Core.Capacity.Services;
using Estimation.Core.Dashboard.Services;
using Estimation.Core.Features.Models;
using Estimation.Core.Features.Services;
using Estimation.Core.JiraIntegration.Client;
using Estimation.Core.JiraIntegration.Client.JiraSync;
using Estimation.Core.JiraIntegration.Services;
using Estimation.MasterData;
using Estimation.Core.PlanningIncrement.Services;
using Estimation.Core.Poker.Services;
using Estimation.Core.Train.Models;
using Estimation.Core.Train.Services;
using Estimation.Core.Resources.Services;
using Estimation.Services.Administration;
using Estimation.Services.JiraIntegration;
using Estimation.Services.Shared;
using Estimation.Services.TeamPlanning;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using Serilog;
using Serilog.Events;
using Estimation.Components.Shared.ModalDialog;

namespace Estimation;

public class Program
{
    private const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

    public static async Task Main(string[] args)
    {
        var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "estimation-log.txt");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(logPath,
                rollingInterval: RollingInterval.Day,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 5,
                outputTemplate: OutputTemplate)
            .WriteTo.Console(outputTemplate: OutputTemplate)
            .CreateBootstrapLogger();

        RegisterGlobalExceptionHandlers();

        try
        {
            Log.Information("Starting Estimation application");

            var builder = WebApplication.CreateBuilder(args);

            // Route every ILogger<T> (framework, EF Core, Kestrel, app services) through Serilog,
            // so the log file becomes the single place to look when the app misbehaves.
            builder.Host.UseSerilog((context, configuration) => configuration
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "Estimation")
                .WriteTo.File(logPath,
                    rollingInterval: RollingInterval.Day,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: 5,
                    outputTemplate: OutputTemplate)
                .WriteTo.Console(outputTemplate: OutputTemplate));

            // A faulting hosted/background service must NOT tear down the host — that would recycle
            // the IIS application pool. Log it and keep the rest of the application serving requests.
            builder.Services.Configure<HostOptions>(options =>
            {
                options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
            });

            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            builder.Services.AddMudServices();
            builder.Services.AddMemoryCache();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<IAuditUserProvider, HttpContextAuditUserProvider>();
            builder.Services.AddSingleton<AuditSaveChangesInterceptor>();

            var connectionString = builder.Configuration.GetConnectionString("EstimationDbConnection")
                ?? throw new InvalidOperationException("Connection string 'EstimationDbConnection' not found.");
            builder.Services.AddDbContextFactory<EstimationDbContext>((sp, options) =>
            {
                options.UseSqlServer(connectionString);
                options.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
            });

            builder.Services.AddScoped<IModalDialogService, ModalDialogService>();
            builder.Services.AddScoped<IBrowserDownloadService, BrowserDownloadService>();
            builder.Services.AddScoped<ISkillService, SkillService>();
            builder.Services.AddScoped<IHumanResourceService, HumanResourceService>();

            builder.Services.AddScoped<ITeamService, TeamService>();
            builder.Services.AddScoped<ITeamMemberUploadService, TeamMemberUploadService>();
            builder.Services.AddScoped<ITeamUploadService, TeamUploadService>();
            builder.Services.AddScoped<IHumanResourceUploadService, HumanResourceUploadService>();
            builder.Services.AddScoped<ISkillUploadService, SkillUploadService>();
            builder.Services.AddScoped<IPiService, PiService>();
            builder.Services.AddScoped<IDepartmentService, DepartmentService>();
            builder.Services.AddScoped<ICapitalProjectService, CapitalProjectService>();
            builder.Services.AddScoped<IStrategicObjectiveService, StrategicObjectiveService>();
            builder.Services.AddScoped<IPortfolioEpicService, PortfolioEpicService>();
            builder.Services.AddScoped<IBusinessOutcomeService, BusinessOutcomeService>();
            builder.Services.AddScoped<IFeatureService, FeatureService>();
            builder.Services.AddScoped<IFeatureCommentService, FeatureCommentService>();
            builder.Services.AddScoped<IFeatureSkillService, FeatureSkillService>();
            builder.Services.AddScoped<IFeatureUploadService, FeatureUploadService>();
            builder.Services.AddScoped<IUnfundedOptionService, UnfundedOptionService>();
            builder.Services.AddScoped<IPiObjectiveService, PiObjectiveService>();
            builder.Services.AddScoped<IRequirementStatusService, RequirementStatusService>();
            builder.Services.AddScoped<IPiPrioritizationService, PiPrioritizationService>();
            builder.Services.AddScoped<IPiCapacityService, PiCapacityService>();
            builder.Services.AddScoped<IStaticSettingsService, StaticSettingsService>();
            builder.Services.AddScoped<IDashboardService, DashboardService>();
            builder.Services.AddScoped<ITechnologyStackService, TechnologyStackService>();
            builder.Services.AddScoped<IAuditLogService, AuditLogService>();
            builder.Services.AddHostedService<AuditLogRetentionBackgroundService>();
            builder.Services.AddScoped<IBackupService, BackupService>();
            builder.Services.AddHostedService<BackupBackgroundService>();
            builder.Services.AddScoped<IHolidayTypeService, HolidayTypeService>();
            builder.Services.AddScoped<IHolidayService, HolidayService>();
            builder.Services.AddScoped<ISprintService, SprintService>();
            builder.Services.AddScoped<IPublicHolidayService, PublicHolidayService>();
            builder.Services.AddScoped<IPublicHolidayUploadService, PublicHolidayUploadService>();
            builder.Services.AddScoped<ICapacityCalendarService, CapacityCalendarService>();
            builder.Services.AddScoped<IHolidayExportService, HolidayExportService>();
            builder.Services.AddScoped<IIcsExportService, IcsExportService>();
            builder.Services.AddScoped<ITeamMemberCoefficientService, TeamMemberCoefficientService>();
            builder.Services.AddScoped<ITeamCapacityService, TeamCapacityService>();
            builder.Services.AddScoped<IArtStackCapacityService, ArtStackCapacityService>();
            builder.Services.AddScoped<IArtSkillCapacityService, ArtSkillCapacityService>();
            builder.Services.AddScoped<TeamPlanningState>();

            builder.Services.Configure<JiraSettings>(builder.Configuration.GetSection("JiraInstance"));
            builder.Services.AddHttpClient();
            builder.Services.AddTransient<JiraResilienceHandler>();
            builder.Services.AddTransient<JiraOAuthSigningHandler>();
            builder.Services.AddHttpClient(JiraIssueService.HttpClientName)
                .AddHttpMessageHandler<JiraResilienceHandler>()
                .AddHttpMessageHandler<JiraOAuthSigningHandler>();
            builder.Services.AddScoped<IJiraAuthService, JiraAuthService>();
            builder.Services.AddScoped<IJiraIssueService, JiraIssueService>();
            builder.Services.AddScoped<IJiraMetadataService, JiraMetadataService>();
            builder.Services.AddScoped<IJiraHierarchySyncService, JiraHierarchySyncService>();
            builder.Services.AddSingleton<IJiraDiffService, JiraDiffService>();
            builder.Services.AddHostedService<JiraLabelsWarmupService>();
            builder.Services.AddScoped<IJiraSyncService, JiraSyncService>();
            builder.Services.AddScoped<IJiraSyncWriter, JiraSyncWriter>();
            builder.Services.AddScoped<IJiraSprintIssuesService, JiraSprintIssuesService>();
            builder.Services.AddScoped<IJiraAgileService, JiraAgileService>();
            builder.Services.AddScoped<ISprintJiraDiscoveryService, SprintJiraDiscoveryService>();
            builder.Services.AddScoped<ISprintMetricsAdminService, SprintMetricsAdminService>();
            builder.Services.AddTransient<FeatureTeamConverter>();
            builder.Services.AddTransient<FeaturePiConverter>();
            builder.Services.AddTransient<FeatureParentConverter>();
            builder.Services.AddTransient<BusinessOutcomeParentConverter>();
            builder.Services.AddTransient<PortfolioEpicParentConverter>();
            builder.Services.AddTransient<StrategicObjectiveParentConverter>();
            builder.Services.AddHostedService<JiraSyncBackgroundService>();
            builder.Services.AddMasterDataModule();

            builder.Services.AddSingleton<IPokerGameRegistry>(_ => new PokerGameRegistry(TimeProvider.System));
            builder.Services.AddScoped<IPokerAssigneeService, PokerAssigneeService>();

            builder.Services.AddSecurity(builder.Configuration);

            var app = builder.Build();

            // Concise, structured per-request logging instead of the verbose framework default.
            app.UseSerilogRequestLogging();

            // Start-up work that depends on external systems is best-effort: failures are logged but
            // must not abort the process, otherwise a transient DB blip at boot recycles the pool.
            ApplyDatabaseMigrations(app);
            ValidateJiraSyncModel();

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error", createScopeForErrors: true);
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
            Log.Fatal(ex, "Estimation application terminated unexpectedly");
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    /// <summary>
    /// Catches exceptions that escape the normal request/hosted-service pipelines (stray background
    /// threads, unobserved tasks) so they are always written to the log file. Unobserved task
    /// exceptions are marked observed to stop them ever escalating and recycling the app pool.
    /// </summary>
    private static void RegisterGlobalExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Fatal(e.ExceptionObject as Exception,
                "Unhandled AppDomain exception (IsTerminating={IsTerminating})", e.IsTerminating);
            Log.CloseAndFlush();
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "Unobserved Task exception");
            e.SetObserved();
        };
    }

    private static void ApplyDatabaseMigrations(WebApplication app)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<EstimationDbContext>>();
            using var dbContext = factory.CreateDbContext();
            dbContext.Database.Migrate();
            Log.Information("Database migrations applied");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Database migration failed at start-up; the application will continue to start");
        }
    }

    private static void ValidateJiraSyncModel()
    {
        try
        {
            JiraSyncModelMap.Validate(
                typeof(Feature),
                typeof(BusinessOutcome),
                typeof(PortfolioEpic),
                typeof(StrategicObjective));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "JiraSync model validation failed; Jira synchronisation may be misconfigured");
        }
    }
}

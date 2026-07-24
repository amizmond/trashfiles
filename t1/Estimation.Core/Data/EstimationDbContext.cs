using Estimation.Core.Administration.Models;
using Estimation.Core.Calendar.Models;
using Estimation.Core.Capacity.Models;
using Estimation.Core.Features.Models;
using Estimation.Core.JiraIntegration.Client;
using Estimation.Core.JiraIntegration.Models;
using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Train.Models;
using Estimation.Core.Resources.Models;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core;

public class EstimationDbContext : DbContext
{
    public EstimationDbContext(DbContextOptions<EstimationDbContext> options) : base(options) { }

    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<SkillLevel> SkillLevels => Set<SkillLevel>();
    public DbSet<HumanResource> HumanResources => Set<HumanResource>();
    public DbSet<HumanResourceSkill> HumanResourceSkills => Set<HumanResourceSkill>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<Pi> Pis => Set<Pi>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<CapitalProject> CapitalProjects => Set<CapitalProject>();
    public DbSet<StrategicObjective> StrategicObjectives => Set<StrategicObjective>();
    public DbSet<PortfolioEpic> PortfolioEpics => Set<PortfolioEpic>();
    public DbSet<BusinessOutcome> BusinessOutcomes => Set<BusinessOutcome>();
    public DbSet<Feature> Features => Set<Feature>();
    public DbSet<PiObjective> PiObjectives => Set<PiObjective>();
    public DbSet<RequirementStatus> RequirementStatuses => Set<RequirementStatus>();
    public DbSet<CapitalProjectStrategicObjective> CapitalProjectStrategicObjectives => Set<CapitalProjectStrategicObjective>();
    public DbSet<CapitalProjectTeam> CapitalProjectTeams => Set<CapitalProjectTeam>();
    public DbSet<StrategicObjectivePortfolioEpic> StrategicObjectivePortfolioEpics => Set<StrategicObjectivePortfolioEpic>();
    public DbSet<FeatureTeam> FeatureTeams => Set<FeatureTeam>();
    public DbSet<UnfundedOption> UnfundedOptions => Set<UnfundedOption>();
    public DbSet<TechnologyStack> TechnologyStacks => Set<TechnologyStack>();
    public DbSet<TechnologyStackSkill> TechnologyStackSkills => Set<TechnologyStackSkill>();
    public DbSet<TeamTechnologyStack> TeamTechnologyStacks => Set<TeamTechnologyStack>();
    public DbSet<FeatureSkill> FeatureSkills => Set<FeatureSkill>();
    public DbSet<TeamMemberTechnologyStack> TeamMemberTechnologyStacks => Set<TeamMemberTechnologyStack>();
    public DbSet<FeatureTeamTechnologyStack> FeatureTeamTechnologyStacks => Set<FeatureTeamTechnologyStack>();
    public DbSet<FeatureTeamStackSkill> FeatureTeamStackSkills => Set<FeatureTeamStackSkill>();

    public DbSet<EmployeeCategory> EmployeeCategories => Set<EmployeeCategory>();
    public DbSet<EmployeeType> EmployeeTypes => Set<EmployeeType>();
    public DbSet<EmployeeVendor> EmployeeVendors => Set<EmployeeVendor>();
    public DbSet<EmployeeRole> EmployeeRoles => Set<EmployeeRole>();
    public DbSet<CorporateGrade> CorporateGrades => Set<CorporateGrade>();
    public DbSet<City> Cities => Set<City>();
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<TeamRole> TeamRoles => Set<TeamRole>();
    public DbSet<JiraToken> JiraTokens => Set<JiraToken>();
    public DbSet<JiraLabelCache> JiraLabelCaches => Set<JiraLabelCache>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<AppPage> AppPages => Set<AppPage>();
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<ProfilePagePermission> ProfilePagePermissions => Set<ProfilePagePermission>();
    public DbSet<ProfileActionPermission> ProfileActionPermissions => Set<ProfileActionPermission>();
    public DbSet<AppUserProfile> AppUserProfiles => Set<AppUserProfile>();

    public DbSet<BackupSettings> BackupSettings => Set<BackupSettings>();
    public DbSet<BackupHistory> BackupHistory => Set<BackupHistory>();

    public DbSet<JiraSyncSettings> JiraSyncSettings => Set<JiraSyncSettings>();
    public DbSet<JiraSyncProjectSettings> JiraSyncProjectSettings => Set<JiraSyncProjectSettings>();
    public DbSet<JiraSyncHistory> JiraSyncHistory => Set<JiraSyncHistory>();

    public DbSet<SprintJiraMetrics> SprintJiraMetrics => Set<SprintJiraMetrics>();
    public DbSet<SprintJiraMetricsIssue> SprintJiraMetricsIssues => Set<SprintJiraMetricsIssue>();
    public DbSet<SprintMetricsSyncSettings> SprintMetricsSyncSettings => Set<SprintMetricsSyncSettings>();
    public DbSet<SprintMetricsSyncHistory> SprintMetricsSyncHistory => Set<SprintMetricsSyncHistory>();

    public DbSet<HolidayType> HolidayTypes => Set<HolidayType>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<Sprint> Sprints => Set<Sprint>();
    public DbSet<PublicHoliday> PublicHolidays => Set<PublicHoliday>();

    public DbSet<TeamMemberCoefficient> TeamMemberCoefficients => Set<TeamMemberCoefficient>();
    public DbSet<TeamCapacityFeatureOrder> TeamCapacityFeatureOrders => Set<TeamCapacityFeatureOrder>();
    public DbSet<FeatureComment> FeatureComments => Set<FeatureComment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Skill>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Name).IsRequired().HasMaxLength(50);
            e.Property(s => s.Description).HasMaxLength(150);
            e.HasMany(s => s.Levels).WithOne().OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SkillLevel>(e =>
        {
            e.HasKey(sl => sl.Id);
            e.Property(sl => sl.Name).IsRequired().HasMaxLength(50);
            e.Property(sl => sl.Description).HasMaxLength(150);
        });

        modelBuilder.Entity<HumanResource>(e =>
        {
            e.HasKey(hr => hr.Id);
            e.Property(hr => hr.EmployeeName).IsRequired().HasMaxLength(70);
            e.Property(hr => hr.FullName).IsRequired().HasMaxLength(100);
            e.Property(hr => hr.EmployeeNumber).HasMaxLength(30);
            e.Property(hr => hr.LineManagerName).HasMaxLength(100);
            e.Property(hr => hr.Cio).HasMaxLength(50);
            e.Property(hr => hr.Cio1).HasMaxLength(50);
            e.Property(hr => hr.Cio2).HasMaxLength(50);
            e.Property(hr => hr.IsActive).HasDefaultValue(true);

            e.HasOne(hr => hr.EmployeeCategory).WithMany()
             .HasForeignKey(hr => hr.EmployeeCategoryId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasOne(hr => hr.EmployeeType).WithMany()
             .HasForeignKey(hr => hr.EmployeeTypeId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasOne(hr => hr.EmployeeRole).WithMany()
             .HasForeignKey(hr => hr.EmployeeRoleId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasOne(hr => hr.EmployeeVendor).WithMany()
             .HasForeignKey(hr => hr.EmployeeVendorId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasOne(hr => hr.City).WithMany()
             .HasForeignKey(hr => hr.CityId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasOne(hr => hr.CorporateGrade).WithMany()
             .HasForeignKey(hr => hr.CorporateGradeId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);

            e.HasIndex(hr => hr.FullName);
            e.HasIndex(hr => hr.EmployeeName);
            e.HasIndex(hr => hr.EmployeeNumber);
            e.HasIndex(hr => hr.IsActive);
        });

        modelBuilder.Entity<HumanResourceSkill>(e =>
        {
            e.HasKey(hrs => new { hrs.HumanResourceId, hrs.SkillId });
            e.HasOne(hrs => hrs.HumanResource).WithMany(hr => hr.HumanResourceSkills)
             .HasForeignKey(hrs => hrs.HumanResourceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(hrs => hrs.Skill).WithMany()
             .HasForeignKey(hrs => hrs.SkillId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(hrs => hrs.SkillLevel).WithMany()
             .HasForeignKey(hrs => hrs.SkillLevelId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);

            e.HasIndex(hrs => hrs.HumanResourceId);
        });

        modelBuilder.Entity<Team>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Name).IsRequired().HasMaxLength(50);
            e.Property(t => t.FullName).HasMaxLength(70);
            e.Property(t => t.OptionalTeamTag).HasMaxLength(50);
            e.Property(t => t.Description).HasMaxLength(200);
            e.Ignore(t => t.TeamMembers);
            e.Ignore(t => t.FeatureTeams);
            e.Ignore(t => t.TeamTechnologyStacks);
        });

        modelBuilder.Entity<TeamMember>(e =>
        {
            e.HasKey(tm => new { tm.TeamId, tm.HumanResourceId });
            e.HasOne(tm => tm.Team).WithMany(t => t.TeamMembers)
             .HasForeignKey(tm => tm.TeamId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(tm => tm.HumanResource).WithMany(hr => hr.TeamMembers)
             .HasForeignKey(tm => tm.HumanResourceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(tm => tm.TeamRole).WithMany()
             .HasForeignKey(tm => tm.TeamRoleId).OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(tm => tm.HumanResourceId);
        });

        modelBuilder.Entity<Pi>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).IsRequired().HasMaxLength(100);
            e.Property(p => p.Description).HasMaxLength(500);
            e.Property(p => p.Priority).HasMaxLength(50);
            e.Property(p => p.Comments).HasMaxLength(250);
            e.Property(p => p.FeatureLabels).HasMaxLength(500);
            e.Property(p => p.LabelMatchMode).HasConversion<int>();
        });

        modelBuilder.Entity<Department>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.Name).IsRequired().HasMaxLength(100);
            e.Property(d => d.Description).HasMaxLength(500);
        });

        modelBuilder.Entity<CapitalProject>(e =>
        {
            e.HasKey(cp => cp.Id);
            e.Property(cp => cp.JiraKey).HasMaxLength(10);
            e.Property(cp => cp.Name).IsRequired().HasMaxLength(100);
            e.Property(cp => cp.Description).HasMaxLength(500);
            e.HasOne(cp => cp.Department).WithMany(d => d.CapitalProjects)
             .HasForeignKey(cp => cp.DepartmentId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        });

        modelBuilder.Entity<CapitalProjectStrategicObjective>(e =>
        {
            e.ToTable("CapitalProjectStrategicObjectives");
            e.HasKey(cpp => new { cpp.CapitalProjectId, cpp.StrategicObjectiveId });
            e.HasOne(cpp => cpp.CapitalProject).WithMany(cp => cp.CapitalProjectStrategicObjectives)
             .HasForeignKey(cpp => cpp.CapitalProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(cpp => cpp.StrategicObjective).WithMany(pp => pp.CapitalProjectStrategicObjectives)
             .HasForeignKey(cpp => cpp.StrategicObjectiveId).OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(cpp => cpp.StrategicObjectiveId);
        });

        modelBuilder.Entity<CapitalProjectTeam>(e =>
        {
            e.HasKey(cpt => new { cpt.CapitalProjectId, cpt.TeamId });
            e.HasOne(cpt => cpt.CapitalProject).WithMany(cp => cp.CapitalProjectTeams)
             .HasForeignKey(cpt => cpt.CapitalProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(cpt => cpt.Team).WithMany(t => t.CapitalProjectTeams)
             .HasForeignKey(cpt => cpt.TeamId).OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(cpt => cpt.TeamId);
        });

        modelBuilder.Entity<StrategicObjective>(e =>
        {
            e.ToTable("StrategicObjectives");
            e.HasKey(pp => pp.Id);
        });

        modelBuilder.Entity<StrategicObjectivePortfolioEpic>(e =>
        {
            e.ToTable("StrategicObjectivePortfolioEpics");
            e.HasKey(ppe => new { ppe.StrategicObjectiveId, ppe.PortfolioEpicId });
            e.HasOne(ppe => ppe.StrategicObjective).WithMany(pp => pp.StrategicObjectivePortfolioEpics)
             .HasForeignKey(ppe => ppe.StrategicObjectiveId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(ppe => ppe.PortfolioEpic).WithMany(pe => pe.StrategicObjectivePortfolioEpics)
             .HasForeignKey(ppe => ppe.PortfolioEpicId).OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(ppe => ppe.PortfolioEpicId);
        });

        modelBuilder.Entity<PortfolioEpic>(e =>
        {
            e.HasKey(pe => pe.Id);
            e.HasOne(f => f.UnfundedOption).WithMany(u => u.PortfolioEpics)
             .HasForeignKey(bo => bo.UnfundedOptionId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        });

        modelBuilder.Entity<BusinessOutcome>(e =>
        {
            e.HasKey(bo => bo.Id);
            e.HasOne(bo => bo.PortfolioEpic).WithMany(pe => pe.BusinessOutcomes)
             .HasForeignKey(bo => bo.PortfolioEpicId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);

            e.HasIndex(bo => bo.PortfolioEpicId);
            e.HasIndex(bo => bo.Ranking);
            e.HasIndex(bo => bo.ArtName);
        });

        modelBuilder.Entity<Feature>(e =>
        {
            e.HasKey(f => f.Id);
            e.HasOne(f => f.BusinessOutcome).WithMany(bo => bo.Features)
             .HasForeignKey(f => f.BusinessOutcomeId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasOne(f => f.Pi).WithMany(pi => pi.Features)
             .HasForeignKey(f => f.PiId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasOne(f => f.UnfundedOption).WithMany(u => u.Features)
             .HasForeignKey(f => f.UnfundedOptionId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasOne(f => f.PiObjective).WithMany(po => po.Features)
             .HasForeignKey(f => f.PiObjectiveId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasOne(f => f.RequirementStatus).WithMany(rs => rs.Features)
             .HasForeignKey(f => f.RequirementStatusId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);

            e.HasIndex(f => f.JiraId);
            e.HasIndex(f => f.ProjectKey);
            e.HasIndex(f => f.Ranking);
            e.HasIndex(f => f.Name);
            e.HasIndex(f => f.BusinessOutcomeId);
            e.HasIndex(f => f.PiId);
            e.HasIndex(f => f.UnfundedOptionId);
            e.HasIndex(f => f.PiObjectiveId);
            e.HasIndex(f => f.RequirementStatusId);
        });

        modelBuilder.Entity<PiObjective>(e =>
        {
            e.HasKey(po => po.Id);
            e.Property(po => po.Name).IsRequired().HasMaxLength(255);
            e.HasIndex(po => po.Name).IsUnique();
        });

        modelBuilder.Entity<RequirementStatus>(e =>
        {
            e.HasKey(rs => rs.Id);
            e.Property(rs => rs.Name).IsRequired().HasMaxLength(30);
            e.HasIndex(rs => rs.Name).IsUnique();
        });

        modelBuilder.Entity<FeatureTeam>(e =>
        {
            e.HasKey(ft => new { ft.FeatureId, ft.TeamId });
            e.HasOne(ft => ft.Feature).WithMany(f => f.FeatureTeams)
             .HasForeignKey(ft => ft.FeatureId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(ft => ft.Team).WithMany(t => t.FeatureTeams)
             .HasForeignKey(ft => ft.TeamId).OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(ft => ft.TeamId);
        });

        modelBuilder.Entity<TechnologyStack>(e =>
        {
            e.HasKey(ts => ts.Id);
            e.Property(ts => ts.Name).IsRequired().HasMaxLength(100);
            e.Property(ts => ts.Description).HasMaxLength(200);
        });

        modelBuilder.Entity<TechnologyStackSkill>(e =>
        {
            e.HasKey(tss => new { tss.TechnologyStackId, tss.SkillId });
            e.HasOne(tss => tss.TechnologyStack).WithMany(ts => ts.TechnologyStackSkills)
             .HasForeignKey(tss => tss.TechnologyStackId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(tss => tss.Skill).WithMany()
             .HasForeignKey(tss => tss.SkillId).OnDelete(DeleteBehavior.Restrict);

            e.Property(tss => tss.Percentage).HasColumnType("decimal(3,2)").HasDefaultValue(1.0m);

            e.HasIndex(tss => tss.SkillId);
        });

        modelBuilder.Entity<TeamTechnologyStack>(e =>
        {
            e.HasKey(tts => new { tts.TeamId, tts.TechnologyStackId });
            e.HasOne(tts => tts.Team).WithMany(t => t.TeamTechnologyStacks)
             .HasForeignKey(tts => tts.TeamId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(tts => tts.TechnologyStack).WithMany(ts => ts.TeamTechnologyStacks)
             .HasForeignKey(tts => tts.TechnologyStackId).OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(tts => tts.TechnologyStackId);
        });

        modelBuilder.Entity<FeatureSkill>(e =>
        {
            e.HasKey(fs => new { fs.FeatureId, fs.SkillId });
            e.HasOne(fs => fs.Feature).WithMany(f => f.FeatureSkills)
             .HasForeignKey(fs => fs.FeatureId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(fs => fs.Skill).WithMany()
             .HasForeignKey(fs => fs.SkillId).OnDelete(DeleteBehavior.Restrict);

            e.Property(fs => fs.Value).HasColumnType("decimal(9,2)");

            e.HasIndex(fs => fs.SkillId);
        });

        modelBuilder.Entity<TeamMemberTechnologyStack>(e =>
        {
            e.HasKey(x => new { x.TeamId, x.HumanResourceId, x.TechnologyStackId });
            e.HasOne(x => x.TeamMember).WithMany(tm => tm.MemberTechnologyStacks)
             .HasForeignKey(x => new { x.TeamId, x.HumanResourceId }).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.TechnologyStack).WithMany()
             .HasForeignKey(x => x.TechnologyStackId).OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.TechnologyStackId);
        });

        modelBuilder.Entity<FeatureTeamTechnologyStack>(e =>
        {
            e.HasKey(x => new { x.FeatureId, x.TeamId, x.TechnologyStackId });
            e.HasOne(x => x.FeatureTeam).WithMany(ft => ft.TechnologyStacks)
             .HasForeignKey(x => new { x.FeatureId, x.TeamId }).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.TechnologyStack).WithMany()
             .HasForeignKey(x => x.TechnologyStackId).OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.TechnologyStackId);
        });

        modelBuilder.Entity<FeatureTeamStackSkill>(e =>
        {
            e.HasKey(x => new { x.FeatureId, x.TeamId, x.TechnologyStackId, x.SkillId });
            e.HasOne(x => x.FeatureTeamTechnologyStack).WithMany(ftts => ftts.SkillValues)
             .HasForeignKey(x => new { x.FeatureId, x.TeamId, x.TechnologyStackId }).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Skill).WithMany()
             .HasForeignKey(x => x.SkillId).OnDelete(DeleteBehavior.Restrict);

            e.Property(x => x.Value).HasColumnType("decimal(9,2)");

            e.HasIndex(x => x.SkillId);
        });

        modelBuilder.Entity<UnfundedOption>(e =>
        {
            e.HasKey(u => u.Id);
            e.Property(u => u.Name).IsRequired().HasMaxLength(50);
            e.Property(u => u.Description).HasMaxLength(150);
        });

        modelBuilder.Entity<EmployeeCategory>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(30);
            e.Property(x => x.Description).HasMaxLength(100);
        });

        modelBuilder.Entity<EmployeeType>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(30);
            e.Property(x => x.Description).HasMaxLength(100);
        });

        modelBuilder.Entity<EmployeeVendor>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(30);
            e.Property(x => x.Description).HasMaxLength(100);
        });

        modelBuilder.Entity<EmployeeRole>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(30);
            e.Property(x => x.Description).HasMaxLength(100);
        });

        modelBuilder.Entity<CorporateGrade>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(10);
            e.Property(x => x.Description).HasMaxLength(100);
        });

        modelBuilder.Entity<City>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(70);
            e.Property(x => x.Description).HasMaxLength(100);

            e.HasOne(x => x.Country).WithMany(c => c.Cities)
             .HasForeignKey(x => x.CountryId).OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.CountryId);
        });

        modelBuilder.Entity<Country>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(70);
            e.Property(x => x.Description).HasMaxLength(100);
        });

        modelBuilder.Entity<TeamRole>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(50);
            e.Property(x => x.Description).HasMaxLength(100);
        });

        modelBuilder.Entity<JiraToken>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.UserName).IsRequired().HasMaxLength(256);
            e.Property(t => t.AccessToken).IsRequired().HasMaxLength(2000);
            e.Property(t => t.AccessTokenSecret).IsRequired().HasMaxLength(2000);
            e.HasIndex(t => t.UserName).IsUnique();
        });

        modelBuilder.Entity<JiraLabelCache>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.CacheKey).IsRequired().HasMaxLength(100);
            e.Property(c => c.LabelsJson).IsRequired().HasColumnType("nvarchar(max)");
            e.HasIndex(c => c.CacheKey).IsUnique();
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.EntityName).IsRequired().HasMaxLength(100);
            e.Property(a => a.EntityId).IsRequired().HasMaxLength(50);
            e.Property(a => a.Action).IsRequired().HasMaxLength(10);
            e.Property(a => a.PropertyName).HasMaxLength(200);
            e.Property(a => a.EntityDisplayName).HasMaxLength(300);
            e.Property(a => a.OldValue).HasColumnType("nvarchar(max)");
            e.Property(a => a.NewValue).HasColumnType("nvarchar(max)");
            e.Property(a => a.UserName).IsRequired().HasMaxLength(256);

            e.HasIndex(a => a.EntityName);
            e.HasIndex(a => a.Timestamp);
            e.HasIndex(a => new { a.EntityName, a.EntityId });
            e.HasIndex(a => a.UserName);
            e.HasIndex(a => a.BatchId);
        });

        modelBuilder.Entity<AppUser>(e =>
        {
            e.HasKey(u => u.Id);
            e.Property(u => u.WindowsUserName).IsRequired().HasMaxLength(256);
            e.Property(u => u.DisplayName).HasMaxLength(200);
            e.Property(u => u.SamAccountName).HasMaxLength(256);
            e.Property(u => u.EmployeeId).HasMaxLength(50);
            e.Property(u => u.EmailAddress).HasMaxLength(256);
            e.Property(u => u.ApprovedBy).HasMaxLength(256);
            e.HasIndex(u => u.WindowsUserName).IsUnique();
        });

        modelBuilder.Entity<AppPage>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Key).IsRequired().HasMaxLength(100);
            e.Property(p => p.DisplayName).IsRequired().HasMaxLength(100);
            e.Property(p => p.Group).HasMaxLength(50);
            e.HasIndex(p => p.Key).IsUnique();

            e.HasData(
                new AppPage { Id = 1, Key = "Dashboard", DisplayName = "Dashboard", Group = null, IsAdminOnly = false, SortOrder = 0, ScopeMode = PageScopeMode.None },
                new AppPage { Id = 2, Key = "Pis", DisplayName = "PI", Group = "Planning Increment", IsAdminOnly = false, SortOrder = 10, ScopeMode = PageScopeMode.GeneralOnly },
                new AppPage { Id = 3, Key = "PiPrioritization", DisplayName = "Prioritization", Group = "Planning Increment", IsAdminOnly = false, SortOrder = 11, ScopeMode = PageScopeMode.GeneralOnly },
                new AppPage { Id = 5, Key = "CapitalProjects", DisplayName = "Solution Trains", Group = "Planning", IsAdminOnly = false, SortOrder = 20, ScopeMode = PageScopeMode.TrainScoped },
                new AppPage { Id = 6, Key = "StrategicObjectives", DisplayName = "Strategic Objectives", Group = "Planning", IsAdminOnly = false, SortOrder = 22, ScopeMode = PageScopeMode.TrainScoped },
                new AppPage { Id = 7, Key = "PortfolioEpics", DisplayName = "Portfolio Epics", Group = "Planning", IsAdminOnly = false, SortOrder = 23, ScopeMode = PageScopeMode.TrainScoped },
                new AppPage { Id = 8, Key = "BusinessOutcomes", DisplayName = "Business Outcomes", Group = "Planning", IsAdminOnly = false, SortOrder = 24, ScopeMode = PageScopeMode.TrainScoped },
                new AppPage { Id = 9, Key = "Features", DisplayName = "Features", Group = "Planning", IsAdminOnly = false, SortOrder = 25, ScopeMode = PageScopeMode.TrainScoped },
                new AppPage { Id = 10, Key = "Teams", DisplayName = "Teams", Group = "Resources", IsAdminOnly = false, SortOrder = 40, ScopeMode = PageScopeMode.TrainScoped },
                new AppPage { Id = 11, Key = "HumanResources", DisplayName = "Resources", Group = "Resources", IsAdminOnly = false, SortOrder = 41, ScopeMode = PageScopeMode.TrainScoped },
                new AppPage { Id = 12, Key = "Skills", DisplayName = "Skills", Group = "Resources", IsAdminOnly = false, SortOrder = 42, ScopeMode = PageScopeMode.GeneralOnly },
                new AppPage { Id = 13, Key = "TechnologyStacks", DisplayName = "Technology Stacks", Group = "Resources", IsAdminOnly = false, SortOrder = 43, ScopeMode = PageScopeMode.GeneralOnly },
                new AppPage { Id = 14, Key = "AuditLog", DisplayName = "Audit Log", Group = "Admin", IsAdminOnly = true, SortOrder = 90, ScopeMode = PageScopeMode.None },
                new AppPage { Id = 15, Key = "StaticSettings", DisplayName = "Static Data", Group = "Admin", IsAdminOnly = true, SortOrder = 91, ScopeMode = PageScopeMode.None },
                new AppPage { Id = 16, Key = "Authorization", DisplayName = "Authorization", Group = "Admin", IsAdminOnly = true, SortOrder = 92, ScopeMode = PageScopeMode.None },
                new AppPage { Id = 17, Key = "DatabaseBackup", DisplayName = "Database Backup", Group = "Admin", IsAdminOnly = true, SortOrder = 93, ScopeMode = PageScopeMode.None },
                new AppPage { Id = 18, Key = "HolidaysSetup", DisplayName = "Holidays Setup", Group = "Admin", IsAdminOnly = true, SortOrder = 94, ScopeMode = PageScopeMode.None },
                new AppPage { Id = 19, Key = "TeamPlanningCapacity", DisplayName = "PI Capacity", Group = "Team planning", IsAdminOnly = false, SortOrder = 30, ScopeMode = PageScopeMode.TrainScoped },
                new AppPage { Id = 20, Key = "TeamPlanningHolidays", DisplayName = "Holidays", Group = "Team planning", IsAdminOnly = false, SortOrder = 31, ScopeMode = PageScopeMode.TrainScoped },
                new AppPage { Id = 21, Key = "TeamPlanningCoefficients", DisplayName = "Coefficients", Group = "Team planning", IsAdminOnly = false, SortOrder = 32, ScopeMode = PageScopeMode.TrainScoped },
                new AppPage { Id = 22, Key = "TeamPlanningSprints", DisplayName = "Sprints", Group = "Team planning", IsAdminOnly = false, SortOrder = 33, ScopeMode = PageScopeMode.TrainScoped },
                new AppPage { Id = 23, Key = "MasterSheet", DisplayName = "Master Sheet", Group = null, IsAdminOnly = true, SortOrder = 101, ScopeMode = PageScopeMode.None },
                new AppPage { Id = 24, Key = "Profiles", DisplayName = "Profiles", Group = "Admin", IsAdminOnly = true, SortOrder = 95, ScopeMode = PageScopeMode.None },
                new AppPage { Id = 25, Key = "JiraSync", DisplayName = "Jira Sync", Group = "Admin", IsAdminOnly = true, SortOrder = 96, ScopeMode = PageScopeMode.None }
            );
        });

        modelBuilder.Entity<BackupSettings>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.BackupFolderPath).IsRequired().HasMaxLength(500);
            e.Property(x => x.DailyTime).HasConversion(
                v => v.ToTimeSpan(),
                v => TimeOnly.FromTimeSpan(v));
        });

        modelBuilder.Entity<BackupHistory>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).IsRequired().HasMaxLength(20);
            e.Property(x => x.FilePath).HasMaxLength(500);
            e.Property(x => x.Message).HasMaxLength(2000);
            e.Property(x => x.TriggeredBy).HasMaxLength(100);
            e.HasIndex(x => x.StartedAt);
        });

        modelBuilder.Entity<JiraSyncSettings>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ServiceAccountUserName).HasMaxLength(256);
            e.Property(x => x.JiraTimeZoneId).HasMaxLength(100);
        });

        modelBuilder.Entity<JiraSyncProjectSettings>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.IssueTypesCsv).HasMaxLength(1000);
            e.Property(x => x.LabelsCsv).HasMaxLength(2000);
            e.Property(x => x.StatusesCsv).HasMaxLength(2000);
            e.Property(x => x.ExcludeCreateStatusesCsv).HasMaxLength(2000);
            e.HasOne(x => x.CapitalProject).WithMany()
             .HasForeignKey(x => x.CapitalProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.CapitalProjectId).IsUnique();
        });

        modelBuilder.Entity<JiraSyncHistory>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).IsRequired().HasMaxLength(20);
            e.Property(x => x.TriggeredBy).HasMaxLength(256);
            // Message is left unbounded (nvarchar(max)) so it can hold the full per-run detail.
            e.HasIndex(x => x.StartedAt);
        });

        modelBuilder.Entity<SprintJiraMetrics>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.FailReason).HasMaxLength(1000);
            e.Property(x => x.CommittedSp).HasColumnType("decimal(9,2)");
            e.Property(x => x.CompletedFromCommittedSp).HasColumnType("decimal(9,2)");
            e.Property(x => x.NotCompletedFromCommittedSp).HasColumnType("decimal(9,2)");
            e.Property(x => x.DeliveredSp).HasColumnType("decimal(9,2)");
            e.Property(x => x.AddedSp).HasColumnType("decimal(9,2)");
            e.Property(x => x.RemovedSp).HasColumnType("decimal(9,2)");
            e.Property(x => x.ReEstimationNetSp).HasColumnType("decimal(9,2)");
            e.Property(x => x.LateEstimatedSp).HasColumnType("decimal(9,2)");

            e.HasOne(x => x.Sprint).WithMany()
             .HasForeignKey(x => x.SprintId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.SprintId).IsUnique();
        });

        modelBuilder.Entity<SprintJiraMetricsIssue>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.IssueKey).IsRequired().HasMaxLength(30);
            e.Property(x => x.IssueType).HasMaxLength(50);
            e.Property(x => x.Summary).HasMaxLength(500);
            e.Property(x => x.StatusAtEnd).HasMaxLength(50);
            e.Property(x => x.SpAtStart).HasColumnType("decimal(9,2)");
            e.Property(x => x.SpAtEnd).HasColumnType("decimal(9,2)");

            e.HasOne(x => x.SprintJiraMetrics).WithMany(m => m.Issues)
             .HasForeignKey(x => x.SprintJiraMetricsId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.SprintJiraMetricsId);
        });

        modelBuilder.Entity<SprintMetricsSyncSettings>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.IssueTypesCsv).IsRequired().HasMaxLength(500);
            e.Property(x => x.DoneStatusesCsv).HasMaxLength(2000);
            e.Property(x => x.LastProbeMessage).HasMaxLength(2000);
        });

        modelBuilder.Entity<SprintMetricsSyncHistory>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).IsRequired().HasMaxLength(20);
            e.Property(x => x.TriggeredBy).HasMaxLength(256);
            // Message is left unbounded (nvarchar(max)) so it can hold the full per-run detail.
            e.HasIndex(x => x.StartedAt);
        });

        modelBuilder.Entity<Profile>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).IsRequired().HasMaxLength(100);
            e.Property(p => p.Description).HasMaxLength(500);
            e.Property(p => p.CreatedBy).HasMaxLength(256);
            e.Property(p => p.ModifiedBy).HasMaxLength(256);
            e.Property(p => p.RestrictTeamEditByRole).HasDefaultValue(true);
            e.HasIndex(p => p.Name).IsUnique();
        });

        modelBuilder.Entity<ProfilePagePermission>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasOne(p => p.Profile).WithMany(pr => pr.PagePermissions)
             .HasForeignKey(p => p.ProfileId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(p => p.AppPage).WithMany()
             .HasForeignKey(p => p.AppPageId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(p => p.CapitalProject).WithMany()
             .HasForeignKey(p => p.CapitalProjectId).OnDelete(DeleteBehavior.Cascade);
            // One row per matrix cell; NULL CapitalProjectId is the single "General access" cell.
            // HasFilter(null) removes EF's default NOT NULL filter so only one General row can exist per page.
            e.HasIndex(p => new { p.ProfileId, p.AppPageId, p.CapitalProjectId }).IsUnique().HasFilter(null);
        });

        modelBuilder.Entity<ProfileActionPermission>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.ActionKey).IsRequired().HasMaxLength(50);
            e.HasOne(p => p.Profile).WithMany(pr => pr.ActionPermissions)
             .HasForeignKey(p => p.ProfileId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => new { p.ProfileId, p.ActionKey }).IsUnique();
        });

        modelBuilder.Entity<AppUserProfile>(e =>
        {
            e.HasKey(p => new { p.AppUserId, p.ProfileId });
            e.HasOne(p => p.AppUser).WithMany(u => u.UserProfiles)
             .HasForeignKey(p => p.AppUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(p => p.Profile).WithMany(pr => pr.UserProfiles)
             .HasForeignKey(p => p.ProfileId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HolidayType>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(50);
            e.Property(x => x.Description).HasMaxLength(150);
            e.Property(x => x.ColorHex).IsRequired().HasMaxLength(7);
            e.Property(x => x.IsActive).HasDefaultValue(true);
            e.HasIndex(x => x.Name).IsUnique();

            e.HasData(
                new HolidayType { Id = 1, Name = "Annual Leave", Description = "Vacation / paid time off", ColorHex = "#1976D2", IsActive = true, SortOrder = 10 },
                new HolidayType { Id = 2, Name = "Sick Leave", Description = "Illness", ColorHex = "#D32F2F", IsActive = true, SortOrder = 20 },
                new HolidayType { Id = 3, Name = "Unpaid Leave", Description = "Unpaid time off", ColorHex = "#616161", IsActive = true, SortOrder = 30 },
                new HolidayType { Id = 4, Name = "Maternity Leave", Description = "Maternity / parental leave", ColorHex = "#5D4037", IsActive = true, SortOrder = 40 },
                new HolidayType { Id = 5, Name = "Training", Description = "Off-site / course", ColorHex = "#388E3C", IsActive = true, SortOrder = 50 },
                new HolidayType { Id = 6, Name = "Other", Description = "Other absence", ColorHex = "#7B1FA2", IsActive = true, SortOrder = 100 }
            );
        });

        modelBuilder.Entity<Holiday>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Comment).HasMaxLength(250);
            e.Property(x => x.HalfDay).HasConversion<int>();

            e.HasOne(x => x.HumanResource).WithMany()
             .HasForeignKey(x => x.HumanResourceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.HolidayType).WithMany()
             .HasForeignKey(x => x.HolidayTypeId).OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => x.HumanResourceId);
            e.HasIndex(x => new { x.HumanResourceId, x.StartDate, x.EndDate });
            e.HasIndex(x => x.StartDate);
        });

        modelBuilder.Entity<Sprint>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(70);
            e.Property(x => x.ColorHex).HasMaxLength(7);
            e.Property(x => x.Comment).HasMaxLength(250);

            e.HasOne(x => x.Team).WithMany()
             .HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Pi).WithMany()
             .HasForeignKey(x => x.PiId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);

            e.Property(x => x.JiraState).HasMaxLength(20);

            e.HasIndex(x => x.TeamId);
            e.HasIndex(x => new { x.TeamId, x.StartDate, x.EndDate });
            e.HasIndex(x => x.PiId);
            e.HasIndex(x => x.JiraSprintId);
        });

        modelBuilder.Entity<PublicHoliday>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(200);

            // Country FK uses NoAction to avoid a multiple-cascade-path error: the only
            // cascade chain to PublicHoliday is Country -> City -> PublicHoliday. Deleting a
            // country removes its country-level holidays explicitly in StaticSettingsService.
            e.HasOne(x => x.Country).WithMany()
             .HasForeignKey(x => x.CountryId).OnDelete(DeleteBehavior.NoAction).IsRequired(false);
            e.HasOne(x => x.City).WithMany()
             .HasForeignKey(x => x.CityId).OnDelete(DeleteBehavior.Cascade).IsRequired(false);

            // NULL-equality (HasFilter(null)) lets the unique index dedup both country-wide
            // rows (CityId NULL) and city rows (CountryId NULL) on the same date.
            e.HasIndex(x => new { x.CountryId, x.CityId, x.Date }).IsUnique().HasFilter(null);
            e.HasIndex(x => x.Date);
        });

        modelBuilder.Entity<TeamMemberCoefficient>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Value).HasColumnType("decimal(3,2)").HasDefaultValue(1.0m);
            e.Property(x => x.Comment).HasMaxLength(250);

            e.HasOne(x => x.Team).WithMany()
             .HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.HumanResource).WithMany()
             .HasForeignKey(x => x.HumanResourceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Pi).WithMany()
             .HasForeignKey(x => x.PiId).IsRequired(false).OnDelete(DeleteBehavior.Cascade);

            // A null PiId marks the team default; HasFilter(null) drops EF's default
            // "IS NOT NULL" filter so the unique index also enforces a single default
            // row per (team, member) � SQL Server treats two NULLs as equal here.
            e.HasIndex(x => new { x.TeamId, x.HumanResourceId, x.PiId }).IsUnique().HasFilter(null);
            e.HasIndex(x => new { x.TeamId, x.PiId });
            e.HasIndex(x => x.HumanResourceId);
            e.HasIndex(x => x.PiId);
        });

        modelBuilder.Entity<TeamCapacityFeatureOrder>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.IsIncluded).HasDefaultValue(true);

            e.HasOne(x => x.Team).WithMany()
             .HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Pi).WithMany()
             .HasForeignKey(x => x.PiId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Feature).WithMany()
             .HasForeignKey(x => x.FeatureId).OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.TeamId, x.PiId, x.FeatureId }).IsUnique();
            e.HasIndex(x => new { x.TeamId, x.PiId, x.SortOrder });
        });

        modelBuilder.Entity<FeatureComment>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Text).IsRequired().HasMaxLength(4000);
            e.Property(c => c.Author).HasMaxLength(256);

            e.HasOne(c => c.Feature).WithMany()
             .HasForeignKey(c => c.FeatureId).OnDelete(DeleteBehavior.Cascade);

            // Comments are read per feature in chronological order.
            e.HasIndex(c => new { c.FeatureId, c.CreatedAt });
        });
    }
}

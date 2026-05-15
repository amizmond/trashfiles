using Estimation.Core.JiraLogic;
using Estimation.Core.Models;
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
    public DbSet<CapitalProjectStrategicObjective> CapitalProjectStrategicObjectives => Set<CapitalProjectStrategicObjective>();
    public DbSet<CapitalProjectTeam> CapitalProjectTeams => Set<CapitalProjectTeam>();
    public DbSet<StrategicObjectivePortfolioEpic> StrategicObjectivePortfolioEpics => Set<StrategicObjectivePortfolioEpic>();
    public DbSet<FeatureTeam> FeatureTeams => Set<FeatureTeam>();
    public DbSet<UnfundedOption> UnfundedOptions => Set<UnfundedOption>();
    public DbSet<TechnologyStack> TechnologyStacks => Set<TechnologyStack>();
    public DbSet<TechnologyStackSkill> TechnologyStackSkills => Set<TechnologyStackSkill>();
    public DbSet<FeatureTechnologyStack> FeatureTechnologyStacks => Set<FeatureTechnologyStack>();
    public DbSet<TeamTechnologyStack> TeamTechnologyStacks => Set<TeamTechnologyStack>();

    public DbSet<EmployeeCategory> EmployeeCategories => Set<EmployeeCategory>();
    public DbSet<EmployeeType> EmployeeTypes => Set<EmployeeType>();
    public DbSet<EmployeeVendor> EmployeeVendors => Set<EmployeeVendor>();
    public DbSet<EmployeeRole> EmployeeRoles => Set<EmployeeRole>();
    public DbSet<CorporateGrade> CorporateGrades => Set<CorporateGrade>();
    public DbSet<City> Cities => Set<City>();
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<TeamRole> TeamRoles => Set<TeamRole>();
    public DbSet<JiraToken> JiraTokens => Set<JiraToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<AppPage> AppPages => Set<AppPage>();
    public DbSet<AppUserPagePermission> AppUserPagePermissions => Set<AppUserPagePermission>();

    public DbSet<BackupSettings> BackupSettings => Set<BackupSettings>();
    public DbSet<BackupHistory> BackupHistory => Set<BackupHistory>();

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
            e.HasOne(hr => hr.Country).WithMany()
             .HasForeignKey(hr => hr.CountryId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasOne(hr => hr.CorporateGrade).WithMany()
             .HasForeignKey(hr => hr.CorporateGradeId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasOne(hr => hr.TeamRole).WithMany()
             .HasForeignKey(hr => hr.TeamRoleId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);

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

            e.HasIndex(tm => tm.HumanResourceId);
        });

        modelBuilder.Entity<Pi>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).IsRequired().HasMaxLength(100);
            e.Property(p => p.Description).HasMaxLength(500);
            e.Property(p => p.Priority).HasMaxLength(50);
            e.Property(p => p.Comments).HasMaxLength(250);
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

            e.HasIndex(f => f.JiraId);
            e.HasIndex(f => f.ProjectKey);
            e.HasIndex(f => f.Ranking);
            e.HasIndex(f => f.Name);
            e.HasIndex(f => f.BusinessOutcomeId);
            e.HasIndex(f => f.PiId);
            e.HasIndex(f => f.UnfundedOptionId);
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

            e.HasIndex(tss => tss.SkillId);
        });

        modelBuilder.Entity<FeatureTechnologyStack>(e =>
        {
            e.HasKey(fts => fts.Id);
            e.HasOne(fts => fts.Feature).WithMany(f => f.FeatureTechnologyStacks)
             .HasForeignKey(fts => fts.FeatureId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(fts => fts.TechnologyStack).WithMany(ts => ts.FeatureTechnologyStacks)
             .HasForeignKey(fts => fts.TechnologyStackId).OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(fts => fts.FeatureId);
            e.HasIndex(fts => new { fts.FeatureId, fts.TechnologyStackId });
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

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.EntityName).IsRequired().HasMaxLength(100);
            e.Property(a => a.EntityId).IsRequired().HasMaxLength(50);
            e.Property(a => a.Action).IsRequired().HasMaxLength(10);
            e.Property(a => a.PropertyName).HasMaxLength(200);
            e.Property(a => a.OldValue).HasColumnType("nvarchar(max)");
            e.Property(a => a.NewValue).HasColumnType("nvarchar(max)");
            e.Property(a => a.UserName).IsRequired().HasMaxLength(256);

            e.HasIndex(a => a.EntityName);
            e.HasIndex(a => a.Timestamp);
            e.HasIndex(a => new { a.EntityName, a.EntityId });
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
                new AppPage { Id = 1, Key = "Dashboard", DisplayName = "Dashboard", Group = null, IsAdminOnly = false, SortOrder = 0 },
                new AppPage { Id = 2, Key = "Pis", DisplayName = "PI", Group = "Planning Increment", IsAdminOnly = false, SortOrder = 10 },
                new AppPage { Id = 3, Key = "PiPrioritization", DisplayName = "Prioritization", Group = "Planning Increment", IsAdminOnly = false, SortOrder = 11 },
                new AppPage { Id = 5, Key = "CapitalProjects", DisplayName = "Solution Trains", Group = "Planning", IsAdminOnly = false, SortOrder = 20 },
                new AppPage { Id = 6, Key = "StrategicObjectives", DisplayName = "Strategic Objectives", Group = "Planning", IsAdminOnly = false, SortOrder = 22 },
                new AppPage { Id = 7, Key = "PortfolioEpics", DisplayName = "Portfolio Epics", Group = "Planning", IsAdminOnly = false, SortOrder = 23 },
                new AppPage { Id = 8, Key = "BusinessOutcomes", DisplayName = "Business Outcomes", Group = "Planning", IsAdminOnly = false, SortOrder = 24 },
                new AppPage { Id = 9, Key = "Features", DisplayName = "Features", Group = "Planning", IsAdminOnly = false, SortOrder = 25 },
                new AppPage { Id = 10, Key = "Teams", DisplayName = "Teams", Group = "Resources", IsAdminOnly = false, SortOrder = 30 },
                new AppPage { Id = 11, Key = "HumanResources", DisplayName = "Resources", Group = "Resources", IsAdminOnly = false, SortOrder = 31 },
                new AppPage { Id = 12, Key = "Skills", DisplayName = "Skills", Group = "Resources", IsAdminOnly = false, SortOrder = 32 },
                new AppPage { Id = 13, Key = "TechnologyStacks", DisplayName = "Technology Stacks", Group = "Resources", IsAdminOnly = false, SortOrder = 33 },
                new AppPage { Id = 14, Key = "AuditLog", DisplayName = "Audit Log", Group = "Admin", IsAdminOnly = true, SortOrder = 90 },
                new AppPage { Id = 15, Key = "StaticSettings", DisplayName = "Static Data", Group = "Admin", IsAdminOnly = true, SortOrder = 91 },
                new AppPage { Id = 16, Key = "Authorization", DisplayName = "Authorization", Group = "Admin", IsAdminOnly = true, SortOrder = 92 },
                new AppPage { Id = 17, Key = "DatabaseBackup", DisplayName = "Database Backup", Group = "Admin", IsAdminOnly = true, SortOrder = 93 }
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

        modelBuilder.Entity<AppUserPagePermission>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasOne(p => p.AppUser).WithMany(u => u.PagePermissions)
             .HasForeignKey(p => p.AppUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(p => p.AppPage).WithMany()
             .HasForeignKey(p => p.AppPageId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => new { p.AppUserId, p.AppPageId }).IsUnique();
        });
    }
}

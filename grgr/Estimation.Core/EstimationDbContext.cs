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
    public DbSet<Label> Labels => Set<Label>();
    public DbSet<FeatureLabel> FeatureLabels => Set<FeatureLabel>();
    public DbSet<JiraToken> JiraTokens => Set<JiraToken>();

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

        modelBuilder.Entity<CapitalProject>(e =>
        {
            e.HasKey(cp => cp.Id);
            e.Property(cp => cp.JiraKey).HasMaxLength(10);
            e.Property(cp => cp.Name).IsRequired().HasMaxLength(100);
            e.Property(cp => cp.Description).HasMaxLength(500);

            e.HasIndex(cp => cp.JiraKey);
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
            e.Property(pp => pp.Name).IsRequired().HasMaxLength(100);
            e.Property(pp => pp.JiraId).HasMaxLength(100);
            e.Property(pp => pp.Description).HasMaxLength(500);
            e.Property(pp => pp.Comments).HasMaxLength(250);
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
            e.Property(pe => pe.JiraId).HasMaxLength(100);
            e.Property(pe => pe.Name).HasMaxLength(100);
            e.Property(pe => pe.Description).HasMaxLength(500);
            e.Property(pe => pe.Comments).HasMaxLength(250);
        });

        modelBuilder.Entity<BusinessOutcome>(e =>
        {
            e.HasKey(bo => bo.Id);
            e.Property(bo => bo.JiraId).HasMaxLength(100);
            e.Property(bo => bo.Name).HasMaxLength(100);
            e.Property(bo => bo.Description).HasMaxLength(500);
            e.Property(bo => bo.Comments).HasMaxLength(250);
            e.Property(bo => bo.ArtName).HasMaxLength(200);
            e.HasOne(bo => bo.PortfolioEpic).WithMany(pe => pe.BusinessOutcomes)
             .HasForeignKey(bo => bo.PortfolioEpicId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);

            e.HasIndex(bo => bo.PortfolioEpicId);
            e.HasIndex(bo => bo.Ranking);
            e.HasIndex(bo => bo.ArtName);
        });

        modelBuilder.Entity<Feature>(e =>
        {
            e.HasKey(f => f.Id);
            e.Property(f => f.ProjectKey).IsRequired().HasMaxLength(10);
            e.Property(f => f.JiraId).HasMaxLength(100);
            e.Property(f => f.Summary).IsRequired(false).HasMaxLength(255);
            e.Property(f => f.Name).IsRequired(false).HasMaxLength(200);
            e.Property(f => f.Description).HasMaxLength(32767);
            e.Property(f => f.Comments).HasMaxLength(250);
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

        modelBuilder.Entity<Label>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.Name).IsRequired().HasMaxLength(255);
            e.HasIndex(l => l.Name).IsUnique();
        });

        modelBuilder.Entity<FeatureLabel>(e =>
        {
            e.HasKey(fl => new { fl.FeatureId, fl.LabelId });
            e.HasOne(fl => fl.Feature).WithMany(f => f.FeatureLabels)
             .HasForeignKey(fl => fl.FeatureId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(fl => fl.Label).WithMany(l => l.FeatureLabels)
             .HasForeignKey(fl => fl.LabelId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<JiraToken>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.UserName).IsRequired().HasMaxLength(256);
            e.Property(t => t.AccessToken).IsRequired().HasMaxLength(2000);
            e.Property(t => t.AccessTokenSecret).IsRequired().HasMaxLength(2000);
            e.HasIndex(t => t.UserName).IsUnique();
        });
    }
}

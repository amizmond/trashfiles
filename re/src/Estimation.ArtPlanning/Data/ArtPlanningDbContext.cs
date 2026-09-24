using Estimation.ArtPlanning.Models.Settings;
using Estimation.ArtPlanning.Models.Source;
using Microsoft.EntityFrameworkCore;

namespace Estimation.ArtPlanning.Data;

public class ArtPlanningDbContext : DbContext
{
    public const string MigrationsHistoryTable = "__ArtPlanningMigrationsHistory";

    public ArtPlanningDbContext(DbContextOptions<ArtPlanningDbContext> options) : base(options)
    {
    }

    public DbSet<ArtPlanningSkillRanking> SkillRankings => Set<ArtPlanningSkillRanking>();
    public DbSet<ArtPlanningSkillLevelAllocation> SkillLevelAllocations => Set<ArtPlanningSkillLevelAllocation>();
    public DbSet<ArtPlanningRoleCoefficient> RoleCoefficients => Set<ArtPlanningRoleCoefficient>();
    public DbSet<ArtPlanningRoleSkillLimit> RoleSkillLimits => Set<ArtPlanningRoleSkillLimit>();
    public DbSet<ArtPlanningTrainCoefficient> TrainCoefficients => Set<ArtPlanningTrainCoefficient>();

    public DbSet<SourceArt> CapitalProjects => Set<SourceArt>();
    public DbSet<SourceArtTeam> CapitalProjectTeams => Set<SourceArtTeam>();
    public DbSet<SourceTeam> Teams => Set<SourceTeam>();
    public DbSet<SourceTeamMember> TeamMembers => Set<SourceTeamMember>();
    public DbSet<SourceTeamRole> TeamRoles => Set<SourceTeamRole>();
    public DbSet<SourceHumanResource> HumanResources => Set<SourceHumanResource>();
    public DbSet<SourceHumanResourceSkill> HumanResourceSkills => Set<SourceHumanResourceSkill>();
    public DbSet<SourceSkill> Skills => Set<SourceSkill>();
    public DbSet<SourceSkillLevel> SkillLevels => Set<SourceSkillLevel>();
    public DbSet<SourcePi> Pis => Set<SourcePi>();
    public DbSet<SourceHoliday> Holidays => Set<SourceHoliday>();
    public DbSet<SourcePublicHoliday> PublicHolidays => Set<SourcePublicHoliday>();
    public DbSet<SourceAppPage> AppPages => Set<SourceAppPage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureSettings(modelBuilder);
        ConfigureSourceMirrors(modelBuilder);
    }

    private static void ConfigureSettings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ArtPlanningSkillRanking>(e =>
        {
            e.ToTable("ArtPlanningSkillRankings");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.CapitalProjectId, x.PiId, x.SkillId }).IsUnique().HasFilter(null);
        });

        modelBuilder.Entity<ArtPlanningSkillLevelAllocation>(e =>
        {
            e.ToTable("ArtPlanningSkillLevelAllocations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Ratio).HasColumnType("decimal(5,4)");
            e.HasIndex(x => new { x.CapitalProjectId, x.PiId, x.SkillLevelValue }).IsUnique().HasFilter(null);
        });

        modelBuilder.Entity<ArtPlanningRoleCoefficient>(e =>
        {
            e.ToTable("ArtPlanningRoleCoefficients");
            e.HasKey(x => x.Id);
            e.Property(x => x.Coefficient).HasColumnType("decimal(5,4)");
            e.HasIndex(x => new { x.CapitalProjectId, x.PiId, x.TeamRoleId }).IsUnique().HasFilter(null);
        });

        modelBuilder.Entity<ArtPlanningRoleSkillLimit>(e =>
        {
            e.ToTable("ArtPlanningRoleSkillLimits");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.CapitalProjectId, x.PiId, x.TeamRoleId, x.SkillId }).IsUnique().HasFilter(null);
        });

        modelBuilder.Entity<ArtPlanningTrainCoefficient>(e =>
        {
            e.ToTable("ArtPlanningTrainCoefficients");
            e.HasKey(x => x.Id);
            e.Property(x => x.Coefficient).HasColumnType("decimal(5,4)");
            e.HasIndex(x => new { x.CapitalProjectId, x.PiId }).IsUnique().HasFilter(null);
        });
    }

    private static void ConfigureSourceMirrors(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SourceArt>(e =>
        {
            e.ToTable("CapitalProjects", t => t.ExcludeFromMigrations());
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100);
        });

        modelBuilder.Entity<SourceArtTeam>(e =>
        {
            e.ToTable("CapitalProjectTeams", t => t.ExcludeFromMigrations());
            e.HasKey(x => new { x.CapitalProjectId, x.TeamId });
        });

        modelBuilder.Entity<SourceTeam>(e =>
        {
            e.ToTable("Teams", t => t.ExcludeFromMigrations());
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(50);
        });

        modelBuilder.Entity<SourceTeamMember>(e =>
        {
            e.ToTable("TeamMembers", t => t.ExcludeFromMigrations());
            e.HasKey(x => new { x.TeamId, x.HumanResourceId });
        });

        modelBuilder.Entity<SourceTeamRole>(e =>
        {
            e.ToTable("TeamRoles", t => t.ExcludeFromMigrations());
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(50);
        });

        modelBuilder.Entity<SourceHumanResource>(e =>
        {
            e.ToTable("HumanResources", t => t.ExcludeFromMigrations());
            e.HasKey(x => x.Id);
            e.Property(x => x.EmployeeNumber).HasMaxLength(30);
            e.Property(x => x.EmployeeName).HasMaxLength(70);
            e.Property(x => x.FullName).HasMaxLength(100);
        });

        modelBuilder.Entity<SourceHumanResourceSkill>(e =>
        {
            e.ToTable("HumanResourceSkills", t => t.ExcludeFromMigrations());
            e.HasKey(x => new { x.HumanResourceId, x.SkillId });
        });

        modelBuilder.Entity<SourceSkill>(e =>
        {
            e.ToTable("Skills", t => t.ExcludeFromMigrations());
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(50);
        });

        modelBuilder.Entity<SourceSkillLevel>(e =>
        {
            e.ToTable("SkillLevels", t => t.ExcludeFromMigrations());
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(50);
        });

        modelBuilder.Entity<SourcePi>(e =>
        {
            e.ToTable("Pis", t => t.ExcludeFromMigrations());
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100);
        });

        modelBuilder.Entity<SourceHoliday>(e =>
        {
            e.ToTable("Holidays", t => t.ExcludeFromMigrations());
            e.HasKey(x => x.Id);
        });

        modelBuilder.Entity<SourcePublicHoliday>(e =>
        {
            e.ToTable("PublicHolidays", t => t.ExcludeFromMigrations());
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100);
        });

        modelBuilder.Entity<SourceAppPage>(e =>
        {
            e.ToTable("AppPages", t => t.ExcludeFromMigrations());
            e.HasKey(x => x.Id);
            e.Property(x => x.Key).HasMaxLength(100);
            e.Property(x => x.DisplayName).HasMaxLength(100);
            e.Property(x => x.Group).HasMaxLength(50);
        });
    }
}

using Estimation.HrReporting.Models;
using Microsoft.EntityFrameworkCore;

namespace Estimation.HrReporting.Data;

public class HrReportingDbContext : DbContext
{
    public const string MigrationsHistoryTable = "__HrReportingMigrationsHistory";

    public HrReportingDbContext(DbContextOptions<HrReportingDbContext> options) : base(options)
    {
    }

    public DbSet<HeadcountUpload> HeadcountUploads => Set<HeadcountUpload>();

    public DbSet<HeadcountRow> HeadcountRows => Set<HeadcountRow>();

    internal DbSet<SourceAppPage> AppPages => Set<SourceAppPage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<HeadcountUpload>(e =>
        {
            e.ToTable("HrHeadcountUploads");
            e.HasKey(x => x.Id);
            e.Property(x => x.DataDate).HasColumnType("date");
            e.Property(x => x.FileName).HasMaxLength(HeadcountUpload.FileNameMaxLength).IsRequired();
            e.Property(x => x.Comment).HasMaxLength(HeadcountUpload.CommentMaxLength);
            e.Property(x => x.UploadedBy).HasMaxLength(HeadcountUpload.UploadedByMaxLength);
            e.Property(x => x.TotalHc).HasColumnType("decimal(18,4)");
            e.Property(x => x.TotalFte).HasColumnType("decimal(18,4)");
            e.HasIndex(x => new { x.DataDate, x.UploadedAt });
            e.HasMany(x => x.Rows)
                .WithOne(x => x.Upload)
                .HasForeignKey(x => x.UploadId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HeadcountRow>(e =>
        {
            e.ToTable("HrHeadcountRows");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UploadId, x.Brid }).IsUnique();

            foreach (var column in HeadcountColumns.All)
            {
                var property = e.Property(column.Property.PropertyType, column.Key);
                switch (column.Kind)
                {
                    case HeadcountValueKind.Text:
                        property.HasMaxLength(column.MaxLength);
                        break;
                    case HeadcountValueKind.Date:
                        property.HasColumnType("date");
                        break;
                    case HeadcountValueKind.Number:
                        property.HasColumnType("decimal(12,4)");
                        break;
                }
            }

            e.Property(x => x.Brid).IsRequired();
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

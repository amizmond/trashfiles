using Estimation.Core.Features.Hygiene.Models;
using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Train.Models;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.Features.Hygiene.Data;

public static class FeatureHygieneModelConfiguration
{
    public static ModelBuilder ApplyFeatureHygiene(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FeatureHygieneRule>(e =>
        {
            e.ToTable("FeatureHygieneRules");
            e.HasKey(r => r.Id);

            e.Property(r => r.Field).HasConversion<string>().HasMaxLength(50);
            e.Property(r => r.Check).HasConversion<string>().HasMaxLength(50);
            e.Property(r => r.ParametersJson).HasMaxLength(FeatureHygieneRule.MaxParametersLength);
            e.Property(r => r.Message).HasMaxLength(FeatureHygieneRule.MaxMessageLength);
            e.Property(r => r.CreatedBy).HasMaxLength(256);
            e.Property(r => r.ModifiedBy).HasMaxLength(256);

            e.HasOne<CapitalProject>().WithMany()
             .HasForeignKey(r => r.CapitalProjectId).OnDelete(DeleteBehavior.Cascade);

            e.HasOne<Pi>().WithMany()
             .HasForeignKey(r => r.PiId).OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(r => new { r.CapitalProjectId, r.PiId, r.SortOrder });
        });

        return modelBuilder;
    }

    public static DbSet<FeatureHygieneRule> FeatureHygieneRules(this EstimationDbContext db) =>
        db.Set<FeatureHygieneRule>();
}

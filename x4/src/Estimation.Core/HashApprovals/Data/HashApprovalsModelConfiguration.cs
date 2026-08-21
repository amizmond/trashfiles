using Estimation.Core.HashApprovals.Models;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.HashApprovals.Data;

/// <summary>
/// Everything the hash-approval table needs from the EF model, kept out of <see cref="EstimationDbContext"/>
/// so the whole feature area can be removed by deleting this folder and dropping its table.
/// </summary>
public static class HashApprovalsModelConfiguration
{
    public static ModelBuilder ApplyHashApprovals(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FeatureStateApproval>(e =>
        {
            // Explicit table name: the context has no DbSet property for this entity, and without
            // it EF would fall back to the singular entity name.
            e.ToTable("FeatureStateApprovals");
            e.HasKey(a => a.Id);
            e.Property(a => a.ArtName).IsRequired().HasMaxLength(100);
            e.Property(a => a.PiName).IsRequired().HasMaxLength(100);
            e.Property(a => a.FeatureKey).IsRequired().HasMaxLength(100);
            e.Property(a => a.JiraId).HasMaxLength(100);
            e.Property(a => a.FeatureName).HasMaxLength(255);
            e.Property(a => a.StateHash).IsRequired().HasMaxLength(80);
            e.Property(a => a.Comment).HasMaxLength(1000);
            e.Property(a => a.ApprovedBy).HasMaxLength(256);
            e.Property(a => a.WithdrawnBy).HasMaxLength(256);

            // Deltas load every active approval of one ART and PI in a single query.
            e.HasIndex(a => new { a.ArtName, a.PiName, a.WithdrawnAt });
            e.HasIndex(a => new { a.ArtName, a.PiName, a.FeatureKey });
        });

        return modelBuilder;
    }

    // The context deliberately exposes no DbSet property for this entity; the accessor keeps the
    // service code readable without touching EstimationDbContext.
    public static DbSet<FeatureStateApproval> FeatureStateApprovals(this EstimationDbContext db) =>
        db.Set<FeatureStateApproval>();
}

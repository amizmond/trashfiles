using Estimation.Core.ReviewRounds.Models;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.ReviewRounds.Data;

/// <summary>
/// Everything the review-round tables need from the EF model, kept out of <see cref="EstimationDbContext"/>
/// so the whole feature area can be removed by deleting this folder and dropping its tables.
/// </summary>
public static class ReviewRoundsModelConfiguration
{
    public static ModelBuilder ApplyReviewRounds(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FeatureChangeReview>(e =>
        {
            // Explicit table names: the context has no DbSet properties for these entities, and
            // without them EF would fall back to the singular entity name.
            e.ToTable("FeatureChangeReviews");
            e.HasKey(r => r.Id);
            e.Property(r => r.ArtName).IsRequired().HasMaxLength(100);
            e.Property(r => r.ArtJiraKey).HasMaxLength(10);
            e.Property(r => r.PiName).IsRequired().HasMaxLength(100);
            e.Property(r => r.Name).HasMaxLength(200);
            e.Property(r => r.CreatedBy).HasMaxLength(256);
            e.Property(r => r.CompletedBy).HasMaxLength(256);
            e.Property(r => r.Status).HasConversion<int>();

            // The two snapshots are the immutable evidence of what was reviewed, so they must
            // not disappear underneath a review. ReviewRoundSnapshotDeletionGuard refuses to
            // delete a referenced snapshot with a readable message before this FK would trip.
            e.HasOne(r => r.BaselineSnapshot).WithMany()
             .HasForeignKey(r => r.BaselineSnapshotId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.ReviewSnapshot).WithMany()
             .HasForeignKey(r => r.ReviewSnapshotId).OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(r => new { r.ArtName, r.PiName, r.CreatedAt });
        });

        modelBuilder.Entity<FeatureChangeReviewItem>(e =>
        {
            e.ToTable("FeatureChangeReviewItems");
            e.HasKey(i => i.Id);
            e.Property(i => i.FeatureKey).IsRequired().HasMaxLength(100);
            e.Property(i => i.JiraId).HasMaxLength(100);
            e.Property(i => i.FeatureName).HasMaxLength(255);
            e.Property(i => i.Summary).HasMaxLength(255);
            e.Property(i => i.ChangeKind).HasConversion<int>();
            e.Property(i => i.Decision).HasConversion<int>();
            e.Property(i => i.Comment).HasMaxLength(1000);
            e.Property(i => i.DecidedBy).HasMaxLength(256);

            e.HasOne(i => i.Review).WithMany(r => r.Items)
             .HasForeignKey(i => i.ReviewId).OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(i => new { i.ReviewId, i.Decision });
            e.HasIndex(i => new { i.ReviewId, i.FeatureKey });
        });

        return modelBuilder;
    }

    // The context deliberately exposes no DbSet properties for these entities; the accessors
    // below keep the service code readable without touching EstimationDbContext.
    public static DbSet<FeatureChangeReview> FeatureChangeReviews(this EstimationDbContext db) =>
        db.Set<FeatureChangeReview>();

    public static DbSet<FeatureChangeReviewItem> FeatureChangeReviewItems(this EstimationDbContext db) =>
        db.Set<FeatureChangeReviewItem>();
}

using Estimation.Core.Administration.Audit;
using Estimation.Core.Features.Models;
using Estimation.Core.Features.Services;
using Estimation.Core.Risks.Models;
using Estimation.Core.Risks.Services;
using Estimation.Core.Tests.Infrastructure;
using Xunit;

namespace Estimation.Core.Tests.Features;

public class CommentTimeZoneTests
{
    private readonly InMemoryDatabase _db = new();

    private static readonly TimeZoneInfo Tokyo = TimeZoneInfo.CreateCustomTimeZone("Test/Tokyo", TimeSpan.FromHours(9), "Test/Tokyo", "Test/Tokyo");

    private static readonly DateTime CreatedAtUtc = new(2026, 8, 18, 22, 30, 0, DateTimeKind.Utc);

    private sealed class StubAuditUser : IAuditUserProvider
    {
        public string? GetCurrentUserName() => "DOMAIN\tester";
    }

    [Fact]
    public async Task Feature_comment_times_are_rendered_in_the_requested_zone()
    {
        await _db.SeedAsync(db =>
        {
            db.Features.Add(new Feature { Id = 1, JiraId = "PAY-1", Summary = "A feature" });
            db.FeatureComments.Add(new FeatureComment
            {
                Id = 1,
                FeatureId = 1,
                Text = "Ready for review",
                Author = "DOMAIN\tester",
                CreatedAt = CreatedAtUtc
            });
        });

        var service = new FeatureCommentService(_db, new StubAuditUser());

        var united = await service.GetUnitedAsync([1], Tokyo);

        Assert.Contains("2026-08-19 07:30", united[1]);
    }

    [Fact]
    public async Task Risk_comment_times_are_rendered_in_the_requested_zone()
    {
        await _db.SeedAsync(db =>
        {
            db.Risks.Add(new Risk { Id = 1, Summary = "Vendor delay" });
            db.RiskComments.Add(new RiskComment
            {
                Id = 1,
                RiskId = 1,
                Text = "Escalated",
                Author = "DOMAIN\tester",
                CreatedAt = CreatedAtUtc
            });
        });

        var service = new RiskCommentService(_db, new StubAuditUser());

        var united = await service.GetUnitedAsync([1], Tokyo);

        Assert.Contains("2026-08-19 07:30", united[1]);
    }

    [Fact]
    public async Task Two_zones_render_the_same_stored_comment_differently()
    {
        await _db.SeedAsync(db =>
        {
            db.Features.Add(new Feature { Id = 1, JiraId = "PAY-1", Summary = "A feature" });
            db.FeatureComments.Add(new FeatureComment
            {
                Id = 1,
                FeatureId = 1,
                Text = "Ready for review",
                CreatedAt = CreatedAtUtc
            });
        });

        var service = new FeatureCommentService(_db, new StubAuditUser());

        var tokyo = await service.GetUnitedAsync([1], Tokyo);
        var utc = await service.GetUnitedAsync([1], TimeZoneInfo.Utc);

        Assert.Contains("2026-08-19 07:30", tokyo[1]);
        Assert.Contains("2026-08-18 22:30", utc[1]);
    }
}

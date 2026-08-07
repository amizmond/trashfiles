using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.Poker.Services;

public record PokerBusinessOutcome(string Key, string Name);

public interface IPokerBusinessOutcomeService
{
    Task<PokerBusinessOutcome?> ResolveAsync(string? parentLinkKey, string? featureLinkKey);
}

public class PokerBusinessOutcomeService : IPokerBusinessOutcomeService
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;

    public PokerBusinessOutcomeService(IDbContextFactory<EstimationDbContext> ctx)
    {
        _ctx = ctx;
    }

    public async Task<PokerBusinessOutcome?> ResolveAsync(string? parentLinkKey, string? featureLinkKey)
    {
        if (string.IsNullOrWhiteSpace(parentLinkKey) && string.IsNullOrWhiteSpace(featureLinkKey))
        {
            return null;
        }

        await using var db = await _ctx.CreateDbContextAsync();

        if (!string.IsNullOrWhiteSpace(parentLinkKey))
        {
            var direct = await db.BusinessOutcomes
                .AsNoTracking()
                .Where(bo => bo.JiraId == parentLinkKey)
                .Select(bo => new PokerBusinessOutcome(bo.JiraId!, bo.Summary))
                .FirstOrDefaultAsync();
            if (direct is not null)
            {
                return direct;
            }
        }

        if (string.IsNullOrWhiteSpace(featureLinkKey))
        {
            return null;
        }

        return await db.Features
            .AsNoTracking()
            .Where(f => f.JiraId == featureLinkKey
                        && f.BusinessOutcome != null
                        && f.BusinessOutcome.JiraId != null)
            .Select(f => new PokerBusinessOutcome(f.BusinessOutcome!.JiraId!, f.BusinessOutcome.Summary))
            .FirstOrDefaultAsync();
    }
}

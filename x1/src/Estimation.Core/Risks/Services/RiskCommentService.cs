using Estimation.Core.Administration.Audit;
using Estimation.Core.Risks.Models;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.Risks.Services;

public record RiskCommentVm(int Id, int RiskId, string Text, string? Author, string? AuthorDisplayName, DateTime CreatedAt)
{
    public string? DisplayAuthor => string.IsNullOrWhiteSpace(AuthorDisplayName) ? Author : AuthorDisplayName;

    public bool IsDone { get; init; }
    public string? DoneBy { get; init; }
    public string? DoneByDisplayName { get; init; }
    public DateTime? DoneAt { get; init; }

    public string? DisplayDoneBy => string.IsNullOrWhiteSpace(DoneByDisplayName) ? DoneBy : DoneByDisplayName;
}

public interface IRiskCommentService
{
    Task<List<RiskCommentVm>> GetForRiskAsync(int riskId);

    Task<Dictionary<int, int>> GetCountsAsync(IReadOnlyCollection<int> riskIds);

    Task<Dictionary<int, string>> GetUnitedAsync(IReadOnlyCollection<int> riskIds, TimeZoneInfo? timeZone = null);

    Task<RiskCommentVm> AddAsync(int riskId, string text, string? author = null);

    Task<bool> DeleteAsync(int commentId, string? requestedBy = null);

    Task<RiskCommentVm?> SetDoneAsync(int commentId, bool isDone, string? user = null);
}

public class RiskCommentService : IRiskCommentService
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;
    private readonly IAuditUserProvider _auditUser;

    public RiskCommentService(IDbContextFactory<EstimationDbContext> ctx, IAuditUserProvider auditUser)
    {
        _ctx = ctx;
        _auditUser = auditUser;
    }

    public async Task<List<RiskCommentVm>> GetForRiskAsync(int riskId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await (
                from c in db.RiskComments
                where c.RiskId == riskId
                join au in db.AppUsers on c.Author equals au.WindowsUserName into users
                from au in users.DefaultIfEmpty()
                join du in db.AppUsers on c.DoneBy equals du.WindowsUserName into doneUsers
                from du in doneUsers.DefaultIfEmpty()
                orderby c.CreatedAt, c.Id
                select new RiskCommentVm(c.Id, c.RiskId, c.Text, c.Author,
                    au != null ? au.DisplayName : null, c.CreatedAt)
                {
                    IsDone = c.IsDone,
                    DoneBy = c.DoneBy,
                    DoneByDisplayName = du != null ? du.DisplayName : null,
                    DoneAt = c.DoneAt,
                })
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<Dictionary<int, int>> GetCountsAsync(IReadOnlyCollection<int> riskIds)
    {
        if (riskIds.Count == 0)
        {
            return new Dictionary<int, int>();
        }

        var ids = riskIds.Distinct().ToList();
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.RiskComments
            .Where(c => ids.Contains(c.RiskId) && !c.IsDone)
            .GroupBy(c => c.RiskId)
            .Select(g => new { RiskId = g.Key, Count = g.Count() })
            .AsNoTracking()
            .ToDictionaryAsync(x => x.RiskId, x => x.Count);
    }

    public async Task<Dictionary<int, string>> GetUnitedAsync(IReadOnlyCollection<int> riskIds, TimeZoneInfo? timeZone = null)
    {
        if (riskIds.Count == 0)
        {
            return new Dictionary<int, string>();
        }

        var ids = riskIds.Distinct().ToList();
        await using var db = await _ctx.CreateDbContextAsync();
        var comments = await (
                from c in db.RiskComments
                where ids.Contains(c.RiskId)
                join au in db.AppUsers on c.Author equals au.WindowsUserName into users
                from au in users.DefaultIfEmpty()
                orderby c.RiskId, c.CreatedAt descending, c.Id descending
                select new { c.RiskId, c.Text, c.Author, DisplayName = au != null ? au.DisplayName : null, c.CreatedAt })
            .AsNoTracking()
            .ToListAsync();

        return comments
            .GroupBy(c => c.RiskId)
            .ToDictionary(
                g => g.Key,
                g => string.Join("\n\n", g.Select(c =>
                {
                    var user = !string.IsNullOrWhiteSpace(c.DisplayName) ? c.DisplayName
                        : !string.IsNullOrWhiteSpace(c.Author) ? c.Author
                        : "Unknown";
                    var time = TimeZoneInfo.ConvertTimeFromUtc(
                        DateTime.SpecifyKind(c.CreatedAt, DateTimeKind.Utc),
                        timeZone ?? TimeZoneInfo.Local).ToString("yyyy-MM-dd HH:mm");
                    return $"{user} / {time}\n{c.Text.Trim()}";
                })));
    }

    public async Task<RiskCommentVm> AddAsync(int riskId, string text, string? author = null)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException("Comment text is required.", nameof(text));
        }
        if (trimmed.Length > RiskComment.MaxTextLength)
        {
            trimmed = trimmed[..RiskComment.MaxTextLength];
        }

        var resolvedAuthor = string.IsNullOrWhiteSpace(author) ? _auditUser.GetCurrentUserName() : author.Trim();
        if (resolvedAuthor is { Length: > 256 })
        {
            resolvedAuthor = resolvedAuthor[..256];
        }

        await using var db = await _ctx.CreateDbContextAsync();
        var entity = new RiskComment
        {
            RiskId = riskId,
            Text = trimmed,
            Author = resolvedAuthor,
            CreatedAt = DateTime.UtcNow,
        };
        db.RiskComments.Add(entity);
        await db.SaveChangesAsync();

        var displayName = await db.AppUsers
            .Where(u => u.WindowsUserName == entity.Author)
            .Select(u => u.DisplayName)
            .FirstOrDefaultAsync();

        return new RiskCommentVm(entity.Id, entity.RiskId, entity.Text, entity.Author, displayName, entity.CreatedAt);
    }

    public async Task<bool> DeleteAsync(int commentId, string? requestedBy = null)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var entity = await db.RiskComments.FirstOrDefaultAsync(c => c.Id == commentId);
        if (entity is null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(requestedBy)
            && !string.Equals(entity.Author, requestedBy.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        db.RiskComments.Remove(entity);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<RiskCommentVm?> SetDoneAsync(int commentId, bool isDone, string? user = null)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var entity = await db.RiskComments.FirstOrDefaultAsync(c => c.Id == commentId);
        if (entity is null)
        {
            return null;
        }

        if (isDone)
        {
            var resolvedUser = string.IsNullOrWhiteSpace(user) ? _auditUser.GetCurrentUserName() : user.Trim();
            if (resolvedUser is { Length: > 256 })
            {
                resolvedUser = resolvedUser[..256];
            }
            entity.IsDone = true;
            entity.DoneBy = resolvedUser;
            entity.DoneAt = DateTime.UtcNow;
        }
        else
        {
            entity.IsDone = false;
            entity.DoneBy = null;
            entity.DoneAt = null;
        }

        await db.SaveChangesAsync();

        var names = await db.AppUsers
            .Where(u => u.WindowsUserName == entity.Author || u.WindowsUserName == entity.DoneBy)
            .Select(u => new { u.WindowsUserName, u.DisplayName })
            .ToListAsync();

        return new RiskCommentVm(entity.Id, entity.RiskId, entity.Text, entity.Author,
            names.FirstOrDefault(u => u.WindowsUserName == entity.Author)?.DisplayName, entity.CreatedAt)
        {
            IsDone = entity.IsDone,
            DoneBy = entity.DoneBy,
            DoneByDisplayName = names.FirstOrDefault(u => u.WindowsUserName == entity.DoneBy)?.DisplayName,
            DoneAt = entity.DoneAt,
        };
    }
}

using Estimation.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.Services;

public class PagePermissionDto
{
    public int AppPageId { get; set; }
    public AccessLevel AccessLevel { get; set; }
}

public class NavPageInfo
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Group { get; set; }
    public bool IsAdminOnly { get; set; }
    public int SortOrder { get; set; }
    public AccessLevel AccessLevel { get; set; }
}

public interface IAppAuthorizationService
{
    Task<AppUser?> GetCurrentUserAsync(string windowsUserName);
    Task<bool> IsAuthorizedAsync(string windowsUserName);
    Task<bool> IsAdminAsync(string windowsUserName);
    Task RequestAccessAsync(string windowsUserName, string? displayName = null, string? samAccountName = null, string? employeeId = null);
    Task<AccessLevel> GetPageAccessAsync(string windowsUserName, string pageKey);
    Task<List<NavPageInfo>> GetNavigationPagesAsync(string windowsUserName);
    Task<bool> HasPendingRequestsAsync();
    Task<List<AppUser>> GetPendingRequestsAsync();
    Task<List<AppUser>> GetAllUsersAsync();
    Task<AppUser?> GetUserByIdAsync(int userId);
    Task<List<AppPage>> GetAllPagesAsync();
    Task<List<AppUserPagePermission>> GetUserPermissionsAsync(int userId);
    Task ApproveUserAsync(int userId, bool isAdmin, List<PagePermissionDto> permissions, string approvedBy);
    Task UpdateUserPermissionsAsync(int userId, bool isAdmin, List<PagePermissionDto> permissions);
    Task AddUserAsync(string windowsUserName, string? displayName, bool isAdmin, List<PagePermissionDto> permissions, string createdBy, string? samAccountName = null, string? employeeId = null);
    Task<bool> SetAdInfoAsync(int userId, string? displayName, string? samAccountName, string? employeeId);
    Task RevokeUserAsync(int userId);
    Task DeleteUserAsync(int userId);
}

public class AppAuthorizationService : IAppAuthorizationService
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;

    public AppAuthorizationService(IDbContextFactory<EstimationDbContext> ctx) => _ctx = ctx;

    public async Task<AppUser?> GetCurrentUserAsync(string windowsUserName)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.AppUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.WindowsUserName == windowsUserName);
    }

    public async Task<bool> IsAuthorizedAsync(string windowsUserName)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.AppUsers
            .AsNoTracking()
            .AnyAsync(u => u.WindowsUserName == windowsUserName && u.IsApproved);
    }

    public async Task<bool> IsAdminAsync(string windowsUserName)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.AppUsers
            .AsNoTracking()
            .AnyAsync(u => u.WindowsUserName == windowsUserName && u.IsApproved && u.IsAdmin);
    }

    public async Task RequestAccessAsync(string windowsUserName, string? displayName = null, string? samAccountName = null, string? employeeId = null)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var existing = await db.AppUsers
            .FirstOrDefaultAsync(u => u.WindowsUserName == windowsUserName);

        if (existing is not null)
        {
            if (!existing.IsAccessRequested && !existing.IsApproved)
            {
                existing.IsAccessRequested = true;
                existing.RequestedAt = DateTime.UtcNow;
            }

            ApplyAdInfo(existing, displayName, samAccountName, employeeId);
            await db.SaveChangesAsync();
            return;
        }

        var user = new AppUser
        {
            WindowsUserName = windowsUserName,
            IsAccessRequested = true,
            RequestedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        ApplyAdInfo(user, displayName, samAccountName, employeeId);
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
    }

    public async Task<AccessLevel> GetPageAccessAsync(string windowsUserName, string pageKey)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var user = await db.AppUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.WindowsUserName == windowsUserName && u.IsApproved);

        if (user is null)
        {
            return AccessLevel.None;
        }

        if (user.IsAdmin)
        {
            return AccessLevel.Edit;
        }

        var page = await db.AppPages
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Key == pageKey);

        if (page is null || page.IsAdminOnly)
        {
            return AccessLevel.None;
        }

        var permission = await db.AppUserPagePermissions
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.AppUserId == user.Id && p.AppPageId == page.Id);

        return permission?.AccessLevel ?? AccessLevel.None;
    }

    public async Task<List<NavPageInfo>> GetNavigationPagesAsync(string windowsUserName)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var user = await db.AppUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.WindowsUserName == windowsUserName && u.IsApproved);

        if (user is null)
        {
            return [];
        }

        var allPages = await db.AppPages
            .AsNoTracking()
            .OrderBy(p => p.SortOrder)
            .ToListAsync();

        if (user.IsAdmin)
        {
            return allPages.Select(p => new NavPageInfo
            {
                Key = p.Key,
                DisplayName = p.DisplayName,
                Group = p.Group,
                IsAdminOnly = p.IsAdminOnly,
                SortOrder = p.SortOrder,
                AccessLevel = AccessLevel.Edit
            }).ToList();
        }

        var permissions = await db.AppUserPagePermissions
            .AsNoTracking()
            .Where(p => p.AppUserId == user.Id)
            .ToDictionaryAsync(p => p.AppPageId, p => p.AccessLevel);

        return allPages
            .Where(p => !p.IsAdminOnly && permissions.ContainsKey(p.Id) && permissions[p.Id] != AccessLevel.None)
            .Select(p => new NavPageInfo
            {
                Key = p.Key,
                DisplayName = p.DisplayName,
                Group = p.Group,
                IsAdminOnly = p.IsAdminOnly,
                SortOrder = p.SortOrder,
                AccessLevel = permissions[p.Id]
            }).ToList();
    }

    public async Task<bool> HasPendingRequestsAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.AppUsers
            .AsNoTracking()
            .AnyAsync(u => u.IsAccessRequested && !u.IsApproved);
    }

    public async Task<List<AppUser>> GetPendingRequestsAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.AppUsers
            .AsNoTracking()
            .Where(u => u.IsAccessRequested && !u.IsApproved)
            .OrderBy(u => u.RequestedAt)
            .ToListAsync();
    }

    public async Task<List<AppUser>> GetAllUsersAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.AppUsers
            .AsNoTracking()
            .OrderBy(u => u.WindowsUserName)
            .ToListAsync();
    }

    public async Task<AppUser?> GetUserByIdAsync(int userId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.AppUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId);
    }

    public async Task<List<AppPage>> GetAllPagesAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.AppPages
            .AsNoTracking()
            .OrderBy(p => p.SortOrder)
            .ToListAsync();
    }

    public async Task<List<AppUserPagePermission>> GetUserPermissionsAsync(int userId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.AppUserPagePermissions
            .AsNoTracking()
            .Include(p => p.AppPage)
            .Where(p => p.AppUserId == userId)
            .ToListAsync();
    }

    public async Task ApproveUserAsync(int userId, bool isAdmin, List<PagePermissionDto> permissions, string approvedBy)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var user = await db.AppUsers.FindAsync(userId);
        if (user is null)
        {
            return;
        }

        user.IsApproved = true;
        user.IsAccessRequested = false;
        user.IsAdmin = isAdmin;
        user.ApprovedAt = DateTime.UtcNow;
        user.ApprovedBy = approvedBy;

        await SavePermissionsAsync(db, userId, permissions);
        await db.SaveChangesAsync();
    }

    public async Task UpdateUserPermissionsAsync(int userId, bool isAdmin, List<PagePermissionDto> permissions)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var user = await db.AppUsers.FindAsync(userId);
        if (user is null)
        {
            return;
        }

        user.IsAdmin = isAdmin;
        await SavePermissionsAsync(db, userId, permissions);
        await db.SaveChangesAsync();
    }

    public async Task AddUserAsync(string windowsUserName, string? displayName, bool isAdmin, List<PagePermissionDto> permissions, string createdBy, string? samAccountName = null, string? employeeId = null)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var existing = await db.AppUsers
            .FirstOrDefaultAsync(u => u.WindowsUserName == windowsUserName);

        if (existing is not null)
        {
            return;
        }

        var user = new AppUser
        {
            WindowsUserName = windowsUserName,
            DisplayName = displayName,
            SamAccountName = samAccountName,
            EmployeeId = employeeId,
            IsAdmin = isAdmin,
            IsApproved = true,
            IsAccessRequested = false,
            ApprovedAt = DateTime.UtcNow,
            ApprovedBy = createdBy,
            CreatedAt = DateTime.UtcNow
        };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();

        await SavePermissionsAsync(db, user.Id, permissions);
        await db.SaveChangesAsync();
    }

    public async Task<bool> SetAdInfoAsync(int userId, string? displayName, string? samAccountName, string? employeeId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var user = await db.AppUsers.FindAsync(userId);
        if (user is null)
        {
            return false;
        }

        ApplyAdInfo(user, displayName, samAccountName, employeeId);
        await db.SaveChangesAsync();
        return true;
    }

    private static void ApplyAdInfo(AppUser user, string? displayName, string? samAccountName, string? employeeId)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            user.DisplayName = displayName;
        }

        if (!string.IsNullOrWhiteSpace(samAccountName))
        {
            user.SamAccountName = samAccountName;
        }

        if (!string.IsNullOrWhiteSpace(employeeId))
        {
            user.EmployeeId = employeeId;
        }
    }

    public async Task RevokeUserAsync(int userId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var user = await db.AppUsers.FindAsync(userId);
        if (user is null)
        {
            return;
        }

        user.IsApproved = false;
        user.IsAdmin = false;
        await db.SaveChangesAsync();
    }

    public async Task DeleteUserAsync(int userId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var user = await db.AppUsers.FindAsync(userId);
        if (user is null)
        {
            return;
        }

        db.AppUsers.Remove(user);
        await db.SaveChangesAsync();
    }

    private static async Task SavePermissionsAsync(EstimationDbContext db, int userId, List<PagePermissionDto> permissions)
    {
        var existing = await db.AppUserPagePermissions
            .Where(p => p.AppUserId == userId)
            .ToListAsync();

        db.AppUserPagePermissions.RemoveRange(existing);

        var newPermissions = permissions
            .Where(p => p.AccessLevel != AccessLevel.None)
            .Select(p => new AppUserPagePermission
            {
                AppUserId = userId,
                AppPageId = p.AppPageId,
                AccessLevel = p.AccessLevel
            });

        db.AppUserPagePermissions.AddRange(newPermissions);
    }
}

using Estimation.Core.Administration.Models;
using Estimation.Core.Resources.Services;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.Administration.Services;

public class NavPageInfo
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Group { get; set; }
    public bool IsAdminOnly { get; set; }
    public int SortOrder { get; set; }
    public AccessLevel AccessLevel { get; set; }
}

public class AppUserListRow
{
    public AppUser User { get; set; } = null!;
    public List<string> Teams { get; set; } = [];
    public List<string> TeamRoles { get; set; } = [];
    public List<string> Profiles { get; set; } = [];
    public List<string> Arts { get; set; } = [];
    public string? RequestedDepartment { get; set; }
    public string? RequestedArt { get; set; }
}

public class AccessRequestRow
{
    public AppUser User { get; set; } = null!;
    public string? RequestedDepartment { get; set; }
    public string? RequestedArt { get; set; }
}

public class AccessRequestInfo
{
    public int? DepartmentId { get; set; }
    public int? CapitalProjectId { get; set; }
    public string? Comment { get; set; }
}

public class SelfServiceTeam
{
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
}

public class SelfServiceUser
{
    public int HumanResourceId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public List<SelfServiceTeam> Teams { get; set; } = [];
}

public interface IAppAuthorizationService
{
    Task<AppUser?> GetCurrentUserAsync(string windowsUserName);

    Task<SelfServiceUser?> FindSelfServiceUserAsync(string? employeeId, string? samAccountName = null);

    Task<bool> IsHrInTeamAsync(int humanResourceId, int teamId);
    Task<bool> IsAuthorizedAsync(string windowsUserName);
    Task<bool> IsAdminAsync(string windowsUserName);
    Task RequestAccessAsync(string windowsUserName, string? displayName = null, string? samAccountName = null, string? employeeId = null, string? emailAddress = null, AccessRequestInfo? request = null);
    Task<AccessLevel> GetPageAccessAsync(string windowsUserName, string pageKey);
    Task<List<NavPageInfo>> GetNavigationPagesAsync(string windowsUserName);
    Task<bool> HasPendingRequestsAsync();
    Task<List<AccessRequestRow>> GetPendingRequestsAsync();
    Task<List<AppUser>> GetAllUsersAsync();
    Task<List<AppUserListRow>> GetUserRowsAsync();
    Task<AppUser?> GetUserByIdAsync(int userId);
    Task<List<AppPage>> GetAllPagesAsync();
    Task<List<int>> GetUserProfileIdsAsync(int userId);
    Task ApproveUserAsync(int userId, bool isAdmin, List<int> profileIds, string approvedBy);
    Task UpdateUserProfilesAsync(int userId, bool isAdmin, List<int> profileIds);
    Task AddUserAsync(string windowsUserName, string? displayName, bool isAdmin, List<int> profileIds, string createdBy, string? samAccountName = null, string? employeeId = null, string? emailAddress = null);
    Task<bool> SetAdInfoAsync(int userId, string? displayName, string? samAccountName, string? employeeId, string? emailAddress);
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

    public async Task<SelfServiceUser?> FindSelfServiceUserAsync(string? employeeId, string? samAccountName = null)
    {
        var key = EmployeeNumberHelper.ResolveUserKey(employeeId, samAccountName);
        var digits = EmployeeNumberHelper.Normalize(key);
        if (digits is null)
        {
            return null;
        }

        await using var db = await _ctx.CreateDbContextAsync();

        var candidates = await db.HumanResources
            .AsNoTracking()
            .Where(hr => hr.EmployeeNumber != null && hr.EmployeeNumber.EndsWith(digits))
            .Select(hr => new
            {
                hr.Id,
                hr.EmployeeNumber,
                hr.FullName,
                Teams = hr.TeamMembers
                    .Select(tm => new { tm.TeamId, TeamName = tm.Team.Name })
                    .ToList()
            })
            .ToListAsync();

        var matches = candidates
            .Where(c => EmployeeNumberHelper.Matches(c.EmployeeNumber, key))
            .ToList();

        if (matches.Count != 1)
        {
            return null;
        }

        var match = matches[0];
        return new SelfServiceUser
        {
            HumanResourceId = match.Id,
            FullName = match.FullName,
            Teams = match.Teams
                .DistinctBy(t => t.TeamId)
                .Select(t => new SelfServiceTeam { TeamId = t.TeamId, TeamName = t.TeamName })
                .OrderBy(t => t.TeamName)
                .ToList()
        };
    }

    public async Task<bool> IsHrInTeamAsync(int humanResourceId, int teamId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.TeamMembers
            .AsNoTracking()
            .AnyAsync(tm => tm.HumanResourceId == humanResourceId && tm.TeamId == teamId);
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

    public async Task RequestAccessAsync(string windowsUserName, string? displayName = null, string? samAccountName = null, string? employeeId = null, string? emailAddress = null, AccessRequestInfo? request = null)
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
                ApplyRequestInfo(existing, request);
            }

            ApplyAdInfo(existing, displayName, samAccountName, employeeId, emailAddress);
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
        ApplyRequestInfo(user, request);
        ApplyAdInfo(user, displayName, samAccountName, employeeId, emailAddress);
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

        if (page.ScopeMode == PageScopeMode.None)
        {
            return AccessLevel.Edit;
        }

        var levels = await ResolvePageLevelsAsync(db, user.Id);
        return levels.TryGetValue(page.Id, out var level) ? level : AccessLevel.None;
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

        var levels = await ResolvePageLevelsAsync(db, user.Id);

        return allPages
            .Where(p => !p.IsAdminOnly)
            .Select(p => new NavPageInfo
            {
                Key = p.Key,
                DisplayName = p.DisplayName,
                Group = p.Group,
                IsAdminOnly = p.IsAdminOnly,
                SortOrder = p.SortOrder,
                AccessLevel = p.ScopeMode == PageScopeMode.None
                    ? AccessLevel.Edit
                    : levels.GetValueOrDefault(p.Id, AccessLevel.None)
            })
            .Where(p => p.AccessLevel != AccessLevel.None)
            .ToList();
    }

    private static async Task<Dictionary<int, AccessLevel>> ResolvePageLevelsAsync(EstimationDbContext db, int userId)
    {
        var rows = await db.AppUserProfiles
            .AsNoTracking()
            .Where(up => up.AppUserId == userId)
            .SelectMany(up => up.Profile.PagePermissions)
            .Select(p => new { p.ProfileId, p.AppPageId, p.CapitalProjectId, p.AccessLevel })
            .ToListAsync();

        var result = new Dictionary<int, AccessLevel>();
        foreach (var pageGroup in rows.GroupBy(r => r.AppPageId))
        {
            var level = AccessLevel.None;
            foreach (var profileGroup in pageGroup.GroupBy(r => r.ProfileId))
            {
                var general = profileGroup.FirstOrDefault(r => r.CapitalProjectId is null);
                var profileLevel = general is not null
                    ? general.AccessLevel
                    : profileGroup.Max(r => r.AccessLevel);
                if (profileLevel > level)
                {
                    level = profileLevel;
                }
            }

            if (level != AccessLevel.None)
            {
                result[pageGroup.Key] = level;
            }
        }

        return result;
    }

    public async Task<bool> HasPendingRequestsAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.AppUsers
            .AsNoTracking()
            .AnyAsync(u => u.IsAccessRequested && !u.IsApproved);
    }

    public async Task<List<AccessRequestRow>> GetPendingRequestsAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var users = await db.AppUsers
            .AsNoTracking()
            .Where(u => u.IsAccessRequested && !u.IsApproved)
            .OrderBy(u => u.RequestedAt)
            .ToListAsync();

        var names = await LoadRequestTargetNamesAsync(db, users);

        return users
            .Select(u => new AccessRequestRow
            {
                User = u,
                RequestedDepartment = LookupName(names.Departments, u.RequestedDepartmentId),
                RequestedArt = LookupName(names.Arts, u.RequestedCapitalProjectId)
            })
            .ToList();
    }

    private static async Task<(Dictionary<int, string> Departments, Dictionary<int, string> Arts)> LoadRequestTargetNamesAsync(
        EstimationDbContext db, IReadOnlyCollection<AppUser> users)
    {
        var departmentIds = users
            .Where(u => u.RequestedDepartmentId.HasValue)
            .Select(u => u.RequestedDepartmentId!.Value)
            .Distinct()
            .ToList();

        var artIds = users
            .Where(u => u.RequestedCapitalProjectId.HasValue)
            .Select(u => u.RequestedCapitalProjectId!.Value)
            .Distinct()
            .ToList();

        var departments = departmentIds.Count == 0
            ? []
            : await db.Departments
                .AsNoTracking()
                .Where(d => departmentIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, d => d.Name);

        var arts = artIds.Count == 0
            ? []
            : await db.CapitalProjects
                .AsNoTracking()
                .Where(cp => artIds.Contains(cp.Id))
                .ToDictionaryAsync(cp => cp.Id, cp => cp.Name);

        return (departments, arts);
    }

    private static string? LookupName(Dictionary<int, string> names, int? id)
        => id.HasValue && names.TryGetValue(id.Value, out var name) ? name : null;

    public async Task<List<AppUser>> GetAllUsersAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.AppUsers
            .AsNoTracking()
            .OrderBy(u => u.WindowsUserName)
            .ToListAsync();
    }

    public async Task<List<AppUserListRow>> GetUserRowsAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var users = await db.AppUsers
            .AsNoTracking()
            .Include(u => u.UserProfiles)
            .ThenInclude(up => up.Profile)
            .OrderBy(u => u.WindowsUserName)
            .ToListAsync();

        var memberships = await db.TeamMembers
            .AsNoTracking()
            .Where(tm => tm.HumanResource.EmployeeNumber != null)
            .Select(tm => new
            {
                tm.HumanResource.EmployeeNumber,
                TeamName = tm.Team.Name,
                RoleName = tm.TeamRole != null ? tm.TeamRole.Name : null,
                ProjectNames = tm.Team.CapitalProjectTeams
                    .Select(cpt => cpt.Art.Name)
                    .ToList()
            })
            .ToListAsync();

        var byEmployee = memberships.ToLookup(
            m => EmployeeNumberHelper.Normalize(m.EmployeeNumber)!,
            StringComparer.OrdinalIgnoreCase);

        var requestNames = await LoadRequestTargetNamesAsync(db, users);

        var rows = new List<AppUserListRow>(users.Count);
        foreach (var user in users)
        {
            var row = new AppUserListRow
            {
                User = user,
                Profiles = user.UserProfiles
                    .Select(up => up.Profile.Name)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Distinct()
                    .OrderBy(n => n)
                    .ToList(),
                RequestedDepartment = LookupName(requestNames.Departments, user.RequestedDepartmentId),
                RequestedArt = LookupName(requestNames.Arts, user.RequestedCapitalProjectId)
            };

            var userKey = EmployeeNumberHelper.ResolveUserKey(user.EmployeeId, user.SamAccountName);
            if (EmployeeNumberHelper.Normalize(userKey) is { } normalizedId)
            {
                var ms = byEmployee[normalizedId].ToList();
                row.Teams = ms.Select(m => m.TeamName)
                    .Distinct().OrderBy(n => n).ToList();
                row.TeamRoles = ms.Where(m => !string.IsNullOrEmpty(m.RoleName))
                    .Select(m => m.RoleName!)
                    .Distinct().OrderBy(n => n).ToList();
                row.Arts = ms.SelectMany(m => m.ProjectNames)
                    .Distinct().OrderBy(n => n).ToList();
            }

            rows.Add(row);
        }

        return rows;
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

    public async Task<List<int>> GetUserProfileIdsAsync(int userId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.AppUserProfiles
            .AsNoTracking()
            .Where(up => up.AppUserId == userId)
            .Select(up => up.ProfileId)
            .ToListAsync();
    }

    public async Task ApproveUserAsync(int userId, bool isAdmin, List<int> profileIds, string approvedBy)
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

        await SaveUserProfilesAsync(db, userId, profileIds);
        await db.SaveChangesAsync();
    }

    public async Task UpdateUserProfilesAsync(int userId, bool isAdmin, List<int> profileIds)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var user = await db.AppUsers.FindAsync(userId);
        if (user is null)
        {
            return;
        }

        user.IsAdmin = isAdmin;
        await SaveUserProfilesAsync(db, userId, profileIds);
        await db.SaveChangesAsync();
    }

    public async Task AddUserAsync(string windowsUserName, string? displayName, bool isAdmin, List<int> profileIds, string createdBy, string? samAccountName = null, string? employeeId = null, string? emailAddress = null)
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
            EmailAddress = emailAddress,
            IsAdmin = isAdmin,
            IsApproved = true,
            IsAccessRequested = false,
            ApprovedAt = DateTime.UtcNow,
            ApprovedBy = createdBy,
            CreatedAt = DateTime.UtcNow
        };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();

        await SaveUserProfilesAsync(db, user.Id, profileIds);
        await db.SaveChangesAsync();
    }

    public async Task<bool> SetAdInfoAsync(int userId, string? displayName, string? samAccountName, string? employeeId, string? emailAddress)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var user = await db.AppUsers.FindAsync(userId);
        if (user is null)
        {
            return false;
        }

        ApplyAdInfo(user, displayName, samAccountName, employeeId, emailAddress);
        await db.SaveChangesAsync();
        return true;
    }

    private static void ApplyRequestInfo(AppUser user, AccessRequestInfo? request)
    {
        if (request is null)
        {
            return;
        }

        user.RequestedDepartmentId = request.DepartmentId;
        user.RequestedCapitalProjectId = request.DepartmentId is null ? null : request.CapitalProjectId;

        var comment = request.Comment?.Trim();
        user.RequestComment = string.IsNullOrEmpty(comment)
            ? null
            : comment.Length > AppUser.MaxRequestCommentLength
                ? comment[..AppUser.MaxRequestCommentLength]
                : comment;
    }

    private static void ApplyAdInfo(AppUser user, string? displayName, string? samAccountName, string? employeeId, string? emailAddress)
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

        if (!string.IsNullOrWhiteSpace(emailAddress))
        {
            user.EmailAddress = emailAddress;
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

    private static async Task SaveUserProfilesAsync(EstimationDbContext db, int userId, List<int> profileIds)
    {
        var existing = await db.AppUserProfiles
            .Where(p => p.AppUserId == userId)
            .ToListAsync();

        db.AppUserProfiles.RemoveRange(existing);

        db.AppUserProfiles.AddRange(profileIds
            .Distinct()
            .Select(id => new AppUserProfile { AppUserId = userId, ProfileId = id }));
    }
}

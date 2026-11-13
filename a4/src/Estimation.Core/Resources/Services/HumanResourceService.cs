using System.Linq.Expressions;
using Estimation.Core.Resources.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Estimation.Core.Resources.Services;

public record HrPagedResult(List<HumanResource> Items, int TotalCount);

public record HrSkillAssignment(int SkillId, string? SkillLevelName, string? SkillLevelDescription);

public record HumanResourceListItem(
    int Id,
    string EmployeeName,
    string? EmployeeNumber,
    bool IsActive,
    string? CountryName,
    string? CityName,
    string? EmployeeCategoryName,
    string? EmployeeTypeName,
    string? EmployeeRoleName,
    string? EmployeeVendorName,
    string? CorporateGradeName,
    string? LineManagerName,
    int? AnnualLeaveDays,
    Dictionary<int, HrSkillAssignment> SkillMap,
    List<HrTeamAssignment> Teams);

public record HrTeamAssignment(int TeamId, string TeamName, string? TeamRoleName, List<string> ArtNames);

public record HrListPagedResult(List<HumanResourceListItem> Items, int TotalCount, int ScopeCount);

public record HrTeamOption(string Name, List<string> ArtNames);

public record HrCityOption(string Name, string CountryName);

public record HrFilterOptions(List<HrTeamOption> Teams, List<HrCityOption> Cities);

public record HrMembership(int TeamId, string TeamName, string? TeamRoleName, List<HrMembershipArt> Arts, List<HrMembershipStack> Stacks);

public record HrMembershipArt(int Id, string Name, string? DepartmentName);

public record HrMembershipStack(int Id, string Name, List<int> SkillIds);

public record HrDirectoryPerson(int Id, string EmployeeName, string? EmployeeNumber, string FullName, string? Email, string? AccountName);

public record HrDirectoryUpdate(int Id, string? FullName, string? Email, string? AccountName)
{
    public bool HasChanges => FullName is not null || Email is not null || AccountName is not null;
}

public interface IHumanResourceService
{
    Task<HrPagedResult> GetPagedAsync(int page, int pageSize, string? search, string? sortField, bool sortAsc);
    Task<HrListPagedResult> GetPagedListAsync(int page, int pageSize, string? sortField, bool sortAsc, HumanResourceFilter filter, CancellationToken ct = default);
    Task<HrFilterOptions> GetFilterOptionsAsync(IReadOnlyCollection<int>? allowedArtIds);
    Task<List<HumanResource>> SearchAsync(string term, int take = 20);
    Task<HumanResource?> GetByIdAsync(int id);
    Task<HumanResource?> GetByEmployeeNumberAsync(string? employeeNumber);
    Task<List<HrMembership>> GetMembershipsAsync(int hrId);
    Task<HumanResource> CreateAsync(HumanResource hr);
    Task<HumanResource> UpdateAsync(HumanResource hr);
    Task<bool> DeleteAsync(int id);
    Task SetSkillAsync(int hrId, int skillId, int? skillLevelId);
    Task RemoveSkillAsync(int hrId, int skillId);
    Task<List<HrDirectoryPerson>> GetActivePeopleForDirectoryAsync();
    Task<int> ApplyDirectoryDetailsAsync(IReadOnlyCollection<HrDirectoryUpdate> updates);
    Task<Dictionary<string, string>> GetEmployeeNumbersForWindowsUsersAsync(IReadOnlyCollection<string> windowsUserNames);
    Task<Dictionary<string, List<string>>> GetWindowsUsersForEmployeeNumbersAsync(IReadOnlyCollection<string> employeeNumbers);
}

public class HumanResourceService : IHumanResourceService
{
    private readonly IDbContextFactory<EstimationDbContext> _contextFactory;

    public HumanResourceService(IDbContextFactory<EstimationDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<HrPagedResult> GetPagedAsync(int page, int pageSize, string? search, string? sortField, bool sortAsc)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var query = context.HumanResources
            .Include(hr => hr.HumanResourceSkills)
                .ThenInclude(hrs => hrs.Skill)
                    .ThenInclude(s => s.Levels)
            .Include(hr => hr.HumanResourceSkills)
                .ThenInclude(hrs => hrs.SkillLevel)
            .Include(hr => hr.EmployeeCategory)
            .Include(hr => hr.EmployeeType)
            .Include(hr => hr.EmployeeRole)
            .Include(hr => hr.EmployeeVendor)
            .Include(hr => hr.City).ThenInclude(c => c!.Country)
            .Include(hr => hr.CorporateGrade)
            .AsSplitQuery()
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(hr =>
                hr.EmployeeName.Contains(term) ||
                (hr.EmployeeNumber != null && hr.EmployeeNumber.Contains(term)));
        }

        var totalCount = await query.CountAsync();

        query = ApplySort(query, sortField, sortAsc);

        var items = await query
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new HrPagedResult(items, totalCount);
    }

    public async Task<HrListPagedResult> GetPagedListAsync(int page, int pageSize, string? sortField, bool sortAsc, HumanResourceFilter filter, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var scoped = filter.ApplyScope(context.HumanResources.AsNoTracking());
        var baseQuery = filter.ApplyConditions(scoped, context.TeamMembers.AsNoTracking());

        var totalCount = await baseQuery.CountAsync(ct);
        var scopeCount = filter.HasConditions ? await scoped.CountAsync(ct) : totalCount;

        var sorted = ApplySort(baseQuery, sortField, sortAsc);

        var projected = await sorted
            .Skip(page * pageSize)
            .Take(pageSize)
            .Select(hr => new
            {
                hr.Id,
                hr.EmployeeName,
                hr.EmployeeNumber,
                hr.IsActive,
                CountryName = hr.City != null ? hr.City.Country.Name : null,
                CityName = hr.City != null ? hr.City.Name : null,
                EmployeeCategoryName = hr.EmployeeCategory != null ? hr.EmployeeCategory.Name : null,
                EmployeeTypeName = hr.EmployeeType != null ? hr.EmployeeType.Name : null,
                EmployeeRoleName = hr.EmployeeRole != null ? hr.EmployeeRole.Name : null,
                EmployeeVendorName = hr.EmployeeVendor != null ? hr.EmployeeVendor.Name : null,
                CorporateGradeName = hr.CorporateGrade != null ? hr.CorporateGrade.Name : null,
                hr.LineManagerName,
                hr.AnnualLeaveDays,
                Skills = hr.HumanResourceSkills.Select(hrs => new
                {
                    hrs.SkillId,
                    SkillLevelName = hrs.SkillLevel != null ? hrs.SkillLevel.Name : null,
                    SkillLevelDescription = hrs.SkillLevel != null ? hrs.SkillLevel.Description : null
                }),
                Teams = hr.TeamMembers.Select(tm => new
                {
                    tm.TeamId,
                    TeamName = tm.Team.Name,
                    TeamRoleName = tm.TeamRole != null ? tm.TeamRole.Name : null,
                    ArtNames = tm.Team.CapitalProjectTeams.Select(cpt => cpt.Art.Name).ToList()
                })
            })
            .ToListAsync(ct);

        var items = projected.Select(hr => new HumanResourceListItem(
            hr.Id,
            hr.EmployeeName,
            hr.EmployeeNumber,
            hr.IsActive,
            hr.CountryName,
            hr.CityName,
            hr.EmployeeCategoryName,
            hr.EmployeeTypeName,
            hr.EmployeeRoleName,
            hr.EmployeeVendorName,
            hr.CorporateGradeName,
            hr.LineManagerName,
            hr.AnnualLeaveDays,
            hr.Skills.ToDictionary(
                s => s.SkillId,
                s => new HrSkillAssignment(s.SkillId, s.SkillLevelName, s.SkillLevelDescription)),
            hr.Teams.Select(t => new HrTeamAssignment(t.TeamId, t.TeamName, t.TeamRoleName, t.ArtNames)).ToList()))
            .ToList();

        return new HrListPagedResult(items, totalCount, scopeCount);
    }

    public async Task<HrFilterOptions> GetFilterOptionsAsync(IReadOnlyCollection<int>? allowedArtIds)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var teams = context.Teams.AsNoTracking();
        if (allowedArtIds is not null)
        {
            teams = teams.Where(t => t.CapitalProjectTeams.Any(cpt => allowedArtIds.Contains(cpt.CapitalProjectId)));
        }

        var teamRows = await teams
            .OrderBy(t => t.Name)
            .Select(t => new { t.Name, ArtNames = t.CapitalProjectTeams.Select(cpt => cpt.Art.Name).ToList() })
            .ToListAsync();

        var people = new HumanResourceFilter { AllowedArtIds = allowedArtIds }
            .ApplyScope(context.HumanResources.AsNoTracking());

        var cities = await people
            .Where(hr => hr.City != null)
            .Select(hr => new { City = hr.City!.Name, Country = hr.City.Country.Name })
            .Distinct()
            .OrderBy(c => c.City)
            .ToListAsync();

        return new HrFilterOptions(
            teamRows.Select(t => new HrTeamOption(t.Name, t.ArtNames)).ToList(),
            cities.Select(c => new HrCityOption(c.City, c.Country)).ToList());
    }

    public async Task<List<HumanResource>> SearchAsync(string term, int take = 20)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var t = term.Trim();
        return await context.HumanResources
            .Where(hr =>
                hr.EmployeeName.Contains(t) ||
                (hr.EmployeeNumber != null && hr.EmployeeNumber.Contains(t)))
            .OrderBy(hr => hr.EmployeeName)
            .Take(take)
            .AsNoTracking()
            .ToListAsync();
    }

    private static IQueryable<HumanResource> ApplySort(IQueryable<HumanResource> query, string? sortField, bool sortAsc)
    {
        var sorted = sortField?.ToLowerInvariant() switch
        {
            "employeenumber" => SortBy(query, hr => hr.EmployeeNumber, sortAsc),
            "linemanagername" => SortBy(query, hr => hr.LineManagerName, sortAsc),
            "country" => SortBy(query, hr => hr.City != null ? hr.City.Country.Name : null, sortAsc),
            "city" => SortBy(query, hr => hr.City != null ? hr.City.Name : null, sortAsc),
            "category" => SortBy(query, hr => hr.EmployeeCategory != null ? hr.EmployeeCategory.Name : null, sortAsc),
            "employeetype" => SortBy(query, hr => hr.EmployeeType != null ? hr.EmployeeType.Name : null, sortAsc),
            "employeerole" => SortBy(query, hr => hr.EmployeeRole != null ? hr.EmployeeRole.Name : null, sortAsc),
            "employeevendor" => SortBy(query, hr => hr.EmployeeVendor != null ? hr.EmployeeVendor.Name : null, sortAsc),
            "corporategrade" => SortBy(query, hr => hr.CorporateGrade != null ? hr.CorporateGrade.Name : null, sortAsc),
            "annualleavedays" => SortBy(query, hr => hr.AnnualLeaveDays, sortAsc),
            _ => SortBy(query, hr => hr.EmployeeName, sortAsc),
        };
        return sorted.ThenBy(hr => hr.EmployeeName).ThenBy(hr => hr.Id);
    }

    private static IOrderedQueryable<HumanResource> SortBy<TKey>(
        IQueryable<HumanResource> query, Expression<Func<HumanResource, TKey>> key, bool sortAsc) =>
        sortAsc ? query.OrderBy(key) : query.OrderByDescending(key);

    public async Task<HumanResource?> GetByIdAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.HumanResources
            .Include(hr => hr.HumanResourceSkills)
                .ThenInclude(hrs => hrs.Skill)
                    .ThenInclude(s => s.Levels)
            .Include(hr => hr.HumanResourceSkills)
                .ThenInclude(hrs => hrs.SkillLevel)
            .Include(hr => hr.EmployeeCategory)
            .Include(hr => hr.EmployeeType)
            .Include(hr => hr.EmployeeRole)
            .Include(hr => hr.EmployeeVendor)
            .Include(hr => hr.City).ThenInclude(c => c!.Country)
            .Include(hr => hr.CorporateGrade)
            .AsSplitQuery()
            .AsNoTracking()
            .FirstOrDefaultAsync(hr => hr.Id == id);
    }

    public async Task<HumanResource?> GetByEmployeeNumberAsync(string? employeeNumber)
    {
        var brid = NormalizeBrid(employeeNumber);
        if (brid is null)
        {
            return null;
        }

        var key = brid.ToUpperInvariant();
        int? id;
        await using (var context = await _contextFactory.CreateDbContextAsync())
        {
            id = await context.HumanResources
                .AsNoTracking()
                .Where(h => h.EmployeeNumber != null && h.EmployeeNumber.Trim().ToUpper() == key)
                .OrderByDescending(h => h.IsActive)
                .ThenBy(h => h.EmployeeName)
                .ThenBy(h => h.Id)
                .Select(h => (int?)h.Id)
                .FirstOrDefaultAsync();
        }

        return id is int found ? await GetByIdAsync(found) : null;
    }

    public async Task<List<HrMembership>> GetMembershipsAsync(int hrId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var rows = await context.TeamMembers
            .AsNoTracking()
            .Where(tm => tm.HumanResourceId == hrId)
            .Select(tm => new
            {
                tm.TeamId,
                TeamName = tm.Team.Name,
                TeamRoleName = tm.TeamRole != null ? tm.TeamRole.Name : null,
                Arts = tm.Team.CapitalProjectTeams
                    .Select(cpt => new HrMembershipArt(
                        cpt.Art.Id,
                        cpt.Art.Name,
                        cpt.Art.Department != null ? cpt.Art.Department.Name : null))
                    .ToList(),
                Stacks = tm.MemberTechnologyStacks
                    .Select(s => new HrMembershipStack(
                        s.TechnologyStack.Id,
                        s.TechnologyStack.Name,
                        s.TechnologyStack.TechnologyStackSkills.Select(tss => tss.SkillId).ToList()))
                    .ToList()
            })
            .AsSplitQuery()
            .ToListAsync();

        return rows
            .OrderBy(r => r.TeamName, StringComparer.OrdinalIgnoreCase)
            .Select(r => new HrMembership(
                r.TeamId,
                r.TeamName,
                r.TeamRoleName,
                r.Arts.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList(),
                r.Stacks.OrderBy(st => st.Name, StringComparer.OrdinalIgnoreCase).ToList()))
            .ToList();
    }

    public async Task<HumanResource> CreateAsync(HumanResource hr)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        hr.EmployeeNumber = NormalizeBrid(hr.EmployeeNumber);
        await EnsureBridIsFreeAsync(context, hr.EmployeeNumber, hr.Id);
        context.HumanResources.Add(hr);
        await context.SaveChangesAsync();
        return hr;
    }

    public async Task<HumanResource> UpdateAsync(HumanResource hr)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var existing = await context.HumanResources
            .FirstOrDefaultAsync(h => h.Id == hr.Id);

        if (existing is null)
        {
            Log.Warning("HumanResource {HrId} not found", hr.Id);
            throw new KeyNotFoundException($"HumanResource {hr.Id} not found.");
        }

        var brid = NormalizeBrid(hr.EmployeeNumber);
        await EnsureBridIsFreeAsync(context, brid, hr.Id);

        existing.IsActive = hr.IsActive;
        existing.EmployeeNumber = brid;
        existing.EmployeeName = hr.EmployeeName;
        existing.FullName = hr.FullName;
        existing.Email = hr.Email;
        existing.AccountName = hr.AccountName;
        existing.LineManagerName = hr.LineManagerName;
        existing.EmployeeCategoryId = hr.EmployeeCategoryId;
        existing.EmployeeTypeId = hr.EmployeeTypeId;
        existing.EmployeeRoleId = hr.EmployeeRoleId;
        existing.EmployeeVendorId = hr.EmployeeVendorId;
        existing.CityId = hr.CityId;
        existing.CorporateGradeId = hr.CorporateGradeId;
        existing.AnnualLeaveDays = hr.AnnualLeaveDays;

        await context.SaveChangesAsync();
        return existing;
    }

    private static string? NormalizeBrid(string? brid) =>
        string.IsNullOrWhiteSpace(brid) ? null : brid.Trim();

    private static async Task EnsureBridIsFreeAsync(EstimationDbContext context, string? brid, int personId)
    {
        if (brid is null)
        {
            return;
        }

        var key = brid.ToUpperInvariant();
        var holder = await context.HumanResources
            .AsNoTracking()
            .Where(h => h.Id != personId && h.EmployeeNumber != null && h.EmployeeNumber.Trim().ToUpper() == key)
            .OrderByDescending(h => h.IsActive)
            .ThenBy(h => h.EmployeeName)
            .Select(h => new { h.EmployeeName, h.IsActive })
            .FirstOrDefaultAsync();

        if (holder is not null)
        {
            throw new InvalidOperationException(
                $"BRID {brid} is already used by {holder.EmployeeName}{(holder.IsActive ? "" : " (inactive)")}.");
        }
    }

    public async Task<bool> DeleteAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var hr = await context.HumanResources.FindAsync(id);
        if (hr is null)
        {
            return false;
        }

        context.HumanResources.Remove(hr);
        await context.SaveChangesAsync();
        return true;
    }

    public async Task SetSkillAsync(int hrId, int skillId, int? skillLevelId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var entry = await context.HumanResourceSkills
            .FirstOrDefaultAsync(x => x.HumanResourceId == hrId && x.SkillId == skillId);

        if (entry is null)
        {
            context.HumanResourceSkills.Add(new HumanResourceSkill
            {
                HumanResourceId = hrId,
                SkillId = skillId,
                SkillLevelId = skillLevelId
            });
        }
        else
        {
            entry.SkillLevelId = skillLevelId;
        }

        await context.SaveChangesAsync();
    }

    public async Task RemoveSkillAsync(int hrId, int skillId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var entry = await context.HumanResourceSkills
            .FirstOrDefaultAsync(x => x.HumanResourceId == hrId && x.SkillId == skillId);

        if (entry is not null)
        {
            context.HumanResourceSkills.Remove(entry);
            await context.SaveChangesAsync();
        }
    }

    public async Task<List<HrDirectoryPerson>> GetActivePeopleForDirectoryAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.HumanResources
            .AsNoTracking()
            .Where(h => h.IsActive)
            .OrderBy(h => h.EmployeeName)
            .ThenBy(h => h.Id)
            .Select(h => new HrDirectoryPerson(h.Id, h.EmployeeName, h.EmployeeNumber, h.FullName, h.Email, h.AccountName))
            .ToListAsync();
    }

    public async Task<int> ApplyDirectoryDetailsAsync(IReadOnlyCollection<HrDirectoryUpdate> updates)
    {
        if (updates.Count == 0)
        {
            return 0;
        }

        var ids = updates.Select(u => u.Id).Distinct().ToList();
        await using var context = await _contextFactory.CreateDbContextAsync();
        var people = await context.HumanResources
            .Where(h => ids.Contains(h.Id))
            .ToDictionaryAsync(h => h.Id);

        var changed = 0;
        foreach (var update in updates)
        {
            if (!people.TryGetValue(update.Id, out var hr))
            {
                continue;
            }

            var before = (hr.FullName, hr.Email, hr.AccountName);
            if (HrDirectoryChanges.Fit(update.FullName, HumanResource.FullNameMaxLength) is { } fullName)
            {
                hr.FullName = fullName;
            }
            if (HrDirectoryChanges.Fit(update.Email, HumanResource.EmailMaxLength) is { } email)
            {
                hr.Email = email;
            }
            if (HrDirectoryChanges.Fit(update.AccountName, HumanResource.AccountNameMaxLength) is { } accountName)
            {
                hr.AccountName = accountName;
            }
            if (before != (hr.FullName, hr.Email, hr.AccountName))
            {
                changed++;
            }
        }

        await context.SaveChangesAsync();
        return changed;
    }

    public async Task<Dictionary<string, string>> GetEmployeeNumbersForWindowsUsersAsync(IReadOnlyCollection<string> windowsUserNames)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var names = windowsUserNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (names.Count == 0)
        {
            return result;
        }

        await using var context = await _contextFactory.CreateDbContextAsync();
        var users = await context.AppUsers
            .AsNoTracking()
            .Where(u => names.Contains(u.WindowsUserName))
            .Select(u => new { u.WindowsUserName, u.SamAccountName, u.EmployeeId })
            .ToListAsync();
        var people = await context.HumanResources
            .AsNoTracking()
            .Where(h => h.EmployeeNumber != null && h.EmployeeNumber != "")
            .Select(h => new { EmployeeNumber = h.EmployeeNumber!, h.AccountName, h.IsActive })
            .ToListAsync();

        foreach (var name in names)
        {
            var login = name[(name.LastIndexOf('\\') + 1)..];
            var candidates = people
                .Where(p => string.Equals(p.AccountName, login, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (candidates.Count == 0
                && users.FirstOrDefault(u => string.Equals(u.WindowsUserName, name, StringComparison.OrdinalIgnoreCase)) is { } user)
            {
                var key = EmployeeNumberHelper.ResolveUserKey(user.EmployeeId, user.SamAccountName);
                candidates = people.Where(p => EmployeeNumberHelper.Matches(p.EmployeeNumber, key)).ToList();
            }

            var match = candidates.Count == 1
                ? candidates[0]
                : candidates.Where(p => p.IsActive).ToList() is [var onlyActive] ? onlyActive : null;
            if (match is not null)
            {
                result[name] = match.EmployeeNumber;
            }
        }

        return result;
    }

    public async Task<Dictionary<string, List<string>>> GetWindowsUsersForEmployeeNumbersAsync(IReadOnlyCollection<string> employeeNumbers)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var numbers = employeeNumbers
            .Where(n => EmployeeNumberHelper.Normalize(n) is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (numbers.Count == 0)
        {
            return result;
        }

        await using var context = await _contextFactory.CreateDbContextAsync();
        var users = await context.AppUsers
            .AsNoTracking()
            .OrderBy(u => u.Id)
            .Select(u => new { u.WindowsUserName, u.SamAccountName, u.EmployeeId })
            .ToListAsync();
        var loginsByDigits = users
            .Select(u => (u.WindowsUserName, Digits: EmployeeNumberHelper.Normalize(EmployeeNumberHelper.ResolveUserKey(u.EmployeeId, u.SamAccountName))))
            .Where(u => u.Digits is not null)
            .ToLookup(u => u.Digits!, u => u.WindowsUserName, StringComparer.OrdinalIgnoreCase);

        foreach (var number in numbers)
        {
            var logins = loginsByDigits[EmployeeNumberHelper.Normalize(number)!].ToList();
            if (logins.Count > 0)
            {
                result[number] = logins;
            }
        }

        return result;
    }
}

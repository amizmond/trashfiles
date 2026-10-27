using Estimation.Core.Resources.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Estimation.Core.Resources.Services;

public record HrPagedResult(List<HumanResource> Items, int TotalCount);

public record HrSkillAssignment(int SkillId, string? SkillLevelName, string? SkillLevelDescription);

public record HumanResourceListItem(
    int Id,
    string FullName,
    string EmployeeName,
    string? EmployeeNumber,
    bool IsActive,
    string? CountryName,
    string? CityName,
    string? EmployeeCategoryName,
    Dictionary<int, HrSkillAssignment> SkillMap,
    List<HrTeamAssignment> Teams);

public record HrTeamAssignment(int TeamId, string TeamName, string? TeamRoleName, List<string> ArtNames);

public record HrListPagedResult(List<HumanResourceListItem> Items, int TotalCount, int ScopeCount);

public record HrTeamOption(string Name, List<string> ArtNames);

public record HrCityOption(string Name, string CountryName);

public record HrFilterOptions(List<HrTeamOption> Teams, List<HrCityOption> Cities);

public interface IHumanResourceService
{
    Task<HrPagedResult> GetPagedAsync(int page, int pageSize, string? search, string? sortField, bool sortAsc);
    Task<HrListPagedResult> GetPagedListAsync(int page, int pageSize, string? sortField, bool sortAsc, HumanResourceFilter filter, CancellationToken ct = default);
    Task<HrFilterOptions> GetFilterOptionsAsync(IReadOnlyCollection<int>? allowedArtIds);
    Task<List<HumanResource>> SearchAsync(string term, int take = 20);
    Task<HumanResource?> GetByIdAsync(int id);
    Task<HumanResource> CreateAsync(HumanResource hr);
    Task<HumanResource> UpdateAsync(HumanResource hr);
    Task<bool> DeleteAsync(int id);
    Task SetSkillAsync(int hrId, int skillId, int? skillLevelId);
    Task RemoveSkillAsync(int hrId, int skillId);
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
                hr.FullName.Contains(term) ||
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
                hr.FullName,
                hr.EmployeeName,
                hr.EmployeeNumber,
                hr.IsActive,
                CountryName = hr.City != null ? hr.City.Country.Name : null,
                CityName = hr.City != null ? hr.City.Name : null,
                EmployeeCategoryName = hr.EmployeeCategory != null ? hr.EmployeeCategory.Name : null,
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
            hr.FullName,
            hr.EmployeeName,
            hr.EmployeeNumber,
            hr.IsActive,
            hr.CountryName,
            hr.CityName,
            hr.EmployeeCategoryName,
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
                hr.FullName.Contains(t) ||
                hr.EmployeeName.Contains(t) ||
                (hr.EmployeeNumber != null && hr.EmployeeNumber.Contains(t)))
            .OrderBy(hr => hr.FullName)
            .Take(take)
            .AsNoTracking()
            .ToListAsync();
    }

    private static IQueryable<HumanResource> ApplySort(IQueryable<HumanResource> query, string? sortField, bool sortAsc)
    {
        return sortField?.ToLowerInvariant() switch
        {
            "employeenumber" => sortAsc ? query.OrderBy(hr => hr.EmployeeNumber) : query.OrderByDescending(hr => hr.EmployeeNumber),
            "employeename" => sortAsc ? query.OrderBy(hr => hr.EmployeeName) : query.OrderByDescending(hr => hr.EmployeeName),
            "cio" => sortAsc ? query.OrderBy(hr => hr.Cio) : query.OrderByDescending(hr => hr.Cio),
            "cio1" => sortAsc ? query.OrderBy(hr => hr.Cio1) : query.OrderByDescending(hr => hr.Cio1),
            "cio2" => sortAsc ? query.OrderBy(hr => hr.Cio2) : query.OrderByDescending(hr => hr.Cio2),
            "linemanagername" => sortAsc ? query.OrderBy(hr => hr.LineManagerName) : query.OrderByDescending(hr => hr.LineManagerName),
            _ => sortAsc ? query.OrderBy(hr => hr.FullName) : query.OrderByDescending(hr => hr.FullName),
        };
    }

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

    public async Task<HumanResource> CreateAsync(HumanResource hr)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
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

        existing.IsActive = hr.IsActive;
        existing.EmployeeNumber = hr.EmployeeNumber;
        existing.EmployeeName = hr.EmployeeName;
        existing.FullName = hr.FullName;
        existing.LineManagerName = hr.LineManagerName;
        existing.Cio = hr.Cio;
        existing.Cio1 = hr.Cio1;
        existing.Cio2 = hr.Cio2;
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
}

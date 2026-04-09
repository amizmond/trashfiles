using Estimation.Core.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Estimation.Core.Services;

public interface IStaticSettingsService
{
    Task<List<EmployeeCategory>> GetAllEmployeeCategoriesAsync();
    Task<EmployeeCategory> CreateEmployeeCategoryAsync(EmployeeCategory entity);
    Task<EmployeeCategory> UpdateEmployeeCategoryAsync(EmployeeCategory entity);
    Task<bool> DeleteEmployeeCategoryAsync(int id);

    Task<List<EmployeeType>> GetAllEmployeeTypesAsync();
    Task<EmployeeType> CreateEmployeeTypeAsync(EmployeeType entity);
    Task<EmployeeType> UpdateEmployeeTypeAsync(EmployeeType entity);
    Task<bool> DeleteEmployeeTypeAsync(int id);

    Task<List<EmployeeVendor>> GetAllEmployeeVendorsAsync();
    Task<EmployeeVendor> CreateEmployeeVendorAsync(EmployeeVendor entity);
    Task<EmployeeVendor> UpdateEmployeeVendorAsync(EmployeeVendor entity);
    Task<bool> DeleteEmployeeVendorAsync(int id);

    Task<List<EmployeeRole>> GetAllEmployeeRolesAsync();
    Task<EmployeeRole> CreateEmployeeRoleAsync(EmployeeRole entity);
    Task<EmployeeRole> UpdateEmployeeRoleAsync(EmployeeRole entity);
    Task<bool> DeleteEmployeeRoleAsync(int id);

    Task<List<CorporateGrade>> GetAllCorporateGradesAsync();
    Task<CorporateGrade> CreateCorporateGradeAsync(CorporateGrade entity);
    Task<CorporateGrade> UpdateCorporateGradeAsync(CorporateGrade entity);
    Task<bool> DeleteCorporateGradeAsync(int id);

    Task<List<City>> GetAllCitiesAsync();
    Task<City> CreateCityAsync(City entity);
    Task<City> UpdateCityAsync(City entity);
    Task<bool> DeleteCityAsync(int id);

    Task<List<Country>> GetAllCountriesAsync();
    Task<Country> CreateCountryAsync(Country entity);
    Task<Country> UpdateCountryAsync(Country entity);
    Task<bool> DeleteCountryAsync(int id);

    Task<List<TeamRole>> GetAllTeamRolesAsync();
    Task<TeamRole> CreateTeamRoleAsync(TeamRole entity);
    Task<TeamRole> UpdateTeamRoleAsync(TeamRole entity);
    Task<bool> DeleteTeamRoleAsync(int id);
}

public class StaticSettingsService : IStaticSettingsService
{
    private readonly IDbContextFactory<EstimationDbContext> _contextFactory;

    public StaticSettingsService(IDbContextFactory<EstimationDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<List<EmployeeCategory>> GetAllEmployeeCategoriesAsync()
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        return await ctx.EmployeeCategories.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
    }

    public async Task<EmployeeCategory> CreateEmployeeCategoryAsync(EmployeeCategory entity)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        ctx.EmployeeCategories.Add(entity);
        await ctx.SaveChangesAsync();
        return entity;
    }

    public async Task<EmployeeCategory> UpdateEmployeeCategoryAsync(EmployeeCategory entity)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        var existing = await ctx.EmployeeCategories.FindAsync(entity.Id);

        if (existing is null)
        {
            Log.Warning("EmployeeCategory {EntityId} not found", entity.Id);
            throw new KeyNotFoundException($"EmployeeCategory {entity.Id} not found.");
        }

        existing.Name = entity.Name;
        existing.Description = entity.Description;
        await ctx.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteEmployeeCategoryAsync(int id)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        var entity = await ctx.EmployeeCategories.FindAsync(id);
        if (entity is null) return false;
        ctx.EmployeeCategories.Remove(entity);
        await ctx.SaveChangesAsync();
        return true;
    }

    public async Task<List<EmployeeType>> GetAllEmployeeTypesAsync()
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        return await ctx.EmployeeTypes.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
    }

    public async Task<EmployeeType> CreateEmployeeTypeAsync(EmployeeType entity)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        ctx.EmployeeTypes.Add(entity);
        await ctx.SaveChangesAsync();
        return entity;
    }

    public async Task<EmployeeType> UpdateEmployeeTypeAsync(EmployeeType entity)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        var existing = await ctx.EmployeeTypes.FindAsync(entity.Id);

        if (existing is null)
        {
            Log.Warning("EmployeeType {EntityId} not found", entity.Id);
            throw new KeyNotFoundException($"EmployeeType {entity.Id} not found.");
        }

        existing.Name = entity.Name;
        existing.Description = entity.Description;
        await ctx.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteEmployeeTypeAsync(int id)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        var entity = await ctx.EmployeeTypes.FindAsync(id);
        if (entity is null) return false;
        ctx.EmployeeTypes.Remove(entity);
        await ctx.SaveChangesAsync();
        return true;
    }

    public async Task<List<EmployeeVendor>> GetAllEmployeeVendorsAsync()
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        return await ctx.EmployeeVendors.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
    }

    public async Task<EmployeeVendor> CreateEmployeeVendorAsync(EmployeeVendor entity)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        ctx.EmployeeVendors.Add(entity);
        await ctx.SaveChangesAsync();
        return entity;
    }

    public async Task<EmployeeVendor> UpdateEmployeeVendorAsync(EmployeeVendor entity)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        var existing = await ctx.EmployeeVendors.FindAsync(entity.Id);

        if (existing is null)
        {
            Log.Warning("EmployeeVendor {EntityId} not found", entity.Id);
            throw new KeyNotFoundException($"EmployeeVendor {entity.Id} not found.");
        }

        existing.Name = entity.Name;
        existing.Description = entity.Description;
        await ctx.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteEmployeeVendorAsync(int id)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        var entity = await ctx.EmployeeVendors.FindAsync(id);
        if (entity is null) return false;
        ctx.EmployeeVendors.Remove(entity);
        await ctx.SaveChangesAsync();
        return true;
    }

    public async Task<List<EmployeeRole>> GetAllEmployeeRolesAsync()
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        return await ctx.EmployeeRoles.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
    }

    public async Task<EmployeeRole> CreateEmployeeRoleAsync(EmployeeRole entity)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        ctx.EmployeeRoles.Add(entity);
        await ctx.SaveChangesAsync();
        return entity;
    }

    public async Task<EmployeeRole> UpdateEmployeeRoleAsync(EmployeeRole entity)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        var existing = await ctx.EmployeeRoles.FindAsync(entity.Id);

        if (existing is null)
        {
            Log.Warning("EmployeeRole {EntityId} not found", entity.Id);
            throw new KeyNotFoundException($"EmployeeRole {entity.Id} not found.");
        }

        existing.Name = entity.Name;
        existing.Description = entity.Description;
        await ctx.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteEmployeeRoleAsync(int id)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        var entity = await ctx.EmployeeRoles.FindAsync(id);
        if (entity is null) return false;
        ctx.EmployeeRoles.Remove(entity);
        await ctx.SaveChangesAsync();
        return true;
    }

    public async Task<List<CorporateGrade>> GetAllCorporateGradesAsync()
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        return await ctx.CorporateGrades.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
    }

    public async Task<CorporateGrade> CreateCorporateGradeAsync(CorporateGrade entity)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        ctx.CorporateGrades.Add(entity);
        await ctx.SaveChangesAsync();
        return entity;
    }

    public async Task<CorporateGrade> UpdateCorporateGradeAsync(CorporateGrade entity)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        var existing = await ctx.CorporateGrades.FindAsync(entity.Id);

        if (existing is null)
        {
            Log.Warning("CorporateGrade {EntityId} not found", entity.Id);
            throw new KeyNotFoundException($"CorporateGrade {entity.Id} not found.");
        }

        existing.Name = entity.Name;
        existing.Description = entity.Description;
        await ctx.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteCorporateGradeAsync(int id)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        var entity = await ctx.CorporateGrades.FindAsync(id);
        if (entity is null) return false;
        ctx.CorporateGrades.Remove(entity);
        await ctx.SaveChangesAsync();
        return true;
    }

    public async Task<List<City>> GetAllCitiesAsync()
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        return await ctx.Cities.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
    }

    public async Task<City> CreateCityAsync(City entity)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        ctx.Cities.Add(entity);
        await ctx.SaveChangesAsync();
        return entity;
    }

    public async Task<City> UpdateCityAsync(City entity)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        var existing = await ctx.Cities.FindAsync(entity.Id);

        if (existing is null)
        {
            Log.Warning("City {EntityId} not found", entity.Id);
            throw new KeyNotFoundException($"City {entity.Id} not found.");
        }

        existing.Name = entity.Name;
        existing.Description = entity.Description;
        await ctx.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteCityAsync(int id)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        var entity = await ctx.Cities.FindAsync(id);
        if (entity is null) return false;
        ctx.Cities.Remove(entity);
        await ctx.SaveChangesAsync();
        return true;
    }

    public async Task<List<Country>> GetAllCountriesAsync()
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        return await ctx.Countries.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
    }

    public async Task<Country> CreateCountryAsync(Country entity)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        ctx.Countries.Add(entity);
        await ctx.SaveChangesAsync();
        return entity;
    }

    public async Task<Country> UpdateCountryAsync(Country entity)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        var existing = await ctx.Countries.FindAsync(entity.Id);

        if (existing is null)
        {
            Log.Warning("Country {EntityId} not found", entity.Id);
            throw new KeyNotFoundException($"Country {entity.Id} not found.");
        }

        existing.Name = entity.Name;
        existing.Description = entity.Description;
        await ctx.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteCountryAsync(int id)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        var entity = await ctx.Countries.FindAsync(id);
        if (entity is null) return false;
        ctx.Countries.Remove(entity);
        await ctx.SaveChangesAsync();
        return true;
    }

    public async Task<List<TeamRole>> GetAllTeamRolesAsync()
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        return await ctx.TeamRoles.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
    }

    public async Task<TeamRole> CreateTeamRoleAsync(TeamRole entity)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        ctx.TeamRoles.Add(entity);
        await ctx.SaveChangesAsync();
        return entity;
    }

    public async Task<TeamRole> UpdateTeamRoleAsync(TeamRole entity)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        var existing = await ctx.TeamRoles.FindAsync(entity.Id);

        if (existing is null)
        {
            Log.Warning("TeamRole {EntityId} not found", entity.Id);
            throw new KeyNotFoundException($"TeamRole {entity.Id} not found.");
        }

        existing.Name = entity.Name;
        existing.Description = entity.Description;
        await ctx.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteTeamRoleAsync(int id)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        var entity = await ctx.TeamRoles.FindAsync(id);
        if (entity is null) return false;
        ctx.TeamRoles.Remove(entity);
        await ctx.SaveChangesAsync();
        return true;
    }
}

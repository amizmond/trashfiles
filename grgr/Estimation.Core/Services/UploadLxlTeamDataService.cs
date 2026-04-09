using System.Collections.Concurrent;
using System.Reflection;
using Estimation.Core.Models;
using Estimation.Excel;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.Services;

public interface IUploadLxlTeamDataService
{
    Task UploadAsync(Stream fileStream);
}

public class UploadLxlTeamDataService : IUploadLxlTeamDataService
{
    private readonly IDbContextFactory<EstimationDbContext> _contextFactory;
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> _namePropertyCache = new();

    public UploadLxlTeamDataService(IDbContextFactory<EstimationDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task UploadAsync(Stream fileStream)
    {
        var readService = new ExcelReadService<UploadLxlTeamData>();
        var rows = readService.ReadSheet(fileStream, "LXL team data").ToList();

        await using var db = await _contextFactory.CreateDbContextAsync();

        var categories = await db.EmployeeCategories.ToListAsync();
        var employeeTypes = await db.EmployeeTypes.ToListAsync();
        var vendors = await db.EmployeeVendors.ToListAsync();
        var roles = await db.EmployeeRoles.ToListAsync();
        var cities = await db.Cities.ToListAsync();
        var countries = await db.Countries.ToListAsync();
        var grades = await db.CorporateGrades.ToListAsync();
        var teams = await db.Teams.Include(t => t.CapitalProjectTeams).ToListAsync();
        var skills = await db.Skills.Include(s => s.Levels).ToListAsync();
        var capitalProjects = await db.CapitalProjects.ToListAsync();
        var existingHumanResources = await db.HumanResources
            .Include(h => h.HumanResourceSkills)
            .ToListAsync();

        await EnsureLookupEntities(db, rows, categories, employeeTypes, vendors, roles, cities, countries, grades, teams, skills, capitalProjects);

        const int BatchSize = 500;
        var pendingChanges = 0;

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.ResourceName))
                continue;

            var employeeNumber = row.ResourceId?.Trim();
            var isExisting = false;
            HumanResource hr;

            if (!string.IsNullOrWhiteSpace(employeeNumber))
            {
                var existing = existingHumanResources.FirstOrDefault(h =>
                    !string.IsNullOrWhiteSpace(h.EmployeeNumber) &&
                    h.EmployeeNumber.Equals(employeeNumber, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    hr = existing;
                    isExisting = true;
                }
                else
                {
                    hr = new HumanResource();
                }
            }
            else
            {
                hr = new HumanResource();
            }

            hr.Cio = row.Cio?.Trim();
            hr.Cio1 = row.Cio1?.Trim();
            hr.Cio2 = row.Cio2?.Trim();
            hr.EmployeeNumber = employeeNumber;
            hr.EmployeeName = row.ResourceName.Trim();
            hr.FullName = row.ResourceName.Trim();
            hr.LineManagerName = row.LineManagerNamePerSap?.Trim();
            hr.IsActive = string.Equals(row.ValidNow?.Trim(), "Yes", StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(row.Category))
            {
                var name = row.Category.Trim();
                var entity = categories.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (entity != null) hr.EmployeeCategoryId = entity.Id;
            }

            if (!string.IsNullOrWhiteSpace(row.EmployeeType))
            {
                var name = row.EmployeeType.Trim();
                var entity = employeeTypes.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (entity != null) hr.EmployeeTypeId = entity.Id;
            }

            if (!string.IsNullOrWhiteSpace(row.Vendor))
            {
                var name = row.Vendor.Trim();
                var entity = vendors.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (entity != null) hr.EmployeeVendorId = entity.Id;
            }

            if (!string.IsNullOrWhiteSpace(row.Role))
            {
                var name = row.Role.Trim();
                var entity = roles.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (entity != null) hr.EmployeeRoleId = entity.Id;
            }

            if (!string.IsNullOrWhiteSpace(row.City))
            {
                var name = row.City.Trim();
                var entity = cities.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (entity != null) hr.CityId = entity.Id;
            }

            if (!string.IsNullOrWhiteSpace(row.Country))
            {
                var name = row.Country.Trim();
                var entity = countries.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (entity != null) hr.CountryId = entity.Id;
            }

            if (!string.IsNullOrWhiteSpace(row.CorporateGrade))
            {
                var name = row.CorporateGrade.Trim();
                var entity = grades.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (entity != null) hr.CorporateGradeId = entity.Id;
            }

            if (!isExisting)
            {
                db.HumanResources.Add(hr);
                existingHumanResources.Add(hr);
            }

            pendingChanges++;

            if (pendingChanges >= BatchSize)
            {
                await db.SaveChangesAsync();
                pendingChanges = 0;
            }
        }

        if (pendingChanges > 0)
        {
            await db.SaveChangesAsync();
            pendingChanges = 0;
        }

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.ResourceName))
                continue;

            var employeeNumber = row.ResourceId?.Trim();
            var hr = FindHr(existingHumanResources, employeeNumber, row.ResourceName.Trim());
            if (hr is null) continue;

            if (!string.IsNullOrWhiteSpace(row.FeatureTeam))
            {
                var teamName = row.FeatureTeam.Trim();
                var team = teams.FirstOrDefault(t => t.Name.Equals(teamName, StringComparison.OrdinalIgnoreCase));
                if (team != null)
                {
                    if (!string.IsNullOrWhiteSpace(row.TeamFullName) && string.IsNullOrWhiteSpace(team.FullName))
                        team.FullName = row.TeamFullName.Trim();

                    if (!string.IsNullOrWhiteSpace(row.OptionalTeamTag) && string.IsNullOrWhiteSpace(team.OptionalTeamTag))
                        team.OptionalTeamTag = row.OptionalTeamTag.Trim();

                    if (!string.IsNullOrWhiteSpace(row.Platform))
                    {
                        var platformName = row.Platform.Trim();
                        var capitalProject = capitalProjects.FirstOrDefault(cp => cp.Name.Equals(platformName, StringComparison.OrdinalIgnoreCase));
                        if (capitalProject != null && !team.CapitalProjectTeams.Any(cpt => cpt.CapitalProjectId == capitalProject.Id))
                        {
                            var cpt = new CapitalProjectTeam { CapitalProjectId = capitalProject.Id, TeamId = team.Id };
                            db.CapitalProjectTeams.Add(cpt);
                            team.CapitalProjectTeams.Add(cpt);
                        }
                    }

                    if (!db.TeamMembers.Local.Any(tm => tm.TeamId == team.Id && tm.HumanResourceId == hr.Id))
                    {
                        var alreadyExists = await db.TeamMembers.AnyAsync(tm => tm.TeamId == team.Id && tm.HumanResourceId == hr.Id);
                        if (!alreadyExists)
                        {
                            db.TeamMembers.Add(new TeamMember { TeamId = team.Id, HumanResourceId = hr.Id });
                        }
                    }
                }
            }

            var skillMappings = new Dictionary<string, string?>
            {
                { "C#", row.CSharp },
                { "C++", row.CPlusPlus },
                { "Java", row.Java },
                { "Python", row.Python },
                { "R", row.R },
                { "SCALA", row.Scala },
                { "JavaScript", row.JavaScript },
                { "SQL (MySQL, PostgreSQL, MSSQL)", row.Sql },
                { "NoSQL (MongoDB, DynamoDB, HBASE etc.)", row.NoSql },
                { "HADOOP (HIVE/Impala)", row.HadoopHiveImpala },
                { "AWS", row.Aws },
                { "Azure", row.Azure },
                { "Docker", row.Docker },
                { "Kubernetes", row.Kubernetes },
                { "CI/CD Pipelines (Jenkins, GitHub Actions)", row.CiCdPipelines },
                { "React.js", row.ReactJs },
                { "Angular", row.Angular },
                { "Qliksense", row.QlikSense },
                { "HTML/CSS", row.HtmlCss },
                { ".NET Core", row.DotNetCore },
                { "Node.js", row.NodeJs },
                { "Spring Boot", row.SpringBoot },
                { "Unit Testing (JUnit, NUnit)", row.UnitTesting },
                { "Test Automation (Selenium, Cypress)", row.TestAutomation },
                { "API Testing (Postman, REST Assured)", row.ApiTesting },
                { "Analysis", row.Analysis },
                { "Support/testing", row.SupportTesting },
                { "DevOps Calc", row.DevOpsCalc },
                { "C#.NET Calc", row.CSharpNetCalc },
                { "R Calc", row.RCalc },
                { "DB/SSIS Calc", row.DbSsisCalc },
                { "QS Calc", row.QsCalc },
            };

            foreach (var (skillName, cellValue) in skillMappings)
            {
                if (string.IsNullOrWhiteSpace(cellValue))
                    continue;

                var skill = skills.FirstOrDefault(s => s.Name.Equals(skillName, StringComparison.OrdinalIgnoreCase));
                if (skill == null) continue;

                int? skillLevelId = null;
                var parsed = ParseSkillLevel(cellValue);
                if (parsed != null)
                {
                    var (levelValue, levelName) = parsed.Value;
                    var skillLevel = skill.Levels.FirstOrDefault(sl =>
                        sl.Value == levelValue && sl.Name.Equals(levelName, StringComparison.OrdinalIgnoreCase));
                    if (skillLevel != null)
                        skillLevelId = skillLevel.Id;
                }

                var existingHrSkill = hr.HumanResourceSkills
                    .FirstOrDefault(hrs => hrs.SkillId == skill.Id);
                if (existingHrSkill != null)
                {
                    existingHrSkill.SkillLevelId = skillLevelId;
                }
                else
                {
                    var newHrSkill = new HumanResourceSkill
                    {
                        HumanResourceId = hr.Id,
                        SkillId = skill.Id,
                        SkillLevelId = skillLevelId
                    };
                    db.HumanResourceSkills.Add(newHrSkill);
                    hr.HumanResourceSkills.Add(newHrSkill);
                }
            }

            pendingChanges++;
            if (pendingChanges >= BatchSize)
            {
                await db.SaveChangesAsync();
                pendingChanges = 0;
            }
        }

        if (pendingChanges > 0)
            await db.SaveChangesAsync();
    }

    private static HumanResource? FindHr(List<HumanResource> hrs, string? employeeNumber, string fullName)
    {
        if (!string.IsNullOrWhiteSpace(employeeNumber))
        {
            var byNumber = hrs.FirstOrDefault(h =>
                !string.IsNullOrWhiteSpace(h.EmployeeNumber) &&
                h.EmployeeNumber.Equals(employeeNumber, StringComparison.OrdinalIgnoreCase));
            if (byNumber != null) return byNumber;
        }
        return hrs.FirstOrDefault(h => h.FullName.Equals(fullName, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task EnsureLookupEntities(
        EstimationDbContext db,
        List<UploadLxlTeamData> rows,
        List<EmployeeCategory> categories,
        List<EmployeeType> employeeTypes,
        List<EmployeeVendor> vendors,
        List<EmployeeRole> roles,
        List<City> cities,
        List<Country> countries,
        List<CorporateGrade> grades,
        List<Team> teams,
        List<Skill> skills,
        List<CapitalProject> capitalProjects)
    {
        var needsSave = false;

        foreach (var row in rows)
        {
            needsSave |= EnsureLookup(db, row.Category, categories, n => new EmployeeCategory { Name = n }, db.EmployeeCategories);
            needsSave |= EnsureLookup(db, row.EmployeeType, employeeTypes, n => new EmployeeType { Name = n }, db.EmployeeTypes);
            needsSave |= EnsureLookup(db, row.Vendor, vendors, n => new EmployeeVendor { Name = n }, db.EmployeeVendors);
            needsSave |= EnsureLookup(db, row.Role, roles, n => new EmployeeRole { Name = n }, db.EmployeeRoles);
            needsSave |= EnsureLookup(db, row.City, cities, n => new City { Name = n }, db.Cities);
            needsSave |= EnsureLookup(db, row.Country, countries, n => new Country { Name = n }, db.Countries);
            needsSave |= EnsureLookup(db, row.CorporateGrade, grades, n => new CorporateGrade { Name = n }, db.CorporateGrades);
            needsSave |= EnsureLookup(db, row.FeatureTeam, teams, n => new Team { Name = n }, db.Teams);
            needsSave |= EnsureLookup(db, row.Platform, capitalProjects, n => new CapitalProject { Name = n }, db.CapitalProjects);

            var skillMappings = GetSkillMappings(row);
            foreach (var (skillName, cellValue) in skillMappings)
            {
                if (string.IsNullOrWhiteSpace(cellValue)) continue;

                var skill = skills.FirstOrDefault(s => s.Name.Equals(skillName.Trim(), StringComparison.OrdinalIgnoreCase));
                if (skill == null)
                {
                    skill = new Skill { Name = skillName.Trim(), Created = DateTime.Now };
                    db.Skills.Add(skill);
                    skills.Add(skill);
                    needsSave = true;
                }

                var parsed = ParseSkillLevel(cellValue);
                if (parsed != null)
                {
                    var (levelValue, levelName) = parsed.Value;
                    var existing = skill.Levels.FirstOrDefault(sl =>
                        sl.Value == levelValue && sl.Name.Equals(levelName, StringComparison.OrdinalIgnoreCase));
                    if (existing == null)
                    {
                        var newLevel = new SkillLevel { Name = levelName, Value = levelValue };
                        skill.Levels.Add(newLevel);
                        needsSave = true;
                    }
                }
            }
        }

        if (needsSave)
            await db.SaveChangesAsync();
    }

    private static bool EnsureLookup<T>(EstimationDbContext db, string? value, List<T> cache, Func<string, T> factory, DbSet<T> dbSet)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var name = value.Trim();

        var nameProp = _namePropertyCache.GetOrAdd(typeof(T), t => t.GetProperty("Name"));
        if (nameProp == null) return false;

        var exists = cache.Any(x =>
        {
            var val = nameProp.GetValue(x) as string;
            return val != null && val.Equals(name, StringComparison.OrdinalIgnoreCase);
        });

        if (!exists)
        {
            var entity = factory(name);
            dbSet.Add(entity);
            cache.Add(entity);
            return true;
        }
        return false;
    }

    private static Dictionary<string, string?> GetSkillMappings(UploadLxlTeamData row) => new()
    {
        { "C#", row.CSharp },
        { "C++", row.CPlusPlus },
        { "Java", row.Java },
        { "Python", row.Python },
        { "R", row.R },
        { "SCALA", row.Scala },
        { "JavaScript", row.JavaScript },
        { "SQL (MySQL, PostgreSQL, MSSQL)", row.Sql },
        { "NoSQL (MongoDB, DynamoDB, HBASE etc.)", row.NoSql },
        { "HADOOP (HIVE/Impala)", row.HadoopHiveImpala },
        { "AWS", row.Aws },
        { "Azure", row.Azure },
        { "Docker", row.Docker },
        { "Kubernetes", row.Kubernetes },
        { "CI/CD Pipelines (Jenkins, GitHub Actions)", row.CiCdPipelines },
        { "React.js", row.ReactJs },
        { "Angular", row.Angular },
        { "Qliksense", row.QlikSense },
        { "HTML/CSS", row.HtmlCss },
        { ".NET Core", row.DotNetCore },
        { "Node.js", row.NodeJs },
        { "Spring Boot", row.SpringBoot },
        { "Unit Testing (JUnit, NUnit)", row.UnitTesting },
        { "Test Automation (Selenium, Cypress)", row.TestAutomation },
        { "API Testing (Postman, REST Assured)", row.ApiTesting },
        { "Analysis", row.Analysis },
        { "Support/testing", row.SupportTesting },
        { "DevOps Calc", row.DevOpsCalc },
        { "C#.NET Calc", row.CSharpNetCalc },
        { "R Calc", row.RCalc },
        { "DB/SSIS Calc", row.DbSsisCalc },
        { "QS Calc", row.QsCalc },
    };

    private static (int Value, string Name)? ParseSkillLevel(string cellValue)
    {
        var trimmed = cellValue.Trim();
        var dotIndex = trimmed.IndexOf('.');
        if (dotIndex <= 0)
            return null;

        var valuePart = trimmed[..dotIndex].Trim();
        var namePart = trimmed[(dotIndex + 1)..].Trim();

        if (int.TryParse(valuePart, out var value) && !string.IsNullOrWhiteSpace(namePart))
            return (value, namePart);

        return null;
    }
}

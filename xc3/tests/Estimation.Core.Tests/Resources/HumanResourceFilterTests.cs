using Estimation.Core.Resources.Models;
using Estimation.Core.Resources.Services;
using Estimation.Core.Tests.Administration;
using Estimation.Core.Tests.Infrastructure;
using Estimation.Core.Train.Models;
using Estimation.Excel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Estimation.Core.Tests.Resources;

public abstract class HumanResourceFilterTestsBase : IAsyncLifetime
{
    protected const string Prefix = "ZQXHR ";

    protected abstract IDbContextFactory<EstimationDbContext> Factory { get; }

    protected int PaymentsArtId { get; private set; }
    protected int LendingArtId { get; private set; }

    protected virtual bool Skip => false;

    protected virtual bool SharedDatabase => false;

    public virtual async Task InitializeAsync()
    {
        if (Skip)
        {
            return;
        }

        await DeleteOwnRowsAsync();

        await using var db = await Factory.CreateDbContextAsync();

        var retail = new Department { Name = Prefix + "Retail" };
        var wholesale = new Department { Name = Prefix + "Wholesale" };
        var payments = new Art { Name = Prefix + "Payments", Department = retail };
        var lending = new Art { Name = Prefix + "Lending", Department = wholesale };

        var argon = new Team { Name = Prefix + "Argon" };
        var boron = new Team { Name = Prefix + "Boron" };
        var juno = new Team { Name = Prefix + "Juno" };
        var lambda = new Team { Name = Prefix + "Lambda" };
        db.CapitalProjectTeams.AddRange(
            new ArtTeam { Art = payments, Team = argon },
            new ArtTeam { Art = lending, Team = boron },
            new ArtTeam { Art = payments, Team = juno },
            new ArtTeam { Art = lending, Team = juno });
        db.Teams.Add(lambda);

        var developer = new TeamRole { Name = Prefix + "Developer" };
        var scrumMaster = new TeamRole { Name = Prefix + "Scrum Master" };

        var java = new Skill { Name = Prefix + "Java", Created = DateTime.UtcNow };
        var react = new Skill { Name = Prefix + "React", Created = DateTime.UtcNow };
        var sql = new Skill { Name = Prefix + "SQL", Created = DateTime.UtcNow };

        var uk = new Country { Name = Prefix + "UK" };
        var czechia = new Country { Name = Prefix + "Czechia" };
        var london = new City { Name = Prefix + "London", Country = uk };
        var prague = new City { Name = Prefix + "Prague", Country = czechia };

        var ann = Person("Ann", city: london,
            teams: [(argon, developer), (boron, scrumMaster)],
            skills: [java, react]);
        var ben = Person("Ben", city: prague,
            teams: [(argon, scrumMaster)],
            skills: [java]);
        var cid = Person("Cid", isActive: false,
            teams: [(boron, developer)],
            skills: [react]);
        var dee = Person("Dee", city: london,
            skills: [sql]);
        var eve = Person("Eve",
            teams: [(lambda, developer), (juno, developer)]);

        var permanent = new EmployeeCategory { Name = Prefix + "Permanent" };
        ann.EmployeeCategory = permanent;
        cid.EmployeeCategory = permanent;
        ben.EmployeeCategory = new EmployeeCategory { Name = Prefix + "Contractor" };
        ann.EmployeeType = new EmployeeType { Name = Prefix + "Full time" };
        ben.EmployeeType = new EmployeeType { Name = Prefix + "Part time" };
        ann.EmployeeRole = new EmployeeRole { Name = Prefix + "Engineer" };
        cid.EmployeeRole = new EmployeeRole { Name = Prefix + "Analyst" };
        ben.EmployeeVendor = new EmployeeVendor { Name = Prefix + "Acme" };
        ann.CorporateGrade = new CorporateGrade { Name = Prefix + "G7" };
        ben.CorporateGrade = new CorporateGrade { Name = Prefix + "G6" };

        db.HumanResources.AddRange(ann, ben, cid, dee, eve);

        await db.SaveChangesAsync();
        PaymentsArtId = payments.Id;
        LendingArtId = lending.Id;
    }

    public virtual async Task DisposeAsync()
    {
        if (Skip)
        {
            return;
        }

        await DeleteOwnRowsAsync();
    }

    private static HumanResource Person(
        string name,
        bool isActive = true,
        City? city = null,
        (Team Team, TeamRole Role)[]? teams = null,
        Skill[]? skills = null) =>
        new()
        {
            FullName = Prefix + name,
            EmployeeName = Prefix + name,
            IsActive = isActive,
            City = city,
            TeamMembers = (teams ?? []).Select(t => new TeamMember { Team = t.Team, TeamRole = t.Role }).ToList(),
            HumanResourceSkills = (skills ?? []).Select(s => new HumanResourceSkill { Skill = s }).ToList(),
        };

    private async Task DeleteOwnRowsAsync()
    {
        if (!SharedDatabase)
        {
            return;
        }

        await using var db = await Factory.CreateDbContextAsync();
        await db.TeamMembers.Where(tm => tm.HumanResource.FullName.StartsWith(Prefix)).ExecuteDeleteAsync();
        await db.HumanResourceSkills.Where(s => s.HumanResource.FullName.StartsWith(Prefix)).ExecuteDeleteAsync();
        await db.HumanResources.Where(hr => hr.FullName.StartsWith(Prefix)).ExecuteDeleteAsync();
        await db.CapitalProjectTeams.Where(c => c.Team.Name.StartsWith(Prefix)).ExecuteDeleteAsync();
        await db.Teams.Where(t => t.Name.StartsWith(Prefix)).ExecuteDeleteAsync();
        await db.CapitalProjects.Where(a => a.Name.StartsWith(Prefix)).ExecuteDeleteAsync();
        await db.Departments.Where(d => d.Name.StartsWith(Prefix)).ExecuteDeleteAsync();
        await db.TeamRoles.Where(r => r.Name.StartsWith(Prefix)).ExecuteDeleteAsync();
        await db.Skills.Where(s => s.Name.StartsWith(Prefix)).ExecuteDeleteAsync();
        await db.Cities.Where(c => c.Name.StartsWith(Prefix)).ExecuteDeleteAsync();
        await db.Countries.Where(c => c.Name.StartsWith(Prefix)).ExecuteDeleteAsync();
        await db.EmployeeCategories.Where(c => c.Name.StartsWith(Prefix)).ExecuteDeleteAsync();
        await db.EmployeeTypes.Where(t => t.Name.StartsWith(Prefix)).ExecuteDeleteAsync();
        await db.EmployeeRoles.Where(r => r.Name.StartsWith(Prefix)).ExecuteDeleteAsync();
        await db.EmployeeVendors.Where(v => v.Name.StartsWith(Prefix)).ExecuteDeleteAsync();
        await db.CorporateGrades.Where(g => g.Name.StartsWith(Prefix)).ExecuteDeleteAsync();
    }

    private static string[] Own(params string[] names) => names.Select(n => Prefix + n).ToArray();

    private async Task<string[]> NamesAsync(HumanResourceFilter filter)
    {
        await using var db = await Factory.CreateDbContextAsync();
        var names = await filter.Apply(db.HumanResources.AsNoTracking(), db.TeamMembers.AsNoTracking())
            .Select(hr => hr.FullName)
            .ToListAsync();
        return names.Where(n => n.StartsWith(Prefix)).Order().ToArray();
    }

    [Fact]
    public async Task A_team_role_counts_only_in_the_selected_team()
    {
        if (Skip)
        {
            return;
        }

        var names = await NamesAsync(new HumanResourceFilter
        {
            TeamNames = Own("Argon"),
            TeamRoleNames = Own("Scrum Master"),
        });

        Assert.Equal(Own("Ben"), names);
    }

    [Fact]
    public async Task ART_department_and_role_all_match_the_same_team()
    {
        if (Skip)
        {
            return;
        }

        Assert.Equal(Own("Cid", "Eve"), await NamesAsync(new HumanResourceFilter
        {
            ArtNames = Own("Lending"),
            TeamRoleNames = Own("Developer"),
        }));

        Assert.Equal(Own("Ann"), await NamesAsync(new HumanResourceFilter
        {
            DepartmentNames = Own("Wholesale"),
            TeamRoleNames = Own("Scrum Master"),
        }));

        Assert.Empty(await NamesAsync(new HumanResourceFilter
        {
            ArtNames = Own("Lending"),
            TeamNames = Own("Argon"),
        }));
    }

    [Fact]
    public async Task Team_not_finds_people_without_a_team_or_outside_the_selected_teams()
    {
        if (Skip)
        {
            return;
        }

        Assert.Equal(Own("Dee"), await NamesAsync(new HumanResourceFilter { TeamNot = true }));

        Assert.Equal(Own("Cid", "Dee", "Eve"), await NamesAsync(new HumanResourceFilter
        {
            TeamNot = true,
            TeamNames = Own("Argon"),
        }));

        Assert.Equal(Own("Cid", "Eve"), await NamesAsync(new HumanResourceFilter
        {
            TeamNot = true,
            TeamNames = Own("Argon"),
            ArtNames = Own("Lending"),
        }));
    }

    [Fact]
    public async Task Skills_match_any_selected_skill_or_with_All_every_one()
    {
        if (Skip)
        {
            return;
        }

        var skills = Own("Java", "React");

        Assert.Equal(Own("Ann", "Ben", "Cid"), await NamesAsync(new HumanResourceFilter { SkillNames = skills }));
        Assert.Equal(Own("Ann"), await NamesAsync(new HumanResourceFilter { SkillNames = skills, AllSkills = true }));
    }

    [Fact]
    public async Task An_unknown_skill_name_matches_nobody_instead_of_being_ignored()
    {
        if (Skip)
        {
            return;
        }

        Assert.Empty(await NamesAsync(new HumanResourceFilter { SkillNames = Own("Cobol") }));
        Assert.Empty(await NamesAsync(new HumanResourceFilter { SkillNames = Own("Java", "Cobol"), AllSkills = true }));
        Assert.Equal(Own("Ann", "Ben"), await NamesAsync(new HumanResourceFilter { SkillNames = Own("Java", "Cobol") }));
    }

    [Fact]
    public async Task Status_location_and_search_filters_still_apply()
    {
        if (Skip)
        {
            return;
        }

        Assert.Equal(Own("Cid"), await NamesAsync(new HumanResourceFilter { IsActive = false }));
        Assert.Equal(Own("Ann", "Dee"), await NamesAsync(new HumanResourceFilter { CountryNames = Own("UK") }));
        Assert.Equal(Own("Ben"), await NamesAsync(new HumanResourceFilter { CityNames = Own("Prague") }));
        Assert.Equal(Own("Eve"), await NamesAsync(new HumanResourceFilter { Search = Prefix + "Ev" }));
    }

    [Fact]
    public async Task Employee_attributes_match_the_selected_values()
    {
        if (Skip)
        {
            return;
        }

        Assert.Equal(Own("Ann", "Cid"), await NamesAsync(new HumanResourceFilter { EmployeeCategoryNames = Own("Permanent") }));
        Assert.Equal(Own("Ann"), await NamesAsync(new HumanResourceFilter { EmployeeTypeNames = Own("Full time") }));
        Assert.Equal(Own("Cid"), await NamesAsync(new HumanResourceFilter { EmployeeRoleNames = Own("Analyst") }));
        Assert.Equal(Own("Ben"), await NamesAsync(new HumanResourceFilter { EmployeeVendorNames = Own("Acme") }));
        Assert.Equal(Own("Ann", "Ben"), await NamesAsync(new HumanResourceFilter { CorporateGradeNames = Own("G6", "G7") }));
    }

    [Fact]
    public async Task Not_with_nothing_selected_finds_people_without_a_value()
    {
        if (Skip)
        {
            return;
        }

        Assert.Equal(Own("Dee", "Eve"), await NamesAsync(new HumanResourceFilter { EmployeeCategoryNot = true }));
        Assert.Equal(Own("Cid", "Dee", "Eve"), await NamesAsync(new HumanResourceFilter { EmployeeTypeNot = true }));
        Assert.Equal(Own("Ben", "Dee", "Eve"), await NamesAsync(new HumanResourceFilter { EmployeeRoleNot = true }));
        Assert.Equal(Own("Ann", "Cid", "Dee", "Eve"), await NamesAsync(new HumanResourceFilter { EmployeeVendorNot = true }));
        Assert.Equal(Own("Cid", "Dee", "Eve"), await NamesAsync(new HumanResourceFilter { CorporateGradeNot = true }));
        Assert.Equal(Own("Cid", "Eve"), await NamesAsync(new HumanResourceFilter { CountryNot = true }));
        Assert.Equal(Own("Cid", "Eve"), await NamesAsync(new HumanResourceFilter { CityNot = true }));
    }

    [Fact]
    public async Task Not_with_values_excludes_them_and_keeps_people_without_a_value()
    {
        if (Skip)
        {
            return;
        }

        Assert.Equal(Own("Ben", "Dee", "Eve"), await NamesAsync(new HumanResourceFilter
        {
            EmployeeCategoryNames = Own("Permanent"),
            EmployeeCategoryNot = true,
        }));
        Assert.Equal(Own("Ben", "Cid", "Dee", "Eve"), await NamesAsync(new HumanResourceFilter
        {
            CorporateGradeNames = Own("G7"),
            CorporateGradeNot = true,
        }));
        Assert.Equal(Own("Ben", "Cid", "Eve"), await NamesAsync(new HumanResourceFilter
        {
            CountryNames = Own("UK"),
            CountryNot = true,
        }));
        Assert.Equal(Own("Ann", "Cid", "Dee", "Eve"), await NamesAsync(new HumanResourceFilter
        {
            CityNames = Own("Prague"),
            CityNot = true,
        }));
        Assert.Equal(Own("Ann", "Cid", "Dee", "Eve"), await NamesAsync(new HumanResourceFilter
        {
            EmployeeVendorNames = Own("Acme"),
            EmployeeVendorNot = true,
        }));
    }

    [Fact]
    public async Task The_paged_list_counts_matches_and_everyone_the_user_may_see()
    {
        if (Skip)
        {
            return;
        }

        var service = new HumanResourceService(Factory);

        var result = await service.GetPagedListAsync(0, 10, null, true, new HumanResourceFilter
        {
            AllowedArtIds = [LendingArtId],
            IsActive = true,
        });

        Assert.Equal(Own("Ann", "Eve"), result.Items.Select(i => i.FullName).Order().ToArray());
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(3, result.ScopeCount);
    }

    [Fact]
    public async Task Filter_options_only_offer_teams_and_cities_inside_the_users_ARTs()
    {
        if (Skip)
        {
            return;
        }

        var service = new HumanResourceService(Factory);

        var scoped = await service.GetFilterOptionsAsync([LendingArtId]);
        Assert.Equal(Own("Boron", "Juno"), scoped.Teams.Select(t => t.Name).Order().ToArray());
        Assert.Equal([(Prefix + "London", Prefix + "UK")], scoped.Cities.Select(c => (c.Name, c.CountryName)).ToArray());
        Assert.Equal(Own("Lending", "Payments"), scoped.Teams.Single(t => t.Name == Prefix + "Juno").ArtNames.Order().ToArray());

        var all = await service.GetFilterOptionsAsync(null);
        Assert.Equal(Own("Argon", "Boron", "Juno", "Lambda"),
            all.Teams.Select(t => t.Name).Where(n => n.StartsWith(Prefix)).Order().ToArray());
        Assert.Equal(Own("London", "Prague"),
            all.Cities.Select(c => c.Name).Where(n => n.StartsWith(Prefix)).Order().ToArray());
    }

    [Fact]
    public async Task The_excel_export_keeps_to_the_users_ARTs()
    {
        if (Skip)
        {
            return;
        }

        var service = new HumanResourceUploadService(Factory);
        var selection = new HrUploadColumnSelection { Columns = [HrUploadColumn.FullName] };

        var workbook = await service.ExportFilteredAsync(selection, new HumanResourceFilter
        {
            AllowedArtIds = [PaymentsArtId],
            Search = Prefix,
        });

        using var stream = new MemoryStream(workbook);
        var (_, rows) = ExcelSheetReader.Read(stream);
        Assert.Equal(Own("Ann", "Ben", "Eve"), rows.Select(r => r[0]).Order().ToArray());
    }
}

public sealed class HumanResourceFilterTests : HumanResourceFilterTestsBase
{
    private readonly InMemoryDatabase _db = new();

    protected override IDbContextFactory<EstimationDbContext> Factory => _db;
}

[Collection(LocalDbCollection.Name)]
public sealed class HumanResourceFilterSqlServerTests : HumanResourceFilterTestsBase
{
    private readonly LocalDbFixture _localDb;

    public HumanResourceFilterSqlServerTests(LocalDbFixture localDb) => _localDb = localDb;

    protected override IDbContextFactory<EstimationDbContext> Factory => _localDb.Factory;

    protected override bool Skip => LocalDbAvailability.UnavailableReason is not null;

    protected override bool SharedDatabase => true;
}

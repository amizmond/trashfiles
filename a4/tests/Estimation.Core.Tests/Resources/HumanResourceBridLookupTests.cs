using Estimation.Core.Resources.Models;
using Estimation.Core.Resources.Services;
using Estimation.Core.Tests.Infrastructure;
using Xunit;

namespace Estimation.Core.Tests.Resources;

public class HumanResourceBridLookupTests
{
    private readonly InMemoryDatabase _db = new();
    private readonly HumanResourceService _service;

    public HumanResourceBridLookupTests()
    {
        _service = new HumanResourceService(_db);
    }

    private Task SeedPersonAsync(int id, string name, string? brid, bool isActive = true) => _db.SeedAsync(db =>
        db.HumanResources.Add(new HumanResource
        {
            Id = id,
            EmployeeName = name,
            FullName = name,
            EmployeeNumber = brid,
            IsActive = isActive
        }));

    [Fact]
    public async Task Finds_the_person_by_brid_without_case_or_surrounding_spaces()
    {
        await SeedPersonAsync(5, "Ada Lovelace", " G01000001 ");
        await _db.SeedAsync(db =>
        {
            db.Skills.Add(new Skill { Id = 3, Name = "C#", Created = new DateTime(2026, 1, 1) });
            db.HumanResourceSkills.Add(new HumanResourceSkill { HumanResourceId = 5, SkillId = 3 });
        });

        var ada = await _service.GetByEmployeeNumberAsync("g01000001");

        Assert.NotNull(ada);
        Assert.Equal(5, ada.Id);
        Assert.Equal("C#", Assert.Single(ada.HumanResourceSkills).Skill.Name);
    }

    [Fact]
    public async Task A_shared_brid_finds_the_active_person()
    {
        await SeedPersonAsync(5, "Ada Lovelace", "G01000001", isActive: false);
        await SeedPersonAsync(6, "Ada King", "G01000001");

        Assert.Equal(6, (await _service.GetByEmployeeNumberAsync("G01000001"))?.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("G09999999")]
    public async Task A_blank_or_unknown_brid_finds_nobody(string? brid)
    {
        await SeedPersonAsync(5, "Ada Lovelace", "G01000001");
        await SeedPersonAsync(6, "Grace Hopper", null);

        Assert.Null(await _service.GetByEmployeeNumberAsync(brid));
    }
}

using Estimation.Core.Resources.Models;
using Estimation.Core.Train.Models;
using Estimation.Services.Shared;
using Xunit;

namespace Estimation.Tests.Shared;

public class DepartmentArtsTests
{
    private static readonly Dictionary<string, string> DepartmentByArt = DepartmentArts.ByArtName(
    [
        new Department { Name = "Retail", CapitalProjects = [new Art { Name = "Payments" }, new Art { Name = "Cards" }] },
        new Department { Name = "Markets", CapitalProjects = [new Art { Name = "Trading" }] },
    ]);

    [Fact]
    public void Without_a_department_every_art_stays()
    {
        var arts = DepartmentArts.InDepartments(["Trading", "Payments", "No Dept"], DepartmentByArt, new HashSet<string>());

        Assert.Equal(["Trading", "Payments", "No Dept"], arts);
    }

    [Fact]
    public void Only_arts_of_the_selected_departments_stay_in_their_order()
    {
        var arts = DepartmentArts.InDepartments(
            ["Trading", "Payments", "Cards", "No Dept"],
            DepartmentByArt,
            new HashSet<string>(["Retail"], StringComparer.OrdinalIgnoreCase));

        Assert.Equal(["Payments", "Cards"], arts);
    }

    [Fact]
    public void Several_departments_keep_the_arts_of_each()
    {
        var arts = DepartmentArts.InDepartments(
            ["Trading", "Payments", "No Dept"],
            DepartmentByArt,
            new HashSet<string>(["Retail", "Markets"], StringComparer.OrdinalIgnoreCase));

        Assert.Equal(["Trading", "Payments"], arts);
    }

    [Fact]
    public void Names_match_in_any_casing()
    {
        var arts = DepartmentArts.InDepartments(
            ["payments", "TRADING"],
            DepartmentByArt,
            new HashSet<string>(["retail"], StringComparer.OrdinalIgnoreCase));

        Assert.Equal(["payments"], arts);
    }

    [Fact]
    public void An_art_listed_under_two_departments_belongs_to_the_first()
    {
        var departmentByArt = DepartmentArts.ByArtName(
        [
            new Department { Name = "Retail", CapitalProjects = [new Art { Name = "Shared" }] },
            new Department { Name = "Markets", CapitalProjects = [new Art { Name = "shared" }] },
        ]);

        Assert.Equal("Retail", departmentByArt["SHARED"]);
    }
}

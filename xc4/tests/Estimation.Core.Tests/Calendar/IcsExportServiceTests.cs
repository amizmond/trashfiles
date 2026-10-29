using System.Text;
using Estimation.Core.Calendar.Models;
using Estimation.Core.Calendar.Services;
using Estimation.Core.Resources.Models;
using Estimation.Core.Tests.Infrastructure;
using Xunit;

namespace Estimation.Core.Tests.Calendar;

public class IcsExportServiceTests
{
    private const int TeamId = 1;

    private readonly InMemoryDatabase _db = new();

    [Fact]
    public async Task A_city_holiday_on_the_date_of_its_countrys_holiday_is_exported_once()
    {
        await _db.SeedAsync(db =>
        {
            db.Countries.Add(new Country { Id = 1, Name = "United Kingdom" });
            db.Cities.AddRange(
                new City { Id = 10, Name = "London", CountryId = 1 },
                new City { Id = 11, Name = "Manchester", CountryId = 1 });
            db.Teams.Add(new Team { Id = TeamId, Name = "Team A" });
            db.HumanResources.AddRange(
                new HumanResource { Id = 100, FullName = "Ann Lee", EmployeeName = "Lee", CityId = 10 },
                new HumanResource { Id = 200, FullName = "Ben Cole", EmployeeName = "Cole", CityId = 11 });
            db.TeamMembers.AddRange(
                new TeamMember { TeamId = TeamId, HumanResourceId = 100 },
                new TeamMember { TeamId = TeamId, HumanResourceId = 200 });
            db.PublicHolidays.AddRange(
                new PublicHoliday { Id = 1, CountryId = 1, Date = new DateTime(2026, 12, 25), Name = "Christmas Day" },
                new PublicHoliday { Id = 2, CityId = 10, Date = new DateTime(2026, 12, 25), Name = "Christmas Day" },
                new PublicHoliday { Id = 3, CityId = 10, Date = new DateTime(2027, 1, 21), Name = "London Borough Day" });
        });

        var ics = Encoding.UTF8.GetString(await new IcsExportService(_db).BuildTeamCalendarAsync(TeamId));

        Assert.Contains("UID:publicholiday-1@estimation", ics);
        Assert.DoesNotContain("UID:publicholiday-2@estimation", ics);
        Assert.Contains("UID:publicholiday-3@estimation", ics);
    }
}

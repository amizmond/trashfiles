using Estimation.Core.Calendar.Models;
using Estimation.Core.Calendar.Services;
using Estimation.Core.Resources.Models;
using Estimation.Core.Tests.Infrastructure;
using Xunit;

namespace Estimation.Core.Tests.Calendar;

public class PublicHolidayResolverTests
{
    private const int Poland = 1;
    private const int Czechia = 2;
    private const int Krakow = 10;
    private const int Warsaw = 11;
    private const int Brno = 20;
    private const int Ann = 1;
    private const int Ben = 2;
    private const int Cat = 3;
    private const int Eva = 4;

    private static readonly DateTime KrakowDay = new(2026, 11, 4);
    private static readonly DateTime IndependenceDay = new(2026, 11, 11);
    private static readonly DateTime SharedDay = new(2026, 11, 17);
    private static readonly DateTime CzechDay = new(2026, 11, 18);

    private readonly InMemoryDatabase _db = new();

    private Task SeedAsync() => _db.SeedAsync(db =>
    {
        db.Countries.AddRange(
            new Country { Id = Poland, Name = "Poland" },
            new Country { Id = Czechia, Name = "Czech Republic" });
        db.Cities.AddRange(
            new City { Id = Krakow, Name = "Krakow", CountryId = Poland },
            new City { Id = Warsaw, Name = "Warsaw", CountryId = Poland },
            new City { Id = Brno, Name = "Brno", CountryId = Czechia });
        db.PublicHolidays.AddRange(
            new PublicHoliday { Id = 1, CityId = Krakow, Date = KrakowDay, Name = "Krakow day" },
            new PublicHoliday { Id = 2, CityId = Krakow, Date = SharedDay, Name = "National day" },
            new PublicHoliday { Id = 3, CountryId = Poland, Date = IndependenceDay, Name = "Independence Day" },
            new PublicHoliday { Id = 4, CountryId = Poland, Date = SharedDay, Name = "National day" },
            new PublicHoliday { Id = 5, CountryId = Czechia, Date = CzechDay, Name = "Czech day" });
    });

    private Task<Dictionary<int, HashSet<DateTime>>> ResolveAsync(DateTime from, DateTime to) =>
        _db.ReadAsync(db => PublicHolidayResolver.ResolveByMemberAsync(
            db, [(Ann, Krakow), (Ben, Warsaw), (Cat, null), (Eva, Brno)], from, to));

    [Fact]
    public async Task People_get_their_citys_holidays_and_their_countrys()
    {
        await SeedAsync();

        var result = await ResolveAsync(new DateTime(2026, 11, 1), new DateTime(2026, 11, 30));

        Assert.Equal([KrakowDay, IndependenceDay, SharedDay], result[Ann].Order());
        Assert.Equal([IndependenceDay, SharedDay], result[Ben].Order());
        Assert.Equal([CzechDay], result[Eva].Order());
    }

    [Fact]
    public async Task A_person_without_a_city_gets_no_public_holidays()
    {
        await SeedAsync();

        var result = await ResolveAsync(new DateTime(2026, 11, 1), new DateTime(2026, 11, 30));

        Assert.Empty(result[Cat]);
    }

    [Fact]
    public async Task Country_holidays_still_apply_in_a_range_that_holds_a_city_holiday()
    {
        await SeedAsync();

        var result = await ResolveAsync(new DateTime(2026, 11, 2), new DateTime(2026, 11, 13));

        Assert.Equal([KrakowDay, IndependenceDay], result[Ann].Order());
    }

    [Fact]
    public void A_city_holiday_on_the_date_of_its_countrys_holiday_is_left_out_of_the_shown_list()
    {
        var krakow = new City { Id = Krakow, Name = "Krakow", CountryId = Poland };
        var brno = new City { Id = Brno, Name = "Brno", CountryId = Czechia };
        var holidays = new List<PublicHoliday>
        {
            new() { Id = 1, CityId = Krakow, City = krakow, Date = KrakowDay, Name = "Krakow day" },
            new() { Id = 2, CityId = Krakow, City = krakow, Date = SharedDay, Name = "National day" },
            new() { Id = 3, CountryId = Poland, Date = SharedDay, Name = "National day" },
            new() { Id = 4, CityId = Brno, City = brno, Date = SharedDay, Name = "Brno day" },
        };

        var shown = PublicHolidayResolver.WithoutCityCopies(holidays);

        Assert.Equal([1, 3, 4], shown.Select(p => p.Id));
    }
}

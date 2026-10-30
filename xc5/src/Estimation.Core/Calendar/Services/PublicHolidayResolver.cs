using Estimation.Core.Calendar.Models;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.Calendar.Services;

public static class PublicHolidayResolver
{
    public static async Task<Dictionary<int, HashSet<DateTime>>> ResolveByMemberAsync(
        EstimationDbContext db,
        IReadOnlyCollection<(int HrId, int? CityId)> members,
        DateTime from,
        DateTime to) =>
        (await LoadAsync(db, members.Select(m => m.CityId).ToList(), from, to)).ResolveByMember(members, from, to);

    public static async Task<PublicHolidayCalendar> LoadAsync(
        EstimationDbContext db,
        IReadOnlyCollection<int?> cityIds,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        var ids = cityIds
            .OfType<int>()
            .Distinct()
            .ToList();
        if (ids.Count == 0)
        {
            return PublicHolidayCalendar.Empty;
        }

        var fromDate = from.Date;
        var toDate = to.Date;

        var cityCountry = await db.Cities
            .Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.CountryId })
            .AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => c.CountryId, cancellationToken);

        var countryIds = cityCountry.Values.Distinct().ToList();

        var cityHolidays = (await db.PublicHolidays
                .Where(p => p.CityId != null && ids.Contains(p.CityId.Value)
                    && p.Date >= fromDate && p.Date <= toDate)
                .AsNoTracking()
                .ToListAsync(cancellationToken))
            .GroupBy(p => p.CityId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Date).ToList());

        var countryHolidays = countryIds.Count == 0
            ? new Dictionary<int, List<DateTime>>()
            : (await db.PublicHolidays
                    .Where(p => p.CountryId != null && p.CityId == null
                        && countryIds.Contains(p.CountryId.Value)
                        && p.Date >= fromDate && p.Date <= toDate)
                    .AsNoTracking()
                    .ToListAsync(cancellationToken))
                .GroupBy(p => p.CountryId!.Value)
                .ToDictionary(g => g.Key, g => g.Select(p => p.Date).ToList());

        return new PublicHolidayCalendar(cityCountry, cityHolidays, countryHolidays);
    }

    public static List<PublicHoliday> WithoutCityCopies(IEnumerable<PublicHoliday> holidays)
    {
        var list = holidays.ToList();
        var countryDays = list
            .Where(p => p.CityId == null && p.CountryId != null)
            .Select(p => (p.CountryId!.Value, p.Date.Date))
            .ToHashSet();
        return list
            .Where(p => p.CityId == null || p.City is not { } city || !countryDays.Contains((city.CountryId, p.Date.Date)))
            .ToList();
    }
}

public sealed class PublicHolidayCalendar
{
    private readonly Dictionary<int, int> _countryByCity;
    private readonly Dictionary<int, List<DateTime>> _cityHolidays;
    private readonly Dictionary<int, List<DateTime>> _countryHolidays;

    internal PublicHolidayCalendar(
        Dictionary<int, int> countryByCity,
        Dictionary<int, List<DateTime>> cityHolidays,
        Dictionary<int, List<DateTime>> countryHolidays)
    {
        _countryByCity = countryByCity;
        _cityHolidays = cityHolidays;
        _countryHolidays = countryHolidays;
    }

    public static PublicHolidayCalendar Empty { get; } = new(new(), new(), new());

    public Dictionary<int, HashSet<DateTime>> ResolveByMember(
        IReadOnlyCollection<(int HrId, int? CityId)> members, DateTime from, DateTime to)
    {
        var result = members.ToDictionary(m => m.HrId, _ => new HashSet<DateTime>());
        var fromDate = from.Date;
        var toDate = to.Date;
        var datesByCity = new Dictionary<int, HashSet<DateTime>>();

        foreach (var m in members)
        {
            if (m.CityId is not int cityId)
            {
                continue;
            }

            if (!datesByCity.TryGetValue(cityId, out var dates))
            {
                dates = Between(_cityHolidays, cityId, fromDate, toDate);
                if (_countryByCity.TryGetValue(cityId, out var countryId))
                {
                    dates.UnionWith(Between(_countryHolidays, countryId, fromDate, toDate));
                }

                datesByCity[cityId] = dates;
            }

            result[m.HrId] = dates;
        }

        return result;
    }

    private static HashSet<DateTime> Between(Dictionary<int, List<DateTime>> holidays, int key, DateTime from, DateTime to) =>
        holidays.TryGetValue(key, out var dates)
            ? dates.Where(d => d >= from && d <= to).Select(d => d.Date).ToHashSet()
            : new HashSet<DateTime>();
}

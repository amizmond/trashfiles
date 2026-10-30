using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.Calendar.Services;

public interface IIcsExportService
{
    Task<byte[]> BuildTeamCalendarAsync(int teamId);
}

public class IcsExportService : IIcsExportService
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;
    public IcsExportService(IDbContextFactory<EstimationDbContext> ctx) => _ctx = ctx;

    public async Task<byte[]> BuildTeamCalendarAsync(int teamId)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var team = await db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == teamId)
            ?? throw new InvalidOperationException("Team not found.");

        var memberIds = await db.TeamMembers
            .Where(tm => tm.TeamId == teamId)
            .Select(tm => tm.HumanResourceId)
            .ToListAsync();

        var holidays = await db.Holidays
            .Include(h => h.HumanResource)
            .Include(h => h.HolidayType)
            .Where(h => memberIds.Contains(h.HumanResourceId))
            .AsNoTracking()
            .ToListAsync();

        var sprints = await db.Sprints
            .Include(s => s.Pi)
            .Where(s => s.TeamId == teamId)
            .AsNoTracking()
            .ToListAsync();

        var cityIds = await db.HumanResources
            .Where(hr => memberIds.Contains(hr.Id) && hr.CityId.HasValue)
            .Select(hr => hr.CityId!.Value)
            .Distinct()
            .ToListAsync();

        var countryIds = cityIds.Count == 0
            ? new List<int>()
            : await db.Cities
                .Where(c => cityIds.Contains(c.Id))
                .Select(c => c.CountryId)
                .Distinct()
                .ToListAsync();

        var publicHolidays = (cityIds.Count == 0 && countryIds.Count == 0)
            ? new List<Models.PublicHoliday>()
            : await db.PublicHolidays
                .Include(p => p.Country)
                .Include(p => p.City).ThenInclude(c => c!.Country)
                .Where(p => (p.CityId != null && cityIds.Contains(p.CityId.Value))
                    || (p.CountryId != null && p.CityId == null && countryIds.Contains(p.CountryId.Value)))
                .AsNoTracking()
                .ToListAsync();

        var sb = new StringBuilder();
        sb.Append("BEGIN:VCALENDAR\r\n");
        sb.Append("VERSION:2.0\r\n");
        sb.Append($"PRODID:-//Estimation//Team {EscapeText(team.Name)}//EN\r\n");
        sb.Append("CALSCALE:GREGORIAN\r\n");
        sb.Append("METHOD:PUBLISH\r\n");
        sb.Append($"X-WR-CALNAME:{EscapeText(team.Name)} - Holidays & Sprints\r\n");

        var now = DateTime.UtcNow;

        foreach (var h in holidays)
        {
            sb.Append("BEGIN:VEVENT\r\n");
            sb.Append($"UID:holiday-{h.Id}@estimation\r\n");
            sb.Append($"DTSTAMP:{now:yyyyMMddTHHmmssZ}\r\n");
            sb.Append($"DTSTART;VALUE=DATE:{h.StartDate:yyyyMMdd}\r\n");
            sb.Append($"DTEND;VALUE=DATE:{h.EndDate.AddDays(1):yyyyMMdd}\r\n");
            var typeName = h.HolidayType?.Name ?? "Leave";
            var who = h.HumanResource?.FullName ?? "Member";
            sb.Append($"SUMMARY:{EscapeText($"{who} - {typeName}")}\r\n");
            if (!string.IsNullOrWhiteSpace(h.Comment))
            {
                sb.Append($"DESCRIPTION:{EscapeText(h.Comment)}\r\n");
            }
            sb.Append("END:VEVENT\r\n");
        }

        foreach (var s in sprints)
        {
            sb.Append("BEGIN:VEVENT\r\n");
            sb.Append($"UID:sprint-{s.Id}@estimation\r\n");
            sb.Append($"DTSTAMP:{now:yyyyMMddTHHmmssZ}\r\n");
            sb.Append($"DTSTART;VALUE=DATE:{s.StartDate:yyyyMMdd}\r\n");
            sb.Append($"DTEND;VALUE=DATE:{s.EndDate.AddDays(1):yyyyMMdd}\r\n");
            var piPart = s.Pi is null ? "" : $" ({s.Pi.Name})";
            sb.Append($"SUMMARY:{EscapeText($"Sprint: {s.Name}{piPart}")}\r\n");
            if (!string.IsNullOrWhiteSpace(s.Comment))
            {
                sb.Append($"DESCRIPTION:{EscapeText(s.Comment)}\r\n");
            }
            sb.Append("END:VEVENT\r\n");
        }

        foreach (var p in PublicHolidayResolver.WithoutCityCopies(publicHolidays))
        {
            sb.Append("BEGIN:VEVENT\r\n");
            sb.Append($"UID:publicholiday-{p.Id}@estimation\r\n");
            sb.Append($"DTSTAMP:{now:yyyyMMddTHHmmssZ}\r\n");
            sb.Append($"DTSTART;VALUE=DATE:{p.Date:yyyyMMdd}\r\n");
            sb.Append($"DTEND;VALUE=DATE:{p.Date.AddDays(1):yyyyMMdd}\r\n");
            var scope = p.City?.Name ?? p.Country?.Name ?? "";
            sb.Append($"SUMMARY:{EscapeText($"{p.Name}{(string.IsNullOrEmpty(scope) ? "" : $" ({scope})")}")}\r\n");
            sb.Append("END:VEVENT\r\n");
        }

        sb.Append("END:VCALENDAR\r\n");

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string EscapeText(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }
        return input
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(";", "\\;", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal)
            .Replace("\r\n", "\\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }
}

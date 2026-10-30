using Estimation.ArtPlanning.Data;
using Estimation.ArtPlanning.Models;
using Estimation.ArtPlanning.Models.Source;
using Microsoft.EntityFrameworkCore;

namespace Estimation.ArtPlanning.Services;

public interface IArtPlanningCapacityService
{
    Task<ArtPlanningCapacityResult> CalculateAsync(int capitalProjectId, int piId, CancellationToken ct = default);
}

public sealed class ArtPlanningCapacityService : IArtPlanningCapacityService
{
    private const int PiTailDaysExcluded = 21;

    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

    private readonly IDbContextFactory<ArtPlanningDbContext> _ctx;
    private readonly IArtPlanningSettingsService _settings;

    public ArtPlanningCapacityService(
        IDbContextFactory<ArtPlanningDbContext> ctx, IArtPlanningSettingsService settings)
    {
        _ctx = ctx;
        _settings = settings;
    }

    public async Task<ArtPlanningCapacityResult> CalculateAsync(
        int capitalProjectId, int piId, CancellationToken ct = default)
    {
        await using var db = await _ctx.CreateDbContextAsync(ct);

        var art = await db.CapitalProjects.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == capitalProjectId, ct);
        var pi = await db.Pis.AsNoTracking().FirstOrDefaultAsync(p => p.Id == piId, ct);
        var settings = await _settings.GetAsync(capitalProjectId, piId, ct);

        var artName = art?.Name ?? $"ART {capitalProjectId}";
        var piName = pi?.Name ?? $"PI {piId}";

        var workDates = BuildWorkDates(pi, out var windowStart, out var windowEnd);
        if (workDates.Count == 0)
        {
            return ArtPlanningCapacityResult.Empty(
                capitalProjectId, artName, piId, piName, settings.TrainCoefficient,
                settings.UsedDefaults, settings.IsEmpty,
                new ArtPlanningDiagnostics(windowStart, windowEnd, 0, 0, 0, 0, 0));
        }

        var trainRows = await BuildTrainEmployeeAsync(db, capitalProjectId, ct);
        if (trainRows.Count == 0)
        {
            return ArtPlanningCapacityResult.Empty(
                capitalProjectId, artName, piId, piName, settings.TrainCoefficient,
                settings.UsedDefaults, settings.IsEmpty,
                new ArtPlanningDiagnostics(windowStart, windowEnd, workDates.Count, 0, 0, 0, 0));
        }

        var leaveDays = await LoadLeaveDaysAsync(db, trainRows, windowStart!.Value, windowEnd!.Value, ct);
        var publicHolidays = await LoadPublicHolidaysAsync(db, trainRows, windowStart.Value, windowEnd.Value, ct);

        var aggregated = BuildDetailsAggr(trainRows, workDates, leaveDays, publicHolidays);

        var bestSkill = await BuildBestSkillAsync(db, settings, ct);

        var rows = Project(aggregated, bestSkill, settings);

        var diagnostics = new ArtPlanningDiagnostics(
            windowStart,
            windowEnd,
            workDates.Count,
            trainRows.Select(r => r.TeamId).Distinct().Count(),
            trainRows.Count(r => r.EmployeeId is not null),
            trainRows.Count(r => r.EmployeeId is null),
            bestSkill.Rows.Where(r => r.Ranking is null).Select(r => r.SkillId).Distinct().Count());

        return new ArtPlanningCapacityResult(
            capitalProjectId, artName, piId, piName, settings.TrainCoefficient,
            settings.UsedDefaults, settings.IsEmpty, rows, diagnostics);
    }

    private static List<DateTime> BuildWorkDates(SourcePi? pi, out DateTime? windowStart, out DateTime? windowEnd)
    {
        windowStart = null;
        windowEnd = null;

        if (pi?.StartDate is null || pi.EndDate is null)
        {
            return new List<DateTime>();
        }

        var start = pi.StartDate.Value.Date;
        var end = pi.EndDate.Value.Date.AddDays(-PiTailDaysExcluded);

        if (end < start)
        {
            return new List<DateTime>();
        }

        windowStart = start;
        windowEnd = end;

        var dates = new List<DateTime>();
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday)
            {
                dates.Add(d);
            }
        }
        return dates;
    }

    private sealed class TrainEmployeeRow
    {
        public required int TeamId { get; init; }
        public required string TeamName { get; init; }
        public int? EmployeeId { get; init; }
        public string? EmployeeNumber { get; init; }
        public string? EmployeeName { get; init; }
        public int? CityId { get; init; }
    }

    private static async Task<List<TrainEmployeeRow>> BuildTrainEmployeeAsync(
        ArtPlanningDbContext db, int capitalProjectId, CancellationToken ct)
    {
        var teams = await db.CapitalProjectTeams.AsNoTracking()
            .Where(cpt => cpt.CapitalProjectId == capitalProjectId)
            .Join(db.Teams.AsNoTracking(), cpt => cpt.TeamId, t => t.Id, (_, t) => t)
            .OrderBy(t => t.Id)
            .ToListAsync(ct);

        if (teams.Count == 0)
        {
            return new List<TrainEmployeeRow>();
        }

        var teamIds = teams.Select(t => t.Id).ToList();
        var members = await db.TeamMembers.AsNoTracking()
            .Where(tm => teamIds.Contains(tm.TeamId))
            .ToListAsync(ct);

        var hrIds = members.Select(m => m.HumanResourceId).Distinct().ToList();
        var humanResources = await db.HumanResources.AsNoTracking()
            .Where(h => hrIds.Contains(h.Id))
            .ToDictionaryAsync(h => h.Id, ct);

        var rows = new List<TrainEmployeeRow>();
        foreach (var team in teams)
        {
            var teamMembers = members.Where(m => m.TeamId == team.Id).OrderBy(m => m.HumanResourceId).ToList();

            if (teamMembers.Count == 0)
            {
                rows.Add(new TrainEmployeeRow { TeamId = team.Id, TeamName = team.Name });
                continue;
            }

            foreach (var member in teamMembers)
            {
                humanResources.TryGetValue(member.HumanResourceId, out var hr);

                if (hr is null || !hr.IsActive)
                {
                    rows.Add(new TrainEmployeeRow { TeamId = team.Id, TeamName = team.Name });
                    continue;
                }

                rows.Add(new TrainEmployeeRow
                {
                    TeamId = team.Id,
                    TeamName = team.Name,
                    EmployeeId = hr.Id,
                    EmployeeNumber = hr.EmployeeNumber,
                    EmployeeName = hr.EmployeeName,
                    CityId = hr.CityId,
                });
            }
        }
        return rows;
    }

    private static async Task<Dictionary<(int EmployeeId, DateTime Day), decimal>> LoadLeaveDaysAsync(
        ArtPlanningDbContext db, List<TrainEmployeeRow> trainRows, DateTime from, DateTime to, CancellationToken ct)
    {
        var employeeIds = trainRows.Where(r => r.EmployeeId is not null)
            .Select(r => r.EmployeeId!.Value).Distinct().ToList();
        var result = new Dictionary<(int, DateTime), decimal>();
        if (employeeIds.Count == 0)
        {
            return result;
        }

        var holidays = await db.Holidays.AsNoTracking()
            .Where(h => employeeIds.Contains(h.HumanResourceId) && h.StartDate <= to && h.EndDate >= from)
            .ToListAsync(ct);

        foreach (var h in holidays)
        {
            var start = h.StartDate.Date < from ? from : h.StartDate.Date;
            var end = h.EndDate.Date > to ? to : h.EndDate.Date;
            var availableOnLeaveDay = h.HalfDay == 0 ? 0m : 0.5m;
            for (var d = start; d <= end; d = d.AddDays(1))
            {
                var key = (h.HumanResourceId, d);
                result[key] = result.TryGetValue(key, out var existing)
                    ? Math.Min(existing, availableOnLeaveDay)
                    : availableOnLeaveDay;
            }
        }
        return result;
    }

    private static async Task<Dictionary<int, HashSet<DateTime>>> LoadPublicHolidaysAsync(
        ArtPlanningDbContext db, List<TrainEmployeeRow> trainRows, DateTime from, DateTime to, CancellationToken ct)
    {
        var cityIds = trainRows.Where(r => r.CityId is not null)
            .Select(r => r.CityId!.Value).Distinct().ToList();
        var result = new Dictionary<int, HashSet<DateTime>>();
        if (cityIds.Count == 0)
        {
            return result;
        }

        var countryByCity = await db.Cities.AsNoTracking()
            .Where(c => cityIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.CountryId, ct);
        var countryIds = countryByCity.Values.Distinct().ToList();

        var rows = await db.PublicHolidays.AsNoTracking()
            .Where(p => p.Date >= from && p.Date <= to
                && ((p.CityId != null && cityIds.Contains(p.CityId.Value))
                    || (p.CityId == null && p.CountryId != null && countryIds.Contains(p.CountryId.Value))))
            .ToListAsync(ct);

        foreach (var cityId in cityIds)
        {
            int? countryId = countryByCity.TryGetValue(cityId, out var id) ? id : null;
            result[cityId] = rows
                .Where(p => p.CityId == cityId || (p.CityId == null && countryId is not null && p.CountryId == countryId))
                .Select(p => p.Date.Date)
                .ToHashSet();
        }
        return result;
    }

    private sealed record AggregatedEmployee(string? EmployeeNumber, string? EmployeeName, decimal EmployeeDays);

    private static List<AggregatedEmployee> BuildDetailsAggr(
        List<TrainEmployeeRow> trainRows,
        List<DateTime> workDates,
        Dictionary<(int EmployeeId, DateTime Day), decimal> leaveDays,
        Dictionary<int, HashSet<DateTime>> publicHolidays)
    {
        var perRow = new List<(TrainEmployeeRow Row, decimal Days)>(trainRows.Count);
        foreach (var row in trainRows)
        {
            var cityHolidays = row.CityId is int city && publicHolidays.TryGetValue(city, out var set)
                ? set
                : null;

            var days = 0m;
            foreach (var day in workDates)
            {
                if (cityHolidays is not null && cityHolidays.Contains(day))
                {
                    continue;
                }

                days += row.EmployeeId is int employeeId
                        && leaveDays.TryGetValue((employeeId, day), out var available)
                    ? available
                    : 1m;
            }
            perRow.Add((row, days));
        }

        return perRow
            .GroupBy(x => (x.Row.EmployeeName, x.Row.EmployeeNumber), EmployeeKeyComparer.Instance)
            .Select(g =>
            {
                var sum = g.Sum(x => x.Days);

                var teamCount = g.Select(x => x.Row.TeamId).Distinct().Count();
                var employeeDays = teamCount == 0 ? 0m : sum / teamCount;

                return new AggregatedEmployee(g.Key.EmployeeNumber, g.Key.EmployeeName, employeeDays);
            })
            .ToList();
    }

    private sealed class EmployeeKeyComparer : IEqualityComparer<(string? EmployeeName, string? EmployeeNumber)>
    {
        public static readonly EmployeeKeyComparer Instance = new();

        public bool Equals(
            (string? EmployeeName, string? EmployeeNumber) x, (string? EmployeeName, string? EmployeeNumber) y) =>
            NameComparer.Equals(x.EmployeeName, y.EmployeeName)
            && NameComparer.Equals(x.EmployeeNumber, y.EmployeeNumber);

        public int GetHashCode((string? EmployeeName, string? EmployeeNumber) obj) =>
            HashCode.Combine(
                obj.EmployeeName is null ? 0 : NameComparer.GetHashCode(obj.EmployeeName),
                obj.EmployeeNumber is null ? 0 : NameComparer.GetHashCode(obj.EmployeeNumber));
    }

    private sealed class BestSkillRow
    {
        public required string? EmployeeNumber { get; init; }
        public required int SkillId { get; init; }
        public required string SkillName { get; init; }
        public required int SkillLevelValue { get; init; }
        public required string? SkillLevelName { get; init; }
        public required string? TeamRoleName { get; init; }
        public required int? Ranking { get; init; }
        public required int UsageOrder { get; init; }
        public int RowNumber { get; set; }
    }

    private sealed record BestSkillSet(List<BestSkillRow> Rows);

    private static async Task<BestSkillSet> BuildBestSkillAsync(
        ArtPlanningDbContext db, ArtPlanningSettings settings, CancellationToken ct)
    {
        var links = await db.CapitalProjectTeams.AsNoTracking().OrderBy(x => x.CapitalProjectId).ToListAsync(ct);
        if (links.Count == 0)
        {
            return new BestSkillSet(new List<BestSkillRow>());
        }

        var teamIds = links.Select(l => l.TeamId).Distinct().ToList();
        var members = await db.TeamMembers.AsNoTracking()
            .Where(tm => teamIds.Contains(tm.TeamId))
            .ToListAsync(ct);

        var hrIds = members.Select(m => m.HumanResourceId).Distinct().ToList();
        var humanResources = await db.HumanResources.AsNoTracking()
            .Where(h => hrIds.Contains(h.Id))
            .ToDictionaryAsync(h => h.Id, ct);

        var hrSkills = await db.HumanResourceSkills.AsNoTracking()
            .Where(s => hrIds.Contains(s.HumanResourceId))
            .ToListAsync(ct);
        var skillsByHr = hrSkills.GroupBy(s => s.HumanResourceId)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.SkillId).ToList());

        var skills = await db.Skills.AsNoTracking().ToDictionaryAsync(s => s.Id, ct);
        var levels = await db.SkillLevels.AsNoTracking().ToDictionaryAsync(l => l.Id, ct);
        var roles = await db.TeamRoles.AsNoTracking().ToDictionaryAsync(r => r.Id, ct);

        var membersByTeam = members.GroupBy(m => m.TeamId)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.HumanResourceId).ToList());

        var candidates = new List<BestSkillRow>();
        foreach (var link in links)
        {
            if (!membersByTeam.TryGetValue(link.TeamId, out var teamMembers))
            {
                continue;
            }

            foreach (var member in teamMembers)
            {
                if (!humanResources.TryGetValue(member.HumanResourceId, out var hr)
                    || !skillsByHr.TryGetValue(hr.Id, out var held))
                {
                    continue;
                }

                var roleName = member.TeamRoleId is int roleId && roles.TryGetValue(roleId, out var role)
                    ? role.Name
                    : null;

                foreach (var hrs in held)
                {
                    if (hrs.SkillLevelId is not int levelId
                        || !levels.TryGetValue(levelId, out var level)
                        || level.Value is not int levelValue
                        || !settings.UsageOrderByLevelValue.TryGetValue(levelValue, out var usageOrder))
                    {
                        continue;
                    }

                    if (!skills.TryGetValue(hrs.SkillId, out var skill))
                    {
                        continue;
                    }

                    candidates.Add(new BestSkillRow
                    {
                        EmployeeNumber = hr.EmployeeNumber,
                        SkillId = skill.Id,
                        SkillName = skill.Name,
                        SkillLevelValue = levelValue,
                        SkillLevelName = level.Name,
                        TeamRoleName = roleName,
                        Ranking = settings.RankingBySkillId.TryGetValue(skill.Id, out var r) ? r : null,
                        UsageOrder = usageOrder,
                    });
                }
            }
        }

        foreach (var partition in candidates.GroupBy(c => c.EmployeeNumber, NameComparer))
        {
            var ordered = partition
                .OrderBy(c => c.UsageOrder)
                .ThenBy(c => c.Ranking is null ? 0 : 1)
                .ThenBy(c => c.Ranking ?? 0)
                .ThenBy(c => c.SkillId)
                .ThenBy(c => c.TeamRoleName ?? string.Empty, NameComparer)
                .ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                ordered[i].RowNumber = i + 1;
            }
        }

        return new BestSkillSet(candidates);
    }

    private static List<ArtPlanningSkillRow> Project(
        List<AggregatedEmployee> aggregated, BestSkillSet bestSkill, ArtPlanningSettings settings)
    {
        var byEmployeeNumber = bestSkill.Rows
            .Where(r => r.EmployeeNumber is not null)
            .GroupBy(r => r.EmployeeNumber!, NameComparer)
            .ToDictionary(g => g.Key, g => g.ToList(), NameComparer);

        var buckets = new Dictionary<string, SkillBucket>(NameComparer);
        var unmatched = new SkillBucket(null, null);

        foreach (var employee in aggregated)
        {
            BestSkillRow? match = null;
            if (employee.EmployeeNumber is not null
                && byEmployeeNumber.TryGetValue(employee.EmployeeNumber, out var forEmployee))
            {
                match = forEmployee
                    .Where(r => Matches(r, settings))
                    .OrderBy(r => IsRoleLimited(r, settings) ? 0 : 1)
                    .ThenBy(r => r.RowNumber)
                    .FirstOrDefault();
            }

            if (match is null)
            {
                unmatched.Add(new ArtPlanningMemberRow(
                    employee.EmployeeNumber, employee.EmployeeName, employee.EmployeeDays,
                    null, null, null, 0m, 0m, 0m));
                continue;
            }

            var ratio = settings.RatioByLevelValue.GetValueOrDefault(match.SkillLevelValue, 0m);
            var roleCoefficient = match.TeamRoleName is not null
                ? settings.CoefficientByRoleName.GetValueOrDefault(match.TeamRoleName, 1m)
                : 1m;
            var capacity = ratio * roleCoefficient * employee.EmployeeDays;

            if (!buckets.TryGetValue(match.SkillName, out var bucket))
            {
                bucket = new SkillBucket(match.SkillId, match.SkillName);
                buckets[match.SkillName] = bucket;
            }

            bucket.Add(new ArtPlanningMemberRow(
                employee.EmployeeNumber, employee.EmployeeName, employee.EmployeeDays,
                match.SkillLevelValue, match.SkillLevelName, match.TeamRoleName,
                ratio, roleCoefficient, capacity));
        }

        var rows = new List<ArtPlanningSkillRow>();

        if (unmatched.Members.Count > 0)
        {
            rows.Add(unmatched.ToRow(settings.TrainCoefficient));
        }
        rows.AddRange(buckets.Values
            .OrderBy(b => b.SkillName, NameComparer)
            .Select(b => b.ToRow(settings.TrainCoefficient)));

        return rows;
    }

    private static bool IsRoleLimited(BestSkillRow row, ArtPlanningSettings settings) =>
        row.TeamRoleName is not null
        && settings.AllowedSkillIdsByRoleName.ContainsKey(row.TeamRoleName);

    private static bool Matches(BestSkillRow row, ArtPlanningSettings settings)
    {
        if (row.TeamRoleName is null)
        {
            return false;
        }

        if (settings.AllowedSkillIdsByRoleName.TryGetValue(row.TeamRoleName, out var allowed))
        {
            return allowed.Contains(row.SkillId);
        }

        return row.RowNumber == 1;
    }

    private sealed class SkillBucket(int? skillId, string? skillName)
    {
        public string? SkillName { get; } = skillName;
        public List<ArtPlanningMemberRow> Members { get; } = new();

        public void Add(ArtPlanningMemberRow member) => Members.Add(member);

        public ArtPlanningSkillRow ToRow(decimal trainCoefficient)
        {
            var workingDays = Members.Sum(m => m.WorkingDays);

            var capacity = Members.Sum(m => m.Capacity) * trainCoefficient;

            return new ArtPlanningSkillRow(
                skillId,
                SkillName,
                workingDays,
                capacity,
                Members
                    .OrderByDescending(m => m.Capacity)
                    .ThenBy(m => m.EmployeeName, NameComparer)
                    .ToList());
        }
    }
}

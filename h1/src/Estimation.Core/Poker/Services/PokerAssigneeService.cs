using Estimation.Core.Resources.Services;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.Poker.Services;

public record PokerAssigneeOption(string DisplayName, string SamAccountName, string WindowsUserName, bool IsTeamMember)
{
    public override string ToString()
    {
        return DisplayName;
    }
}

public interface IPokerAssigneeService
{
    Task<List<PokerAssigneeOption>> GetAssignableUsersAsync(int? teamId);
}

public class PokerAssigneeService : IPokerAssigneeService
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;

    public PokerAssigneeService(IDbContextFactory<EstimationDbContext> ctx)
    {
        _ctx = ctx;
    }

    public async Task<List<PokerAssigneeOption>> GetAssignableUsersAsync(int? teamId)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var users = await db.AppUsers
            .AsNoTracking()
            .Where(u => u.IsApproved && u.SamAccountName != null && u.SamAccountName != "")
            .Select(u => new { u.DisplayName, u.WindowsUserName, u.SamAccountName, u.EmployeeId })
            .ToListAsync();

        var teamEmployeeNumbers = new List<string>();
        if (teamId.HasValue)
        {
            teamEmployeeNumbers = await db.HumanResources
                .AsNoTracking()
                .Where(hr => hr.EmployeeNumber != null
                             && hr.TeamMembers.Any(tm => tm.TeamId == teamId.Value))
                .Select(hr => hr.EmployeeNumber!)
                .ToListAsync();
        }

        return users
            .Select(u =>
            {
                var key = EmployeeNumberHelper.ResolveUserKey(u.EmployeeId, u.SamAccountName);
                var isTeamMember = teamEmployeeNumbers.Any(n => EmployeeNumberHelper.Matches(n, key));
                var displayName = string.IsNullOrWhiteSpace(u.DisplayName) ? u.WindowsUserName : u.DisplayName!;
                return new PokerAssigneeOption(displayName, u.SamAccountName!, u.WindowsUserName, isTeamMember);
            })
            .OrderByDescending(o => o.IsTeamMember)
            .ThenBy(o => o.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

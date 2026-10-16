using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.Capacity.Services;

public record ArtCapacityTeamRow(
    int TeamId,
    string TeamName,
    string WindowMode,
    DateTime? WindowStart,
    DateTime? WindowEnd,
    int MemberCount,
    decimal StoryPointsPerDay,
    double Capacity,
    decimal Allocated);

public record ArtCapacityStackDemandRow(
    int TechnologyStackId,
    string StackName,
    decimal Demand,
    decimal Allocated,
    decimal Remaining,
    decimal BelowLine);

public record ArtCapacityResult(
    double TotalCapacity,
    int MemberCount,
    List<ArtCapacityTeamRow> Teams,
    List<ArtCapacityStackDemandRow> StackDemands);

public record ArtCapacityTeamBoard(int TeamId, string TeamName, TeamCapacityResult Board);

public interface IArtCapacityService
{
    Task<ArtCapacityResult> GetAsync(int? capitalProjectId, int piId, bool includeIpSprints = false);
    Task<List<ArtCapacityTeamBoard>> GetTeamBoardsAsync(int? capitalProjectId, int piId, bool includeIpSprints = false);
}

public class ArtCapacityService : IArtCapacityService
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;
    private readonly ITeamCapacityService _teamCapacity;

    public ArtCapacityService(IDbContextFactory<EstimationDbContext> ctx, ITeamCapacityService teamCapacity)
    {
        _ctx = ctx;
        _teamCapacity = teamCapacity;
    }

    public async Task<ArtCapacityResult> GetAsync(int? capitalProjectId, int piId, bool includeIpSprints = false)
    {
        var boards = await GetTeamBoardsAsync(capitalProjectId, piId, includeIpSprints);
        if (boards.Count == 0)
        {
            return new ArtCapacityResult(0d, 0, new(), new());
        }

        var teamRows = new List<ArtCapacityTeamRow>();
        var poolByMember = new Dictionary<int, decimal>();
        var stacks = new StackDemandAccumulator();
        foreach (var (teamId, teamName, board) in boards)
        {
            var allocations = TeamBoardAllocator.Allocate(
                board.Features.Where(f => f.IsIncludedInCapacity).Select(f => f.FeatureId),
                board.Demands,
                board.MemberMeta);
            teamRows.Add(new ArtCapacityTeamRow(
                teamId,
                teamName,
                board.WindowMode,
                board.WindowStart,
                board.WindowEnd,
                board.Members.Count,
                board.StoryPointsPerDay,
                board.MemberMeta.Sum(m => m.EffectiveDays),
                allocations.Values.Sum(a => a.Reserved) / board.StoryPointsPerDay));
            AbsorbMembers(poolByMember, board.MemberMeta);
            stacks.Absorb(allocations.Values, board);
        }

        return new ArtCapacityResult(
            poolByMember.Values.Sum(pool => (double)pool), poolByMember.Count, teamRows, stacks.ToRows());
    }

    public async Task<List<ArtCapacityTeamBoard>> GetTeamBoardsAsync(int? capitalProjectId, int piId, bool includeIpSprints = false)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var teamsQuery = capitalProjectId is int artId
            ? db.Teams.Where(t => t.CapitalProjectTeams.Any(cpt => cpt.CapitalProjectId == artId))
            : db.Teams.Where(t => !t.CapitalProjectTeams.Any());
        var teams = await teamsQuery
            .OrderBy(t => t.Name)
            .Select(t => new { t.Id, t.Name })
            .AsNoTracking()
            .ToListAsync();

        var boards = new List<ArtCapacityTeamBoard>();
        foreach (var team in teams)
        {
            boards.Add(new ArtCapacityTeamBoard(team.Id, team.Name, await _teamCapacity.GetAsync(team.Id, piId, includeIpSprints)));
        }

        return boards;
    }

    internal static void AbsorbMembers(Dictionary<int, decimal> poolByMember, List<TeamMemberMeta> meta)
    {
        foreach (var m in meta)
        {
            var pool = (decimal)m.EffectiveDays;
            poolByMember[m.HumanResourceId] = poolByMember.TryGetValue(m.HumanResourceId, out var existing)
                ? Math.Max(existing, pool)
                : pool;
        }
    }

    /// <summary>
    /// Rolls every ART team's per-stack demand into one set of rows — the ART-wide equivalent of the
    /// team board's "Demand capacity" table.
    /// </summary>
    private sealed class StackDemandAccumulator
    {
        private readonly Dictionary<int, string> _names = new();
        private readonly Dictionary<int, decimal> _demand = new();
        private readonly Dictionary<int, decimal> _allocated = new();
        private readonly Dictionary<int, decimal> _belowLine = new();

        public void Absorb(IEnumerable<TeamBoardFeatureAllocation> allocations, TeamCapacityResult board)
        {
            var perDay = board.StoryPointsPerDay;
            foreach (var s in allocations.SelectMany(a => a.Stacks))
            {
                _names[s.TechnologyStackId] = s.StackName;
                _demand[s.TechnologyStackId] = _demand.GetValueOrDefault(s.TechnologyStackId) + s.Required / perDay;
                _allocated[s.TechnologyStackId] = _allocated.GetValueOrDefault(s.TechnologyStackId) + s.Reserved / perDay;
            }

            foreach (var f in board.Features.Where(x => !x.IsIncludedInCapacity))
            {
                var demand = board.Demands.GetValueOrDefault(f.FeatureId);
                if (demand is null)
                {
                    continue;
                }
                foreach (var s in demand.Stacks)
                {
                    var sp = (decimal)(s.StoryPoints ?? 0);
                    if (sp <= 0m)
                    {
                        continue;
                    }
                    _names.TryAdd(s.TechnologyStackId, s.StackName);
                    _belowLine[s.TechnologyStackId] = _belowLine.GetValueOrDefault(s.TechnologyStackId) + sp / perDay;
                }
            }
        }

        public List<ArtCapacityStackDemandRow> ToRows() =>
            _names.OrderBy(kv => kv.Value)
                .Select(kv =>
                {
                    var demand = _demand.GetValueOrDefault(kv.Key);
                    var allocated = _allocated.GetValueOrDefault(kv.Key);
                    return new ArtCapacityStackDemandRow(
                        kv.Key, kv.Value, demand, allocated, demand - allocated,
                        _belowLine.GetValueOrDefault(kv.Key));
                })
                .ToList();
    }
}

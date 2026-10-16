namespace Estimation.Core.Capacity.Services;

public record TeamBoardMemberTake(int HumanResourceId, string FullName, decimal StoryPoints);

public record TeamBoardStackAllocation(
    int TechnologyStackId,
    string StackName,
    decimal Required,
    decimal Reserved,
    List<TeamBoardMemberTake> Members);

public record TeamBoardFeatureAllocation(
    int FeatureId,
    bool IsValid,
    decimal Required,
    decimal Reserved,
    List<TeamBoardStackAllocation> Stacks)
{
    public bool IsFullyReserved => IsValid && Reserved >= Required;
}

public static class TeamBoardAllocator
{
    public static Dictionary<int, TeamBoardFeatureAllocation> Allocate(
        IEnumerable<int> includedFeatureIdsInBoardOrder,
        IReadOnlyDictionary<int, TeamFeatureDemand> demands,
        IReadOnlyList<TeamMemberMeta> members)
    {
        var free = members.ToDictionary(m => m.HumanResourceId, m => (decimal)m.Capacity);
        var result = new Dictionary<int, TeamBoardFeatureAllocation>();
        foreach (var featureId in includedFeatureIdsInBoardOrder)
        {
            result[featureId] = AllocateFeature(featureId, demands.GetValueOrDefault(featureId), members, free);
        }
        return result;
    }

    private static TeamBoardFeatureAllocation AllocateFeature(
        int featureId,
        TeamFeatureDemand? demand,
        IReadOnlyList<TeamMemberMeta> members,
        Dictionary<int, decimal> free)
    {
        var stacks = demand?.Stacks.Where(s => (s.StoryPoints ?? 0) > 0).ToList()
            ?? new List<TeamFeatureStackDemand>();
        if (stacks.Count == 0)
        {
            return new TeamBoardFeatureAllocation(featureId, false, 0m, 0m, new List<TeamBoardStackAllocation>());
        }

        var allocated = new List<TeamBoardStackAllocation>();
        foreach (var stack in stacks)
        {
            var required = (decimal)(stack.StoryPoints ?? 0);
            var takes = new List<TeamBoardMemberTake>();
            var stackMembers = members
                .Where(m => m.StackIds.Contains(stack.TechnologyStackId))
                .OrderByDescending(m => free.GetValueOrDefault(m.HumanResourceId, 0m));

            var remaining = required;
            foreach (var m in stackMembers)
            {
                if (remaining <= 0m)
                {
                    break;
                }
                var avail = free.GetValueOrDefault(m.HumanResourceId, 0m);
                if (avail <= 0m)
                {
                    continue;
                }
                var take = Math.Min(remaining, avail);
                free[m.HumanResourceId] = avail - take;
                remaining -= take;
                takes.Add(new TeamBoardMemberTake(m.HumanResourceId, m.FullName, take));
            }

            allocated.Add(new TeamBoardStackAllocation(
                stack.TechnologyStackId, stack.StackName, required, required - remaining, takes));
        }

        return new TeamBoardFeatureAllocation(
            featureId,
            true,
            allocated.Sum(s => s.Required),
            allocated.Sum(s => s.Reserved),
            allocated);
    }
}

using Estimation.Core.Capacity.Services;
using Estimation.Core.Features.Models;
using Estimation.Core.Features.Services;
using Estimation.Core.JiraIntegration.Models;
using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Shared.Services;
using Estimation.Core.Train.Services;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.Features.DependencyMap.Services;

public enum DependencyEndKind
{
    InScope = 0,
    OtherPi = 1,
    OtherArt = 2
}

public enum DependencyStubKind
{
    BusinessOutcome = 0,
    PortfolioEpic = 1,
    StrategicObjective = 2,
    NotInTool = 3
}

public sealed record DependencyArtSprint(int Id, string Name, DateTime Start, DateTime End, bool IsIp);

public sealed record DependencyTeam(int TeamId, string Name, bool InArt, bool IsShared, IReadOnlyList<string> ArtNames);

public sealed record DependencyPlannedSprint(
    int SprintId,
    string Name,
    DateTime Start,
    DateTime End,
    int? SourceArtSprintId);

public sealed record DependencyFeatureTeam(
    int TeamId,
    bool IsPrimary,
    bool BelowLine,
    int? SortOrder,
    IReadOnlyList<DependencyPlannedSprint> Sprints);

public sealed record DependencyFeature(
    int FeatureId,
    string? JiraId,
    string? Name,
    string Summary,
    string? Status,
    string? RagStatus,
    int? Ranking,
    DependencyEndKind Kind,
    int? ArtId,
    string? ArtName,
    TrainScope Scope,
    int? PiId,
    string? PiName,
    DateTime? PiStart,
    bool InSelectedPi,
    IReadOnlyList<DependencyFeatureTeam> Teams) : IFeatureFilterRow
{
    public string? FreeTextDependencies { get; init; }

    public bool ExternalDependencies { get; init; }

    public string? Labels { get; init; }

    public string? BusinessOutcomeJiraId { get; init; }

    public string? BusinessOutcomeSummary { get; init; }

    public string? PortfolioEpicJiraId { get; init; }

    public string? PortfolioEpicSummary { get; init; }

    public IReadOnlyList<FeatureFilterRef> StrategicObjectives { get; init; } = [];

    public string? RequirementStatus { get; init; }

    public string? TechnicalApproval { get; init; }

    public string? FundingStatus { get; init; }

    public string Title => string.IsNullOrWhiteSpace(Name) ? Summary : Name;
}

public sealed record DependencyStub(string Key, DependencyStubKind Kind, string? Summary, string? Status);

public sealed record DependencyLinkRow(
    string JiraLinkId,
    string TypeName,
    string Outward,
    string Inward,
    string FromKey,
    string ToKey);

public sealed record DependencyMapData(
    int ArtId,
    string ArtName,
    int PiId,
    string PiName,
    DateTime? PiStart,
    IReadOnlyList<DependencyArtSprint> ArtSprints,
    IReadOnlyList<DependencyTeam> Teams,
    IReadOnlyList<DependencyFeature> Features,
    IReadOnlyList<DependencyStub> Stubs,
    IReadOnlyList<DependencyLinkRow> Links,
    int FeaturesWithoutKey)
{
    public bool HasArtSprints => ArtSprints.Count > 0;
}

public interface IDependencyMapService
{
    Task<DependencyMapData?> GetAsync(
        int capitalProjectId,
        int piId,
        bool includePiLabelMatches = true,
        CancellationToken cancellationToken = default);

    async Task<IReadOnlyDictionary<int, DependencyMapData?>> GetManyAsync(
        IReadOnlyCollection<int> capitalProjectIds,
        int piId,
        bool includePiLabelMatches = true,
        CancellationToken cancellationToken = default)
    {
        var maps = new Dictionary<int, DependencyMapData?>();
        foreach (var capitalProjectId in capitalProjectIds.Distinct())
        {
            maps[capitalProjectId] = await GetAsync(capitalProjectId, piId, includePiLabelMatches, cancellationToken);
        }

        return maps;
    }
}

public class DependencyMapService : IDependencyMapService
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;

    public DependencyMapService(IDbContextFactory<EstimationDbContext> ctx)
    {
        _ctx = ctx;
    }

    public async Task<DependencyMapData?> GetAsync(
        int capitalProjectId,
        int piId,
        bool includePiLabelMatches = true,
        CancellationToken cancellationToken = default) =>
        (await GetManyAsync([capitalProjectId], piId, includePiLabelMatches, cancellationToken))[capitalProjectId];

    public async Task<IReadOnlyDictionary<int, DependencyMapData?>> GetManyAsync(
        IReadOnlyCollection<int> capitalProjectIds,
        int piId,
        bool includePiLabelMatches = true,
        CancellationToken cancellationToken = default)
    {
        var maps = capitalProjectIds.Distinct().ToDictionary(id => id, _ => (DependencyMapData?)null);

        if (maps.Count == 0)
        {
            return maps;
        }

        await using var db = await _ctx.CreateDbContextAsync(cancellationToken);

        var artNames = await db.CapitalProjects
            .AsNoTracking()
            .Select(a => new { a.Id, a.Name })
            .ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);
        var artIds = maps.Keys.Where(artNames.ContainsKey).ToList();

        if (artIds.Count == 0)
        {
            return maps;
        }

        var pis = await db.Pis.AsNoTracking().ToListAsync(cancellationToken);
        var pi = pis.FirstOrDefault(p => p.Id == piId);

        if (pi is null)
        {
            return maps;
        }

        var samePiIds = (await db.Pis
                .AsNoTracking()
                .Where(p => p.Name == pi.Name)
                .Select(p => p.Id)
                .ToListAsync(cancellationToken))
            .Append(pi.Id)
            .ToHashSet();

        var artTeamIds = (await db.CapitalProjectTeams
                .AsNoTracking()
                .Where(c => artIds.Contains(c.CapitalProjectId))
                .Select(c => new { c.CapitalProjectId, c.TeamId })
                .ToListAsync(cancellationToken))
            .ToLookup(c => c.CapitalProjectId, c => c.TeamId);

        var samePiSprints = (await db.CapitalProjectSprints
                .AsNoTracking()
                .Where(s => artIds.Contains(s.CapitalProjectId) && samePiIds.Contains(s.PiId))
                .ToListAsync(cancellationToken))
            .ToLookup(s => s.CapitalProjectId);

        var piStarts = await LoadPiStartsAsync(db, pis, cancellationToken);

        var matcher = await ArtMatcher.LoadAsync(db, cancellationToken);
        var labelRules = includePiLabelMatches
            ? await FeatureScope.LoadPiLabelRulesAsync(db, pi.Name, cancellationToken)
            : [];

        var inScope = await LoadInScopeAsync(db, matcher, artIds, pi, labelRules, cancellationToken);
        var inScopeKeys = artIds.ToDictionary(
            artId => artId,
            artId => new HashSet<string>(
                inScope[artId].Select(f => KeyOf(f.JiraId)).OfType<string>(),
                StringComparer.OrdinalIgnoreCase));
        var links = await LoadLinksAsync(db, inScopeKeys, cancellationToken);

        var otherKeys = artIds.ToDictionary(
            artId => artId,
            artId => links[artId]
                .SelectMany(l => new[] { l.FromKey, l.ToKey })
                .Where(k => k.Length > 0 && !inScopeKeys[artId].Contains(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList());

        var outside = await LoadOutsideAsync(db, matcher, otherKeys, cancellationToken);
        var stubs = await LoadStubsAsync(
            db,
            artIds.ToDictionary(
                artId => artId,
                artId =>
                {
                    var resolved = new HashSet<string>(outside[artId].Select(o => o.Feature.JiraId!.Trim()), StringComparer.OrdinalIgnoreCase);
                    return otherKeys[artId].Where(k => !resolved.Contains(k)).ToList();
                }),
            cancellationToken);

        var loaded = artIds.SelectMany(artId => inScope[artId].Concat(outside[artId].Select(o => o.Feature))).ToList();
        var featureIds = loaded.Select(f => f.Id).Distinct().ToList();
        var featurePiIds = loaded
            .GroupBy(f => f.Id)
            .ToDictionary(g => g.Key, g => g.First().PiId);
        var orders = featureIds.Count == 0
            ? new Dictionary<(int FeatureId, int TeamId), CapacityOrder>()
            : (await db.TeamCapacityFeatureOrders
                    .AsNoTracking()
                    .Where(o => samePiIds.Contains(o.PiId) && featureIds.Contains(o.FeatureId))
                    .Select(o => new { o.FeatureId, o.TeamId, o.PiId, o.SortOrder, o.IsIncluded })
                    .ToListAsync(cancellationToken))
                .OrderBy(o => o.PiId == pi.Id ? 0 : o.PiId == featurePiIds.GetValueOrDefault(o.FeatureId) ? 1 : 2)
                .ThenBy(o => o.PiId)
                .DistinctBy(o => (o.FeatureId, o.TeamId))
                .ToDictionary(o => (o.FeatureId, o.TeamId), o => new CapacityOrder(o.SortOrder, o.IsIncluded));

        var artLines = await TeamPrioritization.LoadAsync(
            db,
            loaded
                .SelectMany(f => f.FeatureTeams)
                .Select(ft => ft.TeamId)
                .Distinct()
                .ToList(),
            pi.Id,
            cancellationToken);

        var features = artIds.ToDictionary(artId => artId, FeaturesOf);

        var teams = await LoadTeamsAsync(
            db,
            artIds.ToDictionary(artId => artId, artId => artTeamIds[artId].ToList()),
            features,
            cancellationToken);

        foreach (var artId in artIds)
        {
            var artSprints = (samePiSprints[artId].Any(s => s.PiId == pi.Id)
                    ? samePiSprints[artId].Where(s => s.PiId == pi.Id)
                    : samePiSprints[artId].OrderBy(s => s.PiId))
                .DistinctBy(s => (s.StartDate.Date, s.EndDate.Date))
                .OrderBy(s => s.StartDate)
                .ThenBy(s => s.Name)
                .Select(s => new DependencyArtSprint(s.Id, s.Name, s.StartDate, s.EndDate, s.IsIpSprint))
                .ToList();

            maps[artId] = new DependencyMapData(
                artId,
                artNames[artId],
                pi.Id,
                pi.Name,
                pi.StartDate ?? artSprints.FirstOrDefault()?.Start ?? piStarts.GetValueOrDefault(pi.Id),
                artSprints,
                teams[artId],
                features[artId],
                stubs[artId],
                links[artId],
                inScope[artId].Count(f => KeyOf(f.JiraId) is null));
        }

        return maps;

        List<DependencyFeature> FeaturesOf(int artId)
        {
            var artName = artNames[artId];
            var artFeatures = inScope[artId]
                .Select(f => ToFeature(f, DependencyEndKind.InScope, artId, artName, matcher, true, f.Pi, piStarts, pi, samePiIds, orders, artLines) with
                {
                    BusinessOutcomeJiraId = f.BusinessOutcome?.JiraId,
                    BusinessOutcomeSummary = f.BusinessOutcome?.Summary,
                    PortfolioEpicJiraId = f.BusinessOutcome?.PortfolioEpic?.JiraId,
                    PortfolioEpicSummary = f.BusinessOutcome?.PortfolioEpic?.Summary,
                    StrategicObjectives = StrategicObjectivesOf(f),
                    RequirementStatus = f.RequirementStatus?.Name,
                    TechnicalApproval = f.TechnicalApproval?.Name,
                    FundingStatus = f.UnfundedOption?.Name
                })
                .ToList();

            foreach (var (feature, kind) in outside[artId]
                         .OrderBy(o => o.Feature.JiraId, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(o => o.Feature.Id))
            {
                if (kind == DependencyEndKind.OtherPi)
                {
                    artFeatures.Add(ToFeature(feature, kind, artId, artName, matcher, false, PiOf(feature), piStarts, pi, samePiIds, orders, artLines));
                    continue;
                }

                var match = matcher.Match(feature);
                var otherArtId = match.IsMatched ? match.ArtId : null;
                var otherArtName = otherArtId is int id && artNames.TryGetValue(id, out var name) ? name : null;
                var inSelectedPi = feature.PiId == pi.Id || FeatureScope.MatchesPi(feature, pi.Name, labelRules);
                var featurePi = inSelectedPi ? feature.Pi : PiOf(feature);
                artFeatures.Add(ToFeature(feature, kind, otherArtId, otherArtName, matcher, inSelectedPi, featurePi, piStarts, pi, samePiIds, orders, artLines));
            }

            return artFeatures;
        }

        Pi? PiOf(Feature feature) =>
            feature.Pi
            ?? (includePiLabelMatches
                ? FeatureTargetSprintPlanner.ResolvePis(null, feature.Labels, pis).FirstOrDefault()
                : null);
    }

    private static async Task<Dictionary<int, DateTime?>> LoadPiStartsAsync(
        EstimationDbContext db,
        IReadOnlyList<Pi> pis,
        CancellationToken cancellationToken)
    {
        var undated = pis.Where(p => p.StartDate is null).Select(p => p.Id).ToList();

        if (undated.Count == 0)
        {
            return pis.ToDictionary(p => p.Id, p => p.StartDate);
        }

        var artSprintStarts = await db.CapitalProjectSprints
            .AsNoTracking()
            .Where(s => undated.Contains(s.PiId))
            .GroupBy(s => s.PiId)
            .Select(g => new { PiId = g.Key, Start = (DateTime?)g.Min(s => s.StartDate) })
            .ToDictionaryAsync(x => x.PiId, x => x.Start, cancellationToken);

        var teamSprintStarts = await db.Sprints
            .AsNoTracking()
            .Where(s => s.PiId != null && undated.Contains(s.PiId.Value))
            .GroupBy(s => s.PiId!.Value)
            .Select(g => new { PiId = g.Key, Start = (DateTime?)g.Min(s => s.StartDate) })
            .ToDictionaryAsync(x => x.PiId, x => x.Start, cancellationToken);

        return pis.ToDictionary(
            p => p.Id,
            p => p.StartDate
                ?? artSprintStarts.GetValueOrDefault(p.Id)
                ?? teamSprintStarts.GetValueOrDefault(p.Id)
                ?? p.EndDate);
    }

    private static async Task<Dictionary<int, List<Feature>>> LoadInScopeAsync(
        EstimationDbContext db,
        ArtMatcher matcher,
        IReadOnlyList<int> artIds,
        Pi pi,
        IReadOnlyList<FeatureScope.PiLabelRule> labelRules,
        CancellationToken cancellationToken)
    {
        var keys = artIds.SelectMany(artId => matcher.KeysOf(artId)).ToList();
        var piId = pi.Id;
        var piName = pi.Name;
        var useLabels = labelRules.Count > 0;

        var candidates = keys.Count == 0
            ? []
            : await db.Features
                .LikelyOnArt(keys)
                .Where(f => f.PiId == piId
                    || (f.Pi != null && f.Pi.Name == piName)
                    || (useLabels && f.Labels != null && f.Labels != ""))
                .Include(f => f.Pi)
                .Include(f => f.RequirementStatus)
                .Include(f => f.TechnicalApproval)
                .Include(f => f.UnfundedOption)
                .Include(f => f.BusinessOutcome)
                    .ThenInclude(bo => bo!.PortfolioEpic)
                        .ThenInclude(pe => pe!.StrategicObjectivePortfolioEpics)
                            .ThenInclude(spe => spe.StrategicObjective)
                .Include(f => f.FeatureTeams).ThenInclude(ft => ft.Team)
                .Include(f => f.FeatureTeams).ThenInclude(ft => ft.Sprints).ThenInclude(fts => fts.Sprint)
                .AsSplitQuery()
                .AsNoTracking()
                .ToListAsync(cancellationToken);

        var byArt = candidates.ToLookup(f => matcher.ArtIdOf(f));
        return artIds.ToDictionary(
            artId => artId,
            artId => byArt[artId]
                .Where(f => f.PiId == piId || FeatureScope.MatchesPi(f, piName, labelRules))
                .OrderBy(f => f.JiraId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.Id)
                .ToList());
    }

    private static async Task<Dictionary<int, List<DependencyLinkRow>>> LoadLinksAsync(
        EstimationDbContext db,
        IReadOnlyDictionary<int, HashSet<string>> inScopeKeys,
        CancellationToken cancellationToken)
    {
        var keys = WithUpperCase(inScopeKeys.Values.SelectMany(k => k));
        var rows = keys.Count == 0
            ? []
            : await db.IssueLinks
                .AsNoTracking()
                .Where(l => keys.Contains(l.FromKey) || keys.Contains(l.ToKey))
                .ToListAsync(cancellationToken);

        return inScopeKeys.ToDictionary(a => a.Key, a => LinksOf(rows, a.Value));
    }

    private static List<DependencyLinkRow> LinksOf(IEnumerable<IssueLink> rows, IReadOnlySet<string> inScopeKeys)
    {
        if (inScopeKeys.Count == 0)
        {
            return [];
        }

        return rows
            .Select(l => (Link: l, From: l.FromKey.Trim(), To: l.ToKey.Trim()))
            .Where(r => inScopeKeys.Contains(r.From) || inScopeKeys.Contains(r.To))
            .DistinctBy(r => r.Link.JiraLinkId, StringComparer.Ordinal)
            .OrderBy(r => r.Link.JiraLinkId, StringComparer.Ordinal)
            .Select(r =>
            {
                var typeName = string.IsNullOrWhiteSpace(r.Link.TypeName)
                    ? DependencyClassifier.UnnamedLinkType
                    : r.Link.TypeName.Trim();

                return new DependencyLinkRow(
                    r.Link.JiraLinkId,
                    typeName,
                    string.IsNullOrWhiteSpace(r.Link.OutwardLabel) ? typeName : r.Link.OutwardLabel.Trim(),
                    string.IsNullOrWhiteSpace(r.Link.InwardLabel) ? typeName : r.Link.InwardLabel.Trim(),
                    r.From,
                    r.To);
            })
            .ToList();
    }

    private static async Task<Dictionary<int, List<(Feature Feature, DependencyEndKind Kind)>>> LoadOutsideAsync(
        EstimationDbContext db,
        ArtMatcher matcher,
        IReadOnlyDictionary<int, List<string>> otherKeys,
        CancellationToken cancellationToken)
    {
        var keys = WithUpperCase(otherKeys.Values.SelectMany(k => k));
        var candidates = keys.Count == 0
            ? []
            : await db.Features
                .Where(f => f.JiraId != null && keys.Contains(f.JiraId))
                .Include(f => f.Pi)
                .Include(f => f.BusinessOutcome)
                .Include(f => f.FeatureTeams).ThenInclude(ft => ft.Team)
                .Include(f => f.FeatureTeams).ThenInclude(ft => ft.Sprints).ThenInclude(fts => fts.Sprint)
                .AsSplitQuery()
                .AsNoTracking()
                .ToListAsync(cancellationToken);

        return otherKeys.ToDictionary(a => a.Key, a => OutsideOf(candidates, matcher, a.Key, a.Value));
    }

    private static List<(Feature Feature, DependencyEndKind Kind)> OutsideOf(
        IEnumerable<Feature> candidates,
        ArtMatcher matcher,
        int artId,
        IReadOnlyList<string> otherKeys)
    {
        if (otherKeys.Count == 0)
        {
            return [];
        }

        var wanted = new HashSet<string>(otherKeys, StringComparer.OrdinalIgnoreCase);

        return candidates
            .Where(f => wanted.Contains(f.JiraId!.Trim()))
            .Select(f => (Feature: f, Kind: matcher.BelongsTo(f, artId) ? DependencyEndKind.OtherPi : DependencyEndKind.OtherArt))
            .GroupBy(o => o.Feature.JiraId!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(o => o.Kind).ThenBy(o => o.Feature.Id).First())
            .ToList();
    }

    private static async Task<Dictionary<int, List<DependencyStub>>> LoadStubsAsync(
        EstimationDbContext db,
        IReadOnlyDictionary<int, List<string>> remainingKeys,
        CancellationToken cancellationToken)
    {
        var remaining = remainingKeys.Values.SelectMany(k => k).Distinct(StringComparer.Ordinal).ToList();

        var outcomes = await FindStubsAsync(
            keys => db.BusinessOutcomes
                .AsNoTracking()
                .Where(x => x.JiraId != null && keys.Contains(x.JiraId))
                .OrderBy(x => x.Id)
                .Select(x => new StubRow(x.JiraId!, x.Summary, x.Status)),
            remaining,
            cancellationToken);
        remaining = remaining.Where(k => !outcomes.ContainsKey(k)).ToList();

        var epics = await FindStubsAsync(
            keys => db.PortfolioEpics
                .AsNoTracking()
                .Where(x => x.JiraId != null && keys.Contains(x.JiraId))
                .OrderBy(x => x.Id)
                .Select(x => new StubRow(x.JiraId!, x.Summary, x.Status)),
            remaining,
            cancellationToken);
        remaining = remaining.Where(k => !epics.ContainsKey(k)).ToList();

        var objectives = await FindStubsAsync(
            keys => db.StrategicObjectives
                .AsNoTracking()
                .Where(x => x.JiraId != null && keys.Contains(x.JiraId))
                .OrderBy(x => x.Id)
                .Select(x => new StubRow(x.JiraId!, x.Summary, x.Status)),
            remaining,
            cancellationToken);

        return remainingKeys.ToDictionary(a => a.Key, a => StubsOf(a.Value, outcomes, epics, objectives));
    }

    private static async Task<Dictionary<string, StubRow>> FindStubsAsync(
        Func<List<string>, IQueryable<StubRow>> query,
        List<string> remaining,
        CancellationToken cancellationToken)
    {
        if (remaining.Count == 0)
        {
            return new Dictionary<string, StubRow>(StringComparer.OrdinalIgnoreCase);
        }

        return (await query(WithUpperCase(remaining)).ToListAsync(cancellationToken))
            .GroupBy(r => r.JiraId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    private static List<DependencyStub> StubsOf(
        List<string> remaining,
        IReadOnlyDictionary<string, StubRow> outcomes,
        IReadOnlyDictionary<string, StubRow> epics,
        IReadOnlyDictionary<string, StubRow> objectives)
    {
        var stubs = new List<DependencyStub>();

        remaining = AddStubs(outcomes, remaining, DependencyStubKind.BusinessOutcome, stubs);
        remaining = AddStubs(epics, remaining, DependencyStubKind.PortfolioEpic, stubs);
        remaining = AddStubs(objectives, remaining, DependencyStubKind.StrategicObjective, stubs);

        stubs.AddRange(remaining.Select(k => new DependencyStub(k, DependencyStubKind.NotInTool, null, null)));

        return stubs
            .OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> AddStubs(
        IReadOnlyDictionary<string, StubRow> found,
        List<string> remaining,
        DependencyStubKind kind,
        List<DependencyStub> stubs)
    {
        if (remaining.Count == 0)
        {
            return remaining;
        }

        var left = new List<string>();

        foreach (var key in remaining)
        {
            if (found.TryGetValue(key, out var row))
            {
                stubs.Add(new DependencyStub(key, kind, row.Summary, row.Status));
            }
            else
            {
                left.Add(key);
            }
        }

        return left;
    }

    private static async Task<Dictionary<int, List<DependencyTeam>>> LoadTeamsAsync(
        EstimationDbContext db,
        IReadOnlyDictionary<int, List<int>> artTeamIds,
        IReadOnlyDictionary<int, List<DependencyFeature>> features,
        CancellationToken cancellationToken)
    {
        var teamIdsByArt = artTeamIds.ToDictionary(
            a => a.Key,
            a => a.Value.Concat(features[a.Key].SelectMany(f => f.Teams).Select(t => t.TeamId)).Distinct().ToList());
        var teamIds = teamIdsByArt.Values.SelectMany(ids => ids).Distinct().ToList();

        if (teamIds.Count == 0)
        {
            return teamIdsByArt.ToDictionary(a => a.Key, _ => new List<DependencyTeam>());
        }

        var rows = (await db.Teams
                .AsNoTracking()
                .Where(t => teamIds.Contains(t.Id))
                .Select(t => new { t.Id, t.Name, t.IsArtSharedTeam })
                .ToListAsync(cancellationToken))
            .ToDictionary(t => t.Id);

        var memberships = (await db.CapitalProjectTeams
                .AsNoTracking()
                .Where(c => teamIds.Contains(c.TeamId))
                .Select(c => new { c.TeamId, ArtName = c.Art.Name })
                .ToListAsync(cancellationToken))
            .GroupBy(c => c.TeamId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g
                    .Select(c => c.ArtName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToList());

        return teamIdsByArt.ToDictionary(
            a => a.Key,
            a =>
            {
                var inArt = artTeamIds[a.Key].ToHashSet();

                return a.Value
                    .Where(rows.ContainsKey)
                    .Select(id => rows[id])
                    .Select(t => new DependencyTeam(
                        t.Id,
                        t.Name,
                        inArt.Contains(t.Id),
                        t.IsArtSharedTeam,
                        memberships.GetValueOrDefault(t.Id) ?? []))
                    .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(t => t.TeamId)
                    .ToList();
            });
    }

    private static DependencyFeature ToFeature(
        Feature feature,
        DependencyEndKind kind,
        int? artId,
        string? artName,
        ArtMatcher matcher,
        bool inSelectedPi,
        Pi? featurePi,
        IReadOnlyDictionary<int, DateTime?> piStarts,
        Pi pi,
        IReadOnlySet<int> samePiIds,
        IReadOnlyDictionary<(int FeatureId, int TeamId), CapacityOrder> orders,
        IReadOnlyDictionary<int, TeamPrioritization> artLines)
    {
        var teams = feature.FeatureTeams
            .Where(ft => ft.Team is not null)
            .OrderByDescending(ft => ft.IsPrimary == true)
            .ThenBy(ft => ft.Team.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(ft => ft.TeamId)
            .Select(ft =>
            {
                var order = inSelectedPi ? orders.GetValueOrDefault((feature.Id, ft.TeamId)) : null;
                var belowArtLine = inSelectedPi
                    && artLines.TryGetValue(ft.TeamId, out var lines)
                    && lines.BelowArtLineOf(feature).Count > 0;
                return new DependencyFeatureTeam(
                    ft.TeamId,
                    ft.IsPrimary == true,
                    order is { IsIncluded: false } || belowArtLine,
                    order?.SortOrder,
                    inSelectedPi ? SprintsOf(ft, pi, samePiIds) : []);
            })
            .ToList();

        return new DependencyFeature(
            feature.Id,
            KeyOf(feature.JiraId),
            feature.Name,
            feature.Summary,
            feature.Status,
            feature.RagStatus,
            feature.Ranking,
            kind,
            artId,
            artName,
            SolutionTrainResolver.ScopeOf(matcher.Match(feature)),
            featurePi?.Id ?? feature.PiId,
            featurePi?.Name,
            featurePi is null ? null : piStarts.GetValueOrDefault(featurePi.Id),
            inSelectedPi,
            teams)
        {
            FreeTextDependencies = string.IsNullOrWhiteSpace(feature.Dependencies) ? null : feature.Dependencies,
            ExternalDependencies = feature.ExternalDependencies,
            Labels = feature.Labels
        };
    }

    private static IReadOnlyList<DependencyPlannedSprint> SprintsOf(FeatureTeam team, Pi pi, IReadOnlySet<int> samePiIds) =>
        team.Sprints
            .Select(x => x.Sprint)
            .Where(s => s is not null
                && (s.PiId is int sprintPiId ? samePiIds.Contains(sprintPiId) : FeatureTargetSprintPlanner.BelongsToPi(s, pi)))
            .OrderBy(s => s.StartDate)
            .ThenBy(s => s.Id)
            .Select(s => new DependencyPlannedSprint(s.Id, s.Name, s.StartDate, s.EndDate, s.SourceArtSprintId))
            .ToList();

    private static IReadOnlyList<FeatureFilterRef> StrategicObjectivesOf(Feature feature) =>
        feature.BusinessOutcome?.PortfolioEpic?.StrategicObjectivePortfolioEpics
            .Select(spe => spe.StrategicObjective)
            .Where(so => !string.IsNullOrEmpty(so.JiraId))
            .Select(so => new FeatureFilterRef(so.JiraId!, so.Summary))
            .DistinctBy(so => so.JiraId, StringComparer.OrdinalIgnoreCase)
            .ToList()
        ?? (IReadOnlyList<FeatureFilterRef>)Array.Empty<FeatureFilterRef>();

    private static string? KeyOf(string? jiraId) => string.IsNullOrWhiteSpace(jiraId) ? null : jiraId.Trim();

    private static List<string> WithUpperCase(IEnumerable<string> keys) =>
        keys.SelectMany(k => new[] { k, k.ToUpperInvariant() }).Distinct(StringComparer.Ordinal).ToList();

    private sealed record CapacityOrder(int SortOrder, bool IsIncluded);

    private sealed record StubRow(string JiraId, string Summary, string? Status);
}

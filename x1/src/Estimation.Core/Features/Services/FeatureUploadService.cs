using Estimation.Core.Features.Models;
using Estimation.Excel;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.Features.Services;

public class FeatureParseResult
{
    public List<FeatureUploadRow> Rows { get; set; } = new();
    public List<string> TechStackNames { get; set; } = new();
    public FeatureUploadColumnSelection AppliedColumns { get; set; } = new();
}

public interface IFeatureUploadService
{
    Task<FeatureExportLookups> GetExportLookupsAsync();

    Task<byte[]> ExportFilteredAsync(FeatureUploadColumnSelection selection, FeatureExportFilter filter, TimeZoneInfo? timeZone = null);

    Task<HashSet<FeatureUploadColumn>> DetectColumnsAsync(Stream fileStream);

    Task<FeatureParseResult> ParseFileAsync(Stream fileStream, FeatureUploadColumnSelection selection);
}

public class FeatureUploadService : IFeatureUploadService
{
    private const string SheetName = "Features";

    private readonly IDbContextFactory<EstimationDbContext> _contextFactory;
    private readonly IFeatureCommentService _commentService;

    public FeatureUploadService(IDbContextFactory<EstimationDbContext> contextFactory, IFeatureCommentService commentService)
    {
        _contextFactory = contextFactory;
        _commentService = commentService;
    }

    public async Task<FeatureExportLookups> GetExportLookupsAsync()
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await BuildLookupsAsync(db);
    }

    public async Task<byte[]> ExportFilteredAsync(FeatureUploadColumnSelection selection, FeatureExportFilter filter, TimeZoneInfo? timeZone = null)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();

        var lookups = await BuildLookupsAsync(db);

        var query = db.Features
            .Include(f => f.BusinessOutcome)
                .ThenInclude(bo => bo!.PortfolioEpic)
                    .ThenInclude(pe => pe!.StrategicObjectivePortfolioEpics)
                        .ThenInclude(ppe => ppe.StrategicObjective)
            .Include(f => f.RequirementStatus)
            .Include(f => f.UnfundedOption)
            .Include(f => f.PiObjective)
            .Include(f => f.Pi)
            .Include(f => f.FeatureTeams).ThenInclude(ft => ft.Team)
            .Include(f => f.FeatureTeams)
                .ThenInclude(ft => ft.TechnologyStacks)
                    .ThenInclude(ftts => ftts.SkillValues)
                        .ThenInclude(sv => sv.Skill)
            .AsSplitQuery()
            .AsNoTracking()
            .AsQueryable();

        List<Feature> features;
        if (filter.FeatureIds is null)
        {
            features = await query.OrderBy(f => f.Ranking).ThenBy(f => f.Name).ToListAsync();
        }
        else if (filter.FeatureIds.Count == 0)
        {
            features = new List<Feature>();
        }
        else
        {
            var idSet = filter.FeatureIds.ToHashSet();
            var fetched = await query.Where(f => idSet.Contains(f.Id)).ToListAsync();
            var byId = fetched.ToDictionary(f => f.Id);
            features = filter.FeatureIds
                .Where(byId.ContainsKey)
                .Select(id => byId[id])
                .ToList();
        }

        var commentsByFeature = selection.Includes(FeatureUploadColumn.Comments)
            ? await _commentService.GetUnitedAsync(features.Select(f => f.Id).ToList(), timeZone)
            : new Dictionary<int, string>();

        var rows = features.Select(f =>
        {
            var tsByName = f.FeatureTeams
                .SelectMany(ft => ft.TechnologyStacks)
                .SelectMany(ftts => ftts.SkillValues)
                .Where(sv => sv.Skill != null)
                .GroupBy(sv => sv.Skill.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => (int?)Math.Round(g.Sum(sv => sv.Value)), StringComparer.OrdinalIgnoreCase);

            var efforts = lookups.TechStackNames
                .Select(tsName => tsByName.TryGetValue(tsName, out var eff) ? eff : null)
                .ToList();

            var techStackEstimation = tsByName.Count > 0
                ? (int?)tsByName.Values.Sum(eff => eff ?? 0)
                : null;

            var bo = f.BusinessOutcome;
            var epic = bo?.PortfolioEpic;
            var firstStrategicObjective = epic?.StrategicObjectivePortfolioEpics
                .Select(spe => spe.StrategicObjective)
                .Where(so => !string.IsNullOrEmpty(so.JiraId))
                .OrderBy(so => so.JiraId, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            return new FeatureExportRow
            {
                ProjectKey = f.ProjectKey,
                JiraId = f.JiraId,
                FeatureName = f.Name,
                Summary = f.Summary,
                Ranking = f.Ranking,
                Description = f.Description,
                AcceptanceCriteria = f.AcceptanceCriteria,
                BusinessOutcome = bo?.JiraId,
                BusinessOutcomeName = bo?.Summary,
                PortfolioEpic = epic?.JiraId,
                PortfolioEpicName = epic?.Summary,
                StrategicObjective = firstStrategicObjective?.JiraId,
                StrategicObjectiveName = firstStrategicObjective?.Summary,
                Labels = f.Labels,
                Team = string.Join(FeatureExcelExportService.MultiValueSeparator,
                    f.FeatureTeams.Select(ft => ft.Team?.Name)
                        .Where(n => !string.IsNullOrWhiteSpace(n)).OrderBy(n => n)),
                Status = f.Status,
                RequirementStatus = f.RequirementStatus?.Name,
                FundingStatus = f.UnfundedOption?.Name,
                PiObjective = f.PiObjective?.Name,
                Pi = f.Pi?.Name,
                Comments = commentsByFeature.GetValueOrDefault(f.Id),
                TargetStart = f.TargetStart,
                TargetEnd = f.TargetEnd,
                DateExpected = f.DateExpected,
                StoryPoints = f.StoryPoints,
                RagExplain = f.RagExplain,
                Dependencies = f.Dependencies,
                TechStackEfforts = efforts,
                TechStackEstimation = techStackEstimation,
                TeamStoryPoints = f.FeatureTeams
                    .Where(ft => !string.IsNullOrWhiteSpace(ft.Team?.Name))
                    .GroupBy(ft => ft.Team!.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Any(ft => ft.StoryPoints.HasValue)
                            ? (int?)g.Sum(ft => ft.StoryPoints ?? 0)
                            : null,
                        StringComparer.OrdinalIgnoreCase)
            };
        }).ToList();

        return FeatureExcelExportService.GenerateFeatureExport(selection, rows, lookups);
    }

    private static async Task<FeatureExportLookups> BuildLookupsAsync(EstimationDbContext db)
    {
        var projectKeys = await db.CapitalProjects
            .Where(cp => cp.JiraKey != null && cp.JiraKey != "")
            .Select(cp => cp.JiraKey!)
            .Distinct().OrderBy(k => k).ToListAsync();

        var teamNames = await db.Teams.Select(t => t.Name).Distinct().OrderBy(n => n).ToListAsync();

        var statusValues = await db.Features
            .Where(f => f.Status != null && f.Status != "")
            .Select(f => f.Status!)
            .Distinct().OrderBy(s => s).ToListAsync();

        var techStackNames = await db.Skills.Select(s => s.Name).Distinct().OrderBy(n => n).ToListAsync();

        var requirementStatusValues = await db.RequirementStatuses
            .Select(rs => rs.Name)
            .Distinct().OrderBy(n => n).ToListAsync();

        var piObjectiveValues = await db.PiObjectives
            .Select(po => po.Name)
            .Distinct().OrderBy(n => n).ToListAsync();

        var fundingStatusValues = await db.UnfundedOptions
            .OrderBy(u => u.Order).ThenBy(u => u.Name)
            .Select(u => u.Name)
            .ToListAsync();

        var businessOutcomes = await db.BusinessOutcomes
            .Select(bo => new { bo.JiraId, bo.Summary })
            .ToListAsync();
        var boPairs = businessOutcomes
            .Where(bo => !string.IsNullOrWhiteSpace(bo.JiraId))
            .Select(bo => (JiraId: bo.JiraId!.Trim(), Name: bo.Summary ?? ""))
            .GroupBy(bo => bo.JiraId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(bo => bo.JiraId)
            .ToList();

        var portfolioEpics = await db.PortfolioEpics
            .Select(pe => new { pe.JiraId, pe.Summary })
            .ToListAsync();
        var epicPairs = portfolioEpics
            .Where(pe => !string.IsNullOrWhiteSpace(pe.JiraId))
            .Select(pe => (JiraId: pe.JiraId!.Trim(), Name: pe.Summary ?? ""))
            .GroupBy(pe => pe.JiraId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(pe => pe.JiraId)
            .ToList();

        var strategicObjectives = await db.StrategicObjectives
            .Select(so => new { so.JiraId, so.Summary })
            .ToListAsync();
        var soPairs = strategicObjectives
            .Where(so => !string.IsNullOrWhiteSpace(so.JiraId))
            .Select(so => (JiraId: so.JiraId!.Trim(), Name: so.Summary ?? ""))
            .GroupBy(so => so.JiraId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(so => so.JiraId)
            .ToList();

        return new FeatureExportLookups
        {
            ProjectKeys = projectKeys,
            TeamNames = teamNames,
            StatusValues = statusValues,
            RequirementStatusValues = requirementStatusValues,
            PiObjectiveValues = piObjectiveValues,
            FundingStatusValues = fundingStatusValues,
            BusinessOutcomes = boPairs,
            PortfolioEpics = epicPairs,
            StrategicObjectives = soPairs,
            TechStackNames = techStackNames
        };
    }

    public async Task<HashSet<FeatureUploadColumn>> DetectColumnsAsync(Stream fileStream)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var techStackNames = await db.Skills.Select(s => s.Name).ToListAsync();

        var (headers, _) = ExcelSheetReader.Read(fileStream, SheetName);
        return HeadersToPresentColumns(headers, techStackNames);
    }

    public async Task<FeatureParseResult> ParseFileAsync(Stream fileStream, FeatureUploadColumnSelection selection)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();

        var teams = await db.Teams.AsNoTracking().ToListAsync();
        var teamByName = teams.ToDictionary(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase);

        var skills = await db.Skills.AsNoTracking().ToListAsync();
        var techStackIdByName = skills.ToDictionary(s => s.Name, s => s.Id, StringComparer.OrdinalIgnoreCase);

        var piObjectiveByNormalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in await db.PiObjectives.AsNoTracking().Select(po => po.Name).ToListAsync())
        {
            piObjectiveByNormalized.TryAdd(NormalizeLookup(name), name);
        }

        var projectKeySet = new HashSet<string>(
            await db.CapitalProjects.Where(cp => cp.JiraKey != null && cp.JiraKey != "")
                .Select(cp => cp.JiraKey!).ToListAsync(),
            StringComparer.OrdinalIgnoreCase);

        var unfundedOptionIdByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var opt in await db.UnfundedOptions.AsNoTracking().Select(u => new { u.Id, u.Name }).ToListAsync())
        {
            if (!string.IsNullOrWhiteSpace(opt.Name))
            {
                unfundedOptionIdByName.TryAdd(opt.Name.Trim(), opt.Id);
            }
        }

        var businessOutcomes = await db.BusinessOutcomes.AsNoTracking().ToListAsync();
        var boIdByJiraId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var bo in businessOutcomes)
        {
            if (!string.IsNullOrWhiteSpace(bo.JiraId))
            {
                boIdByJiraId.TryAdd(bo.JiraId.Trim(), bo.Id);
            }
        }

        var existingFeatures = await db.Features
            .Include(f => f.BusinessOutcome)
            .Include(f => f.RequirementStatus)
            .Include(f => f.UnfundedOption)
            .Include(f => f.PiObjective)
            .Include(f => f.FeatureTeams).ThenInclude(ft => ft.Team)
            .Include(f => f.FeatureSkills).ThenInclude(fs => fs.Skill)
            .AsSplitQuery()
            .AsNoTracking()
            .ToListAsync();
        var existingByJiraId = existingFeatures
            .Where(f => !string.IsNullOrWhiteSpace(f.JiraId))
            .GroupBy(f => f.JiraId!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var (headers, dataRows) = ExcelSheetReader.Read(fileStream, SheetName);
        var colMap = ExcelSheetReader.BuildColumnMap(headers);

        var present = HeadersToPresentColumns(headers, skills.Select(s => s.Name).ToList());

        bool Apply(FeatureUploadColumn column) => present.Contains(column) && selection.Includes(column);

        var appliedColumns = new FeatureUploadColumnSelection
        {
            Columns = Enum.GetValues<FeatureUploadColumn>().Where(Apply).ToHashSet()
        };

        var techStackColumns = new List<(int Index, string Name, int Id)>();
        if (Apply(FeatureUploadColumn.TechStack))
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var header = headers[i].Trim();
                // Keep the first column for a skill: an export can repeat the name further right in the
                // team story-point block (a team named like a skill), which must not be read as effort.
                if (techStackIdByName.TryGetValue(header, out var tsId)
                    && techStackColumns.All(c => c.Id != tsId))
                {
                    techStackColumns.Add((i, header, tsId));
                }
            }
        }

        var rows = new List<FeatureUploadRow>();

        foreach (var dataRow in dataRows)
        {
            var jiraId = Norm(GetCell(dataRow, colMap, FeatureUploadColumn.JiraId));

            Feature? existing = null;
            if (!string.IsNullOrWhiteSpace(jiraId))
            {
                existingByJiraId.TryGetValue(jiraId, out existing);
            }

            var row = new FeatureUploadRow
            {
                ExistingFeatureId = existing?.Id,
                IsNew = existing is null,
                AppliedColumns = appliedColumns,
                JiraId = jiraId,
                CurrentJiraId = existing?.JiraId,
            };

            if (Apply(FeatureUploadColumn.ProjectKey))
            {
                row.ProjectKey = Norm(GetCell(dataRow, colMap, FeatureUploadColumn.ProjectKey));
                row.CurrentProjectKey = existing?.ProjectKey;
                if (existing is not null && !string.IsNullOrWhiteSpace(existing.ProjectKey))
                {
                    row.ProjectKeyLocked = true;
                    row.ProjectKey = existing.ProjectKey;
                    row.ProjectKeyChanged = false;
                }
                else
                {
                    row.ProjectKeyChanged = existing is not null && TextDiffers(existing.ProjectKey, row.ProjectKey);
                }
            }
            else
            {
                row.CurrentProjectKey = existing?.ProjectKey;
            }

            if (Apply(FeatureUploadColumn.FeatureName))
            {
                row.FeatureName = Norm(GetCell(dataRow, colMap, FeatureUploadColumn.FeatureName));
                row.CurrentFeatureName = existing?.Name;
                row.FeatureNameChanged = existing is not null && TextDiffers(existing.Name, row.FeatureName);
            }
            else
            {
                row.CurrentFeatureName = existing?.Name;
            }

            if (Apply(FeatureUploadColumn.Summary))
            {
                row.Summary = Norm(GetCell(dataRow, colMap, FeatureUploadColumn.Summary));
                row.CurrentSummary = existing?.Summary;
                row.SummaryChanged = existing is not null && TextDiffers(existing.Summary, row.Summary);
            }
            else
            {
                row.CurrentSummary = existing?.Summary;
            }

            if (Apply(FeatureUploadColumn.Ranking))
            {
                row.RankingRaw = Norm(GetCell(dataRow, colMap, FeatureUploadColumn.Ranking));
                row.Ranking = ParseRanking(row.RankingRaw);
                row.CurrentRanking = existing?.Ranking;
                row.RankingChanged = existing is not null && existing.Ranking != row.Ranking;
            }
            else
            {
                row.CurrentRanking = existing?.Ranking;
            }

            if (Apply(FeatureUploadColumn.Description))
            {
                row.Description = Norm(GetCell(dataRow, colMap, FeatureUploadColumn.Description));
                row.CurrentDescription = existing?.Description;
                row.DescriptionChanged = existing is not null && TextDiffers(existing.Description, row.Description);
            }
            else
            {
                row.CurrentDescription = existing?.Description;
            }

            if (Apply(FeatureUploadColumn.AcceptanceCriteria))
            {
                row.AcceptanceCriteria = Norm(GetCell(dataRow, colMap, FeatureUploadColumn.AcceptanceCriteria));
                row.CurrentAcceptanceCriteria = existing?.AcceptanceCriteria;
                row.AcceptanceCriteriaChanged = existing is not null && TextDiffers(existing.AcceptanceCriteria, row.AcceptanceCriteria);
            }
            else
            {
                row.CurrentAcceptanceCriteria = existing?.AcceptanceCriteria;
            }

            row.CurrentBusinessOutcome = existing?.BusinessOutcome?.JiraId;
            if (Apply(FeatureUploadColumn.BusinessOutcome))
            {
                var boJiraId = ExtractBusinessOutcomeJiraId(GetCell(dataRow, colMap, FeatureUploadColumn.BusinessOutcome));
                row.BusinessOutcome = boJiraId;
                if (!string.IsNullOrWhiteSpace(boJiraId))
                {
                    if (boIdByJiraId.TryGetValue(boJiraId, out var boId))
                    {
                        row.BusinessOutcomeId = boId;
                    }
                    else
                    {
                        row.ValidationErrors[nameof(FeatureUploadRow.BusinessOutcome)] =
                            $"Business Outcome '{boJiraId}' not found";
                    }
                }
                row.BusinessOutcomeChanged = existing is not null
                    && existing.BusinessOutcomeId != row.BusinessOutcomeId;
            }

            if (Apply(FeatureUploadColumn.Labels))
            {
                row.Labels = Norm(GetCell(dataRow, colMap, FeatureUploadColumn.Labels));
                row.CurrentLabels = existing?.Labels;
                row.LabelsChanged = existing is not null && TextDiffers(existing.Labels, row.Labels);
            }
            else
            {
                row.CurrentLabels = existing?.Labels;
            }

            var currentTeamNames = existing?.FeatureTeams
                .Select(ft => ft.Team?.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .OrderBy(n => n)
                .ToList() ?? new List<string>();
            row.CurrentTeam = string.Join(FeatureExcelExportService.MultiValueSeparator, currentTeamNames);
            if (Apply(FeatureUploadColumn.Team))
            {
                row.Team = Norm(GetCell(dataRow, colMap, FeatureUploadColumn.Team));
                var uploadedTeamNames = SplitMulti(row.Team);
                var unknownTeams = new List<string>();
                foreach (var name in uploadedTeamNames)
                {
                    if (teamByName.TryGetValue(name, out var team))
                    {
                        row.TeamIds.Add(team.Id);
                    }
                    else
                    {
                        unknownTeams.Add(name);
                    }
                }
                if (unknownTeams.Count > 0)
                {
                    row.ValidationErrors[nameof(FeatureUploadRow.Team)] =
                        $"Team(s) not found: {string.Join(", ", unknownTeams)}";
                }
                row.TeamChanged = existing is not null && SetDiffers(uploadedTeamNames, currentTeamNames);
            }

            row.CurrentStatus = existing?.Status;
            if (Apply(FeatureUploadColumn.Status))
            {
                var rawStatus = Norm(GetCell(dataRow, colMap, FeatureUploadColumn.Status));
                if (!string.IsNullOrWhiteSpace(rawStatus))
                {
                    row.Status = rawStatus;
                }
                else if (!string.IsNullOrWhiteSpace(existing?.Status))
                {
                    row.Status = existing!.Status;
                }
                else
                {
                    row.Status = FeatureUploadData.DefaultStatus;
                }
                row.StatusChanged = existing is not null && TextDiffers(existing.Status, row.Status);
            }

            row.CurrentRequirementStatus = existing?.RequirementStatus?.Name;
            if (Apply(FeatureUploadColumn.RequirementStatus))
            {
                row.RequirementStatus = Norm(GetCell(dataRow, colMap, FeatureUploadColumn.RequirementStatus));
                row.RequirementStatusChanged = existing is not null
                    && TextDiffers(row.CurrentRequirementStatus, row.RequirementStatus);
            }

            row.CurrentFundingStatus = existing?.UnfundedOption?.Name;
            if (Apply(FeatureUploadColumn.FundingStatus))
            {
                row.FundingStatus = Norm(GetCell(dataRow, colMap, FeatureUploadColumn.FundingStatus));
                if (!string.IsNullOrWhiteSpace(row.FundingStatus))
                {
                    if (unfundedOptionIdByName.TryGetValue(row.FundingStatus, out var optId))
                    {
                        row.FundingStatusId = optId;
                    }
                    else
                    {
                        row.ValidationErrors[nameof(FeatureUploadRow.FundingStatus)] =
                            $"Funding Status '{row.FundingStatus}' not found";
                    }
                }
                row.FundingStatusChanged = existing is not null
                    && existing.UnfundedOptionId != row.FundingStatusId;
            }

            row.CurrentPiObjective = existing?.PiObjective?.Name;
            if (Apply(FeatureUploadColumn.PiObjective))
            {
                row.PiObjective = Norm(GetCell(dataRow, colMap, FeatureUploadColumn.PiObjective));
                if (!string.IsNullOrWhiteSpace(row.PiObjective)
                    && piObjectiveByNormalized.TryGetValue(NormalizeLookup(row.PiObjective), out var canonical)
                    && !string.Equals(canonical, row.PiObjective, StringComparison.Ordinal))
                {
                    row.ValidationErrors[nameof(FeatureUploadRow.PiObjective)] =
                        $"'{row.PiObjective}' differs from existing '{canonical}' only by case or spacing — use '{canonical}'";
                }
                row.PiObjectiveChanged = existing is not null
                    && TextDiffers(row.CurrentPiObjective, row.PiObjective);
            }

            row.CurrentTargetStart = existing?.TargetStart;
            if (Apply(FeatureUploadColumn.TargetStart))
            {
                row.TargetStartRaw = Norm(GetCell(dataRow, colMap, FeatureUploadColumn.TargetStart));
                row.TargetStart = ParseDate(row.TargetStartRaw);
                row.TargetStartChanged = existing is not null && DateDiffers(existing.TargetStart, row.TargetStart);
            }

            row.CurrentTargetEnd = existing?.TargetEnd;
            if (Apply(FeatureUploadColumn.TargetEnd))
            {
                row.TargetEndRaw = Norm(GetCell(dataRow, colMap, FeatureUploadColumn.TargetEnd));
                row.TargetEnd = ParseDate(row.TargetEndRaw);
                row.TargetEndChanged = existing is not null && DateDiffers(existing.TargetEnd, row.TargetEnd);
            }

            row.CurrentDateExpected = existing?.DateExpected;
            if (Apply(FeatureUploadColumn.DateExpected))
            {
                row.DateExpectedRaw = Norm(GetCell(dataRow, colMap, FeatureUploadColumn.DateExpected));
                row.DateExpected = ParseDate(row.DateExpectedRaw);
                row.DateExpectedChanged = existing is not null && DateDiffers(existing.DateExpected, row.DateExpected);
            }

            row.CurrentStoryPoints = existing?.StoryPoints;
            if (Apply(FeatureUploadColumn.StoryPoints))
            {
                row.StoryPointsRaw = Norm(GetCell(dataRow, colMap, FeatureUploadColumn.StoryPoints));
                row.StoryPoints = ParseRanking(row.StoryPointsRaw);
                row.StoryPointsChanged = existing is not null && existing.StoryPoints != row.StoryPoints;
            }

            if (Apply(FeatureUploadColumn.RagExplain))
            {
                row.RagExplain = Norm(GetCell(dataRow, colMap, FeatureUploadColumn.RagExplain));
                row.CurrentRagExplain = existing?.RagExplain;
                row.RagExplainChanged = existing is not null && TextDiffers(existing.RagExplain, row.RagExplain);
            }
            else
            {
                row.CurrentRagExplain = existing?.RagExplain;
            }

            if (Apply(FeatureUploadColumn.Dependencies))
            {
                row.Dependencies = Norm(GetCell(dataRow, colMap, FeatureUploadColumn.Dependencies));
                row.CurrentDependencies = existing?.Dependencies;
                row.DependenciesChanged = existing is not null && TextDiffers(existing.Dependencies, row.Dependencies);
            }
            else
            {
                row.CurrentDependencies = existing?.Dependencies;
            }

            if (techStackColumns.Count > 0)
            {
                var existingTs = existing?.FeatureSkills
                    .Where(fs => fs.Skill != null)
                    .ToDictionary(fs => fs.SkillId, fs => (int?)Math.Round(fs.Value))
                    ?? new Dictionary<int, int?>();

                foreach (var (index, name, tsId) in techStackColumns)
                {
                    var raw = Norm(index < dataRow.Count ? dataRow[index] : null);
                    existingTs.TryGetValue(tsId, out var oldEffort);

                    var item = new FeatureTechStackUploadItem
                    {
                        TechStackId = tsId,
                        TechStackName = name,
                        RawValue = raw,
                        OldEffort = oldEffort
                    };

                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        item.NewEffort = null;
                        item.IsRemoved = oldEffort.HasValue;
                        item.IsChanged = oldEffort.HasValue;
                    }
                    else if (int.TryParse(raw, System.Globalization.NumberStyles.Any,
                                 System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                    {
                        if (parsed < 0)
                        {
                            item.Error = "Estimated effort must be >= 0";
                            row.ValidationErrors[$"TS:{name}"] = item.Error;
                        }
                        item.NewEffort = parsed;
                        item.IsChanged = oldEffort != parsed;
                    }
                    else
                    {
                        item.Error = $"'{raw}' is not a valid number";
                        row.ValidationErrors[$"TS:{name}"] = item.Error;
                    }

                    row.TechStacks.Add(item);
                }
            }

            ValidateRow(row, projectKeySet);

            rows.Add(row);
        }

        return new FeatureParseResult
        {
            Rows = rows,
            TechStackNames = techStackColumns.Select(c => c.Name).ToList(),
            AppliedColumns = appliedColumns
        };
    }

    private static void ValidateRow(FeatureUploadRow row, HashSet<string> projectKeySet)
    {
        if (row.IsNew)
        {
            if (string.IsNullOrWhiteSpace(row.ProjectKey))
            {
                row.ValidationErrors[nameof(FeatureUploadRow.ProjectKey)] =
                    "Project Key is required — the feature will not be created";
            }
            if (string.IsNullOrWhiteSpace(row.FeatureName))
            {
                row.ValidationErrors[nameof(FeatureUploadRow.FeatureName)] =
                    "Feature Name is required — the feature will not be created";
            }
            if (string.IsNullOrWhiteSpace(row.Summary))
            {
                row.ValidationErrors[nameof(FeatureUploadRow.Summary)] =
                    "Feature Summary is required — the feature will not be created";
            }
        }

        if (row.Summary?.Length > 255)
        {
            row.ValidationErrors[nameof(FeatureUploadRow.Summary)] = $"Max length 255 (current: {row.Summary.Length})";
        }
        if (row.JiraId?.Length > 100)
        {
            row.ValidationErrors[nameof(FeatureUploadRow.JiraId)] = $"Max length 100 (current: {row.JiraId.Length})";
        }
        if (row.FeatureName?.Length > 255)
        {
            row.ValidationErrors[nameof(FeatureUploadRow.FeatureName)] = $"Max length 255 (current: {row.FeatureName.Length})";
        }
        if (row.Description?.Length > 32767)
        {
            row.ValidationErrors[nameof(FeatureUploadRow.Description)] = "Max length 32767";
        }
        if (row.AcceptanceCriteria?.Length > 32767)
        {
            row.ValidationErrors[nameof(FeatureUploadRow.AcceptanceCriteria)] = "Max length 32767";
        }
        if (row.Labels?.Length > 4000)
        {
            row.ValidationErrors[nameof(FeatureUploadRow.Labels)] = $"Max length 4000 (current: {row.Labels.Length})";
        }
        if (row.Status?.Length > 50)
        {
            row.ValidationErrors[nameof(FeatureUploadRow.Status)] = $"Max length 50 (current: {row.Status.Length})";
        }
        if (row.RequirementStatus?.Length > 30)
        {
            row.ValidationErrors[nameof(FeatureUploadRow.RequirementStatus)] = $"Max length 30 (current: {row.RequirementStatus.Length})";
        }
        if (row.PiObjective?.Length > 255)
        {
            row.ValidationErrors[nameof(FeatureUploadRow.PiObjective)] = $"Max length 255 (current: {row.PiObjective.Length})";
        }

        if (!string.IsNullOrWhiteSpace(row.RankingRaw) && row.Ranking is null)
        {
            row.ValidationErrors[nameof(FeatureUploadRow.Ranking)] = $"'{row.RankingRaw}' is not a valid number";
        }
        if (!string.IsNullOrWhiteSpace(row.StoryPointsRaw) && row.StoryPoints is null)
        {
            row.ValidationErrors[nameof(FeatureUploadRow.StoryPoints)] = $"'{row.StoryPointsRaw}' is not a valid number";
        }
        if (!string.IsNullOrWhiteSpace(row.TargetStartRaw) && row.TargetStart is null)
        {
            row.ValidationErrors[nameof(FeatureUploadRow.TargetStart)] = $"'{row.TargetStartRaw}' is not a valid date";
        }
        if (!string.IsNullOrWhiteSpace(row.TargetEndRaw) && row.TargetEnd is null)
        {
            row.ValidationErrors[nameof(FeatureUploadRow.TargetEnd)] = $"'{row.TargetEndRaw}' is not a valid date";
        }
        if (!string.IsNullOrWhiteSpace(row.DateExpectedRaw) && row.DateExpected is null)
        {
            row.ValidationErrors[nameof(FeatureUploadRow.DateExpected)] = $"'{row.DateExpectedRaw}' is not a valid date";
        }
        if (row.RagExplain?.Length > 255)
        {
            row.ValidationErrors[nameof(FeatureUploadRow.RagExplain)] = $"Max length 255 (current: {row.RagExplain.Length})";
        }
        if (row.Dependencies?.Length > 255)
        {
            row.ValidationErrors[nameof(FeatureUploadRow.Dependencies)] = $"Max length 255 (current: {row.Dependencies.Length})";
        }

        if (!string.IsNullOrWhiteSpace(row.ProjectKey) && !row.ProjectKeyLocked
            && !projectKeySet.Contains(row.ProjectKey.Trim()))
        {
            row.ValidationErrors[nameof(FeatureUploadRow.ProjectKey)] = $"Project Key '{row.ProjectKey}' not found";
        }
    }

    private static string? GetCell(List<string> row, Dictionary<string, int> colMap, FeatureUploadColumn column)
    {
        var value = ExcelSheetReader.GetCell(row, colMap, FeatureExcelExportService.Headers[column]);
        if (value is null && FeatureExcelExportService.HeaderAliases.TryGetValue(column, out var aliases))
        {
            foreach (var alias in aliases)
            {
                value = ExcelSheetReader.GetCell(row, colMap, alias);
                if (value is not null)
                {
                    break;
                }
            }
        }
        return value;
    }

    private static string NormalizeLookup(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }
        return System.Text.RegularExpressions.Regex.Replace(value.Trim(), @"\s+", " ").ToLowerInvariant();
    }

    private static int? ParseRanking(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        if (double.TryParse(raw, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var dbl))
        {
            return (int)dbl;
        }
        return null;
    }

    private static DateTime? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        var s = raw.Trim();
        if (DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
        {
            return dt.Date;
        }
        if (double.TryParse(s, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var serial))
        {
            try
            {
                return DateTime.FromOADate(serial).Date;
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    private static bool DateDiffers(DateTime? a, DateTime? b) => a?.Date != b?.Date;

    private static List<string> SplitMulti(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new List<string>();
        }
        return raw.Split(FeatureExcelExportService.MultiValueSplitChar,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool SetDiffers(IEnumerable<string> a, IEnumerable<string> b)
    {
        var setA = new HashSet<string>(a, StringComparer.OrdinalIgnoreCase);
        var setB = new HashSet<string>(b, StringComparer.OrdinalIgnoreCase);
        return !setA.SetEquals(setB);
    }

    private static string? Norm(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ExtractBusinessOutcomeJiraId(string? cell)
    {
        if (string.IsNullOrWhiteSpace(cell))
        {
            return null;
        }

        var trimmed = cell.Trim();
        var separator = trimmed.IndexOf(" - ", StringComparison.Ordinal);
        return separator > 0 ? trimmed[..separator].Trim() : trimmed;
    }

    private static string NormForCompare(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }
        return value.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
    }

    private static bool TextDiffers(string? a, string? b) =>
        !string.Equals(NormForCompare(a), NormForCompare(b), StringComparison.Ordinal);

    private static HashSet<FeatureUploadColumn> HeadersToPresentColumns(List<string> headers, List<string> techStackNames)
    {
        var headerToColumn = FeatureExcelExportService.Headers
            .Where(kvp => !FeatureExcelExportService.ExportOnlyColumns.Contains(kvp.Key))
            .ToDictionary(kvp => kvp.Value, kvp => kvp.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var (column, aliases) in FeatureExcelExportService.HeaderAliases)
        {
            if (FeatureExcelExportService.ExportOnlyColumns.Contains(column))
            {
                continue;
            }
            foreach (var alias in aliases)
            {
                headerToColumn.TryAdd(alias, column);
            }
        }

        var techStackSet = new HashSet<string>(techStackNames, StringComparer.OrdinalIgnoreCase);

        var present = new HashSet<FeatureUploadColumn>();
        foreach (var raw in headers)
        {
            var header = raw.Trim();
            if (header.Length == 0)
            {
                continue;
            }
            if (headerToColumn.TryGetValue(header, out var column))
            {
                present.Add(column);
            }
            else if (techStackSet.Contains(header))
            {
                present.Add(FeatureUploadColumn.TechStack);
            }
        }
        return present;
    }
}

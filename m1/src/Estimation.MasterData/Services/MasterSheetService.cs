using Estimation.Components.Shared;
using Estimation.Core;
using Estimation.Core.Administration.Audit;
using Estimation.Core.Features.Models;
using Estimation.Core.Features.Services;
using Estimation.Core.JiraIntegration.Client;
using Estimation.MasterData.Models;
using Estimation.Core.Train.Models;
using Estimation.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Estimation.MasterData.Services;

public interface IMasterSheetService
{
    Task<Result> UpsertRowAsync(MasterRow row, bool createJiraInJirsServer = false);
    Task<Result> UpsertMultipleRowsAsync(List<MasterRow> rows, bool createJiraInJirsServer = false);
    public Task<List<BusinessOutcome>> GetBusinessOutcomeHierarchy();
    public Task<List<Feature>> GetAllWithHierarchyAsync();
    Task<byte[]> ExportFilteredAsync(string fileName);
    Task<MasterSheetExportLookups> GetExportLookupsAsync();
    Task<HashSet<MasterSheetUploadColumn>> DetectColumnsAsync(Stream fileStream);
    Task<MasterSheetParseResult> ParseFileAsync(Stream fileStream);
    Task<Result> UpsertFromUploadAsync(MasterSheetUploadData data);
}

public class MasterSheetService : IMasterSheetService
{
    private readonly EstimationDbContext _db;
    private readonly ILogger<MasterSheetService> _logger;
    private readonly IAuditUserProvider _auditUser;
    private readonly IJiraIssueService _jiraIssueService;
    private readonly IMasterDataCacheService _masterDataCacheService;

    public MasterSheetService(EstimationDbContext db, ILogger<MasterSheetService> logger, IAuditUserProvider auditUser, IJiraIssueService jiraIssueService, IMasterDataCacheService masterDataCacheService)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _auditUser = auditUser ?? throw new ArgumentNullException(nameof(_auditUser));
        _jiraIssueService = jiraIssueService;
        _masterDataCacheService = masterDataCacheService;
    }

    public async Task<Result> UpsertMultipleRowsAsync(List<MasterRow> rows, bool createJiraInJirsServer)
    {
        if (rows == null || !rows.Any())
        {
            return Result.Failure("No rows to process");
        }

        var failedRows = new List<string>();
        var successCount = 0;

        foreach (var row in rows)
        {
            var result = await UpsertRowAsync(row, createJiraInJirsServer);
            if (result.IsSuccess)
            {
                successCount++;
            }
            else
            {
                failedRows.Add($"Row {row.Id}: {result.ErrorMessage}");
            }
        }

        if (failedRows.Any())
        {
            var errorMessage = $"Processed {successCount}/{rows.Count} rows. Failures:\n" +
                             string.Join("\n", failedRows);
            return Result.Failure(errorMessage);
        }
        var sucesss = $"Processed {successCount}/{rows.Count} rows";
        return Result.Success(sucesss);
    }

    public async Task<Result> UpsertRowAsync(MasterRow row, bool createJiraInJirsServer)
    {
        if (row == null)
        {
            return Result.Failure("Row cannot be null");
        }

        var validationResult = ValidateRow(row);
        if (!validationResult.IsSuccess)
        {
            return validationResult;
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();

        try
        {
            var epicResult = await UpdateEpicAsync(row);
            if (!epicResult.IsSuccess)
            {
                await transaction.RollbackAsync();
                return epicResult;
            }

            var featureResult = await UpsertFeatureAsync(row, createJiraInJirsServer);
            if (!featureResult.IsSuccess)
            {
                await transaction.RollbackAsync();
                return featureResult;
            }

            await transaction.CommitAsync();
            _logger.LogInformation("Successfully upserted row with ID: {RowId}", row.Id);

            return Result.Success();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error upserting row with ID: {RowId}", row.Id);
            return Result.Failure($"Failed to upsert row: {ex.Message}");
        }
    }

    public async Task<List<BusinessOutcome>> GetBusinessOutcomeHierarchy()
    {
        return await _db.BusinessOutcomes.Include(x => x.PortfolioEpic).
            ThenInclude(pe => pe!.StrategicObjectivePortfolioEpics)
                    .ThenInclude(ppe => ppe.StrategicObjective)
            .AsNoTracking().OrderBy(bo => bo.Id).ToListAsync();
    }

    public async Task<List<Feature>> GetAllWithHierarchyAsync()
    {
        return await _db.Features
            .Include(f => f.Pi)
            .Include(f => f.PiObjective)
            .Include(f => f.RequirementStatus)
            .Include(f => f.BusinessOutcome)
                  .ThenInclude(bo => bo!.PortfolioEpic!.UnfundedOption)
               .Include(f => f.BusinessOutcome)
                   .ThenInclude(bo => bo!.PortfolioEpic)
                    .ThenInclude(pe => pe!.StrategicObjectivePortfolioEpics)
                        .ThenInclude(ppe => ppe.StrategicObjective)
                            .ThenInclude(pp => pp.CapitalProjectStrategicObjectives)
                                .ThenInclude(cpp => cpp.Art)
            .Include(f => f.FeatureTeams).ThenInclude(ft => ft.Team)
            .Include(f => f.FeatureSkills).ThenInclude(fs => fs.Skill)
            .AsSplitQuery()
            .AsNoTracking()
            .OrderBy(f => f.Ranking).ThenBy(f => f.Name)
            .ToListAsync();
    }

    public async Task<byte[]> ExportFilteredAsync(string fileName)
    {
        var lookups = await BuildLookupsAsync();

        var features = new List<Feature>();
        if (fileName.Equals("MasterSheet.xlsx"))
        {
            var featureData = await _masterDataCacheService.GetFeaturesAsync();
            features = featureData.ToList();
        }

        var counter = features.Count;

        var masterSheetExportRows = new List<MasterSheetExportRow>(counter);

        foreach (var feature in features)
        {
            var bo = feature.BusinessOutcome;

            var epic = bo?.PortfolioEpic;

            // Columns are now feature skills; cell value is FeatureSkill.Value rounded to a whole number.
            var tsByName = feature.FeatureSkills
                .Where(fs => fs.Skill != null)
                .ToDictionary(fs => fs.Skill.Name, fs => (int?)Math.Round(fs.Value), StringComparer.OrdinalIgnoreCase);

            var efforts = lookups.TechStackNames
                .Select(tsName => tsByName.TryGetValue(tsName, out var eff) ? eff : null)
                .ToList();

            var teams = feature.FeatureTeams.Where(x => x.IsPrimary == true)
                .Select(ft => new MasterRowTeamRef(ft.TeamId, ft.Team.Name, ft.IsPrimary))
                .ToList();

            var programRefs = epic?.StrategicObjectivePortfolioEpics
                ?.Select(ppe => ppe.StrategicObjective)
                .Select(p => new MasterRowJiraRef(p.Id, p.JiraId!, p.Summary, p.Description))
                .DistinctBy(p => p.SoTableId)
                .ToList() ?? [];

            var masterSheetExportRow = new MasterSheetExportRow
            {
                MasterId = feature.Id,
                ProjectKey = feature.ProjectKey,
                ProgramJiraId = programRefs?.FirstOrDefault()?.JiraId,
                ProgramName = programRefs?.FirstOrDefault()?.Summary,
                EpicJiraId = epic?.JiraId,
                EpicName = epic?.Summary,
                EpicRanking = epic?.Ranking,
                UnfundedOption = epic?.UnfundedOption?.Name,
                BusinessOutcomeJiraId = bo?.JiraId,
                BusinessOutcome = bo?.Summary,
                PIName = feature.Pi?.Name,
                ConfidencePercentage = feature.ConfidencePercentage,
                TeamName = teams?.FirstOrDefault()?.Name,
                Dependencies = feature.Dependencies,
                DevEstimates = CalculateDevEstimate(tsByName),
                EstimatedDays = feature.StoryPoints,
                FeatureJiraId = feature.JiraId,
                FeatureName = feature.Name,
                FeatureLabels = feature.Labels,
                FeatureSummary = feature.Summary,
                L6Owner = epic?.L6Owner,
                TargetEndDate = feature.TargetEnd,
                TechStacks = efforts
            };

            masterSheetExportRows.Add(masterSheetExportRow);
        }

        return MasterSheetExcelExportService.GenerateMasterSheetExport(masterSheetExportRows, lookups);
    }

    private int? CalculateDevEstimate(Dictionary<string, int?> listTechStacksRefrence)
    {
        return listTechStacksRefrence?.Where(x => x.Key != "Tech PO" && x.Value != null).Sum(y => y.Value);
    }

    public async Task<MasterSheetExportLookups> GetExportLookupsAsync()
    {
        return await BuildLookupsAsync();
    }

    private async Task<MasterSheetExportLookups> BuildLookupsAsync()
    {
        var projects = await _masterDataCacheService.GetCapitalProjectsAsync();
        var projectKeys = projects
            .SelectMany(cp => cp.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(k => k).ToList();

        var teamsData = await _masterDataCacheService.GetTeamsAsync();
        var teamNames = teamsData.Select(t => t.Name).Distinct().OrderBy(n => n).ToList();

        // Repointed to Skills: the dynamic master-sheet columns now represent feature skills.
        var techStackNames = await _db.Skills.Select(s => s.Name).Distinct().OrderBy(n => n).ToListAsync();

        var boData = await _masterDataCacheService.GetBusinessOutcomesAsync();
        var businessOutcomes = boData
            .Select(bo => new { bo.JiraId, bo.Summary })
            .ToList();
        var boOptions = businessOutcomes
            .Where(bo => !string.IsNullOrWhiteSpace(bo.JiraId))
            .Select(bo => (JiraId: bo.JiraId!.Trim(), Name: bo.Summary ?? ""))
            .GroupBy(bo => bo.JiraId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(bo => bo.JiraId)
            .ToList();

        var epicData = await _masterDataCacheService.GetPortfolioEpicsAsync();
        var epics = epicData
            .Select(ep => new { ep.JiraId, ep.Summary })
            .ToList();
        var epicOptions = epics
            .Where(pe => !string.IsNullOrWhiteSpace(pe.JiraId))
            .Select(pe => (JiraId: pe.JiraId!.Trim(), Name: pe.Summary ?? ""))
            .GroupBy(pe => pe.JiraId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(pe => pe.JiraId)
            .ToList();

        var programData = await _masterDataCacheService.GetStrategicObjectivesAsync();
        var programs = programData
            .Select(pr => new { pr.JiraId, pr.Summary })
            .ToList();
        var programOptions = programs
            .Where(so => !string.IsNullOrWhiteSpace(so.JiraId))
            .Select(so => (JiraId: so.JiraId!.Trim(), Name: so.Summary ?? ""))
            .GroupBy(so => so.JiraId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(so => so.JiraId)
            .ToList();

        var piData = await _masterDataCacheService.GetPlanningIncrementsAsync();
        var piNames = piData.Select(t => t.Name).Distinct().OrderBy(n => n).ToList();

        var unfundedData = await _masterDataCacheService.GetUnfundedOptionsAsync();
        var unfundeOptions = unfundedData.Select(t => t.Name).Distinct().OrderBy(n => n).ToList();

        return new MasterSheetExportLookups
        {
            ProjectKeys = projectKeys,
            TeamNames = teamNames,
            BusinessOutcomeOptions = boOptions,
            UnfundedOptions = unfundeOptions,
            EpicOptions = epicOptions,
            PIOptions = piNames,
            ProgramOptions = programOptions,
            TechStackNames = techStackNames
        };
    }

    private async Task<Result> UpdateEpicAsync(MasterRow row)
    {
        try
        {
            if (row.EpicTableId == null)
            {
                return Result.Failure("Epic Table ID is required");
            }

            var existingEpic = await _db.PortfolioEpics
                .Include(e => e.StrategicObjectivePortfolioEpics)
                .FirstOrDefaultAsync(x => x.Id == row.EpicTableId);

            if (existingEpic == null)
            {
                return Result.Failure($"Epic with ID {row.EpicTableId} not found for project {row.ProjectKey}");
            }

            // Update Epic properties
            existingEpic.Ranking = row.EpicRanking;
            existingEpic.L6Owner = row.L6Owner;
            existingEpic.UnfundedOptionId = row.UnfundedOptionId;
            await _db.SaveChangesAsync();

            _logger.LogDebug("Updated epic ID: {EpicId} for row ID: {RowId}", existingEpic.Id, row.Id);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating epic for row ID: {RowId}", row.Id);
            return Result.Failure($"Failed to update epic: {ex.Message}");
        }
    }

    private async Task<Result> UpsertFeatureAsync(MasterRow row, bool createJiraInJiraServer)
    {
        try
        {
            if (createJiraInJiraServer && row.JiraId == null)
            {
                await CreateFeatureInJiraServer(row);
            }
            else
            {
                if (createJiraInJiraServer == true)
                {
                    var gfedTeams = await GetGfedTeams(row);
                    await _jiraIssueService.UpdateIssueAsync(_auditUser.GetCurrentUserName(), row.JiraId!.Trim(), new JiraUpdateIssueRequest
                    {
                        Summary = row.FeatureSummary,
                        Labels = ParseLabelsForJira(row.FeatureLabel),
                        ParentJiraKey = row.BoJiraId,
                        StoryPoints = row.EstimatedDays,
                        FeatureName = row.FeatureName,
                        PlanningIncrement = row.PiName,
                        GfedTeams = gfedTeams,
                        TargetEnd = row.TargetEnd,
                        FieldsToUpdate = new HashSet<string> { JiraUpdateFields.GfedTeam, JiraUpdateFields.Summary,JiraUpdateFields.PlanningIncrement,
                        JiraUpdateFields.ParentLink,JiraUpdateFields.Labels,JiraUpdateFields.FeatureName,
                        JiraUpdateFields.StoryPoints,JiraUpdateFields.TargetEnd},
                    });
                }
            }
            var piId = row.PiId;
            var teamId = row.Teams?.FirstOrDefault()?.TeamId;
            var techEfforts = ExtractTechStackEfforts(row);

            var featureMasterSheet = new FeatureMasterSheet
            {
                ExistingFeatureId = (bool)row.IsNewRow ? null : row.Id,
                JiraId = row.JiraId?.Trim(),
                ProjectKey = row.ProjectKey,
                FeatureName = row.FeatureName?.Trim(),
                FeatureSummary = row.FeatureSummary?.Trim(),
                BusinessOutcomeId = row.BoId,
                TargetEndDate = row.TargetEnd,
                PiId = piId,
                TeamId = teamId,
                StoryPoints = row.EstimatedDays,
                Dependencies = row.Dependencies?.Trim(),
                ConfidencePercentage = row.ConfidencePercentage,
                ConnectToJira = createJiraInJiraServer,
                TechStackEfforts = techEfforts,
                FeatureLabel = row.FeatureLabel
            };

            await UpsertFromMasterSheetAsync(featureMasterSheet);

            _logger.LogDebug("Upserted feature for row ID: {RowId}", row.Id);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error upserting feature for row ID: {RowId}", row.Id);
            return Result.Failure($"Failed to upsert feature: {ex.Message}");
        }
    }

    private List<string>? ParseLabelsForJira(string? labels)
    {
        if (string.IsNullOrEmpty(labels))
        {
            return null;
        }
        return labels
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private async Task<List<string>>? GetGfedTeams(MasterRow row)
    {
        var allTeam = await _masterDataCacheService.GetTeamsAsync();
        return row.Teams
       .Select(ft => allTeam.FirstOrDefault(t => t.Id == ft.TeamId))
       .Where(t => t is not null)
       .Select(t => JiraTeamMatcher.CanonicalName(t!))
       .ToList();
    }

    private async Task CreateFeatureInJiraServer(MasterRow row)
    {
        var gfedTeams = await GetGfedTeams(row);

        var jiraKey = await _jiraIssueService.CreateIssueAsync(_auditUser.GetCurrentUserName(),
            new JiraCreateIssueRequest
            {
                ProjectKey = row.ProjectKey!.Trim(),
                IssueType = JiraIssueTypes.Feature,
                Summary = row.FeatureSummary,
                FeatureName = row.FeatureName,
                Labels = ParseLabelsForJira(row.FeatureLabel),
                ParentJiraKey = row.BoJiraId,
                GfedTeams = gfedTeams,
                TargetEnd = row.TargetEnd,
                // we need to add full name of team as gfed team we are showing gfed team in jira
            });

        if (string.IsNullOrWhiteSpace(row.JiraId))
        {
            row.JiraId = jiraKey;
        }
    }

    private async Task UpsertFromMasterSheetAsync(FeatureMasterSheet data)
    {
        if (data.ExistingFeatureId.HasValue)
        {
            // Update existing feature
            var existingFeature = await _db.Features
                .Include(f => f.FeatureSkills)
                .Include(t => t.FeatureTeams)
                .FirstOrDefaultAsync(f => f.Id == data.ExistingFeatureId.Value);

            if (existingFeature == null)
            {
                throw new InvalidOperationException($"Feature with ID {data.ExistingFeatureId} not found");
            }

            UpdateFeatureProperties(existingFeature, data);

            // Update tech stack efforts
            UpdateTechStackEfforts(existingFeature, data.TechStackEfforts);

            _db.Features.Update(existingFeature);
        }
        else
        {
            // Create new feature
            var newFeature = new Feature
            {
                JiraId = data.JiraId,
                ProjectKey = data.ProjectKey,
                IssueType = JiraIssueTypes.Feature,
                Name = data.FeatureName,
                Summary = data.FeatureSummary,
                BusinessOutcomeId = data.BusinessOutcomeId,
                TargetEnd = data.TargetEndDate,
                PiId = data.PiId,
                Dependencies = data.Dependencies,
                Labels = data.FeatureLabel,
                ConfidencePercentage = data.ConfidencePercentage,
                IsLinkedToTheJira = data.ConnectToJira ? true : null,
                ModifiedBy = _auditUser.GetCurrentUserName(),
                ModifiedAt = DateTime.UtcNow,
                StoryPoints = data.StoryPoints,
                FeatureSkills = data.TechStackEfforts
                    .Where(kvp => kvp.Value.HasValue)
                    .Select(kvp => new FeatureSkill
                    {
                        SkillId = kvp.Key,
                        Value = kvp.Value!.Value
                    })
                    .ToList(),
            };
            UpdateTeamAndMakePrimary(newFeature, data);
            await _db.Features.AddAsync(newFeature);
        }

        await _db.SaveChangesAsync();
    }

    private void UpdateFeatureProperties(Feature feature, FeatureMasterSheet data)
    {
        feature.JiraId = data.JiraId;
        feature.Name = data.FeatureName;
        feature.Summary = data.FeatureSummary;
        feature.IssueType = JiraIssueTypes.Feature;
        feature.BusinessOutcomeId = data.BusinessOutcomeId;
        feature.TargetEnd = data.TargetEndDate;
        feature.PiId = data.PiId;
        feature.Dependencies = data.Dependencies;
        feature.ConfidencePercentage = data.ConfidencePercentage;
        feature.Labels = data.FeatureLabel;
        feature.StoryPoints = data.StoryPoints;
        feature.ModifiedBy = _auditUser.GetCurrentUserName();
        feature.ModifiedAt = DateTime.UtcNow;

        UpdateTeamAndMakePrimary(feature, data);
    }

    private void UpdateTeamAndMakePrimary(Feature feature, FeatureMasterSheet data)
    {
        if (data.TeamId.HasValue)
        {
            // Initialize FeatureTeams if null
            if (feature.FeatureTeams == null)
            {
                feature.FeatureTeams = new List<FeatureTeam>();
            }

            // Check if team already exists
            var existingTeam = feature.FeatureTeams
                .FirstOrDefault(ft => ft.TeamId == data.TeamId.Value);

            if (existingTeam != null)
            {
                // Team exists, set it as primary
                existingTeam.IsPrimary = true;
                _logger.LogDebug("Set existing team {TeamId} as primary for feature {FeatureId}",
                    data.TeamId.Value, feature.Id);
            }
            else
            {
                // Team doesn't exist, add it as primary
                feature.FeatureTeams.Add(new FeatureTeam
                {
                    FeatureId = feature.Id,
                    TeamId = data.TeamId.Value,
                    IsPrimary = true
                });
                _logger.LogDebug("Added new team {TeamId} as primary for feature {FeatureId}",
                    data.TeamId.Value, feature.Id);
            }

            // Set all other teams to non-primary
            var otherTeams = feature.FeatureTeams
                .Where(ft => ft.TeamId != data.TeamId.Value)
                .ToList();

            foreach (var team in otherTeams)
            {
                team.IsPrimary = false;
            }

            if (otherTeams.Any())
            {
                _logger.LogDebug("Set {Count} other teams to non-primary for feature {FeatureId}",
                    otherTeams.Count, feature.Id);
            }
        }
    }

    private void UpdateTechStackEfforts(Feature feature, Dictionary<int, int?> newEfforts)
    {
        // newEfforts is keyed by SkillId now. Remove General FeatureSkills not present in the new data.
        var toRemove = feature.FeatureSkills
            .Where(e => !newEfforts.ContainsKey(e.SkillId))
            .ToList();

        foreach (var fs in toRemove)
        {
            _db.FeatureSkills.Remove(fs);
        }

        foreach (var (skillId, value) in newEfforts)
        {
            if (!value.HasValue)
            {
                continue;
            }

            var existing = feature.FeatureSkills.FirstOrDefault(e => e.SkillId == skillId);
            if (existing != null)
            {
                existing.Value = value.Value;
            }
            else
            {
                feature.FeatureSkills.Add(new FeatureSkill
                {
                    SkillId = skillId,
                    Value = value.Value
                });
            }
        }
    }

    private Dictionary<int, int?> ExtractTechStackEfforts(MasterRow row)
    {
        var techEfforts = new Dictionary<int, int?>();

        if (row.TechStacks == null || !row.TechStacks.Any())
        {
            return techEfforts;
        }

        foreach (var techStack in row.TechStacks)
        {
            if (techStack.Id.HasValue && techStack.EstimatedEffort.HasValue)
            {
                techEfforts[techStack.Id.Value] = techStack.EstimatedEffort;
            }
        }

        return techEfforts;
    }

    private Result ValidateRow(MasterRow row)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(row.ProjectKey))
        {
            errors.Add("Project Key is required");
        }

        if (row.EpicTableId == null)
        {
            errors.Add("Epic is required");
        }

        if (row.BoId == null)
        {
            errors.Add("Business Outcome is required");
        }

        if (string.IsNullOrWhiteSpace(row.FeatureName))
        {
            errors.Add("Feature Name is required");
        }

        if (row.EpicRanking.HasValue && (row.EpicRanking < 0 || row.EpicRanking > 100))
        {
            errors.Add("Epic Ranking must be between 0 and 100");
        }

        if (row.ConfidencePercentage.HasValue &&
            (row.ConfidencePercentage < 0 || row.ConfidencePercentage > 100))
        {
            errors.Add("Confidence Percentage must be between 0 and 100");
        }

        if (row.EstimatedDays.HasValue && row.EstimatedDays < 0)
        {
            errors.Add("Estimated Days cannot be negative");
        }

        if (row.StrategicObjective == null || !row.StrategicObjective.Any())
        {
            errors.Add("Strategic Objective is required");
        }

        if (errors.Any())
        {
            return Result.Failure(string.Join(", ", errors));
        }

        return Result.Success();
    }

    public async Task<HashSet<MasterSheetUploadColumn>> DetectColumnsAsync(Stream fileStream)
    {
        var db = await _masterDataCacheService.GetTechnologyStacksAsync();
        var techStackNames = db.Select(t => t.Name).ToList();

        var (headers, _) = ExcelSheetReader.Read(fileStream, MasterSheetExcelExportService.MasterSheetName);
        return MasterSheetExcelExportService.HeadersToPresentColumns(headers, techStackNames);
    }

    public async Task<MasterSheetParseResult> ParseFileAsync(Stream fileStream)
    {
        var teams = await _masterDataCacheService.GetTeamsAsync();
        var teamByName = teams.ToDictionary(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase);

        var skills = await _db.Skills.AsNoTracking().ToListAsync();
        var techStackIdByName = skills.ToDictionary(s => s.Name, s => s.Id, StringComparer.OrdinalIgnoreCase);

        var projects = await _masterDataCacheService.GetCapitalProjectsAsync();
        var projectKeySet = new HashSet<string>(
            projects.SelectMany(cp => cp.Keys).ToList(),
            StringComparer.OrdinalIgnoreCase);
        var artLookup = ArtHelper.BuildLookup(projects);

        var businessOutcomes = await _masterDataCacheService.GetBusinessOutcomesAsync();
        // Business Outcome is matched by Jira Id (the "Business Outcome Jira Id" column); the paired
        // "Business Outcome Name" column is read-only and never read on import.
        var boIdByJiraId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var bo in businessOutcomes)
        {
            if (!string.IsNullOrWhiteSpace(bo.JiraId))
            {
                boIdByJiraId.TryAdd(bo.JiraId.Trim(), bo.Id);
            }
        }

        var existingFeatures = await _masterDataCacheService.GetFeaturesAsync();
        var existingByJiraId = existingFeatures
            .Where(f => !string.IsNullOrWhiteSpace(f.JiraId))
            .GroupBy(f => f.JiraId!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var (headers, dataRows) = ExcelSheetReader.Read(fileStream, MasterSheetExcelExportService.MasterSheetName);
        var colMap = ExcelSheetReader.BuildColumnMap(headers);

        var present = MasterSheetExcelExportService.HeadersToPresentColumns(headers, skills.Select(s => s.Name).ToList());

        // A column is applied only when present in the file AND selected by the user.
        bool Apply(MasterSheetUploadColumn column) => present.Contains(column);

        var appliedColumns = new MasterSheetUploadColumnSelection
        {
            Columns = Enum.GetValues<MasterSheetUploadColumn>().Where(Apply).ToHashSet()
        };

        // Tech-stack columns to read (only when TechStack is applied), in file order.
        var techStackColumns = new List<(int Index, string Name, int Id)>();
        if (Apply(MasterSheetUploadColumn.TechStacks))
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var header = headers[i].Trim();
                if (techStackIdByName.TryGetValue(header, out var tsId))
                {
                    techStackColumns.Add((i, header, tsId));
                }
            }
        }

        var rows = new List<MasterSheetUploadRow>();

        foreach (var dataRow in dataRows)
        {
            var jiraId = MasterSheetExcelExportService.Norm(MasterSheetExcelExportService.GetCell(dataRow, colMap, MasterSheetUploadColumn.FeatureJiraId));

            Feature? existing = null;
            if (!string.IsNullOrWhiteSpace(jiraId))
            {
                existingByJiraId.TryGetValue(jiraId, out existing);
            }

            var row = new MasterSheetUploadRow
            {
                ExistingFeatureId = existing?.Id,
                IsNew = existing is null,
                AppliedColumns = appliedColumns,
                JiraId = jiraId,
                CurrentJiraId = existing?.JiraId,
            };

            // --- Project Key (locked when the existing record already has one) ---
            if (Apply(MasterSheetUploadColumn.ProjectKey))
            {
                row.ProjectKey = MasterSheetExcelExportService.Norm(MasterSheetExcelExportService.GetCell(dataRow, colMap, MasterSheetUploadColumn.ProjectKey));
                row.CurrentProjectKey = existing?.ProjectKey;
                if (existing is not null && !string.IsNullOrWhiteSpace(existing.ProjectKey))
                {
                    // Requirement: an existing record whose Project is populated cannot be changed from Excel.
                    row.ProjectKeyLocked = true;
                    row.ProjectKey = existing.ProjectKey;
                    row.ProjectKeyChanged = false;
                }
                else
                {
                    row.ProjectKeyChanged = existing is not null && MasterSheetExcelExportService.TextDiffers(existing.ProjectKey, row.ProjectKey);
                }
            }
            else
            {
                row.CurrentProjectKey = existing?.ProjectKey;
            }

            if (Apply(MasterSheetUploadColumn.FeatureName))
            {
                row.FeatureName = MasterSheetExcelExportService.Norm(MasterSheetExcelExportService.GetCell(dataRow, colMap, MasterSheetUploadColumn.FeatureName));
                row.CurrentFeatureName = existing?.Name;
                row.FeatureNameChanged = existing is not null && MasterSheetExcelExportService.TextDiffers(existing.Name, row.FeatureName);
            }
            else
            {
                row.CurrentFeatureName = existing?.Name;
            }

            if (Apply(MasterSheetUploadColumn.FeatureSummary))
            {
                row.Summary = MasterSheetExcelExportService.Norm(MasterSheetExcelExportService.GetCell(dataRow, colMap, MasterSheetUploadColumn.FeatureSummary));
                row.CurrentSummary = existing?.Summary;
                row.SummaryChanged = existing is not null && MasterSheetExcelExportService.TextDiffers(existing.Summary, row.Summary);
            }
            else
            {
                row.CurrentSummary = existing?.Summary;
            }

            if (Apply(MasterSheetUploadColumn.ConfidencePercentage))
            {
                row.ConfidencePercentageRaw = MasterSheetExcelExportService.Norm(MasterSheetExcelExportService.GetCell(dataRow, colMap, MasterSheetUploadColumn.ConfidencePercentage));
                row.ConfidencePercentage = MasterSheetExcelExportService.ParseRanking(row.ConfidencePercentageRaw);
                row.CurrentConfidencePercentage = existing?.ConfidencePercentage;
                row.ConfidencePercentageChanged = existing is not null && existing.ConfidencePercentage != row.ConfidencePercentage;
            }
            else
            {
                row.CurrentConfidencePercentage = existing?.ConfidencePercentage;
            }

            if (Apply(MasterSheetUploadColumn.PI))
            {
                row.PIIdRaw = MasterSheetExcelExportService.Norm(MasterSheetExcelExportService.GetCell(dataRow, colMap, MasterSheetUploadColumn.PI));
                row.PIId = MasterSheetExcelExportService.ParseRanking(row.PIIdRaw);
                row.CurrentPIId = existing?.PiId;
                row.PIIdChanged = existing is not null && existing.PiId != row.PIId;
            }
            else
            {
                row.CurrentPIId = existing?.PiId;
            }

            // --- Business Outcome (matched by Jira Id; the Name column is read-only and ignored) ---
            row.CurrentBusinessOutcome = existing?.BusinessOutcome?.JiraId;
            if (Apply(MasterSheetUploadColumn.BusinessOutcome))
            {
                var boJiraId = MasterSheetExcelExportService.ExtractBusinessOutcomeJiraId(MasterSheetExcelExportService.GetCell(dataRow, colMap, MasterSheetUploadColumn.BusinessOutcome));
                row.BusinessOutcome = boJiraId;
                if (!string.IsNullOrWhiteSpace(boJiraId))
                {
                    if (boIdByJiraId.TryGetValue(boJiraId, out var boId))
                    {
                        row.BusinessOutcomeId = boId;
                    }
                    else
                    {
                        row.ValidationErrors[nameof(MasterSheetUploadRow.BusinessOutcome)] =
                            $"Business Outcome '{boJiraId}' not found";
                    }
                }
                row.BusinessOutcomeChanged = existing is not null
                    && existing.BusinessOutcomeId != row.BusinessOutcomeId;
            }

            if (Apply(MasterSheetUploadColumn.FeatureLabels))
            {
                row.Labels = MasterSheetExcelExportService.Norm(MasterSheetExcelExportService.GetCell(dataRow, colMap, MasterSheetUploadColumn.FeatureLabels));
                row.CurrentLabels = existing?.Labels;
                row.LabelsChanged = existing is not null && MasterSheetExcelExportService.TextDiffers(existing.Labels, row.Labels);
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
            row.CurrentTeam = string.Join(MasterSheetExcelExportService.MultiValueSeparator, currentTeamNames);
            if (Apply(MasterSheetUploadColumn.Team))
            {
                row.Team = MasterSheetExcelExportService.Norm(MasterSheetExcelExportService.GetCell(dataRow, colMap, MasterSheetUploadColumn.Team));
                var uploadedTeamNames = MasterSheetExcelExportService.SplitMulti(row.Team);
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
                row.TeamChanged = existing is not null && MasterSheetExcelExportService.SetDiffers(uploadedTeamNames, currentTeamNames);
            }

            row.CurrentTargetEnd = existing?.TargetEnd;
            if (Apply(MasterSheetUploadColumn.TargetEndDate))
            {
                row.TargetEndRaw = MasterSheetExcelExportService.Norm(MasterSheetExcelExportService.GetCell(dataRow, colMap, MasterSheetUploadColumn.TargetEndDate));
                row.TargetEnd = MasterSheetExcelExportService.ParseDate(row.TargetEndRaw);
                row.TargetEndChanged = existing is not null && MasterSheetExcelExportService.DateDiffers(existing.TargetEnd, row.TargetEnd);
            }

            row.CurrentStoryPoints = existing?.StoryPoints;
            if (Apply(MasterSheetUploadColumn.EstimatedDays))
            {
                row.StoryPointsRaw = MasterSheetExcelExportService.Norm(MasterSheetExcelExportService.GetCell(dataRow, colMap, MasterSheetUploadColumn.EstimatedDays));
                row.StoryPoints = MasterSheetExcelExportService.ParseRanking(row.StoryPointsRaw);
                row.StoryPointsChanged = existing is not null && existing.StoryPoints != row.StoryPoints;
            }

            if (Apply(MasterSheetUploadColumn.Dependencies))
            {
                row.Dependencies = MasterSheetExcelExportService.Norm(MasterSheetExcelExportService.GetCell(dataRow, colMap, MasterSheetUploadColumn.Dependencies));
                row.CurrentDependencies = existing?.Dependencies;
                row.DependenciesChanged = existing is not null && MasterSheetExcelExportService.TextDiffers(existing.Dependencies, row.Dependencies);
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
                    var raw = MasterSheetExcelExportService.Norm(index < dataRow.Count ? dataRow[index] : null);
                    existingTs.TryGetValue(tsId, out var oldEffort);

                    var item = new MasterSheetTechStackUploadItem
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

            MasterSheetExcelExportService.ValidateExcelUploadRow(row, projectKeySet);

            if (existing is not null && (row.LabelsChanged || row.ProjectKeyChanged))
            {
                var after = artLookup.Matcher.Match(
                    row.ProjectKeyChanged ? row.ProjectKey : existing.ProjectKey,
                    existing.JiraId,
                    row.LabelsChanged ? row.Labels : existing.Labels,
                    existing.Components);
                row.ArtMove = ArtHelper.DescribeMove(artLookup.Matcher.Match(existing), after, artLookup);
            }

            rows.Add(row);
        }

        return new MasterSheetParseResult
        {
            Rows = rows,
            TechStackNames = techStackColumns.Select(c => c.Name).ToList(),
            AppliedColumns = appliedColumns
        };
    }

    public async Task<Result> UpsertFromUploadAsync(MasterSheetUploadData data)
    {
        var userName = _auditUser.GetCurrentUserName();

        bool Has(MasterSheetUploadColumn column) => data.AppliedColumns.Includes(column);

        Feature feature;
        if (data.ExistingFeatureId.HasValue)
        {
            feature = await _db.Features
                .Include(f => f.FeatureTeams)
                .Include(f => f.FeatureSkills)
                .FirstOrDefaultAsync(f => f.Id == data.ExistingFeatureId.Value)
                ?? throw new KeyNotFoundException($"Feature {data.ExistingFeatureId.Value} not found.");

            feature.JiraId = data.JiraId?.Trim();

            // An existing record whose Project is already populated cannot be changed from Excel.
            if (Has(MasterSheetUploadColumn.ProjectKey) && string.IsNullOrWhiteSpace(feature.ProjectKey))
            {
                feature.ProjectKey = data.ProjectKey?.Trim();
            }
            if (Has(MasterSheetUploadColumn.FeatureSummary) && !string.IsNullOrWhiteSpace(data.Summary))
            {
                feature.Summary = data.Summary.Trim();
            }
            if (Has(MasterSheetUploadColumn.FeatureName))
            {
                feature.Name = data.FeatureName?.Trim();
            }
            if (Has(MasterSheetUploadColumn.BusinessOutcome))
            {
                feature.BusinessOutcomeId = data.BusinessOutcomeId;
            }
            if (Has(MasterSheetUploadColumn.FeatureLabels))
            {
                feature.Labels = data.Labels?.Trim();
            }
            if (Has(MasterSheetUploadColumn.TargetEndDate))
            {
                feature.TargetEnd = data.TargetEnd;
            }
            if (Has(MasterSheetUploadColumn.EstimatedDays))
            {
                feature.StoryPoints = data.StoryPoints;
            }
            if (Has(MasterSheetUploadColumn.Dependencies))
            {
                feature.Dependencies = data.Dependencies?.Trim();
            }
            if (data.ConnectToJira)
            {
                feature.IsLinkedToTheJira = true;
            }
            feature.ModifiedBy = userName;
            feature.ModifiedAt = DateTime.UtcNow;
        }
        else
        {
            feature = new Feature
            {
                JiraId = data.JiraId?.Trim(),
                ProjectKey = Has(MasterSheetUploadColumn.ProjectKey) ? data.ProjectKey?.Trim() : null,
                IssueType = JiraIssueTypes.Feature,
                Name = Has(MasterSheetUploadColumn.FeatureName) ? data.FeatureName?.Trim() : null,
                Summary = data.Summary?.Trim(),
                BusinessOutcomeId = Has(MasterSheetUploadColumn.BusinessOutcome) ? data.BusinessOutcomeId : null,
                Labels = Has(MasterSheetUploadColumn.FeatureLabels) ? data.Labels?.Trim() : null,
                // Default the status to "Backlog" when none is supplied on a new feature.
                Status = FeatureUploadData.DefaultStatus,
                TargetEnd = Has(MasterSheetUploadColumn.TargetEndDate) ? data.TargetEnd : null,
                StoryPoints = Has(MasterSheetUploadColumn.EstimatedDays) ? data.StoryPoints : null,
                Dependencies = Has(MasterSheetUploadColumn.Dependencies) ? data.Dependencies?.Trim() : null,
                IsLinkedToTheJira = data.ConnectToJira ? true : null,
                ModifiedBy = userName,
                ModifiedAt = DateTime.UtcNow
            };
            _db.Features.Add(feature);
            await _db.SaveChangesAsync(); // get feature.Id

            feature = await _db.Features
                .Include(f => f.FeatureTeams)
                .Include(f => f.FeatureSkills)
                .FirstAsync(f => f.Id == feature.Id);
        }

        if (Has(MasterSheetUploadColumn.Team))
        {
            _db.FeatureTeams.RemoveRange(feature.FeatureTeams);
            foreach (var teamId in data.TeamIds.Distinct())
            {
                _db.FeatureTeams.Add(new FeatureTeam { FeatureId = feature.Id, TeamId = teamId });
            }
        }

        // data.TechStackEfforts is keyed by SkillId now.
        var existingTs = feature.FeatureSkills.ToDictionary(fs => fs.SkillId);

        foreach (var (skillId, value) in data.TechStackEfforts)
        {
            if (value.HasValue)
            {
                if (existingTs.TryGetValue(skillId, out var existing))
                {
                    existing.Value = value.Value;
                }
                else
                {
                    _db.FeatureSkills.Add(new FeatureSkill
                    {
                        FeatureId = feature.Id,
                        SkillId = skillId,
                        Value = value.Value
                    });
                }
            }
            else
            {
                // Empty cell: remove if it existed
                if (existingTs.TryGetValue(skillId, out var toRemove))
                {
                    _db.FeatureSkills.Remove(toRemove);
                }
            }
        }

        await _db.SaveChangesAsync();
        return Result.Success();
    }
}

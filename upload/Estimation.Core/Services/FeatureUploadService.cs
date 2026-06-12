using Estimation.Core.Models;
using Estimation.Excel;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.Services;

/// <summary>
/// Result of parsing a feature upload: the parsed rows plus the tech-stack columns that were
/// present in the file (and applied), used to render the preview's dynamic tech-stack columns.
/// </summary>
public class FeatureParseResult
{
    public List<FeatureUploadRow> Rows { get; set; } = new();
    public List<string> TechStackNames { get; set; } = new();
    public FeatureUploadColumnSelection AppliedColumns { get; set; } = new();
}

public interface IFeatureUploadService
{
    /// <summary>Lookup lists backing the export's dropdowns (project keys, teams, statuses, BOs, tech stacks).</summary>
    Task<FeatureExportLookups> GetExportLookupsAsync();

    /// <summary>
    /// Exports the features identified by <paramref name="filter"/> (the current list selection),
    /// including only the columns selected in <paramref name="selection"/>.
    /// </summary>
    Task<byte[]> ExportFilteredAsync(FeatureUploadColumnSelection selection, FeatureExportFilter filter);

    /// <summary>Reads only the header row of an upload and returns which logical columns are present.</summary>
    Task<HashSet<FeatureUploadColumn>> DetectColumnsAsync(Stream fileStream);

    /// <summary>Parses an upload, applying only the columns the user selected, with DB change detection.</summary>
    Task<FeatureParseResult> ParseFileAsync(Stream fileStream, FeatureUploadColumnSelection selection);
}

public class FeatureUploadService : IFeatureUploadService
{
    private const string SheetName = "Features";

    private readonly IDbContextFactory<EstimationDbContext> _contextFactory;

    public FeatureUploadService(IDbContextFactory<EstimationDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    // ── Export ──

    public async Task<FeatureExportLookups> GetExportLookupsAsync()
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await BuildLookupsAsync(db);
    }

    public async Task<byte[]> ExportFilteredAsync(FeatureUploadColumnSelection selection, FeatureExportFilter filter)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();

        var lookups = await BuildLookupsAsync(db);

        var query = db.Features
            .Include(f => f.BusinessOutcome)
            .Include(f => f.RequirementStatus)
            .Include(f => f.FeatureTeams).ThenInclude(ft => ft.Team)
            .Include(f => f.FeatureTechnologyStacks).ThenInclude(fts => fts.TechnologyStack)
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
            // Preserve the order the list passed in.
            features = filter.FeatureIds
                .Where(byId.ContainsKey)
                .Select(id => byId[id])
                .ToList();
        }

        var rows = features.Select(f =>
        {
            var tsByName = f.FeatureTechnologyStacks
                .Where(fts => fts.TechnologyStack != null)
                .ToDictionary(fts => fts.TechnologyStack.Name, fts => fts.EstimatedEffort, StringComparer.OrdinalIgnoreCase);

            var efforts = lookups.TechStackNames
                .Select(tsName => tsByName.TryGetValue(tsName, out var eff) ? eff : null)
                .ToList();

            var bo = f.BusinessOutcome;

            return new FeatureExportRow
            {
                ProjectKey = f.ProjectKey,
                JiraId = f.JiraId,
                FeatureName = f.Name,
                Summary = f.Summary,
                Ranking = f.Ranking,
                Description = f.Description,
                AcceptanceCriteria = f.AcceptanceCriteria,
                BusinessOutcome = bo != null
                    ? FeatureExcelExportService.FormatBusinessOutcomeDisplay(bo.JiraId, bo.Summary)
                    : null,
                Labels = f.Labels,
                Team = string.Join(FeatureExcelExportService.MultiValueSeparator,
                    f.FeatureTeams.Select(ft => ft.Team?.Name)
                        .Where(n => !string.IsNullOrWhiteSpace(n)).OrderBy(n => n)),
                Status = f.Status,
                RequirementStatus = f.RequirementStatus?.Name,
                Comments = f.Comments,
                TargetStart = f.TargetStart,
                TargetEnd = f.TargetEnd,
                DateExpected = f.DateExpected,
                StoryPoints = f.StoryPoints,
                RagExplain = f.RagExplain,
                Dependencies = f.Dependencies,
                TechStackEfforts = efforts
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

        var techStackNames = await db.TechnologyStacks.Select(t => t.Name).Distinct().OrderBy(n => n).ToListAsync();

        var requirementStatusValues = await db.RequirementStatuses
            .Select(rs => rs.Name)
            .Distinct().OrderBy(n => n).ToListAsync();

        var businessOutcomes = await db.BusinessOutcomes
            .Select(bo => new { bo.JiraId, bo.Summary })
            .ToListAsync();
        var boOptions = businessOutcomes
            .Select(bo => FeatureExcelExportService.FormatBusinessOutcomeDisplay(bo.JiraId, bo.Summary))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct().OrderBy(s => s).ToList();

        return new FeatureExportLookups
        {
            ProjectKeys = projectKeys,
            TeamNames = teamNames,
            StatusValues = statusValues,
            RequirementStatusValues = requirementStatusValues,
            BusinessOutcomeOptions = boOptions,
            TechStackNames = techStackNames
        };
    }

    // ── Column detection ──

    public async Task<HashSet<FeatureUploadColumn>> DetectColumnsAsync(Stream fileStream)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var techStackNames = await db.TechnologyStacks.Select(t => t.Name).ToListAsync();

        var (headers, _) = ExcelSheetReader.Read(fileStream, SheetName);
        return HeadersToPresentColumns(headers, techStackNames);
    }

    // ── Parse ──

    public async Task<FeatureParseResult> ParseFileAsync(Stream fileStream, FeatureUploadColumnSelection selection)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();

        var teams = await db.Teams.AsNoTracking().ToListAsync();
        var teamByName = teams.ToDictionary(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase);

        var techStacks = await db.TechnologyStacks.AsNoTracking().ToListAsync();
        var techStackIdByName = techStacks.ToDictionary(ts => ts.Name, ts => ts.Id, StringComparer.OrdinalIgnoreCase);

        var projectKeySet = new HashSet<string>(
            await db.CapitalProjects.Where(cp => cp.JiraKey != null && cp.JiraKey != "")
                .Select(cp => cp.JiraKey!).ToListAsync(),
            StringComparer.OrdinalIgnoreCase);

        var businessOutcomes = await db.BusinessOutcomes.AsNoTracking().ToListAsync();
        var boIdByDisplay = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var bo in businessOutcomes)
        {
            var display = FeatureExcelExportService.FormatBusinessOutcomeDisplay(bo.JiraId, bo.Summary);
            if (!string.IsNullOrWhiteSpace(display))
            {
                boIdByDisplay.TryAdd(display, bo.Id);
            }
        }
        var boDisplayById = businessOutcomes.ToDictionary(
            bo => bo.Id,
            bo => FeatureExcelExportService.FormatBusinessOutcomeDisplay(bo.JiraId, bo.Summary));

        var existingFeatures = await db.Features
            .Include(f => f.BusinessOutcome)
            .Include(f => f.RequirementStatus)
            .Include(f => f.FeatureTeams).ThenInclude(ft => ft.Team)
            .Include(f => f.FeatureTechnologyStacks).ThenInclude(fts => fts.TechnologyStack)
            .AsSplitQuery()
            .AsNoTracking()
            .ToListAsync();
        var existingByJiraId = existingFeatures
            .Where(f => !string.IsNullOrWhiteSpace(f.JiraId))
            .GroupBy(f => f.JiraId!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var (headers, dataRows) = ExcelSheetReader.Read(fileStream, SheetName);
        var colMap = ExcelSheetReader.BuildColumnMap(headers);

        var present = HeadersToPresentColumns(headers, techStacks.Select(t => t.Name).ToList());

        // A column is applied only when present in the file AND selected by the user.
        bool Apply(FeatureUploadColumn column) => present.Contains(column) && selection.Includes(column);

        var appliedColumns = new FeatureUploadColumnSelection
        {
            Columns = Enum.GetValues<FeatureUploadColumn>().Where(Apply).ToHashSet()
        };

        // Tech-stack columns to read (only when TechStack is applied), in file order.
        var techStackColumns = new List<(int Index, string Name, int Id)>();
        if (Apply(FeatureUploadColumn.TechStack))
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

            // --- Project Key (locked when the existing record already has one) ---
            if (Apply(FeatureUploadColumn.ProjectKey))
            {
                row.ProjectKey = Norm(GetCell(dataRow, colMap, FeatureUploadColumn.ProjectKey));
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
                    row.ProjectKeyChanged = existing is not null && TextDiffers(existing.ProjectKey, row.ProjectKey);
                }
            }
            else
            {
                row.CurrentProjectKey = existing?.ProjectKey;
            }

            // --- Feature Name ---
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

            // --- Summary ---
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

            // --- Ranking ---
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

            // --- Description (multiline-normalized comparison fixes false "changed" diffs) ---
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

            // --- Acceptance Criteria ---
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

            // --- Business Outcome ---
            var currentBoDisplay = existing?.BusinessOutcome != null
                ? FeatureExcelExportService.FormatBusinessOutcomeDisplay(existing.BusinessOutcome.JiraId, existing.BusinessOutcome.Summary)
                : null;
            row.CurrentBusinessOutcome = currentBoDisplay;
            if (Apply(FeatureUploadColumn.BusinessOutcome))
            {
                row.BusinessOutcome = Norm(GetCell(dataRow, colMap, FeatureUploadColumn.BusinessOutcome));
                if (!string.IsNullOrWhiteSpace(row.BusinessOutcome))
                {
                    if (boIdByDisplay.TryGetValue(row.BusinessOutcome, out var boId))
                    {
                        row.BusinessOutcomeId = boId;
                    }
                    else
                    {
                        row.ValidationErrors[nameof(FeatureUploadRow.BusinessOutcome)] =
                            $"Business Outcome '{row.BusinessOutcome}' not found";
                    }
                }
                row.BusinessOutcomeChanged = existing is not null
                    && existing.BusinessOutcomeId != row.BusinessOutcomeId;
            }

            // --- Labels ---
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

            // --- Team (one cell may list several teams separated by ';') ---
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

            // --- Status: an empty cell keeps the existing status, except that a feature with no
            //     status in the DB (new or existing) defaults to "Backlog". ---
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
                    row.Status = existing!.Status; // existing status present -> keep it
                }
                else
                {
                    row.Status = FeatureUploadData.DefaultStatus; // empty in DB and Excel -> Backlog
                }
                row.StatusChanged = existing is not null && TextDiffers(existing.Status, row.Status);
            }

            // --- Requirement Status (normalized lookup; an empty cell clears it, an unknown value
            //     auto-creates a new RequirementStatus on save) ---
            row.CurrentRequirementStatus = existing?.RequirementStatus?.Name;
            if (Apply(FeatureUploadColumn.RequirementStatus))
            {
                row.RequirementStatus = Norm(GetCell(dataRow, colMap, FeatureUploadColumn.RequirementStatus));
                row.RequirementStatusChanged = existing is not null
                    && TextDiffers(row.CurrentRequirementStatus, row.RequirementStatus);
            }

            // --- Comments ---
            if (Apply(FeatureUploadColumn.Comments))
            {
                row.Comments = Norm(GetCell(dataRow, colMap, FeatureUploadColumn.Comments));
                row.CurrentComments = existing?.Comments;
                row.CommentsChanged = existing is not null && TextDiffers(existing.Comments, row.Comments);
            }
            else
            {
                row.CurrentComments = existing?.Comments;
            }

            // --- Target Start ---
            row.CurrentTargetStart = existing?.TargetStart;
            if (Apply(FeatureUploadColumn.TargetStart))
            {
                row.TargetStartRaw = Norm(GetCell(dataRow, colMap, FeatureUploadColumn.TargetStart));
                row.TargetStart = ParseDate(row.TargetStartRaw);
                row.TargetStartChanged = existing is not null && DateDiffers(existing.TargetStart, row.TargetStart);
            }

            // --- Target End ---
            row.CurrentTargetEnd = existing?.TargetEnd;
            if (Apply(FeatureUploadColumn.TargetEnd))
            {
                row.TargetEndRaw = Norm(GetCell(dataRow, colMap, FeatureUploadColumn.TargetEnd));
                row.TargetEnd = ParseDate(row.TargetEndRaw);
                row.TargetEndChanged = existing is not null && DateDiffers(existing.TargetEnd, row.TargetEnd);
            }

            // --- Date Expected ---
            row.CurrentDateExpected = existing?.DateExpected;
            if (Apply(FeatureUploadColumn.DateExpected))
            {
                row.DateExpectedRaw = Norm(GetCell(dataRow, colMap, FeatureUploadColumn.DateExpected));
                row.DateExpected = ParseDate(row.DateExpectedRaw);
                row.DateExpectedChanged = existing is not null && DateDiffers(existing.DateExpected, row.DateExpected);
            }

            // --- Story Points ---
            row.CurrentStoryPoints = existing?.StoryPoints;
            if (Apply(FeatureUploadColumn.StoryPoints))
            {
                row.StoryPointsRaw = Norm(GetCell(dataRow, colMap, FeatureUploadColumn.StoryPoints));
                row.StoryPoints = ParseRanking(row.StoryPointsRaw);
                row.StoryPointsChanged = existing is not null && existing.StoryPoints != row.StoryPoints;
            }

            // --- Rag Explain ---
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

            // --- Dependencies ---
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

            // --- Tech stacks (empty cell removes an existing assignment) ---
            if (techStackColumns.Count > 0)
            {
                var existingTs = existing?.FeatureTechnologyStacks
                    .Where(fts => fts.TechnologyStack != null)
                    .ToDictionary(fts => fts.TechnologyStackId, fts => fts.EstimatedEffort)
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

    // ── Validation ──

    private static void ValidateRow(FeatureUploadRow row, HashSet<string> projectKeySet)
    {
        // A row with no Feature Jira ID creates a new feature, which requires Project Key, Feature
        // Name and Feature Summary (mirrors the mandatory fields on the Feature edit form). Any
        // missing value invalidates the row so the feature is not created.
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
        if (row.Comments?.Length > 250)
        {
            row.ValidationErrors[nameof(FeatureUploadRow.Comments)] = $"Max length 250 (current: {row.Comments.Length})";
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

    // ── Helpers ──

    private static string? GetCell(List<string> row, Dictionary<string, int> colMap, FeatureUploadColumn column)
    {
        return ExcelSheetReader.GetCell(row, colMap, FeatureExcelExportService.Headers[column]);
    }

    private static int? ParseRanking(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        // Handle "123.0" from Excel numeric cells.
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
        // Excel stores dates as serial numbers when the cell is formatted as a date.
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

    // Splits a multi-value cell ("Team A; Team B") into distinct, trimmed names.
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

    // Normalizes line endings and trims so equal text that differs only in whitespace/newlines is
    // not reported as a change (fixes Feature Description showing as changed for identical content).
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
            .ToDictionary(kvp => kvp.Value, kvp => kvp.Key, StringComparer.OrdinalIgnoreCase);
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

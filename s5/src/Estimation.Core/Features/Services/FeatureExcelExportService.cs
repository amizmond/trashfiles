using Estimation.Core.Features.Models;
using Estimation.Excel;

namespace Estimation.Core.Features.Services;

public class FeatureExportRow
{
    public string? ProjectKey { get; set; }
    public string? JiraId { get; set; }
    public string? FeatureName { get; set; }
    public string? Summary { get; set; }
    public int? Ranking { get; set; }
    public string? Description { get; set; }
    public string? AcceptanceCriteria { get; set; }

    public string? BusinessOutcome { get; set; }

    public string? BusinessOutcomeName { get; set; }

    public string? PortfolioEpic { get; set; }

    public string? PortfolioEpicName { get; set; }

    public string? StrategicObjective { get; set; }

    public string? StrategicObjectiveName { get; set; }

    public string? Labels { get; set; }
    public string? Team { get; set; }
    public string? Status { get; set; }
    public string? RequirementStatus { get; set; }
    public string? TechnicalApproval { get; set; }

    public string? PiObjective { get; set; }

    public string? Pi { get; set; }

    public string? Comments { get; set; }
    public DateTime? TargetStart { get; set; }
    public DateTime? TargetEnd { get; set; }
    public DateTime? DateExpected { get; set; }
    public int? StoryPoints { get; set; }
    public string? RagExplain { get; set; }
    public string? Dependencies { get; set; }
    public bool ExternalDependencies { get; set; }

    public string? FundingStatus { get; set; }

    public List<int?> TechStackEfforts { get; set; } = new();

    public int? TechStackEstimation { get; set; }

    public Dictionary<string, int?> TeamStoryPoints { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class FeatureExportLookups
{
    public List<string> ProjectKeys { get; set; } = new();
    public List<string> TeamNames { get; set; } = new();
    public List<string> StatusValues { get; set; } = new();
    public List<string> RequirementStatusValues { get; set; } = new();
    public List<string> TechnicalApprovalValues { get; set; } = new();
    public List<string> PiObjectiveValues { get; set; } = new();

    public List<string> FundingStatusValues { get; set; } = new();

    public List<(string JiraId, string Name)> BusinessOutcomes { get; set; } = new();

    public List<(string JiraId, string Name)> PortfolioEpics { get; set; } = new();

    public List<(string JiraId, string Name)> StrategicObjectives { get; set; } = new();

    public List<string> TechStackNames { get; set; } = new();
}

public static class FeatureExcelExportService
{
    private const string FeaturesSheetName = "Features";
    private const string StaticDataSheetName = "Static Data";

    public static readonly IReadOnlyDictionary<FeatureUploadColumn, string> Headers =
        new Dictionary<FeatureUploadColumn, string>
        {
            [FeatureUploadColumn.ProjectKey] = "Project Key",
            [FeatureUploadColumn.JiraId] = "Feature Jira ID",
            [FeatureUploadColumn.FeatureName] = "Feature Name",
            [FeatureUploadColumn.Summary] = "Feature Summary",
            [FeatureUploadColumn.Ranking] = "Ranking",
            [FeatureUploadColumn.Description] = "Feature Description",
            [FeatureUploadColumn.AcceptanceCriteria] = "Acceptance Criteria",
            [FeatureUploadColumn.BusinessOutcome] = "Business Outcome Jira Id",
            [FeatureUploadColumn.BusinessOutcomeName] = "Business Outcome Name",
            [FeatureUploadColumn.PortfolioEpic] = "Portfolio Epic Jira Id",
            [FeatureUploadColumn.PortfolioEpicName] = "Portfolio Epic Name",
            [FeatureUploadColumn.StrategicObjective] = "Strategic Objective Jira Id",
            [FeatureUploadColumn.StrategicObjectiveName] = "Strategic Objective Name",
            [FeatureUploadColumn.Labels] = "Labels",
            [FeatureUploadColumn.Team] = "GFED Teams",
            [FeatureUploadColumn.Status] = "Status",
            [FeatureUploadColumn.RequirementStatus] = "Requirement Status",
            [FeatureUploadColumn.TechnicalApproval] = "Design Approval",
            [FeatureUploadColumn.FundingStatus] = "Funding Status",
            [FeatureUploadColumn.PiObjective] = "Pi Objective",
            [FeatureUploadColumn.Pi] = "Feature PI",
            [FeatureUploadColumn.Comments] = "Comments",
            [FeatureUploadColumn.TargetStart] = "Target Start",
            [FeatureUploadColumn.TargetEnd] = "Target End",
            [FeatureUploadColumn.DateExpected] = "Date Expected",
            [FeatureUploadColumn.StoryPoints] = "Story Points",
            [FeatureUploadColumn.TechStackEstimation] = "Tech Stack Estimation",
            [FeatureUploadColumn.RagExplain] = "Rag Explain",
            [FeatureUploadColumn.Dependencies] = "Dependencies",
            [FeatureUploadColumn.ExternalDependencies] = "External Dependencies",
        };

    public static readonly IReadOnlyDictionary<FeatureUploadColumn, string[]> HeaderAliases =
        new Dictionary<FeatureUploadColumn, string[]>
        {
            [FeatureUploadColumn.Team] = new[] { "Team" },
            [FeatureUploadColumn.BusinessOutcome] = new[] { "Business Outcome" },
        };

    public static readonly IReadOnlySet<FeatureUploadColumn> ExportOnlyColumns =
        new HashSet<FeatureUploadColumn>
        {
            FeatureUploadColumn.Pi,
            FeatureUploadColumn.Comments,
            FeatureUploadColumn.BusinessOutcomeName,
            FeatureUploadColumn.PortfolioEpic,
            FeatureUploadColumn.PortfolioEpicName,
            FeatureUploadColumn.StrategicObjective,
            FeatureUploadColumn.StrategicObjectiveName,
            FeatureUploadColumn.TechStackEstimation,
            FeatureUploadColumn.TeamStoryPoints,
        };

    private sealed record JiraRefColumn(
        FeatureUploadColumn JiraIdColumn,
        FeatureUploadColumn NameColumn,
        string JiraIdsDefinedName,
        string LookupDefinedName,
        string ErrorTitle,
        string Error);

    private static readonly JiraRefColumn[] JiraRefColumns =
    {
        new(FeatureUploadColumn.BusinessOutcome, FeatureUploadColumn.BusinessOutcomeName,
            "BusinessOutcomeJiraIdsList", "BusinessOutcomeLookup",
            "Invalid Business Outcome", "Please select a valid Business Outcome Jira Id."),
        new(FeatureUploadColumn.PortfolioEpic, FeatureUploadColumn.PortfolioEpicName,
            "PortfolioEpicJiraIdsList", "PortfolioEpicLookup",
            "Invalid Portfolio Epic", "Please select a valid Portfolio Epic Jira Id."),
        new(FeatureUploadColumn.StrategicObjective, FeatureUploadColumn.StrategicObjectiveName,
            "StrategicObjectiveJiraIdsList", "StrategicObjectiveLookup",
            "Invalid Strategic Objective", "Please select a valid Strategic Objective Jira Id."),
    };

    private static List<(string JiraId, string Name)> JiraRefPairs(FeatureUploadColumn jiraIdColumn, FeatureExportLookups lookups) =>
        jiraIdColumn switch
        {
            FeatureUploadColumn.BusinessOutcome => lookups.BusinessOutcomes,
            FeatureUploadColumn.PortfolioEpic => lookups.PortfolioEpics,
            FeatureUploadColumn.StrategicObjective => lookups.StrategicObjectives,
            _ => new()
        };

    private static string? JiraRefName(FeatureUploadColumn nameColumn, FeatureExportRow row) =>
        nameColumn switch
        {
            FeatureUploadColumn.BusinessOutcomeName => row.BusinessOutcomeName,
            FeatureUploadColumn.PortfolioEpicName => row.PortfolioEpicName,
            FeatureUploadColumn.StrategicObjectiveName => row.StrategicObjectiveName,
            _ => null
        };

    public static readonly FeatureUploadColumn[] ColumnOrder =
    {
        FeatureUploadColumn.ProjectKey,
        FeatureUploadColumn.JiraId,
        FeatureUploadColumn.FeatureName,
        FeatureUploadColumn.Summary,
        FeatureUploadColumn.Ranking,
        FeatureUploadColumn.Description,
        FeatureUploadColumn.AcceptanceCriteria,
        FeatureUploadColumn.BusinessOutcome,
        FeatureUploadColumn.BusinessOutcomeName,
        FeatureUploadColumn.PortfolioEpic,
        FeatureUploadColumn.PortfolioEpicName,
        FeatureUploadColumn.StrategicObjective,
        FeatureUploadColumn.StrategicObjectiveName,
        FeatureUploadColumn.Labels,
        FeatureUploadColumn.Team,
        FeatureUploadColumn.Status,
        FeatureUploadColumn.RequirementStatus,
        FeatureUploadColumn.TechnicalApproval,
        FeatureUploadColumn.FundingStatus,
        FeatureUploadColumn.PiObjective,
        FeatureUploadColumn.Pi,
        FeatureUploadColumn.Comments,
        FeatureUploadColumn.TargetStart,
        FeatureUploadColumn.TargetEnd,
        FeatureUploadColumn.DateExpected,
        FeatureUploadColumn.StoryPoints,
        FeatureUploadColumn.TechStackEstimation,
        FeatureUploadColumn.RagExplain,
        FeatureUploadColumn.Dependencies,
        FeatureUploadColumn.ExternalDependencies,
    };

    public const string DateFormat = "yyyy-MM-dd";

    public const string BooleanYes = "Yes";
    public const string BooleanNo = "No";

    public static readonly List<string> BooleanValues = new() { BooleanYes, BooleanNo };

    public const string MultiValueSeparator = "; ";
    public const char MultiValueSplitChar = ';';

    private static double ColumnWidthFor(FeatureUploadColumn column) => column switch
    {
        FeatureUploadColumn.ProjectKey => 15,
        FeatureUploadColumn.JiraId => 18,
        FeatureUploadColumn.FeatureName => 40,
        FeatureUploadColumn.Summary => 40,
        FeatureUploadColumn.Ranking => 10,
        FeatureUploadColumn.Description => 40,
        FeatureUploadColumn.AcceptanceCriteria => 40,
        FeatureUploadColumn.BusinessOutcome => 22,
        FeatureUploadColumn.BusinessOutcomeName => 35,
        FeatureUploadColumn.PortfolioEpic => 22,
        FeatureUploadColumn.PortfolioEpicName => 35,
        FeatureUploadColumn.StrategicObjective => 22,
        FeatureUploadColumn.StrategicObjectiveName => 35,
        FeatureUploadColumn.Labels => 30,
        FeatureUploadColumn.Team => 20,
        FeatureUploadColumn.Status => 18,
        FeatureUploadColumn.RequirementStatus => 20,
        FeatureUploadColumn.TechnicalApproval => 20,
        FeatureUploadColumn.FundingStatus => 20,
        FeatureUploadColumn.PiObjective => 30,
        FeatureUploadColumn.Pi => 18,
        FeatureUploadColumn.Comments => 60,
        FeatureUploadColumn.TargetStart => 14,
        FeatureUploadColumn.TargetEnd => 14,
        FeatureUploadColumn.DateExpected => 14,
        FeatureUploadColumn.StoryPoints => 12,
        FeatureUploadColumn.TechStackEstimation => 20,
        FeatureUploadColumn.RagExplain => 35,
        FeatureUploadColumn.Dependencies => 35,
        FeatureUploadColumn.ExternalDependencies => 20,
        _ => 18
    };

    public static byte[] GenerateFeatureExport(
        FeatureUploadColumnSelection selection,
        List<FeatureExportRow> rows,
        FeatureExportLookups lookups)
    {
        var workbook = new ExcelWorkbookBuilder();
        var sheet = workbook.AddSheet(FeaturesSheetName);

        var selectedColumns = ColumnOrder.Where(selection.Includes).ToList();
        var includeTechStacks = selection.Includes(FeatureUploadColumn.TechStack)
            && lookups.TechStackNames.Count > 0;
        var teamStoryPointNames = TeamStoryPointColumnNames(selection, rows);

        for (var i = 0; i < selectedColumns.Count; i++)
        {
            sheet.AddColumnWidth(new ColumnWidth((uint)(i + 1), ColumnWidthFor(selectedColumns[i])));
        }
        if (includeTechStacks)
        {
            sheet.AddColumnWidth(new ColumnWidth(
                (uint)(selectedColumns.Count + 1),
                (uint)(selectedColumns.Count + lookups.TechStackNames.Count),
                18));
        }

        var teamStoryPointsStart = selectedColumns.Count
            + (includeTechStacks ? lookups.TechStackNames.Count : 0);
        if (teamStoryPointNames.Count > 0)
        {
            sheet.AddColumnWidth(new ColumnWidth(
                (uint)(teamStoryPointsStart + 1),
                (uint)(teamStoryPointsStart + teamStoryPointNames.Count),
                18));
        }

        var headerTexts = selectedColumns.Select(c => Headers[c]).ToList();
        if (includeTechStacks)
        {
            headerTexts.AddRange(lookups.TechStackNames);
        }
        headerTexts.AddRange(teamStoryPointNames);
        sheet.WriteColoredHeader(headerTexts).FreezeTopRow();

        sheet.SetAutoFilter(headerTexts.Count, rows.Count);

        var nameFormulaConfig = new Dictionary<FeatureUploadColumn, (string JiraIdColumnRef, string LookupDefinedName)>();
        foreach (var block in JiraRefColumns)
        {
            var jiraIdIndex = selectedColumns.IndexOf(block.JiraIdColumn);
            if (selectedColumns.Contains(block.NameColumn)
                && jiraIdIndex >= 0
                && JiraRefPairs(block.JiraIdColumn, lookups).Count > 0)
            {
                nameFormulaConfig[block.NameColumn] = (ColumnLetter(jiraIdIndex), block.LookupDefinedName);
            }
        }

        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            var excelRowIndex = r + 2;
            var dataRow = sheet.AddRow();
            foreach (var column in selectedColumns)
            {
                WriteCell(dataRow, column, row, excelRowIndex, nameFormulaConfig);
            }

            if (includeTechStacks)
            {
                foreach (var effort in row.TechStackEfforts)
                {
                    dataRow.Number(effort);
                }
            }

            foreach (var teamName in teamStoryPointNames)
            {
                dataRow.Number(row.TeamStoryPoints.GetValueOrDefault(teamName));
            }
        }

        var lastDataRow = Math.Max(rows.Count + 2, 1000);

        WriteStaticDataAndValidations(workbook, sheet, selection, selectedColumns, lookups, lastDataRow);

        if (includeTechStacks)
        {
            for (var i = 0; i < lookups.TechStackNames.Count; i++)
            {
                sheet.AddWholeNumberValidation(selectedColumns.Count + i, 2, lastDataRow,
                    "Invalid Effort", $"Estimated Effort for {lookups.TechStackNames[i]} must be a whole number >= 0.");
            }
        }

        return workbook.ToArray();
    }

    private static List<string> TeamStoryPointColumnNames(
        FeatureUploadColumnSelection selection,
        List<FeatureExportRow> rows)
    {
        if (!selection.Includes(FeatureUploadColumn.TeamStoryPoints))
        {
            return new List<string>();
        }

        return rows
            .SelectMany(r => r.TeamStoryPoints.Keys)
            .Select(name => name.Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void WriteCell(
        ExcelRowBuilder dataRow,
        FeatureUploadColumn column,
        FeatureExportRow row,
        int excelRowIndex,
        IReadOnlyDictionary<FeatureUploadColumn, (string JiraIdColumnRef, string LookupDefinedName)> nameFormulaConfig)
    {
        switch (column)
        {
            case FeatureUploadColumn.ProjectKey:
                dataRow.Text(row.ProjectKey);
                break;
            case FeatureUploadColumn.JiraId:
                dataRow.Text(row.JiraId);
                break;
            case FeatureUploadColumn.FeatureName:
                dataRow.Text(row.FeatureName);
                break;
            case FeatureUploadColumn.Summary:
                dataRow.Text(row.Summary);
                break;
            case FeatureUploadColumn.Ranking:
                dataRow.Number(row.Ranking);
                break;
            case FeatureUploadColumn.Description:
                dataRow.Text(row.Description);
                break;
            case FeatureUploadColumn.AcceptanceCriteria:
                dataRow.Text(row.AcceptanceCriteria);
                break;
            case FeatureUploadColumn.BusinessOutcome:
                dataRow.Text(row.BusinessOutcome);
                break;
            case FeatureUploadColumn.PortfolioEpic:
                dataRow.Text(row.PortfolioEpic);
                break;
            case FeatureUploadColumn.StrategicObjective:
                dataRow.Text(row.StrategicObjective);
                break;
            case FeatureUploadColumn.BusinessOutcomeName:
            case FeatureUploadColumn.PortfolioEpicName:
            case FeatureUploadColumn.StrategicObjectiveName:
                WriteJiraRefNameCell(dataRow, column, row, excelRowIndex, nameFormulaConfig);
                break;
            case FeatureUploadColumn.Labels:
                dataRow.Text(row.Labels);
                break;
            case FeatureUploadColumn.Team:
                dataRow.Text(row.Team);
                break;
            case FeatureUploadColumn.Status:
                dataRow.Text(row.Status);
                break;
            case FeatureUploadColumn.RequirementStatus:
                dataRow.Text(row.RequirementStatus);
                break;
            case FeatureUploadColumn.TechnicalApproval:
                dataRow.Text(row.TechnicalApproval);
                break;
            case FeatureUploadColumn.FundingStatus:
                dataRow.Text(row.FundingStatus);
                break;
            case FeatureUploadColumn.PiObjective:
                dataRow.Text(row.PiObjective);
                break;
            case FeatureUploadColumn.Pi:
                dataRow.Text(row.Pi);
                break;
            case FeatureUploadColumn.Comments:
                dataRow.Text(row.Comments, wrap: true);
                break;
            case FeatureUploadColumn.TargetStart:
                dataRow.Text(row.TargetStart?.ToString(DateFormat));
                break;
            case FeatureUploadColumn.TargetEnd:
                dataRow.Text(row.TargetEnd?.ToString(DateFormat));
                break;
            case FeatureUploadColumn.DateExpected:
                dataRow.Text(row.DateExpected?.ToString(DateFormat));
                break;
            case FeatureUploadColumn.StoryPoints:
                dataRow.Number(row.StoryPoints);
                break;
            case FeatureUploadColumn.TechStackEstimation:
                dataRow.Number(row.TechStackEstimation);
                break;
            case FeatureUploadColumn.RagExplain:
                dataRow.Text(row.RagExplain);
                break;
            case FeatureUploadColumn.Dependencies:
                dataRow.Text(row.Dependencies);
                break;
            case FeatureUploadColumn.ExternalDependencies:
                dataRow.Text(row.ExternalDependencies ? BooleanYes : BooleanNo);
                break;
        }
    }

    private static void WriteJiraRefNameCell(
        ExcelRowBuilder dataRow,
        FeatureUploadColumn nameColumn,
        FeatureExportRow row,
        int excelRowIndex,
        IReadOnlyDictionary<FeatureUploadColumn, (string JiraIdColumnRef, string LookupDefinedName)> nameFormulaConfig)
    {
        var nameValue = JiraRefName(nameColumn, row);
        if (nameFormulaConfig.TryGetValue(nameColumn, out var cfg))
        {
            var formula = $"IFERROR(VLOOKUP({cfg.JiraIdColumnRef}{excelRowIndex},{cfg.LookupDefinedName},2,FALSE),\"\")";
            dataRow.Formula(formula, nameValue, derived: true);
        }
        else
        {
            dataRow.Text(nameValue, derived: true);
        }
    }

    private static void WriteStaticDataAndValidations(
        ExcelWorkbookBuilder workbook,
        ExcelSheetBuilder featuresSheet,
        FeatureUploadColumnSelection selection,
        List<FeatureUploadColumn> selectedColumns,
        FeatureExportLookups lookups,
        int lastDataRow)
    {
        var dropdowns = new List<(FeatureUploadColumn Column, List<string> Values, string DefinedName, string ErrorTitle, string Error, bool Blocking)>();

        if (selection.Includes(FeatureUploadColumn.ProjectKey) && lookups.ProjectKeys.Count > 0)
        {
            dropdowns.Add((FeatureUploadColumn.ProjectKey, lookups.ProjectKeys, "ProjectKeysList",
                "Invalid Project Key", "Please select a valid project key.", true));
        }
        if (selection.Includes(FeatureUploadColumn.Team) && lookups.TeamNames.Count > 0)
        {
            dropdowns.Add((FeatureUploadColumn.Team, lookups.TeamNames, "TeamsList",
                "Invalid Team", "Please select a valid team.", false));
        }
        if (selection.Includes(FeatureUploadColumn.Status) && lookups.StatusValues.Count > 0)
        {
            dropdowns.Add((FeatureUploadColumn.Status, lookups.StatusValues, "StatusList",
                "Invalid Status", "Please select a valid status.", false));
        }
        if (selection.Includes(FeatureUploadColumn.RequirementStatus) && lookups.RequirementStatusValues.Count > 0)
        {
            dropdowns.Add((FeatureUploadColumn.RequirementStatus, lookups.RequirementStatusValues, "RequirementStatusList",
                "Invalid Requirement Status", "Please select a valid requirement status.", false));
        }
        if (selection.Includes(FeatureUploadColumn.ExternalDependencies))
        {
            dropdowns.Add((FeatureUploadColumn.ExternalDependencies, BooleanValues, "ExternalDependenciesList",
                "Invalid External Dependencies", "Please select Yes or No.", true));
        }
        if (selection.Includes(FeatureUploadColumn.TechnicalApproval) && lookups.TechnicalApprovalValues.Count > 0)
        {
            dropdowns.Add((FeatureUploadColumn.TechnicalApproval, lookups.TechnicalApprovalValues, "TechnicalApprovalList",
                "Invalid Design Approval", "Please select a valid design approval.", true));
        }
        if (selection.Includes(FeatureUploadColumn.PiObjective) && lookups.PiObjectiveValues.Count > 0)
        {
            dropdowns.Add((FeatureUploadColumn.PiObjective, lookups.PiObjectiveValues, "PiObjectivesList",
                "Invalid Pi Objective", "Please select a valid Pi Objective.", false));
        }
        if (selection.Includes(FeatureUploadColumn.FundingStatus) && lookups.FundingStatusValues.Count > 0)
        {
            dropdowns.Add((FeatureUploadColumn.FundingStatus, lookups.FundingStatusValues, "FundingStatusList",
                "Invalid Funding Status", "Please select a valid Funding Status.", true));
        }

        var refBlocks = JiraRefColumns
            .Select(b => (Block: b, Pairs: JiraRefPairs(b.JiraIdColumn, lookups)))
            .Where(x => selection.Includes(x.Block.JiraIdColumn) && x.Pairs.Count > 0)
            .ToList();

        if (dropdowns.Count == 0 && refBlocks.Count == 0)
        {
            return;
        }

        var staticSheet = workbook.AddSheet(StaticDataSheetName);

        var totalStaticColumns = dropdowns.Count + refBlocks.Count * 2;
        for (var i = 0; i < totalStaticColumns; i++)
        {
            staticSheet.AddColumnWidth(new ColumnWidth((uint)(i + 1), 50));
        }

        var headers = dropdowns.Select(d => Headers[d.Column]).ToList();
        foreach (var (block, _) in refBlocks)
        {
            headers.Add(Headers[block.JiraIdColumn]);
            headers.Add(Headers[block.NameColumn]);
        }
        staticSheet.WriteHeader(headers);

        var genericMax = dropdowns.Count > 0 ? dropdowns.Max(d => d.Values.Count) : 0;
        var refMax = refBlocks.Count > 0 ? refBlocks.Max(x => x.Pairs.Count) : 0;
        var maxRows = Math.Max(genericMax, refMax);
        for (var r = 0; r < maxRows; r++)
        {
            var dataRow = staticSheet.AddRow();
            for (var c = 0; c < dropdowns.Count; c++)
            {
                var values = dropdowns[c].Values;
                dataRow.Text(r < values.Count ? values[r] : null, skipIfEmpty: true);
            }

            foreach (var (_, pairs) in refBlocks)
            {
                var hasPair = r < pairs.Count;
                dataRow.Text(hasPair ? pairs[r].JiraId : null, skipIfEmpty: true);
                dataRow.Text(hasPair ? pairs[r].Name : null, skipIfEmpty: true);
            }
        }

        for (var c = 0; c < dropdowns.Count; c++)
        {
            var (column, values, definedName, errorTitle, error, blocking) = dropdowns[c];
            var colRef = ColumnLetter(c);
            var lastRow = values.Count + 1;
            workbook.AddDefinedName(definedName, $"'{StaticDataSheetName}'!${colRef}$2:${colRef}${lastRow}");

            var featureColumnIndex = selectedColumns.IndexOf(column);
            if (featureColumnIndex >= 0)
            {
                featuresSheet.AddNamedListValidation(featureColumnIndex, 2, lastDataRow, definedName, errorTitle, error, blocking);
            }
        }

        for (var i = 0; i < refBlocks.Count; i++)
        {
            var (block, pairs) = refBlocks[i];
            var jiraIdColRef = ColumnLetter(dropdowns.Count + i * 2);
            var nameColRef = ColumnLetter(dropdowns.Count + i * 2 + 1);
            var lastRow = pairs.Count + 1;

            workbook.AddDefinedName(block.JiraIdsDefinedName,
                $"'{StaticDataSheetName}'!${jiraIdColRef}$2:${jiraIdColRef}${lastRow}");
            workbook.AddDefinedName(block.LookupDefinedName,
                $"'{StaticDataSheetName}'!${jiraIdColRef}$2:${nameColRef}${lastRow}");

            var featureColumnIndex = selectedColumns.IndexOf(block.JiraIdColumn);
            if (featureColumnIndex >= 0)
            {
                featuresSheet.AddNamedListValidation(featureColumnIndex, 2, lastDataRow,
                    block.JiraIdsDefinedName, block.ErrorTitle, block.Error, true);
            }
        }
    }

    private static string ColumnLetter(int zeroBasedIndex)
    {
        var result = "";
        var index = zeroBasedIndex;
        while (index >= 0)
        {
            result = (char)('A' + index % 26) + result;
            index = index / 26 - 1;
        }
        return result;
    }
}

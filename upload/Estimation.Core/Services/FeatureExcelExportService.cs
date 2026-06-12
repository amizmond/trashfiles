using Estimation.Core.Models;
using Estimation.Excel;

namespace Estimation.Core.Services;

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
    public string? Labels { get; set; }
    public string? Team { get; set; }
    public string? Status { get; set; }
    public string? RequirementStatus { get; set; }
    public string? Comments { get; set; }
    public DateTime? TargetStart { get; set; }
    public DateTime? TargetEnd { get; set; }
    public DateTime? DateExpected { get; set; }
    public int? StoryPoints { get; set; }
    public string? RagExplain { get; set; }
    public string? Dependencies { get; set; }

    /// <summary>Effort per tech stack, aligned by index with the export's tech-stack name list.</summary>
    public List<int?> TechStackEfforts { get; set; } = new();
}

/// <summary>
/// Lookup lists that back the export's dropdown columns. All dropdown values live on a single
/// hidden "Static Data" sheet (referenced by defined names) so lists can exceed Excel's 255-char
/// inline limit and tolerate commas in the values.
/// </summary>
public class FeatureExportLookups
{
    public List<string> ProjectKeys { get; set; } = new();
    public List<string> TeamNames { get; set; } = new();
    public List<string> StatusValues { get; set; } = new();
    public List<string> RequirementStatusValues { get; set; } = new();
    public List<string> BusinessOutcomeOptions { get; set; } = new();
    public List<string> TechStackNames { get; set; } = new();
}

/// <summary>
/// Excel export for Planning features. Builds its column set from the user's column selection,
/// mirroring the resources export. Kept separate from the resource/HR exports.
/// </summary>
public static class FeatureExcelExportService
{
    private const string FeaturesSheetName = "Features";
    private const string StaticDataSheetName = "Static Data";

    // Header text for each non-tech-stack column. Single source of truth for export and import.
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
            [FeatureUploadColumn.BusinessOutcome] = "Business Outcome",
            [FeatureUploadColumn.Labels] = "Labels",
            [FeatureUploadColumn.Team] = "Team",
            [FeatureUploadColumn.Status] = "Status",
            [FeatureUploadColumn.RequirementStatus] = "Requirement Status",
            [FeatureUploadColumn.Comments] = "Comments",
            [FeatureUploadColumn.TargetStart] = "Target Start",
            [FeatureUploadColumn.TargetEnd] = "Target End",
            [FeatureUploadColumn.DateExpected] = "Date Expected",
            [FeatureUploadColumn.StoryPoints] = "Story Points",
            [FeatureUploadColumn.RagExplain] = "Rag Explain",
            [FeatureUploadColumn.Dependencies] = "Dependencies",
        };

    // Fixed left-to-right order of the non-tech-stack columns in the workbook.
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
        FeatureUploadColumn.Labels,
        FeatureUploadColumn.Team,
        FeatureUploadColumn.Status,
        FeatureUploadColumn.RequirementStatus,
        FeatureUploadColumn.Comments,
        FeatureUploadColumn.TargetStart,
        FeatureUploadColumn.TargetEnd,
        FeatureUploadColumn.DateExpected,
        FeatureUploadColumn.StoryPoints,
        FeatureUploadColumn.RagExplain,
        FeatureUploadColumn.Dependencies,
    };

    // Date columns are written and parsed using this format.
    public const string DateFormat = "yyyy-MM-dd";

    // A feature can have several teams; they are written into the single Team cell separated by this.
    public const string MultiValueSeparator = "; ";
    public const char MultiValueSplitChar = ';';

    public static string FormatBusinessOutcomeDisplay(string? jiraId, string? summary)
    {
        var name = summary?.Trim() ?? "";
        var key = jiraId?.Trim() ?? "";
        if (key.Length == 0)
        {
            return name;
        }

        return $"{key} - {name}";
    }

    private static double ColumnWidthFor(FeatureUploadColumn column) => column switch
    {
        FeatureUploadColumn.ProjectKey => 15,
        FeatureUploadColumn.JiraId => 18,
        FeatureUploadColumn.FeatureName => 40,
        FeatureUploadColumn.Summary => 40,
        FeatureUploadColumn.Ranking => 10,
        FeatureUploadColumn.Description => 40,
        FeatureUploadColumn.AcceptanceCriteria => 40,
        FeatureUploadColumn.BusinessOutcome => 35,
        FeatureUploadColumn.Labels => 30,
        FeatureUploadColumn.Team => 20,
        FeatureUploadColumn.Status => 18,
        FeatureUploadColumn.RequirementStatus => 20,
        FeatureUploadColumn.Comments => 25,
        FeatureUploadColumn.TargetStart => 14,
        FeatureUploadColumn.TargetEnd => 14,
        FeatureUploadColumn.DateExpected => 14,
        FeatureUploadColumn.StoryPoints => 12,
        FeatureUploadColumn.RagExplain => 35,
        FeatureUploadColumn.Dependencies => 35,
        _ => 18
    };

    public static byte[] GenerateFeatureExport(
        FeatureUploadColumnSelection selection,
        List<FeatureExportRow> rows,
        FeatureExportLookups lookups)
    {
        var workbook = new ExcelWorkbookBuilder();
        var sheet = workbook.AddSheet(FeaturesSheetName);

        // Resolve the ordered list of selected non-tech-stack columns.
        var selectedColumns = ColumnOrder.Where(selection.Includes).ToList();
        var includeTechStacks = selection.Includes(FeatureUploadColumn.TechStack)
            && lookups.TechStackNames.Count > 0;

        // Column widths: each selected fixed column, then a single range for the tech-stack columns.
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

        // Header row.
        var headerTexts = selectedColumns.Select(c => Headers[c]).ToList();
        if (includeTechStacks)
        {
            headerTexts.AddRange(lookups.TechStackNames);
        }
        sheet.WriteHeader(headerTexts);

        // Data rows.
        foreach (var row in rows)
        {
            var dataRow = sheet.AddRow();
            foreach (var column in selectedColumns)
            {
                WriteCell(dataRow, column, row);
            }

            if (includeTechStacks)
            {
                foreach (var effort in row.TechStackEfforts)
                {
                    dataRow.Number(effort);
                }
            }
        }

        var lastDataRow = Math.Max(rows.Count + 2, 1000);

        // All dropdown sources live on the Static Data sheet and are referenced by defined names.
        WriteStaticDataAndValidations(workbook, sheet, selection, selectedColumns, lookups, lastDataRow);

        // Tech-stack effort columns: whole number >= 0.
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

    private static void WriteCell(ExcelRowBuilder dataRow, FeatureUploadColumn column, FeatureExportRow row)
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
            case FeatureUploadColumn.Comments:
                dataRow.Text(row.Comments);
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
            case FeatureUploadColumn.RagExplain:
                dataRow.Text(row.RagExplain);
                break;
            case FeatureUploadColumn.Dependencies:
                dataRow.Text(row.Dependencies);
                break;
        }
    }

    // Writes one column per dropdown source onto the Static Data sheet, registers a defined name for
    // each, and attaches a named-list validation to the matching column on the Features sheet.
    private static void WriteStaticDataAndValidations(
        ExcelWorkbookBuilder workbook,
        ExcelSheetBuilder featuresSheet,
        FeatureUploadColumnSelection selection,
        List<FeatureUploadColumn> selectedColumns,
        FeatureExportLookups lookups,
        int lastDataRow)
    {
        // (Feature column that uses a dropdown, its values, defined name, validation messages, blocking).
        // Project Key / Business Outcome / Team must match existing DB records, so their dropdowns are
        // restrictive. Status is free text — its dropdown only suggests existing values, so it is
        // non-blocking and the user can type any new status.
        var dropdowns = new List<(FeatureUploadColumn Column, List<string> Values, string DefinedName, string ErrorTitle, string Error, bool Blocking)>();

        if (selection.Includes(FeatureUploadColumn.ProjectKey) && lookups.ProjectKeys.Count > 0)
        {
            dropdowns.Add((FeatureUploadColumn.ProjectKey, lookups.ProjectKeys, "ProjectKeysList",
                "Invalid Project Key", "Please select a valid project key.", true));
        }
        if (selection.Includes(FeatureUploadColumn.BusinessOutcome) && lookups.BusinessOutcomeOptions.Count > 0)
        {
            dropdowns.Add((FeatureUploadColumn.BusinessOutcome, lookups.BusinessOutcomeOptions, "BusinessOutcomesList",
                "Invalid Business Outcome", "Please select a valid Business Outcome.", true));
        }
        if (selection.Includes(FeatureUploadColumn.Team) && lookups.TeamNames.Count > 0)
        {
            // Non-blocking: a feature can have several teams, entered as "Team A; Team B" in one cell,
            // so the dropdown only suggests team names rather than restricting the cell to one value.
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
            // Non-blocking: the dropdown suggests existing requirement statuses, but the user can type
            // a new value, which is created on save (mirrors the feature edit form's autocomplete).
            dropdowns.Add((FeatureUploadColumn.RequirementStatus, lookups.RequirementStatusValues, "RequirementStatusList",
                "Invalid Requirement Status", "Please select a valid requirement status.", false));
        }

        if (dropdowns.Count == 0)
        {
            return;
        }

        var staticSheet = workbook.AddSheet(StaticDataSheetName);

        for (var i = 0; i < dropdowns.Count; i++)
        {
            staticSheet.AddColumnWidth(new ColumnWidth((uint)(i + 1), 50));
        }

        // Header row for the static-data columns.
        staticSheet.WriteHeader(dropdowns.Select(d => Headers[d.Column]));

        // Write the values column-by-column. Each AddRow only appends to the columns it touches, so
        // build rows up to the longest list and place each dropdown's value in its own column index.
        var maxRows = dropdowns.Max(d => d.Values.Count);
        for (var r = 0; r < maxRows; r++)
        {
            var dataRow = staticSheet.AddRow();
            for (var c = 0; c < dropdowns.Count; c++)
            {
                var values = dropdowns[c].Values;
                dataRow.Text(r < values.Count ? values[r] : null, skipIfEmpty: true);
            }
        }

        // Defined names + validations.
        for (var c = 0; c < dropdowns.Count; c++)
        {
            var (column, values, definedName, errorTitle, error, blocking) = dropdowns[c];
            var colRef = ColumnLetter(c);
            var lastRow = values.Count + 1; // +1 for the header row
            workbook.AddDefinedName(definedName, $"'{StaticDataSheetName}'!${colRef}$2:${colRef}${lastRow}");

            var featureColumnIndex = selectedColumns.IndexOf(column);
            if (featureColumnIndex >= 0)
            {
                featuresSheet.AddNamedListValidation(featureColumnIndex, 2, lastDataRow, definedName, errorTitle, error, blocking);
            }
        }
    }

    /// <summary>Converts a zero-based column index to its Excel column letters (0 =&gt; "A", 26 =&gt; "AA").</summary>
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

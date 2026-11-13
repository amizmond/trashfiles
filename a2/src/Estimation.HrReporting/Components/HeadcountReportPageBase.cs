using Estimation.HrReporting.Models;
using Estimation.HrReporting.Services;
using Estimation.Services.Shared;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Serilog;

namespace Estimation.HrReporting.Components;

public abstract class HeadcountReportPageBase : HrReportingPageBase
{
    [Inject]
    protected IHeadcountSnapshotService Snapshots { get; set; } = null!;

    [Inject]
    protected NavigationManager Navigation { get; set; } = null!;

    [Inject]
    protected ISnackbar Snackbar { get; set; } = null!;

    [Inject]
    protected IBrowserDownloadService Downloads { get; set; } = null!;

    [SupplyParameterFromQuery(Name = "upload")]
    public int? RequestedUploadId { get; set; }

    protected List<HeadcountUpload> Uploads { get; private set; } = [];

    protected HeadcountUpload? Current { get; private set; }

    protected IReadOnlyList<HeadcountRow> Rows { get; private set; } = [];

    protected List<HeadcountRow> Filtered { get; private set; } = [];

    protected HeadcountFilter Filter { get; } = new();

    protected Dictionary<HeadcountColumn, Dictionary<string, int>>? Facets { get; private set; }

    protected bool DrawerOpen { get; set; }

    protected bool Loading { get; private set; } = true;

    protected bool InitialLoadDone { get; private set; }

    protected bool MissingRequested { get; private set; }

    protected bool Exporting { get; set; }

    private bool _uploadsLoaded;
    private bool _defaultsApplied;
    private int? _shownUploadId;

    protected abstract string Route { get; }

    protected abstract void ApplyDefaultFilter();

    protected abstract void Recompute();

    protected override async Task OnParametersSetAsync()
    {
        try
        {
            if (!_uploadsLoaded)
            {
                Uploads = await Snapshots.GetUploadsAsync();
                _uploadsLoaded = true;
                await OnUploadsLoadedAsync();
            }

            var target = RequestedUploadId is int id ? Uploads.FirstOrDefault(u => u.Id == id) : null;
            MissingRequested = RequestedUploadId is not null && target is null;
            target ??= Uploads.FirstOrDefault();

            if (target is null || target.Id != _shownUploadId)
            {
                Loading = true;
                Current = target;
                Rows = target is null ? [] : await Snapshots.GetRowsAsync(target.Id);
                _shownUploadId = target?.Id;

                if (!_defaultsApplied && Rows.Count > 0)
                {
                    ApplyDefaultFilter();
                    _defaultsApplied = true;
                }

                Refilter();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load the headcount for {Route}", Route);
            Snackbar.Add("The headcount could not be loaded.", Severity.Error);
        }
        finally
        {
            Loading = false;
            InitialLoadDone = true;
        }
    }

    protected virtual Task OnUploadsLoadedAsync() => Task.CompletedTask;

    protected void Refilter()
    {
        Filtered = HeadcountQuery.Apply(Rows, Filter);
        Facets = DrawerOpen ? HeadcountQuery.Facets(Rows, Filter) : null;
        Recompute();
    }

    protected void SelectUpload(int uploadId) =>
        Navigation.NavigateTo(HrReportingRoutes.WithUpload(
            Route, Uploads.Count > 0 && Uploads[0].Id == uploadId ? null : uploadId));

    protected void OpenFilters()
    {
        Facets ??= HeadcountQuery.Facets(Rows, Filter);
        DrawerOpen = true;
    }

    protected Task OnFilterChangedAsync()
    {
        Refilter();
        Facets ??= HeadcountQuery.Facets(Rows, Filter);
        return Task.CompletedTask;
    }

    protected void SetDefault(HeadcountColumn column, string value)
    {
        var present = Rows.Select(column.Format).FirstOrDefault(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));
        if (present is not null)
        {
            Filter.For(column).Values = [present];
        }
    }

    protected string FilterSummary
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Filter.Search))
            {
                parts.Add($"search \"{Filter.Search}\"");
            }

            foreach (var column in HeadcountColumns.Filterable)
            {
                if (!Filter.Columns.TryGetValue(column, out var f) || !f.IsActive)
                {
                    continue;
                }

                var values = f.Values.Count == 0 ? "(empty)" : string.Join(", ", f.Values);
                parts.Add(f.Not ? $"{column.Header} not {values}" : $"{column.Header}: {values}");
            }

            return parts.Count == 0 ? "no filters" : string.Join(" · ", parts);
        }
    }

    protected async Task DownloadAsync(Func<byte[]> build, string fileName)
    {
        Exporting = true;
        try
        {
            var bytes = await Task.Run(build);
            await Downloads.DownloadAsync(bytes, fileName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Export of {FileName} failed", fileName);
            Snackbar.Add("The export failed.", Severity.Error);
        }
        finally
        {
            Exporting = false;
        }
    }
}

using Estimation.Core.Administration.Services;
using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.PlanningIncrement.Services;
using Estimation.Core.Shared.Services;
using Estimation.Core.Train.Models;
using Estimation.Core.Train.Services;
using Estimation.Services.Administration;
using Estimation.Services.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Estimation.Components.FeatureHygiene;

/// <summary>
/// ART and PI selection shared by the hygiene report and the rules page: ARTs with a Jira key the
/// user may view, PIs newest first, the last choice remembered in the browser, the global filter
/// as the first-visit default, and edit rights resolved for the selected ART.
/// </summary>
public abstract class FeatureHygieneScopedPageBase : ComponentBase
{
    public const string PageKey = "FeatureHygiene";

    private const string ArtStorageKey = "featureHygiene.selectedArtId";
    private const string PiStorageKey = "featureHygiene.selectedPiId";

    [Inject] protected ICapitalProjectService CapitalProjects { get; set; } = null!;

    [Inject] protected IPiService PiService { get; set; } = null!;

    [Inject] protected IUserPermissionService Permissions { get; set; } = null!;

    [Inject] protected IWindowsAuthService WindowsAuth { get; set; } = null!;

    [Inject] protected GlobalFilterState GlobalFilter { get; set; } = null!;

    [Inject] protected IJSRuntime JS { get; set; } = null!;

    protected List<CapitalProject> Arts { get; private set; } = new();

    protected List<Pi> Pis { get; private set; } = new();

    protected int? ArtId { get; private set; }

    protected int? PiId { get; private set; }

    protected bool CanEdit { get; private set; }

    /// <summary>False until the remembered selection has been restored after the first render.</summary>
    protected bool ScopeReady { get; private set; }

    protected CapitalProject? SelectedArt => Arts.FirstOrDefault(a => a.Id == ArtId);

    protected Pi? SelectedPi => Pis.FirstOrDefault(p => p.Id == PiId);

    private UserPermissionSet _permissions = UserPermissionSet.Empty;
    private bool _restored;

    protected abstract Task OnScopeChangedAsync();

    protected override async Task OnInitializedAsync()
    {
        var userName = await WindowsAuth.GetUserName();
        _permissions = userName is null
            ? UserPermissionSet.Empty
            : await Permissions.GetPermissionSetAsync(userName);

        var arts = await CapitalProjects.GetAllLightAsync();
        Arts = arts
            .Where(a => !string.IsNullOrWhiteSpace(a.JiraKey))
            .Where(a => _permissions.CanView(PageKey, TrainScope.ForTrains([a.Id])))
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var pis = await PiService.GetAllLightAsync();
        Pis = pis
            .OrderByDescending(p => p.StartDate ?? DateTime.MinValue)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || _restored)
        {
            return;
        }

        _restored = true;

        var storedArt = await ReadStoredIntAsync(ArtStorageKey);
        var storedPi = await ReadStoredIntAsync(PiStorageKey);
        var (globalArt, globalPi) = await GlobalFilterDefaultsAsync();

        ArtId = FirstSelectable(Arts.Select(a => a.Id), storedArt, globalArt) ?? Arts.FirstOrDefault()?.Id;
        PiId = FirstSelectable(Pis.Select(p => p.Id), storedPi, globalPi) ?? CurrentPi()?.Id ?? Pis.FirstOrDefault()?.Id;

        ScopeReady = true;
        RefreshCanEdit();
        await OnScopeChangedAsync();
        StateHasChanged();
    }

    protected async Task OnArtChangedAsync(int? artId)
    {
        if (artId == ArtId)
        {
            return;
        }

        ArtId = artId;
        await WriteStoredIntAsync(ArtStorageKey, artId);
        RefreshCanEdit();
        await OnScopeChangedAsync();
    }

    protected async Task OnPiChangedAsync(int? piId)
    {
        if (piId == PiId)
        {
            return;
        }

        PiId = piId;
        await WriteStoredIntAsync(PiStorageKey, piId);
        await OnScopeChangedAsync();
    }

    protected bool CanEditArt(int artId) => _permissions.CanEdit(PageKey, TrainScope.ForTrains([artId]));

    private void RefreshCanEdit()
    {
        CanEdit = ArtId is int artId && CanEditArt(artId);
    }

    private Pi? CurrentPi()
    {
        var today = DateTime.UtcNow.Date;

        return Pis.FirstOrDefault(p =>
            p.StartDate is { } start && start.Date <= today
            && (p.EndDate is null || p.EndDate.Value.Date >= today));
    }

    private static int? FirstSelectable(IEnumerable<int> selectable, params int?[] candidates)
    {
        var allowed = selectable.ToHashSet();

        foreach (var candidate in candidates)
        {
            if (candidate is int id && allowed.Contains(id))
            {
                return id;
            }
        }

        return null;
    }

    private async Task<(int? ArtId, int? PiId)> GlobalFilterDefaultsAsync()
    {
        try
        {
            await GlobalFilter.EnsureLoadedAsync();
            var values = GlobalFilter.Values;

            return (
                values.CapitalProjectIds.Count == 1 ? values.CapitalProjectIds[0] : null,
                values.PiIds.Count == 1 ? values.PiIds[0] : null);
        }
        catch
        {
            return (null, null);
        }
    }

    private async Task<int?> ReadStoredIntAsync(string key)
    {
        try
        {
            var raw = await JS.InvokeAsync<string?>("localStorage.getItem", key);
            return int.TryParse(raw, out var value) ? value : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task WriteStoredIntAsync(string key, int? value)
    {
        try
        {
            if (value is null)
            {
                await JS.InvokeVoidAsync("localStorage.removeItem", key);
            }
            else
            {
                await JS.InvokeVoidAsync("localStorage.setItem", key, value.Value.ToString());
            }
        }
        catch
        {
            // Storage is a convenience; a browser that refuses it must not break the page.
        }
    }
}

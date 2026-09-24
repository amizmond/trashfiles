using Estimation.ArtPlanning.Data;
using Estimation.ArtPlanning.Models.Source;
using Estimation.Core.Administration.Services;
using Estimation.Core.Shared.Services;
using Estimation.Services.Administration;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace Estimation.ArtPlanning.Components;

public abstract class ArtPlanningScopedPageBase : ComponentBase
{
    protected const string SelectedArtStorageKey = "artCapacity.selectedArtId";

    [Inject] protected IDbContextFactory<ArtPlanningDbContext> DbFactory { get; set; } = null!;

    [Inject] protected IUserPermissionService Permissions { get; set; } = null!;

    [Inject] protected IWindowsAuthService WindowsAuth { get; set; } = null!;

    [Inject] protected IJSRuntime JS { get; set; } = null!;

    protected List<SourceArt> Arts { get; private set; } = new();

    protected List<SourcePi> Pis { get; private set; } = new();

    protected int? ArtId { get; private set; }

    protected int? PiId { get; private set; }

    protected bool CanEdit { get; private set; }

    private bool _restored;

    protected abstract Task OnScopeChangedAsync();

    protected virtual bool IsSelectablePi(int id) => Pis.Any(p => p.Id == id);

    protected virtual int? InitialPiId() => Pis.FirstOrDefault()?.Id;

    protected override async Task OnInitializedAsync()
    {
        await using var db = await DbFactory.CreateDbContextAsync();

        Arts = await db.CapitalProjects.AsNoTracking()
            .OrderBy(a => a.Name)
            .ToListAsync();
        Pis = await db.Pis.AsNoTracking()
            .OrderByDescending(p => p.StartDate)
            .ThenBy(p => p.Name)
            .ToListAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || _restored)
        {
            return;
        }
        _restored = true;

        var storedArt = await ReadStoredIntAsync(SelectedArtStorageKey);
        ArtId = storedArt is int art && Arts.Any(a => a.Id == art)
            ? art
            : Arts.FirstOrDefault()?.Id;

        var storedPi = await ReadStoredIntAsync(PiStorageKey);
        PiId = storedPi is int pi && IsSelectablePi(pi) ? pi : InitialPiId();

        await RefreshCanEditAsync();
        await OnScopeChangedAsync();
        StateHasChanged();
    }

    protected abstract string PiStorageKey { get; }

    protected async Task OnArtChangedAsync(int? artId)
    {
        if (artId == ArtId)
        {
            return;
        }
        ArtId = artId;
        await WriteStoredIntAsync(SelectedArtStorageKey, artId);
        await RefreshCanEditAsync();
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

    private async Task RefreshCanEditAsync()
    {
        CanEdit = ArtId is int artId && await ResolveCanEditAsync(artId);
    }

    protected async Task<bool> ResolveCanEditAsync(int artId)
    {
        var userName = await WindowsAuth.GetUserName();
        if (userName is null)
        {
            return false;
        }
        var permissions = await Permissions.GetPermissionSetAsync(userName);
        return permissions.CanEdit(ArtPlanningRoutes.PageKey, TrainScope.ForTrains(new[] { artId }));
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

using System.Globalization;
using Estimation.Core.Administration.Models;
using Estimation.HrReporting.Models;
using Estimation.Services.Shared;
using Microsoft.AspNetCore.Components;

namespace Estimation.HrReporting.Components;

public abstract class HrReportingPageBase : ComponentBase
{
    [CascadingParameter(Name = "UserPageAccess")]
    protected Dictionary<string, AccessLevel> UserPageAccess { get; set; } = new();

    [CascadingParameter(Name = "IsAdmin")]
    protected bool IsAdmin { get; set; }

    [Inject]
    protected IUserTimeZoneService UserTime { get; set; } = null!;

    protected virtual string PageKey => HrReportingRoutes.PageKey;

    protected bool CanEdit =>
        UserPageAccess.TryGetValue(PageKey, out var level) && level == AccessLevel.Edit;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && !UserTime.IsResolved)
        {
            await UserTime.EnsureResolvedAsync();
            StateHasChanged();
        }
    }

    protected static string Count(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

    protected static string Amount(decimal value) => value.ToString("#,0.##", CultureInfo.CurrentCulture);

    protected string UploadLabel(HeadcountUpload upload, IReadOnlyCollection<HeadcountUpload> all) =>
        UploadLabels.Label(upload, all, UserTime);
}

public static class UploadLabels
{
    public static string Label(HeadcountUpload upload, IReadOnlyCollection<HeadcountUpload> all, IUserTimeZoneService userTime)
    {
        var label = $"{upload.DataDate.ToString(HeadcountColumn.DateFormat, CultureInfo.InvariantCulture)} · {upload.RowCount.ToString("N0", CultureInfo.CurrentCulture)} people";
        var sameDate = all.Count(u => u.DataDate == upload.DataDate) > 1;
        return sameDate ? $"{label} · uploaded {userTime.Format(upload.UploadedAt)}" : label;
    }
}

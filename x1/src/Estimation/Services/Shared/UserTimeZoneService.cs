using Microsoft.JSInterop;
using Serilog;

namespace Estimation.Services.Shared;

public interface IUserTimeZoneService
{
    TimeZoneInfo TimeZone { get; }

    bool IsResolved { get; }

    Task<TimeZoneInfo> EnsureResolvedAsync();

    DateTime ToLocal(DateTime utc);

    DateTime? ToLocal(DateTime? utc);

    string Format(DateTime? utc, string? format = null, string emptyText = "—");

    DateTime ToUtc(DateTime local);
}

public sealed class UserTimeZoneService : IUserTimeZoneService
{
    public const string DefaultFormat = "dd MMM yyyy HH:mm";

    private readonly IJSRuntime _js;
    private TimeZoneInfo? _resolved;
    private Task<TimeZoneInfo>? _resolving;

    public UserTimeZoneService(IJSRuntime js) => _js = js;

    public TimeZoneInfo TimeZone => _resolved ?? TimeZoneInfo.Local;

    public bool IsResolved => _resolved is not null;

    public async Task<TimeZoneInfo> EnsureResolvedAsync()
    {
        if (_resolved is not null)
        {
            return _resolved;
        }

        _resolving ??= ResolveAsync();

        try
        {
            return await _resolving;
        }
        finally
        {
            if (_resolved is null)
            {
                // The attempt failed — usually because it ran while prerendering. Forget it so the
                // next render tries again instead of being stuck on the server timezone forever.
                _resolving = null;
            }
        }
    }

    public DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZone);

    public DateTime? ToLocal(DateTime? utc) => utc is null ? null : ToLocal(utc.Value);

    public string Format(DateTime? utc, string? format = null, string emptyText = "—") =>
        utc is null ? emptyText : ToLocal(utc.Value).ToString(format ?? DefaultFormat);

    public DateTime ToUtc(DateTime local)
    {
        var wallClock = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        var zone = TimeZone;
        var guard = 0;

        while (zone.IsInvalidTime(wallClock) && guard++ < 24)
        {
            wallClock = wallClock.AddMinutes(15);
        }

        return TimeZoneInfo.ConvertTimeToUtc(wallClock, zone);
    }

    private async Task<TimeZoneInfo> ResolveAsync()
    {
        try
        {
            var id = await _js.InvokeAsync<string?>("getBrowserTimeZone");

            if (!string.IsNullOrWhiteSpace(id))
            {
                _resolved = TimeZoneInfo.FindSystemTimeZoneById(id);
            }
        }
        catch (Exception ex)
        {
            // Prerendering has no JS, and an id this host does not know throws. Neither is worth
            // failing a page over: the server timezone stands in and the next circuit retries.
            Log.Debug(ex, "Browser timezone could not be resolved; falling back to {TimeZone}", TimeZoneInfo.Local.Id);
        }

        return TimeZone;
    }
}

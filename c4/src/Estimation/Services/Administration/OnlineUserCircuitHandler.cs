using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Serilog;

namespace Estimation.Services.Administration;

public class OnlineUserCircuitHandler : CircuitHandler, IDisposable
{
    private readonly IOnlineUserTracker _tracker;
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly NavigationManager _navigation;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private string? _circuitId;

    public OnlineUserCircuitHandler(
        IOnlineUserTracker tracker,
        AuthenticationStateProvider authStateProvider,
        NavigationManager navigation,
        IHttpContextAccessor httpContextAccessor)
    {
        _tracker = tracker;
        _authStateProvider = authStateProvider;
        _navigation = navigation;
        _httpContextAccessor = httpContextAccessor;
    }

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        if (_circuitId is null)
        {
            _circuitId = circuit.Id;
            _navigation.LocationChanged += OnLocationChanged;
        }

        return Task.CompletedTask;
    }

    public override async Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        try
        {
            var authState = await _authStateProvider.GetAuthenticationStateAsync();
            var userName = authState.User.Identity?.Name;

            if (!string.IsNullOrWhiteSpace(userName))
            {
                var http = _httpContextAccessor.HttpContext;
                var ip = http?.Connection.RemoteIpAddress;
                if (ip is { IsIPv4MappedToIPv6: true })
                {
                    ip = ip.MapToIPv4();
                }

                _tracker.Connect(circuit.Id, userName, CurrentPage(), http?.Request.Headers.UserAgent.ToString(), ip?.ToString());
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to register online user for circuit {CircuitId}", circuit.Id);
        }
    }

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _tracker.ConnectionLost(circuit.Id);
        return Task.CompletedTask;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        Unsubscribe();
        _tracker.Disconnect(circuit.Id);
        return Task.CompletedTask;
    }

    public void RecordUserActivity()
    {
        if (_circuitId is not null)
        {
            _tracker.RecordActivity(_circuitId);
        }
    }

    public void Dispose() => Unsubscribe();

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        if (_circuitId is not null)
        {
            _tracker.PageChanged(_circuitId, ToPage(e.Location));
        }
    }

    private string? CurrentPage()
    {
        try
        {
            return ToPage(_navigation.Uri);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private string ToPage(string uri)
    {
        try
        {
            return "/" + _navigation.ToBaseRelativePath(uri);
        }
        catch (ArgumentException)
        {
            return uri;
        }
    }

    private void Unsubscribe()
    {
        if (_circuitId is not null)
        {
            _navigation.LocationChanged -= OnLocationChanged;
        }
    }
}

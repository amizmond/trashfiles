using System.Collections.Concurrent;

namespace Estimation.Services.Administration;

public class OnlineSession
{
    public string CircuitId { get; init; } = string.Empty;

    public DateTime OpenedAtUtc { get; init; }

    public DateTime LastActivityUtc { get; init; }

    public string? Page { get; init; }

    public bool IsConnected { get; init; }

    public DateTime? ConnectionLostAtUtc { get; init; }

    public string? UserAgent { get; init; }

    public string? IpAddress { get; init; }
}

public class OnlineUser
{
    public string WindowsUserName { get; init; } = string.Empty;

    public DateTime ConnectedAtUtc { get; init; }

    public DateTime LastActivityUtc { get; init; }

    public IReadOnlyList<OnlineSession> Sessions { get; init; } = [];
}

public interface IOnlineUserTracker
{
    void Connect(string circuitId, string windowsUserName, string? page = null, string? userAgent = null, string? ipAddress = null);

    void ConnectionLost(string circuitId);

    void PageChanged(string circuitId, string page);

    void RecordActivity(string circuitId);

    void Disconnect(string circuitId);

    List<OnlineUser> GetOnlineUsers();
}

public class OnlineUserTracker : IOnlineUserTracker
{
    private sealed class CircuitSession(string circuitId, string windowsUserName, DateTime openedAtUtc)
    {
        private readonly Lock _gate = new();
        private DateTime? _lastActivityUtc;
        private string? _page;
        private bool _isConnected;
        private DateTime? _connectionLostAtUtc;
        private string? _userAgent;
        private string? _ipAddress;

        public string WindowsUserName { get; } = windowsUserName;

        public void MarkConnected(string? page, string? userAgent, string? ipAddress)
        {
            lock (_gate)
            {
                _isConnected = true;
                _connectionLostAtUtc = null;
                _page = string.IsNullOrWhiteSpace(page) ? _page : page;
                _userAgent = string.IsNullOrWhiteSpace(userAgent) ? _userAgent : userAgent;
                _ipAddress = string.IsNullOrWhiteSpace(ipAddress) ? _ipAddress : ipAddress;
            }
        }

        public void MarkConnectionLost(DateTime nowUtc)
        {
            lock (_gate)
            {
                _isConnected = false;
                _connectionLostAtUtc = nowUtc;
            }
        }

        public void SetPage(DateTime nowUtc, string page)
        {
            lock (_gate)
            {
                _page = page;
                _lastActivityUtc = nowUtc;
            }
        }

        public void Touch(DateTime nowUtc)
        {
            lock (_gate)
            {
                _lastActivityUtc = nowUtc;
            }
        }

        public OnlineSession ToSnapshot()
        {
            lock (_gate)
            {
                return new OnlineSession
                {
                    CircuitId = circuitId,
                    OpenedAtUtc = openedAtUtc,
                    LastActivityUtc = _lastActivityUtc ?? openedAtUtc,
                    Page = _page,
                    IsConnected = _isConnected,
                    ConnectionLostAtUtc = _connectionLostAtUtc,
                    UserAgent = _userAgent,
                    IpAddress = _ipAddress
                };
            }
        }
    }

    private readonly ConcurrentDictionary<string, CircuitSession> _sessions = new();
    private readonly TimeProvider _time;

    public OnlineUserTracker(TimeProvider time)
    {
        _time = time;
    }

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public void Connect(string circuitId, string windowsUserName, string? page = null, string? userAgent = null, string? ipAddress = null)
    {
        if (string.IsNullOrWhiteSpace(circuitId) || string.IsNullOrWhiteSpace(windowsUserName))
        {
            return;
        }

        var now = UtcNow;
        var session = _sessions.GetOrAdd(circuitId, id => new CircuitSession(id, windowsUserName, now));
        session.MarkConnected(page, userAgent, ipAddress);
    }

    public void ConnectionLost(string circuitId)
    {
        if (TryGet(circuitId, out var session))
        {
            session.MarkConnectionLost(UtcNow);
        }
    }

    public void PageChanged(string circuitId, string page)
    {
        if (!string.IsNullOrWhiteSpace(page) && TryGet(circuitId, out var session))
        {
            session.SetPage(UtcNow, page);
        }
    }

    public void RecordActivity(string circuitId)
    {
        if (TryGet(circuitId, out var session))
        {
            session.Touch(UtcNow);
        }
    }

    public void Disconnect(string circuitId)
    {
        if (!string.IsNullOrWhiteSpace(circuitId))
        {
            _sessions.TryRemove(circuitId, out _);
        }
    }

    public List<OnlineUser> GetOnlineUsers()
        => _sessions.Values
            .GroupBy(s => s.WindowsUserName, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var sessions = g.Select(s => s.ToSnapshot())
                    .OrderByDescending(s => s.LastActivityUtc)
                    .ToList();

                return new OnlineUser
                {
                    WindowsUserName = g.Key,
                    ConnectedAtUtc = sessions.Min(s => s.OpenedAtUtc),
                    LastActivityUtc = sessions.Max(s => s.LastActivityUtc),
                    Sessions = sessions
                };
            })
            .OrderBy(u => u.WindowsUserName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private bool TryGet(string circuitId, out CircuitSession session)
    {
        if (string.IsNullOrWhiteSpace(circuitId))
        {
            session = null!;
            return false;
        }

        return _sessions.TryGetValue(circuitId, out session!);
    }
}

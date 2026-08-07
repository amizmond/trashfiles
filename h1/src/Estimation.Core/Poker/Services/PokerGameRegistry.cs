using Estimation.Core.JiraIntegration.Client;
using Estimation.Core.Poker.Models;
using Serilog;

namespace Estimation.Core.Poker.Services;

public interface IPokerGameRegistry
{
    event Action? RoomsChanged;

    IReadOnlyList<PokerRoomSummary> GetActiveRooms();

    PokerRoom? GetRoom(Guid roomId);

    PokerRoom? FindRoomForUser(string userName);

    PokerRoom CreateRoom(PokerRoomCreateRequest request);

    bool Join(Guid roomId, string userName, string displayName);

    void Disconnect(Guid roomId, string userName);

    void Leave(Guid roomId, string userName);

    bool SetVote(Guid roomId, string userName, string card);

    bool RevealCards(Guid roomId, string userName);

    bool ResetVotes(Guid roomId, string userName);

    bool StartEstimation(Guid roomId, string userName, string issueKey);

    bool BackToTable(Guid roomId, string userName);

    bool ToggleHand(Guid roomId, string userName);

    bool SetTopic(Guid roomId, string userName, string? topic);

    bool UpdateIssue(Guid roomId, string issueKey, Action<JiraIssueResponse> mutate);

    void Sweep();
}

public sealed class PokerGameRegistry : IPokerGameRegistry, IDisposable
{
    public static readonly TimeSpan DisconnectGrace = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(15);

    private readonly object _sync = new();
    private readonly Dictionary<Guid, PokerRoom> _rooms = new();
    private readonly TimeProvider _time;
    private readonly ITimer? _timer;

    public event Action? RoomsChanged;

    public PokerGameRegistry(TimeProvider time, bool enableSweepTimer = true)
    {
        _time = time;
        if (enableSweepTimer)
        {
            _timer = _time.CreateTimer(_ => SweepSafe(), null, SweepInterval, SweepInterval);
        }
    }

    public IReadOnlyList<PokerRoomSummary> GetActiveRooms()
    {
        lock (_sync)
        {
            return _rooms.Values
                .OrderByDescending(r => r.CreatedAtUtc)
                .Select(r => new PokerRoomSummary(
                    r.Id,
                    r.Name,
                    r.Mode,
                    r.FindPlayer(r.PresenterUserName)?.DisplayName ?? r.PresenterUserName,
                    r.Players.Count,
                    r.Players.Count(p => p.IsConnected),
                    r.CreatedAtUtc,
                    r.SourceDescription))
                .ToList();
        }
    }

    public PokerRoom? GetRoom(Guid roomId)
    {
        lock (_sync)
        {
            return _rooms.GetValueOrDefault(roomId);
        }
    }

    public PokerRoom? FindRoomForUser(string userName)
    {
        lock (_sync)
        {
            return _rooms.Values
                .OrderByDescending(r => r.CreatedAtUtc)
                .FirstOrDefault(r => r.FindPlayer(userName) is not null);
        }
    }

    public PokerRoom CreateRoom(PokerRoomCreateRequest request)
    {
        var notify = new Notifications();
        PokerRoom room;
        lock (_sync)
        {
            var now = UtcNow();
            room = new PokerRoom
            {
                Name = request.Name,
                Mode = request.Mode,
                Deck = request.Deck.ToList(),
                TeamId = request.TeamId,
                AllowVolunteers = request.AllowVolunteers && request.Mode == PokerRoomMode.Jira,
                SourceDescription = request.SourceDescription,
                CreatedAtUtc = now,
                Phase = request.Mode == PokerRoomMode.Jira ? PokerPhase.IssueTable : PokerPhase.Voting,
                PresenterUserName = request.CreatorUserName,
            };
            room.Issues = request.Issues?.ToList() ?? [];

            room.Players = new List<PokerPlayer>
            {
                new()
                {
                    UserName = request.CreatorUserName,
                    DisplayName = request.CreatorDisplayName,
                    JoinedAtUtc = now,
                    ConnectionCount = 0,
                    DisconnectedAtUtc = now,
                },
            };

            RemoveFromOtherRooms(request.CreatorUserName, exceptRoomId: room.Id, notify);
            _rooms[room.Id] = room;
            notify.RoomsListChanged = true;
        }

        notify.Raise(this);
        Log.Information("Poker room {RoomName} ({RoomId}, {Mode}) created by {UserName}",
            room.Name, room.Id, room.Mode, request.CreatorUserName);
        return room;
    }

    public bool Join(Guid roomId, string userName, string displayName)
    {
        var notify = new Notifications();
        bool joined;
        lock (_sync)
        {
            if (!_rooms.TryGetValue(roomId, out var room))
            {
                joined = false;
            }
            else
            {
                RemoveFromOtherRooms(userName, exceptRoomId: roomId, notify);

                var player = room.FindPlayer(userName);
                if (player is null)
                {
                    player = new PokerPlayer
                    {
                        UserName = userName,
                        DisplayName = displayName,
                        JoinedAtUtc = UtcNow(),
                    };
                    room.Players = [.. room.Players, player];
                }

                player.DisplayName = displayName;
                player.ConnectionCount++;
                player.DisconnectedAtUtc = null;

                notify.Room(room);
                notify.RoomsListChanged = true;
                joined = true;
            }
        }

        notify.Raise(this);
        return joined;
    }

    public void Disconnect(Guid roomId, string userName)
    {
        var notify = new Notifications();
        lock (_sync)
        {
            if (_rooms.TryGetValue(roomId, out var room) && room.FindPlayer(userName) is { } player)
            {
                player.ConnectionCount = Math.Max(0, player.ConnectionCount - 1);
                if (player.ConnectionCount == 0 && player.DisconnectedAtUtc is null)
                {
                    player.DisconnectedAtUtc = UtcNow();
                }
                notify.Room(room);
                notify.RoomsListChanged = true;
            }
        }

        notify.Raise(this);
    }

    public void Leave(Guid roomId, string userName)
    {
        var notify = new Notifications();
        lock (_sync)
        {
            if (_rooms.TryGetValue(roomId, out var room))
            {
                RemovePlayer(room, userName, notify);
            }
        }

        notify.Raise(this);
    }

    public bool SetVote(Guid roomId, string userName, string card)
    {
        return Mutate(roomId, notify: true, room =>
        {
            if (room.CardsRevealed
                || (room.Mode == PokerRoomMode.Jira && room.Phase != PokerPhase.Voting)
                || !room.Deck.Contains(card))
            {
                return false;
            }
            if (room.FindPlayer(userName) is not { } player)
            {
                return false;
            }

            player.Vote = player.Vote == card ? null : card;
            return true;
        });
    }

    public bool RevealCards(Guid roomId, string userName)
    {
        return Mutate(roomId, notify: true, room =>
        {
            if (!room.IsPresenter(userName)
                || room.CardsRevealed
                || (room.Mode == PokerRoomMode.Jira && room.Phase != PokerPhase.Voting))
            {
                return false;
            }

            room.CardsRevealed = true;
            return true;
        });
    }

    public bool ResetVotes(Guid roomId, string userName)
    {
        return Mutate(roomId, notify: true, room =>
        {
            if (!room.IsPresenter(userName))
            {
                return false;
            }

            ClearVotes(room);
            return true;
        });
    }

    public bool StartEstimation(Guid roomId, string userName, string issueKey)
    {
        return Mutate(roomId, notify: true, room =>
        {
            if (room.Mode != PokerRoomMode.Jira || !room.IsPresenter(userName))
            {
                return false;
            }
            if (!room.Issues.Any(i => string.Equals(i.Key, issueKey, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            room.CurrentIssueKey = issueKey;
            room.Phase = PokerPhase.Voting;
            ClearVotes(room);
            ClearHands(room);
            return true;
        });
    }

    public bool BackToTable(Guid roomId, string userName)
    {
        return Mutate(roomId, notify: true, room =>
        {
            if (room.Mode != PokerRoomMode.Jira || !room.IsPresenter(userName))
            {
                return false;
            }

            room.Phase = PokerPhase.IssueTable;
            room.CurrentIssueKey = null;
            ClearVotes(room);
            ClearHands(room);
            return true;
        });
    }

    public bool ToggleHand(Guid roomId, string userName)
    {
        return Mutate(roomId, notify: true, room =>
        {
            if (!room.AllowVolunteers || room.Phase != PokerPhase.Voting || room.CurrentIssueKey is null)
            {
                return false;
            }
            if (room.FindPlayer(userName) is not { } player)
            {
                return false;
            }

            player.HasRaisedHand = !player.HasRaisedHand;
            return true;
        });
    }

    public bool SetTopic(Guid roomId, string userName, string? topic)
    {
        return Mutate(roomId, notify: true, room =>
        {
            if (room.Mode != PokerRoomMode.Simple || !room.IsPresenter(userName))
            {
                return false;
            }

            room.Topic = topic;
            return true;
        });
    }

    public bool UpdateIssue(Guid roomId, string issueKey, Action<JiraIssueResponse> mutate)
    {
        return Mutate(roomId, notify: true, room =>
        {
            var issue = room.Issues.FirstOrDefault(i => string.Equals(i.Key, issueKey, StringComparison.OrdinalIgnoreCase));
            if (issue is null)
            {
                return false;
            }

            mutate(issue);
            return true;
        });
    }

    public void Sweep()
    {
        var notify = new Notifications();
        lock (_sync)
        {
            var cutoff = UtcNow() - DisconnectGrace;
            foreach (var room in _rooms.Values.ToList())
            {
                var expired = room.Players
                    .Where(p => !p.IsConnected && p.DisconnectedAtUtc is { } at && at <= cutoff)
                    .Select(p => p.UserName)
                    .ToList();
                foreach (var userName in expired)
                {
                    RemovePlayer(room, userName, notify);
                }
            }
        }

        notify.Raise(this);
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }

    private void SweepSafe()
    {
        try
        {
            Sweep();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Poker room sweep failed");
        }
    }

    private DateTime UtcNow()
    {
        return _time.GetUtcNow().UtcDateTime;
    }

    private static void ClearVotes(PokerRoom room)
    {
        foreach (var player in room.Players)
        {
            player.Vote = null;
        }
        room.CardsRevealed = false;
    }

    private static void ClearHands(PokerRoom room)
    {
        foreach (var player in room.Players)
        {
            player.HasRaisedHand = false;
        }
    }

    private bool Mutate(Guid roomId, bool notify, Func<PokerRoom, bool> action)
    {
        var notifications = new Notifications();
        bool changed;
        lock (_sync)
        {
            if (!_rooms.TryGetValue(roomId, out var room))
            {
                changed = false;
            }
            else
            {
                changed = action(room);
                if (changed && notify)
                {
                    notifications.Room(room);
                }
            }
        }

        notifications.Raise(this);
        return changed;
    }

    private void RemoveFromOtherRooms(string userName, Guid exceptRoomId, Notifications notify)
    {
        foreach (var other in _rooms.Values.Where(r => r.Id != exceptRoomId).ToList())
        {
            if (other.FindPlayer(userName) is not null)
            {
                RemovePlayer(other, userName, notify);
            }
        }
    }

    private void RemovePlayer(PokerRoom room, string userName, Notifications notify)
    {
        if (room.FindPlayer(userName) is null)
        {
            return;
        }

        room.Players = room.Players
            .Where(p => !string.Equals(p.UserName, userName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        notify.Room(room);
        notify.RoomsListChanged = true;

        if (room.Players.Count == 0)
        {
            _rooms.Remove(room.Id);
            Log.Information("Poker room {RoomName} ({RoomId}) removed — last player left", room.Name, room.Id);
            return;
        }

        if (room.FindPlayer(room.PresenterUserName) is null)
        {
            var successor = room.Players
                .OrderByDescending(p => p.IsConnected)
                .ThenBy(p => p.JoinedAtUtc)
                .First();
            room.PresenterUserName = successor.UserName;
            Log.Information("Poker room {RoomName} ({RoomId}): presenter left, promoted {UserName}",
                room.Name, room.Id, successor.UserName);
        }
    }

    private sealed class Notifications
    {
        private readonly HashSet<PokerRoom> _rooms = [];

        public bool RoomsListChanged { get; set; }

        public void Room(PokerRoom room)
        {
            _rooms.Add(room);
        }

        public void Raise(PokerGameRegistry registry)
        {
            foreach (var room in _rooms)
            {
                try
                {
                    room.RaiseChanged();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Poker room change handler failed for room {RoomId}", room.Id);
                }
            }

            if (RoomsListChanged)
            {
                try
                {
                    registry.RoomsChanged?.Invoke();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Poker rooms-changed handler failed");
                }
            }
        }
    }
}

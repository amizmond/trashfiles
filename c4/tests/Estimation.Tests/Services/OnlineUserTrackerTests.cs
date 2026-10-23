using Estimation.Services.Administration;
using Xunit;

namespace Estimation.Tests.Services;

public class OnlineUserTrackerTests
{
    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 23, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }

    private readonly TestTimeProvider _time = new();
    private readonly OnlineUserTracker _tracker;

    public OnlineUserTrackerTests()
    {
        _tracker = new OnlineUserTracker(_time);
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    [Fact]
    public void Tabs_of_the_same_user_are_grouped_regardless_of_case()
    {
        _tracker.Connect("c1", @"CORP\jane", "/features");
        _time.Advance(TimeSpan.FromMinutes(5));
        _tracker.Connect("c2", @"corp\JANE", "/teams");
        _tracker.Connect("c3", @"CORP\bob", "/");

        var users = _tracker.GetOnlineUsers();

        Assert.Equal(2, users.Count);
        Assert.Equal(@"CORP\bob", users[0].WindowsUserName);
        var jane = users[1];
        Assert.Equal(2, jane.Sessions.Count);
        Assert.Equal(Now.AddMinutes(-5), jane.ConnectedAtUtc);
        Assert.Equal("/teams", jane.Sessions[0].Page);
    }

    [Fact]
    public void A_reconnect_keeps_the_opened_time_and_clears_the_lost_connection()
    {
        var opened = Now;
        _tracker.Connect("c1", @"CORP\jane", "/features", "UA", "10.0.0.5");
        _time.Advance(TimeSpan.FromMinutes(1));
        _tracker.ConnectionLost("c1");

        var lost = Assert.Single(Assert.Single(_tracker.GetOnlineUsers()).Sessions);
        Assert.False(lost.IsConnected);
        Assert.Equal(Now, lost.ConnectionLostAtUtc);

        _time.Advance(TimeSpan.FromMinutes(1));
        _tracker.Connect("c1", @"CORP\jane", null, null, null);

        var back = Assert.Single(Assert.Single(_tracker.GetOnlineUsers()).Sessions);
        Assert.True(back.IsConnected);
        Assert.Null(back.ConnectionLostAtUtc);
        Assert.Equal(opened, back.OpenedAtUtc);
        Assert.Equal(opened, back.LastActivityUtc);
        Assert.Equal("/features", back.Page);
        Assert.Equal("UA", back.UserAgent);
        Assert.Equal("10.0.0.5", back.IpAddress);
    }

    [Fact]
    public void Page_changes_and_activity_update_the_last_activity()
    {
        _tracker.Connect("c1", @"CORP\jane", "/features");
        _time.Advance(TimeSpan.FromMinutes(3));
        _tracker.PageChanged("c1", "/teams/4");

        var session = Assert.Single(Assert.Single(_tracker.GetOnlineUsers()).Sessions);
        Assert.Equal("/teams/4", session.Page);
        Assert.Equal(Now, session.LastActivityUtc);

        _time.Advance(TimeSpan.FromMinutes(2));
        _tracker.RecordActivity("c1");

        var user = Assert.Single(_tracker.GetOnlineUsers());
        Assert.Equal(Now, user.LastActivityUtc);
        Assert.Equal(Now.AddMinutes(-5), user.ConnectedAtUtc);
    }

    [Fact]
    public void Unknown_circuits_are_ignored_and_closed_ones_removed()
    {
        _tracker.RecordActivity("nope");
        _tracker.PageChanged("nope", "/x");
        _tracker.ConnectionLost("nope");
        Assert.Empty(_tracker.GetOnlineUsers());

        _tracker.Connect("c1", @"CORP\jane");
        _tracker.Connect("", @"CORP\bob");
        _tracker.Connect("c2", " ");
        Assert.Single(_tracker.GetOnlineUsers());

        _tracker.Disconnect("c1");
        Assert.Empty(_tracker.GetOnlineUsers());
    }
}

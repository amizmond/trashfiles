using Estimation.Core.JiraIntegration.Client;

namespace Estimation.Core.Poker.Models;

public enum PokerRoomMode
{
    Simple,

    Jira,
}

public enum PokerPhase
{
    IssueTable,

    Voting,
}

public static class PokerDeck
{
    public const string QuestionCard = "?";

    public const string CoffeeCard = "☕";

    public static readonly IReadOnlyList<string> Default =
        ["0", "1", "2", "3", "5", "8", "13", "21", "40", "100", QuestionCard, CoffeeCard];
}

public class PokerPlayer
{
    public required string UserName { get; init; }

    public required string DisplayName { get; set; }

    public string? Vote { get; set; }

    public bool HasRaisedHand { get; set; }

    public DateTime JoinedAtUtc { get; set; }

    public int ConnectionCount { get; set; }

    public DateTime? DisconnectedAtUtc { get; set; }

    public bool IsConnected => ConnectionCount > 0;
}

public class PokerRoom
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Name { get; set; }

    public PokerRoomMode Mode { get; init; }

    public IReadOnlyList<string> Deck { get; init; } = PokerDeck.Default;

    public int? TeamId { get; init; }

    public bool AllowVolunteers { get; init; }

    public string PresenterUserName { get; internal set; } = string.Empty;

    public PokerPhase Phase { get; internal set; }

    public IReadOnlyList<JiraIssueResponse> Issues { get; internal set; } = [];

    public string? CurrentIssueKey { get; internal set; }

    public string? Topic { get; internal set; }

    public bool CardsRevealed { get; internal set; }

    public IReadOnlyList<PokerPlayer> Players { get; internal set; } = [];

    public DateTime CreatedAtUtc { get; internal set; }

    public string? SourceDescription { get; init; }

    public event Action? Changed;

    internal void RaiseChanged()
    {
        Changed?.Invoke();
    }

    public bool IsPresenter(string userName)
    {
        return string.Equals(PresenterUserName, userName, StringComparison.OrdinalIgnoreCase);
    }

    public PokerPlayer? FindPlayer(string userName)
    {
        return Players.FirstOrDefault(p => string.Equals(p.UserName, userName, StringComparison.OrdinalIgnoreCase));
    }

    public JiraIssueResponse? CurrentIssue
    {
        get
        {
            if (string.IsNullOrEmpty(CurrentIssueKey))
            {
                return null;
            }
            return Issues.FirstOrDefault(i => string.Equals(i.Key, CurrentIssueKey, StringComparison.OrdinalIgnoreCase));
        }
    }
}

public record PokerRoomSummary(
    Guid Id,
    string Name,
    PokerRoomMode Mode,
    string PresenterDisplayName,
    int PlayerCount,
    int ConnectedCount,
    DateTime CreatedAtUtc,
    string? SourceDescription);

public class PokerRoomCreateRequest
{
    public required string Name { get; set; }

    public PokerRoomMode Mode { get; set; }

    public required List<string> Deck { get; set; }

    public int? TeamId { get; set; }

    public bool AllowVolunteers { get; set; }

    public List<JiraIssueResponse>? Issues { get; set; }

    public string? SourceDescription { get; set; }

    public required string CreatorUserName { get; set; }

    public required string CreatorDisplayName { get; set; }
}

using Estimation.Core.JiraIntegration.Client;
using Estimation.Core.Poker.Models;
using Estimation.Core.Poker.Services;
using Xunit;

namespace Estimation.Core.Tests;

public class PokerGameRegistryTests
{
    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 07, 22, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }

    private readonly TestTimeProvider _time = new();
    private readonly PokerGameRegistry _registry;

    public PokerGameRegistryTests()
    {
        _registry = new PokerGameRegistry(_time, enableSweepTimer: false);
    }

    private PokerRoom CreateSimpleRoom(string creator = "alice", string displayName = "Alice")
    {
        return _registry.CreateRoom(new PokerRoomCreateRequest
        {
            Name = "Team A",
            Mode = PokerRoomMode.Simple,
            Deck = [.. PokerDeck.Default],
            CreatorUserName = creator,
            CreatorDisplayName = displayName,
        });
    }

    private PokerRoom CreateJiraRoom(string creator = "alice", params string[] issueKeys)
    {
        return CreateJiraRoom(allowVolunteers: false, creator, issueKeys);
    }

    private PokerRoom CreateJiraRoom(bool allowVolunteers, string creator = "alice", params string[] issueKeys)
    {
        var keys = issueKeys.Length > 0 ? issueKeys : ["ABC-1", "ABC-2"];
        return _registry.CreateRoom(new PokerRoomCreateRequest
        {
            Name = "Team A",
            Mode = PokerRoomMode.Jira,
            Deck = [.. PokerDeck.Default],
            Issues = keys.Select(k => new JiraIssueResponse { Key = k, Summary = k }).ToList(),
            AllowVolunteers = allowVolunteers,
            CreatorUserName = creator,
            CreatorDisplayName = creator,
        });
    }

    [Fact]
    public void CreateRoom_SeatsCreatorAsPresenter_AndListsTheRoom()
    {
        var room = CreateSimpleRoom();

        Assert.Equal("alice", room.PresenterUserName);
        Assert.True(room.IsPresenter("ALICE"));
        Assert.NotNull(room.FindPlayer("alice"));
        Assert.Equal(PokerPhase.Voting, room.Phase);

        var summaries = _registry.GetActiveRooms();
        var summary = Assert.Single(summaries);
        Assert.Equal(room.Id, summary.Id);
        Assert.Equal(1, summary.PlayerCount);
        Assert.Equal(0, summary.ConnectedCount);
    }

    [Fact]
    public void CreateRoom_JiraMode_StartsAtIssueTable()
    {
        var room = CreateJiraRoom();

        Assert.Equal(PokerPhase.IssueTable, room.Phase);
        Assert.Equal(2, room.Issues.Count);
    }

    [Fact]
    public void Join_AddsPlayer_AndCountsConnections()
    {
        var room = CreateSimpleRoom();

        Assert.True(_registry.Join(room.Id, "bob", "Bob"));
        Assert.True(_registry.Join(room.Id, "bob", "Bob"));

        var bob = room.FindPlayer("bob");
        Assert.NotNull(bob);
        Assert.Equal(2, bob!.ConnectionCount);

        _registry.Disconnect(room.Id, "bob");
        Assert.True(bob.IsConnected);
        _registry.Disconnect(room.Id, "bob");
        Assert.False(bob.IsConnected);
        Assert.NotNull(bob.DisconnectedAtUtc);
    }

    [Fact]
    public void Join_UnknownRoom_ReturnsFalse()
    {
        Assert.False(_registry.Join(Guid.NewGuid(), "bob", "Bob"));
    }

    [Fact]
    public void Join_SecondRoom_RemovesUserFromTheFirst()
    {
        var first = CreateSimpleRoom();
        _registry.Join(first.Id, "bob", "Bob");

        var second = CreateSimpleRoom(creator: "carol", displayName: "Carol");
        _registry.Join(second.Id, "bob", "Bob");

        Assert.Null(first.FindPlayer("bob"));
        Assert.NotNull(second.FindPlayer("bob"));
        Assert.Equal(second.Id, _registry.FindRoomForUser("bob")!.Id);
    }

    [Fact]
    public void SetVote_StoresToggle_AndValidatesCard()
    {
        var room = CreateSimpleRoom();
        _registry.Join(room.Id, "alice", "Alice");

        Assert.False(_registry.SetVote(room.Id, "alice", "7"));

        Assert.True(_registry.SetVote(room.Id, "alice", "5"));
        Assert.Equal("5", room.FindPlayer("alice")!.Vote);

        Assert.True(_registry.SetVote(room.Id, "alice", "8"));
        Assert.Equal("8", room.FindPlayer("alice")!.Vote);

        Assert.True(_registry.SetVote(room.Id, "alice", "8"));
        Assert.Null(room.FindPlayer("alice")!.Vote);
    }

    [Fact]
    public void RevealCards_IsPresenterOnly_AndFreezesVotes()
    {
        var room = CreateSimpleRoom();
        _registry.Join(room.Id, "alice", "Alice");
        _registry.Join(room.Id, "bob", "Bob");
        _registry.SetVote(room.Id, "bob", "3");

        Assert.False(_registry.RevealCards(room.Id, "bob"));
        Assert.False(room.CardsRevealed);

        Assert.True(_registry.RevealCards(room.Id, "alice"));
        Assert.True(room.CardsRevealed);

        Assert.False(_registry.SetVote(room.Id, "bob", "5"));
        Assert.Equal("3", room.FindPlayer("bob")!.Vote);
    }

    [Fact]
    public void ResetVotes_ClearsVotesAndReveal_ForPresenterOnly()
    {
        var room = CreateSimpleRoom();
        _registry.Join(room.Id, "alice", "Alice");
        _registry.Join(room.Id, "bob", "Bob");
        _registry.SetVote(room.Id, "bob", "13");
        _registry.RevealCards(room.Id, "alice");

        Assert.False(_registry.ResetVotes(room.Id, "bob"));

        Assert.True(_registry.ResetVotes(room.Id, "alice"));
        Assert.False(room.CardsRevealed);
        Assert.Null(room.FindPlayer("bob")!.Vote);
    }

    [Fact]
    public void StartEstimation_MovesRoomToVoting_AndClearsPreviousRound()
    {
        var room = CreateJiraRoom();
        _registry.Join(room.Id, "alice", "Alice");
        _registry.Join(room.Id, "bob", "Bob");

        Assert.False(_registry.SetVote(room.Id, "bob", "5"));

        Assert.False(_registry.StartEstimation(room.Id, "bob", "ABC-1"));
        Assert.False(_registry.StartEstimation(room.Id, "alice", "NOPE-9"));

        Assert.True(_registry.StartEstimation(room.Id, "alice", "ABC-1"));
        Assert.Equal(PokerPhase.Voting, room.Phase);
        Assert.Equal("ABC-1", room.CurrentIssueKey);

        _registry.SetVote(room.Id, "bob", "5");
        _registry.RevealCards(room.Id, "alice");

        Assert.True(_registry.StartEstimation(room.Id, "alice", "ABC-2"));
        Assert.False(room.CardsRevealed);
        Assert.Null(room.FindPlayer("bob")!.Vote);
    }

    [Fact]
    public void BackToTable_EndsTheRound()
    {
        var room = CreateJiraRoom();
        _registry.Join(room.Id, "alice", "Alice");
        _registry.StartEstimation(room.Id, "alice", "ABC-1");
        _registry.SetVote(room.Id, "alice", "5");

        Assert.True(_registry.BackToTable(room.Id, "alice"));
        Assert.Equal(PokerPhase.IssueTable, room.Phase);
        Assert.Null(room.CurrentIssueKey);
        Assert.Null(room.FindPlayer("alice")!.Vote);
    }

    [Fact]
    public void Leave_Presenter_PromotesLongestPresentConnectedPlayer()
    {
        var room = CreateSimpleRoom();
        _registry.Join(room.Id, "alice", "Alice");
        _time.Advance(TimeSpan.FromSeconds(1));
        _registry.Join(room.Id, "bob", "Bob");
        _time.Advance(TimeSpan.FromSeconds(1));
        _registry.Join(room.Id, "carol", "Carol");

        _registry.Disconnect(room.Id, "bob");

        _registry.Leave(room.Id, "alice");

        Assert.Equal("carol", room.PresenterUserName);
    }

    [Fact]
    public void Leave_LastPlayer_RemovesTheRoom()
    {
        var room = CreateSimpleRoom();
        _registry.Join(room.Id, "alice", "Alice");

        _registry.Leave(room.Id, "alice");

        Assert.Null(_registry.GetRoom(room.Id));
        Assert.Empty(_registry.GetActiveRooms());
    }

    [Fact]
    public void Disconnect_KeepsSeatAndVote_UntilGraceExpires()
    {
        var room = CreateSimpleRoom();
        _registry.Join(room.Id, "alice", "Alice");
        _registry.Join(room.Id, "bob", "Bob");
        _registry.SetVote(room.Id, "bob", "8");

        _registry.Disconnect(room.Id, "bob");
        _time.Advance(TimeSpan.FromSeconds(30));
        _registry.Sweep();

        var bob = room.FindPlayer("bob");
        Assert.NotNull(bob);
        Assert.Equal("8", bob!.Vote);

        Assert.True(_registry.Join(room.Id, "bob", "Bob"));
        Assert.True(room.FindPlayer("bob")!.IsConnected);
        Assert.Equal("8", room.FindPlayer("bob")!.Vote);

        _registry.Disconnect(room.Id, "bob");
        _time.Advance(PokerGameRegistry.DisconnectGrace + TimeSpan.FromSeconds(1));
        _registry.Sweep();

        Assert.Null(room.FindPlayer("bob"));
    }

    [Fact]
    public void Sweep_RemovesEmptyRoom_AndPromotesWhenPresenterExpires()
    {
        var room = CreateSimpleRoom();
        _registry.Join(room.Id, "alice", "Alice");
        _time.Advance(TimeSpan.FromSeconds(1));
        _registry.Join(room.Id, "bob", "Bob");

        _registry.Disconnect(room.Id, "alice");
        _time.Advance(PokerGameRegistry.DisconnectGrace + TimeSpan.FromSeconds(1));
        _registry.Sweep();

        Assert.NotNull(_registry.GetRoom(room.Id));
        Assert.Equal("bob", room.PresenterUserName);

        _registry.Disconnect(room.Id, "bob");
        _time.Advance(PokerGameRegistry.DisconnectGrace + TimeSpan.FromSeconds(1));
        _registry.Sweep();

        Assert.Null(_registry.GetRoom(room.Id));
    }

    [Fact]
    public void Sweep_RemovesRoom_WhoseCreatorNeverConnected()
    {
        var room = CreateSimpleRoom();

        _time.Advance(PokerGameRegistry.DisconnectGrace + TimeSpan.FromSeconds(1));
        _registry.Sweep();

        Assert.Null(_registry.GetRoom(room.Id));
    }

    [Fact]
    public void SetTopic_SimpleModePresenterOnly()
    {
        var simple = CreateSimpleRoom();
        _registry.Join(simple.Id, "alice", "Alice");
        _registry.Join(simple.Id, "bob", "Bob");

        Assert.False(_registry.SetTopic(simple.Id, "bob", "Login form"));
        Assert.True(_registry.SetTopic(simple.Id, "alice", "Login form"));
        Assert.Equal("Login form", simple.Topic);

        var jira = CreateJiraRoom(creator: "carol");
        Assert.False(_registry.SetTopic(jira.Id, "carol", "Nope"));
    }

    [Fact]
    public void UpdateIssue_MutatesTheRoomIssue()
    {
        var room = CreateJiraRoom();

        Assert.True(_registry.UpdateIssue(room.Id, "abc-1", i => i.StoryPoints = 8));
        Assert.Equal(8, room.Issues.First(i => i.Key == "ABC-1").StoryPoints);

        Assert.False(_registry.UpdateIssue(room.Id, "NOPE-1", i => i.StoryPoints = 1));
    }

    [Fact]
    public void ToggleHand_RaisesAndLowersTheHand_OnlyWhileAnIssueIsBeingEstimated()
    {
        var room = CreateJiraRoom(allowVolunteers: true);
        _registry.Join(room.Id, "alice", "Alice");
        _registry.Join(room.Id, "bob", "Bob");

        Assert.False(_registry.ToggleHand(room.Id, "bob"));

        _registry.StartEstimation(room.Id, "alice", "ABC-1");
        Assert.True(_registry.ToggleHand(room.Id, "bob"));
        Assert.True(room.FindPlayer("bob")!.HasRaisedHand);

        Assert.True(_registry.ToggleHand(room.Id, "bob"));
        Assert.False(room.FindPlayer("bob")!.HasRaisedHand);

        Assert.False(_registry.ToggleHand(room.Id, "carol"));
    }

    [Fact]
    public void ToggleHand_IsRefused_WhenTheRoomDoesNotAllowVolunteers()
    {
        var room = CreateJiraRoom(allowVolunteers: false);
        _registry.Join(room.Id, "alice", "Alice");
        _registry.StartEstimation(room.Id, "alice", "ABC-1");

        Assert.False(_registry.ToggleHand(room.Id, "alice"));
        Assert.False(room.FindPlayer("alice")!.HasRaisedHand);
    }

    [Fact]
    public void SimpleRooms_NeverAllowVolunteers_BecauseThereIsNothingToAssign()
    {
        var room = _registry.CreateRoom(new PokerRoomCreateRequest
        {
            Name = "Team A",
            Mode = PokerRoomMode.Simple,
            Deck = [.. PokerDeck.Default],
            AllowVolunteers = true,
            CreatorUserName = "alice",
            CreatorDisplayName = "Alice",
        });

        Assert.False(room.AllowVolunteers);
        _registry.Join(room.Id, "alice", "Alice");
        Assert.False(_registry.ToggleHand(room.Id, "alice"));
    }

    [Fact]
    public void Hands_DropWhenTheEstimatedIssueChanges_ButSurviveANewRound()
    {
        var room = CreateJiraRoom(allowVolunteers: true);
        _registry.Join(room.Id, "alice", "Alice");
        _registry.Join(room.Id, "bob", "Bob");
        _registry.StartEstimation(room.Id, "alice", "ABC-1");
        _registry.ToggleHand(room.Id, "bob");

        _registry.ResetVotes(room.Id, "alice");
        Assert.True(room.FindPlayer("bob")!.HasRaisedHand);

        _registry.StartEstimation(room.Id, "alice", "ABC-2");
        Assert.False(room.FindPlayer("bob")!.HasRaisedHand);

        _registry.ToggleHand(room.Id, "bob");
        _registry.BackToTable(room.Id, "alice");
        Assert.False(room.FindPlayer("bob")!.HasRaisedHand);
    }

    [Fact]
    public void Deck_OffersTheCoffeeCard_AndNoHalfPoint()
    {
        Assert.Contains(PokerDeck.CoffeeCard, PokerDeck.Default);
        Assert.DoesNotContain("0.5", PokerDeck.Default);
    }

    [Fact]
    public void VoteStats_TreatsTheCoffeeCardAsAPlayedCard_ButNotAsAnEstimate()
    {
        var stats = PokerVoteStats.Compute(["8", "8", PokerDeck.CoffeeCard, PokerDeck.CoffeeCard, PokerDeck.CoffeeCard]);

        Assert.Equal(5, stats.VoteCount);
        Assert.Equal(0, stats.QuestionCount);
        Assert.Equal(8m, stats.Average);
        Assert.Equal("8", stats.Suggested);
        Assert.Equal(40, stats.ConsensusPercent);
        Assert.Equal(3, stats.Distribution[PokerDeck.CoffeeCard]);
    }

    [Fact]
    public void VoteStats_ComputesNumericAggregates_ExcludingQuestionCards()
    {
        var stats = PokerVoteStats.Compute(["0.5", "1", "2", PokerDeck.QuestionCard, null]);

        Assert.Equal(4, stats.VoteCount);
        Assert.Equal(1, stats.QuestionCount);
        Assert.Equal(0.5m, stats.Min);
        Assert.Equal(2m, stats.Max);
        Assert.Equal(1.2m, stats.Average);
    }

    [Fact]
    public void VoteStats_AllQuestionCards_HasNoNumericAggregates()
    {
        var stats = PokerVoteStats.Compute([PokerDeck.QuestionCard, PokerDeck.QuestionCard]);

        Assert.Equal(2, stats.VoteCount);
        Assert.Equal(2, stats.QuestionCount);
        Assert.Null(stats.Min);
        Assert.Null(stats.Max);
        Assert.Null(stats.Average);
        Assert.Null(stats.Median);
        Assert.Null(stats.Suggested);
        Assert.Equal(0, stats.ConsensusPercent);
    }

    [Fact]
    public void VoteStats_Median_AveragesTheTwoMiddleVotes_ForAnEvenCount()
    {
        Assert.Equal(3.5m, PokerVoteStats.Compute(["1", "2", "8", "5"]).Median);
        Assert.Equal(5m, PokerVoteStats.Compute(["1", "2", "5", "8", "13"]).Median);
    }

    [Fact]
    public void VoteStats_SuggestsTheMostPlayedCard_AndScoresConsensusOverTheWholeTable()
    {
        var stats = PokerVoteStats.Compute(["8", "5", "8", "3", "2", "21", PokerDeck.QuestionCard]);

        Assert.Equal("8", stats.Suggested);
        Assert.Equal(29, stats.ConsensusPercent);
    }

    [Fact]
    public void VoteStats_TiedSuggestion_TakesTheCardClosestToTheAverage()
    {
        var stats = PokerVoteStats.Compute(["3", "3", "13", "13", "8", "8"]);

        Assert.Equal(8m, stats.Average);
        Assert.Equal("8", stats.Suggested);
    }

    [Fact]
    public void VoteStats_Distribution_CountsEveryPlayedCard()
    {
        var stats = PokerVoteStats.Compute(["8", "8", "3", PokerDeck.QuestionCard, null]);

        Assert.Equal(2, stats.Distribution["8"]);
        Assert.Equal(1, stats.Distribution["3"]);
        Assert.Equal(1, stats.Distribution[PokerDeck.QuestionCard]);
        Assert.False(stats.Distribution.ContainsKey("5"));
    }
}

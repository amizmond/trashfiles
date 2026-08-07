using System.Globalization;

using Estimation.Core.Poker.Models;

namespace Estimation.Core.Poker.Services;

public record PokerVoteStats(
    int VoteCount,
    int QuestionCount,
    decimal? Min,
    decimal? Max,
    decimal? Average,
    decimal? Median,
    string? Suggested,
    int ConsensusPercent,
    IReadOnlyDictionary<string, int> Distribution)
{
    public static PokerVoteStats Compute(IEnumerable<string?> votes)
    {
        var voteCount = 0;
        var questionCount = 0;
        var numeric = new List<decimal>();
        var distribution = new Dictionary<string, int>(StringComparer.Ordinal);
        var numericCards = new Dictionary<string, (decimal Value, int Count)>(StringComparer.Ordinal);

        foreach (var vote in votes)
        {
            if (string.IsNullOrEmpty(vote))
            {
                continue;
            }

            voteCount++;
            distribution[vote] = distribution.GetValueOrDefault(vote) + 1;

            if (vote == PokerDeck.QuestionCard)
            {
                questionCount++;
            }
            else if (decimal.TryParse(vote, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            {
                var current = numericCards.GetValueOrDefault(vote);
                numericCards[vote] = (value, current.Count + 1);
                numeric.Add(value);
            }
        }

        if (numeric.Count == 0)
        {
            return new PokerVoteStats(voteCount, questionCount, null, null, null, null, null, 0, distribution);
        }

        var average = Math.Round(numeric.Average(), 1);

        var suggested = numericCards
            .OrderByDescending(c => c.Value.Count)
            .ThenBy(c => Math.Abs(c.Value.Value - average))
            .ThenByDescending(c => c.Value.Value)
            .First();

        return new PokerVoteStats(
            voteCount,
            questionCount,
            numeric.Min(),
            numeric.Max(),
            average,
            ComputeMedian(numeric),
            suggested.Key,
            (int)Math.Round(suggested.Value.Count * 100m / voteCount, MidpointRounding.AwayFromZero),
            distribution);
    }

    private static decimal ComputeMedian(List<decimal> numeric)
    {
        numeric.Sort();
        var middle = numeric.Count / 2;
        return numeric.Count % 2 == 1
            ? numeric[middle]
            : Math.Round((numeric[middle - 1] + numeric[middle]) / 2m, 1);
    }
}

using Estimation.Core.Features.Hygiene.Services;
using Xunit;

namespace Estimation.Core.Tests.Features.Hygiene;

public class HygieneTextTests
{
    [Fact]
    public void Normalize_strips_jira_wiki_markup_and_collapses_whitespace()
    {
        const string text = "h2. Task Description\r\n\r\n* first *bold* point\r\n{panel:title=Result}the outcome{panel}\r\n||col a||col b||\r\n[docs|http://example.test] and {{mono}}";

        var normalized = HygieneText.Normalize(text);

        Assert.Equal("Task Description first bold point the outcome col a col b docs and mono", normalized);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("h2. \n{panel}{panel}\n** --", false)]
    [InlineData("h2. Result", true)]
    [InlineData("42", true)]
    public void HasContent_ignores_markup_and_symbols(string? text, bool expected)
    {
        Assert.Equal(expected, HygieneText.HasContent(text));
    }

    [Theory]
    [InlineData("Task Description done", "Task Description", true)]
    [InlineData("task   description done", "Task Description", true)]
    [InlineData("The results are in", "Result", false)]
    [InlineData("The result is in", "Result", true)]
    [InlineData("Description of the task", "Task Description", false)]
    [InlineData("", "Result", false)]
    public void ContainsPhrase_matches_whole_words_in_order_ignoring_case(string text, string phrase, bool expected)
    {
        Assert.Equal(expected, HygieneText.ContainsPhrase(HygieneText.Normalize(text), phrase));
    }

    [Fact]
    public void MissingPhrases_lists_the_phrases_that_are_absent()
    {
        var text = HygieneText.Normalize("h2. Task Description\nsomething");

        var missing = HygieneText.MissingPhrases(text, ["Task Description", "Result"]);

        Assert.Equal(["Result"], missing);
    }

    [Fact]
    public void CountOtherWords_is_zero_for_an_unfilled_template()
    {
        var text = HygieneText.Normalize("h2. Task Description\n\nh2. Result\n-");

        Assert.Equal(0, HygieneText.CountOtherWords(text, ["Task Description", "Result"]));
    }

    [Fact]
    public void CountOtherWords_counts_words_outside_the_phrases()
    {
        var text = HygieneText.Normalize("h2. Task Description\nBuild the *engine*.\nh2. Result\n-");

        Assert.Equal(3, HygieneText.CountOtherWords(text, ["Task Description", "Result"]));
    }

    [Fact]
    public void CountOtherWords_does_not_remove_partial_matches()
    {
        var text = HygieneText.Normalize("Results matter");

        Assert.Equal(2, HygieneText.CountOtherWords(text, ["Result"]));
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("- -- ***", 0)]
    [InlineData("n/a", 1)]
    [InlineData("one two 3", 3)]
    public void CountWords_counts_tokens_with_a_letter_or_digit(string? text, int expected)
    {
        Assert.Equal(expected, HygieneText.CountWords(text));
    }

    [Fact]
    public void Excerpt_shortens_long_text()
    {
        var text = new string('a', 300);

        var excerpt = HygieneText.Excerpt(text, 20);

        Assert.Equal(new string('a', 20) + "…", excerpt);
        Assert.Null(HygieneText.Excerpt("h1. "));
    }
}

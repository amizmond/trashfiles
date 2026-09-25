using Estimation.Components.Train;
using Estimation.Core.Train.Services;
using Xunit;

namespace Estimation.Tests.Train;

public class ArtFilterValuesInputTests
{
    [Theory]
    [InlineData("!A")]
    [InlineData("!(empty)")]
    [InlineData("!A,!(empty)")]
    public void A_value_added_after_removing_every_value_of_a_not_rule_keeps_not(string stored)
    {
        var cleared = JiraValueFilter.Parse(stored).WithoutValue("A").WithEmpty(false);

        Assert.False(cleared.IsActive);
        Assert.Equal("!B", ArtFilterValuesInput.Add(cleared, true, ["B"]).ToCsv());
    }

    [Fact]
    public void A_value_added_to_an_empty_rule_without_not_is_an_include()
    {
        Assert.Equal("B", ArtFilterValuesInput.Add(JiraValueFilter.Any, false, ["B"]).ToCsv());
    }

    [Theory]
    [InlineData("A", true, "A,B")]
    [InlineData("!A", false, "!A,!B")]
    [InlineData("(empty)", true, "B,(empty)")]
    public void A_rule_with_values_keeps_its_own_polarity(string stored, bool not, string expected)
    {
        Assert.Equal(expected, ArtFilterValuesInput.Add(JiraValueFilter.Parse(stored), not, ["B"]).ToCsv());
    }

    [Fact]
    public void A_mixed_rule_takes_the_added_value_as_an_include()
    {
        Assert.Equal("A,B,!C", ArtFilterValuesInput.Add(JiraValueFilter.Parse("A,!C"), true, ["B"]).ToCsv());
    }

    [Theory]
    [InlineData(null, "OtherComment", true, "!OtherComment")]
    [InlineData(null, "OtherComment", false, "OtherComment")]
    [InlineData("", " a , b ", true, "!a,!b")]
    [InlineData("!TestComment", "OtherComment", false, "!TestComment,!OtherComment")]
    [InlineData("TestComment", "OtherComment", true, "TestComment,OtherComment")]
    [InlineData(null, null, true, null)]
    [InlineData("!A", "(no component)", false, "!A")]
    public void Typed_text_that_was_not_added_is_saved_with_the_rule_polarity(string? stored, string? pending, bool not, string? expected)
    {
        Assert.Equal(expected, ArtFilterValuesInput.Merge(stored, pending, not, "component"));
    }
}

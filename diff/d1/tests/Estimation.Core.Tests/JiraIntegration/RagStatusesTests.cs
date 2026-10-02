using Estimation.Core.JiraIntegration.Models;
using Xunit;

namespace Estimation.Core.Tests.JiraIntegration;

public class RagStatusesTests
{
    [Theory]
    [InlineData("Green – Everything going to plan", "Green")]
    [InlineData("Amber – Still going to plan but issues", "Amber")]
    [InlineData("Red – Issues have affected the plan for this request", "Red")]
    [InlineData("Green - Everything going to plan", "Green")]
    [InlineData("  red: reworded by a Jira admin ", "Red")]
    [InlineData("AMBER", "Amber")]
    [InlineData("Green", "Green")]
    public void An_option_is_recognised_by_its_first_word(string optionText, string expected)
    {
        Assert.Equal(expected, RagStatuses.Normalize(optionText));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_text_is_no_status(string? optionText)
    {
        Assert.Null(RagStatuses.Normalize(optionText));
    }

    [Theory]
    [InlineData("Blue – Delivered", "Blue – Delivered")]
    [InlineData(" Greenish ", "Greenish")]
    [InlineData("Not red", "Not red")]
    public void An_option_that_starts_with_no_known_colour_keeps_its_text(string optionText, string expected)
    {
        Assert.Equal(expected, RagStatuses.Normalize(optionText));
        Assert.False(RagStatuses.IsKnown(optionText));
    }

    [Fact]
    public void The_three_colours_are_the_known_statuses()
    {
        Assert.Equal(new[] { "Green", "Amber", "Red" }, RagStatuses.All);
        Assert.All(RagStatuses.All, colour => Assert.True(RagStatuses.IsKnown(colour)));
        Assert.False(RagStatuses.IsKnown(null));
    }

    [Fact]
    public void Two_values_are_the_same_when_they_name_the_same_colour()
    {
        Assert.True(RagStatuses.Same("Green", "Green - Everything going to plan"));
        Assert.True(RagStatuses.Same(null, " "));
        Assert.True(RagStatuses.Same("Blue – Delivered", "blue – delivered"));
        Assert.False(RagStatuses.Same("Green", "Amber"));
        Assert.False(RagStatuses.Same("Green", null));
    }

    [Fact]
    public void The_label_of_a_colour_is_its_full_sentence()
    {
        Assert.Equal("Green – Everything going to plan", RagStatuses.Label("Green"));
        Assert.Equal("Amber – Still going to plan but issues", RagStatuses.Label("amber"));
        Assert.Equal("Red – Issues have affected the plan for this request", RagStatuses.Label("Red"));
        Assert.Equal("Blue – Delivered", RagStatuses.Label("Blue – Delivered"));
        Assert.Equal(string.Empty, RagStatuses.Label(null));
    }

    [Fact]
    public void Statuses_order_from_green_to_red_then_unknown_then_none()
    {
        var ordered = new[] { null, "Red", "Blue – Delivered", "Green", "Amber" }
            .OrderBy(RagStatuses.Order)
            .ToArray();

        Assert.Equal(new[] { "Green", "Amber", "Red", "Blue – Delivered", null }, ordered);
    }

    [Fact]
    public void The_choices_are_the_three_colours_followed_by_any_other_value_in_use()
    {
        var choices = RagStatuses.Choices(["Red", null, "Blue – Delivered", "blue – delivered", "", "Green - reworded"]);

        Assert.Equal(new[] { "Green", "Amber", "Red", "Blue – Delivered" }, choices);
    }
}

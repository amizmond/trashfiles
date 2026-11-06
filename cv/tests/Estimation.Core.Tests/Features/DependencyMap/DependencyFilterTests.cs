using Estimation.Core.Features.DependencyMap.Services;
using Xunit;

namespace Estimation.Core.Tests.Features.DependencyMap;

public class DependencyFilterTests
{
    private static readonly DependencyRisk[] AllRisks =
    [
        DependencyRisk.OutOfOrder,
        DependencyRisk.AtRisk,
        DependencyRisk.InOrder,
        DependencyRisk.NotChecked
    ];

    [Fact]
    public void Defaults_are_not_active()
    {
        var filter = new DependencyFilter();

        Assert.Equal(0, filter.ActiveCount);
        Assert.False(filter.IsActive);
        Assert.Empty(filter.LinkTypes);
        Assert.False(filter.LinkTypesNot);
        Assert.True(filter.ShowOtherPi);
        Assert.True(filter.ShowOtherArt);
        Assert.False(filter.ShowNotInTool);
        Assert.True(filter.ShowWithinTeam);
        Assert.Empty(filter.Risks);
        Assert.False(filter.ShowUnlinked);
    }

    [Theory]
    [InlineData("types")]
    [InlineData("typesNot")]
    [InlineData("otherPi")]
    [InlineData("otherArt")]
    [InlineData("notInTool")]
    [InlineData("withinTeam")]
    [InlineData("risk")]
    [InlineData("unlinked")]
    public void Each_changed_setting_counts_once(string setting)
    {
        var filter = new DependencyFilter();

        Change(filter, setting);

        Assert.Equal(1, filter.ActiveCount);
        Assert.True(filter.IsActive);
    }

    [Fact]
    public void Every_changed_setting_adds_up()
    {
        var filter = new DependencyFilter();

        foreach (var setting in new[] { "types", "otherPi", "otherArt", "notInTool", "withinTeam", "risk", "unlinked" })
        {
            Change(filter, setting);
        }

        Assert.Equal(7, filter.ActiveCount);
    }

    [Fact]
    public void Several_link_types_or_risks_count_once_each()
    {
        var filter = new DependencyFilter
        {
            LinkTypes = ["Blocks", "Dependency", "Relates"],
            Risks = [DependencyRisk.OutOfOrder, DependencyRisk.AtRisk, DependencyRisk.NotChecked]
        };

        Assert.Equal(2, filter.ActiveCount);
    }

    [Fact]
    public void All_four_risks_selected_counts_as_not_active()
    {
        var filter = new DependencyFilter { Risks = AllRisks };

        Assert.Equal(0, filter.ActiveCount);
        Assert.False(filter.IsActive);
    }

    [Fact]
    public void Repeated_risks_are_counted_by_distinct_value()
    {
        var filter = new DependencyFilter
        {
            Risks = [DependencyRisk.AtRisk, DependencyRisk.AtRisk, DependencyRisk.AtRisk, DependencyRisk.AtRisk]
        };

        Assert.Equal(1, filter.ActiveCount);
    }

    [Fact]
    public void Clear_restores_the_defaults()
    {
        var filter = new DependencyFilter
        {
            LinkTypes = ["Blocks"],
            LinkTypesNot = true,
            ShowOtherPi = false,
            ShowOtherArt = false,
            ShowNotInTool = true,
            ShowWithinTeam = false,
            Risks = [DependencyRisk.OutOfOrder],
            ShowUnlinked = true
        };

        filter.Clear();

        Assert.Equal(0, filter.ActiveCount);
        Assert.Empty(filter.LinkTypes);
        Assert.False(filter.LinkTypesNot);
        Assert.True(filter.ShowOtherPi);
        Assert.True(filter.ShowOtherArt);
        Assert.False(filter.ShowNotInTool);
        Assert.True(filter.ShowWithinTeam);
        Assert.Empty(filter.Risks);
        Assert.False(filter.ShowUnlinked);
    }

    [Fact]
    public void Known_link_types_drop_unknown_names_and_take_the_known_spelling()
    {
        var filter = new DependencyFilter { LinkTypes = ["blocks", "Depends", " RELATES ", "Blocks"] };

        filter.KeepKnownLinkTypes(["Blocks", "Relates"]);

        Assert.Equal(["Blocks", "Relates"], filter.LinkTypes);
        Assert.Equal(1, filter.ActiveCount);
    }

    [Fact]
    public void Known_link_types_turn_the_filter_off_when_none_is_left()
    {
        var filter = new DependencyFilter { LinkTypes = ["Depends"] };

        filter.KeepKnownLinkTypes(["Blocks"]);

        Assert.Empty(filter.LinkTypes);
        Assert.Equal(0, filter.ActiveCount);
    }

    [Fact]
    public void Known_link_types_leave_an_empty_filter_empty()
    {
        var filter = new DependencyFilter();

        filter.KeepKnownLinkTypes(["Blocks"]);

        Assert.Empty(filter.LinkTypes);
    }

    [Fact]
    public void Known_link_types_keep_hidden_types_that_this_scope_does_not_have()
    {
        var filter = new DependencyFilter { LinkTypes = ["cloners", " Duplicate ", "CLONERS"], LinkTypesNot = true };

        filter.KeepKnownLinkTypes(["Blocks", "Cloners"]);

        Assert.Equal(["Cloners", "Duplicate"], filter.LinkTypes);
        Assert.True(filter.LinkTypesNot);
        Assert.True(filter.ShowsLinkType("Blocks"));
        Assert.False(filter.ShowsLinkType("Cloners"));
    }

    [Fact]
    public void Known_link_types_turn_not_off_when_no_type_is_left()
    {
        var filter = new DependencyFilter { LinkTypes = [" "], LinkTypesNot = true };

        filter.KeepKnownLinkTypes(["Blocks"]);

        Assert.Empty(filter.LinkTypes);
        Assert.False(filter.LinkTypesNot);
    }

    [Fact]
    public void Not_counts_once_with_link_types_and_not_at_all_without_them()
    {
        Assert.Equal(1, new DependencyFilter { LinkTypes = ["Cloners", "Relates"], LinkTypesNot = true }.ActiveCount);
        Assert.Equal(0, new DependencyFilter { LinkTypesNot = true }.ActiveCount);
        Assert.Equal(0, new DependencyFilter { LinkTypes = [" ", ""] }.ActiveCount);
    }

    [Theory]
    [InlineData(false, "Blocks", true)]
    [InlineData(false, "cloners", true)]
    [InlineData(false, "Relates", false)]
    [InlineData(true, "Blocks", false)]
    [InlineData(true, " CLONERS ", false)]
    [InlineData(true, "Relates", true)]
    public void Listed_link_types_are_the_only_ones_shown_or_with_not_the_hidden_ones(bool not, string type, bool shown)
    {
        var filter = new DependencyFilter { LinkTypes = ["Blocks", "Cloners"], LinkTypesNot = not };

        Assert.Equal(shown, filter.ShowsLinkType(type));
    }

    [Fact]
    public void Every_link_type_is_shown_without_a_type_filter()
    {
        var filter = new DependencyFilter { LinkTypesNot = true };

        Assert.True(filter.ShowsLinkType("Cloners"));
        Assert.True(filter.ShowsLinkType(null));
    }

    [Fact]
    public void Toggling_a_type_without_a_filter_hides_just_that_type()
    {
        var filter = new DependencyFilter();

        filter.ToggleLinkType("Cloners", KnownTypes);

        Assert.True(filter.LinkTypesNot);
        Assert.Equal(["Cloners"], filter.LinkTypes);
        Assert.False(filter.ShowsLinkType("Cloners"));
        Assert.True(filter.ShowsLinkType("Blocks"));
        Assert.Equal(1, filter.ActiveCount);
    }

    [Fact]
    public void Toggling_hidden_types_adds_and_removes_them_until_the_filter_is_off()
    {
        var filter = new DependencyFilter();

        filter.ToggleLinkType("Cloners", KnownTypes);
        filter.ToggleLinkType("Relates", KnownTypes);

        Assert.True(filter.LinkTypesNot);
        Assert.Equal(["Cloners", "Relates"], filter.LinkTypes);

        filter.ToggleLinkType("cloners", KnownTypes);

        Assert.Equal(["Relates"], filter.LinkTypes);
        Assert.True(filter.ShowsLinkType("Cloners"));

        filter.ToggleLinkType("Relates", KnownTypes);

        Assert.Empty(filter.LinkTypes);
        Assert.False(filter.LinkTypesNot);
        Assert.Equal(0, filter.ActiveCount);
    }

    [Fact]
    public void Toggling_in_show_only_mode_keeps_that_mode()
    {
        var filter = new DependencyFilter { LinkTypes = ["Blocks"] };

        filter.ToggleLinkType("Relates", KnownTypes);

        Assert.False(filter.LinkTypesNot);
        Assert.Equal(["Blocks", "Relates"], filter.LinkTypes);

        filter.ToggleLinkType("Blocks", KnownTypes);

        Assert.False(filter.LinkTypesNot);
        Assert.Equal(["Relates"], filter.LinkTypes);
    }

    [Fact]
    public void Showing_the_last_hidden_type_in_show_only_mode_turns_the_filter_off()
    {
        var filter = new DependencyFilter { LinkTypes = ["Blocks", "Relates"] };

        filter.ToggleLinkType("Cloners", KnownTypes);

        Assert.Empty(filter.LinkTypes);
        Assert.False(filter.LinkTypesNot);
        Assert.All(KnownTypes, t => Assert.True(filter.ShowsLinkType(t)));
    }

    [Fact]
    public void Hiding_the_last_shown_type_in_show_only_mode_hides_every_type()
    {
        var filter = new DependencyFilter { LinkTypes = ["Blocks"] };

        filter.ToggleLinkType("Blocks", KnownTypes);

        Assert.True(filter.LinkTypesNot);
        Assert.Equal(KnownTypes, filter.LinkTypes);
        Assert.All(KnownTypes, t => Assert.False(filter.ShowsLinkType(t)));
        Assert.Equal(1, filter.ActiveCount);
    }

    [Fact]
    public void Show_all_link_types_turns_only_the_type_filter_off()
    {
        var filter = new DependencyFilter { LinkTypes = ["Cloners"], LinkTypesNot = true, ShowOtherPi = false };

        filter.ShowAllLinkTypes();

        Assert.Empty(filter.LinkTypes);
        Assert.False(filter.LinkTypesNot);
        Assert.Equal(1, filter.ActiveCount);
    }

    private static readonly string[] KnownTypes = ["Blocks", "Cloners", "Relates"];

    private static void Change(DependencyFilter filter, string setting)
    {
        switch (setting)
        {
            case "types":
                filter.LinkTypes = ["Blocks"];
                break;
            case "typesNot":
                filter.LinkTypes = ["Cloners"];
                filter.LinkTypesNot = true;
                break;
            case "otherPi":
                filter.ShowOtherPi = false;
                break;
            case "otherArt":
                filter.ShowOtherArt = false;
                break;
            case "notInTool":
                filter.ShowNotInTool = true;
                break;
            case "withinTeam":
                filter.ShowWithinTeam = false;
                break;
            case "risk":
                filter.Risks = [DependencyRisk.OutOfOrder];
                break;
            case "unlinked":
                filter.ShowUnlinked = true;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(setting), setting, null);
        }
    }
}

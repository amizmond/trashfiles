using Estimation.Components.DepartmentPlanning;
using Xunit;

namespace Estimation.Tests.DepartmentPlanning;

public class PlanningFormatTests
{
    [Theory]
    [InlineData("10", "10")]
    [InlineData("10.5", "10.5")]
    [InlineData("3.34", "3.34")]
    [InlineData("3.33", "3.33")]
    [InlineData("0.03", "0.03")]
    [InlineData("0", "0")]
    public void Story_points_keep_the_hundredths_a_split_gives_them(string value, string shown)
    {
        Assert.Equal(shown, PlanningFormat.Sp(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
    }
}

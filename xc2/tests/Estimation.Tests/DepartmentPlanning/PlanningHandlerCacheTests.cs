using Estimation.Components.DepartmentPlanning.Views;
using Xunit;

namespace Estimation.Tests.DepartmentPlanning;

public class PlanningHandlerCacheTests
{
    private sealed record Element(string Key);

    [Fact]
    public void The_same_key_gets_the_same_handler_instance_and_it_is_made_once()
    {
        var made = 0;
        var cache = new PlanningHandlerCache<string, Func<string>>(key =>
        {
            made++;
            return () => key;
        });

        var first = cache.For("so:1");
        var second = cache.For("so:1");

        Assert.Same(first, second);
        Assert.Equal(1, made);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void Each_key_gets_its_own_handler_bound_to_that_key()
    {
        var cache = new PlanningHandlerCache<string, Func<string>>(key => () => key, StringComparer.Ordinal);

        var one = cache.For("so:1");
        var two = cache.For("so:2");
        var upper = cache.For("SO:1");

        Assert.NotSame(one, two);
        Assert.NotSame(one, upper);
        Assert.Equal("so:1", one());
        Assert.Equal("so:2", two());
        Assert.Equal("SO:1", upper());
    }

    [Fact]
    public void Clearing_makes_new_handlers_for_the_next_layout()
    {
        var cache = new PlanningHandlerCache<string, Func<string>>(key => () => key);

        var before = cache.For("so:1");
        cache.Clear();
        var after = cache.For("so:1");

        Assert.Equal(1, cache.Count);
        Assert.NotSame(before, after);
        Assert.Same(after, cache.For("so:1"));
    }

    [Fact]
    public void Keyed_by_reference_two_equal_elements_of_different_layouts_get_different_handlers()
    {
        var cache = new PlanningHandlerCache<Element, Func<Element>>(element => () => element, ReferenceEqualityComparer.Instance);
        var old = new Element("pe:1");
        var rebuilt = new Element("pe:1");

        Assert.Equal(old, rebuilt);
        Assert.NotSame(cache.For(old), cache.For(rebuilt));
        Assert.Same(rebuilt, cache.For(rebuilt)());
        Assert.Same(cache.For(old), cache.For(old));
    }
}

namespace Estimation.Components.DepartmentPlanning.Views;

public sealed class PlanningHandlerCache<TKey, THandler>(Func<TKey, THandler> create, IEqualityComparer<TKey>? comparer = null)
    where TKey : notnull
    where THandler : Delegate
{
    private readonly Dictionary<TKey, THandler> _handlers = new(comparer);

    public int Count => _handlers.Count;

    public THandler For(TKey key)
    {
        if (!_handlers.TryGetValue(key, out var handler))
        {
            handler = create(key);
            _handlers.Add(key, handler);
        }

        return handler;
    }

    public void Clear() => _handlers.Clear();
}

using Estimation.HrReporting.Models;
using Microsoft.Extensions.Caching.Memory;

namespace Estimation.HrReporting.Services;

public sealed class HeadcountRowsCache : IDisposable
{
    public const long MaxCachedRows = 250_000;

    private static readonly TimeSpan SlidingExpiration = TimeSpan.FromMinutes(30);

    private static readonly HeadcountColumn[] TextColumns =
        HeadcountColumns.All.Where(c => c.Kind == HeadcountValueKind.Text).ToArray();

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = MaxCachedRows });

    public bool TryGet(int uploadId, out IReadOnlyList<HeadcountRow> rows)
    {
        if (_cache.TryGetValue(uploadId, out IReadOnlyList<HeadcountRow>? cached) && cached is not null)
        {
            rows = cached;
            return true;
        }

        rows = [];
        return false;
    }

    public IReadOnlyList<HeadcountRow> Set(int uploadId, List<HeadcountRow> rows)
    {
        Intern(rows);
        IReadOnlyList<HeadcountRow> stored = rows.AsReadOnly();
        _cache.Set(uploadId, stored, new MemoryCacheEntryOptions
        {
            SlidingExpiration = SlidingExpiration,
            Size = Math.Max(1, rows.Count)
        });
        return stored;
    }

    public void Remove(int uploadId) => _cache.Remove(uploadId);

    public void Dispose() => _cache.Dispose();

    private static void Intern(List<HeadcountRow> rows)
    {
        var pool = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            row.Upload = null;
            foreach (var column in TextColumns)
            {
                if (column.GetValue(row) is not string text)
                {
                    continue;
                }

                if (pool.TryGetValue(text, out var shared))
                {
                    column.SetValue(row, shared);
                }
                else
                {
                    pool[text] = text;
                }
            }
        }
    }
}

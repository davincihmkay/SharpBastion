using System.Collections.Concurrent;

namespace SharpBastion.Domain;

public class PendingKagiSearchQueue
{
    private readonly ConcurrentDictionary<string, PendingKagiSearch> _pending =
        new(StringComparer.OrdinalIgnoreCase);

    public bool Enqueue(PendingKagiSearch search)
    {
        var isDuplicate = _pending.Values.Any(p =>
            p.RepositoryName.Value.Equals(search.RepositoryName.Value, StringComparison.OrdinalIgnoreCase) &&
            p.Query.Value.Equals(search.Query.Value, StringComparison.OrdinalIgnoreCase));

        return !isDuplicate && _pending.TryAdd(search.Id, search);
    }

    public IReadOnlyCollection<PendingKagiSearch> ListPending() => _pending.Values.ToList();

    public bool TryResolve(string id, out PendingKagiSearch? search) => _pending.TryRemove(id, out search);

    public int Count => _pending.Count;
}
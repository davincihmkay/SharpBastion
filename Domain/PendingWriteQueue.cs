using System.Collections.Concurrent;

namespace SharpBastion.Domain;

public class PendingWriteQueue
{
    private readonly ConcurrentDictionary<string, File> _pending =
        new(StringComparer.OrdinalIgnoreCase);

    public bool Enqueue(File file)
    {
        var pendingWrite = file.GetPendingWrite();
        if (pendingWrite is null)
            throw new ArgumentException("File has no pending write.", nameof(file));

        return _pending.TryAdd(pendingWrite.AbsolutePath.Value, file);
    }

    public IReadOnlyCollection<File> ListPending() => _pending.Values.ToList();

    public bool TryResolve(string id, out File? file) => _pending.TryRemove(id, out file);

    public int Count => _pending.Count;
}
using System.Collections.Concurrent;

namespace Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

/// <summary>
/// Serializes work for one authority inside one application process. Deployments where multiple
/// application instances share a workspace must additionally use the repository distributed execution lock.
/// </summary>
public interface IStoryFrameExecutionLock
{
    Task<IAsyncDisposable> AcquireAsync(string key, CancellationToken cancellationToken);
}

public sealed class InProcessStoryFrameExecutionLock : IStoryFrameExecutionLock
{
    private sealed class Entry { public readonly SemaphoreSlim Semaphore = new(1, 1); public int References; }
    private readonly ConcurrentDictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);
    internal int EntryCount => entries.Count;

    public async Task<IAsyncDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Entry entry;
        while (true)
        {
            entry = entries.GetOrAdd(key, static _ => new());
            Interlocked.Increment(ref entry.References);
            if (entries.TryGetValue(key, out var current) && ReferenceEquals(entry, current)) break;
            Interlocked.Decrement(ref entry.References);
        }
        try { await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch { ReleaseReference(key, entry); throw; }
        return new Lease(this, key, entry);
    }

    private void ReleaseReference(string key, Entry entry)
    {
        if (Interlocked.Decrement(ref entry.References) == 0)
            entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
    }
    private sealed class Lease(InProcessStoryFrameExecutionLock owner, string key, Entry entry) : IAsyncDisposable
    {
        private int disposed;
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0) { entry.Semaphore.Release(); owner.ReleaseReference(key, entry); }
            return ValueTask.CompletedTask;
        }
    }
}

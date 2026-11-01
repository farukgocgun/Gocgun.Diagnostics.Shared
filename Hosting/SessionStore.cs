namespace Gocgun.Diagnostics.Shared.Hosting;

/// <summary>In-memory session bag. Credentials stay in this process and expire with the cookie lifetime.</summary>
internal sealed class SessionStore<T> : ISessionSweeper
    where T : class
{
    private readonly Dictionary<string, Entry> items = new(StringComparer.Ordinal);
    private readonly Lock gate = new();
    private readonly DiagnosticsOptions options;
    private readonly TimeProvider time;

    public SessionStore(DiagnosticsOptions options, TimeProvider time)
    {
        this.options = options;
        this.time = time;
    }

    public async ValueTask SweepAsync()
    {
        List<Entry> expired = [];
        lock (gate)
        {
            var now = time.GetUtcNow();
            foreach (var pair in items.Where(pair => pair.Value.ExpiresAt <= now).ToArray())
            {
                items.Remove(pair.Key);
                expired.Add(pair.Value);
            }
        }

        foreach (var entry in expired)
        {
            await DisposeValueAsync(entry.Value);
        }
    }

    /// <summary>Returns the live session, or creates one. An expired entry is disposed before its replacement is handed out.</summary>
    /// <param name="sid">Session id.</param>
    /// <param name="factory">Creates the value for a new session.</param>
    /// <returns>The session value.</returns>
    internal async ValueTask<T> GetOrAddAsync(string sid, Func<string, T> factory)
    {
        Entry? expired;
        lock (gate)
        {
            if (!items.TryGetValue(sid, out expired) || expired.ExpiresAt > time.GetUtcNow())
            {
                return expired?.Value ?? Add(sid, factory);
            }

            items.Remove(sid);
        }

        // The old value can share resources keyed by the same sid, so it is released before the replacement exists.
        await DisposeValueAsync(expired.Value);
        lock (gate)
        {
            if (items.TryGetValue(sid, out var raced) && raced.ExpiresAt > time.GetUtcNow())
            {
                return raced.Value;
            }

            return Add(sid, factory);
        }
    }

    internal bool TryGet(string sid, out T? value)
    {
        lock (gate)
        {
            if (items.TryGetValue(sid, out var existing) && existing.ExpiresAt > time.GetUtcNow())
            {
                value = existing.Value;
                return true;
            }

            value = null;
            return false;
        }
    }

    /// <summary>Removes one session and disposes its value.</summary>
    /// <param name="sid">Session id.</param>
    /// <returns>The removed value, or <see langword="null"/> when there was none.</returns>
    internal async ValueTask<T?> RemoveAsync(string sid)
    {
        Entry? removed;
        lock (gate)
        {
            items.Remove(sid, out removed);
        }

        if (removed is null)
        {
            return null;
        }

        await DisposeValueAsync(removed.Value);
        return removed.Value;
    }

    private static async ValueTask DisposeValueAsync(T value)
    {
        if (value is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else if (value is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private T Add(string sid, Func<string, T> factory)
    {
        var created = factory(sid);
        items[sid] = new Entry(created, time.GetUtcNow() + options.SessionLifetime);
        return created;
    }

    private sealed record Entry(T Value, DateTimeOffset ExpiresAt);
}

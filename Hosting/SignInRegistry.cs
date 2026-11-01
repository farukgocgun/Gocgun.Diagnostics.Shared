using System.Collections.Concurrent;

namespace Gocgun.Diagnostics.Shared.Hosting;

/// <summary>Session ids that may still use a cookie. Logout removes the id, so a copied cookie stops working.</summary>
internal sealed class SignInRegistry
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> active = new(StringComparer.Ordinal);
    private readonly DiagnosticsOptions options;
    private readonly TimeProvider time;

    public SignInRegistry(DiagnosticsOptions options, TimeProvider time)
    {
        this.options = options;
        this.time = time;
    }

    internal void Add(string sid)
    {
        var now = time.GetUtcNow();
        foreach (var pair in active)
        {
            if (pair.Value <= now)
            {
                active.TryRemove(pair.Key, out _);
            }
        }

        active[sid] = now + options.SessionLifetime;
    }

    internal void Remove(string sid)
    {
        active.TryRemove(sid, out _);
    }

    internal bool IsActive(string sid)
    {
        return active.TryGetValue(sid, out var expires) && expires > time.GetUtcNow();
    }
}

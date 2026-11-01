namespace Gocgun.Diagnostics.Shared.Auth;

/// <summary>Only a loopback host may be bound. Empty input keeps the loopback default.</summary>
internal static class BindingRules
{
    internal static void EnsureSafe(string? urls)
    {
        if (string.IsNullOrWhiteSpace(urls))
        {
            return;
        }

        foreach (var part in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Uri.TryCreate(part, UriKind.Absolute, out var uri))
            {
                throw new InvalidOperationException("Binding URL must be absolute.");
            }

            if (!IsLoopback(uri))
            {
                throw new InvalidOperationException("Binding URL must use a loopback host.");
            }
        }
    }

    /// <summary>Lists the addresses the server really bound that are not loopback.</summary>
    /// <param name="addresses">Addresses from <c>IServerAddressesFeature</c>.</param>
    /// <returns>The offending addresses; empty when all are loopback.</returns>
    internal static IReadOnlyList<string> FindNonLoopback(IEnumerable<string> addresses)
    {
        return addresses
            .Where(address => !Uri.TryCreate(address, UriKind.Absolute, out var uri) || !IsLoopback(uri))
            .ToArray();
    }

    private static bool IsLoopback(Uri uri)
    {
        return uri.IsLoopback || uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase);
    }
}

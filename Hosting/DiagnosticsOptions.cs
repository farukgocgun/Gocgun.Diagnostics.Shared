using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Gocgun.Diagnostics.Shared.Hosting;

/// <summary>Settings from the <c>Diagnostics</c> configuration section. Every value has a safe default.</summary>
internal sealed class DiagnosticsOptions
{
    /// <summary>Default cookie and in-memory session lifetime in hours.</summary>
    internal const int DefaultSessionHours = 8;

    /// <summary>Default number of failed logins before the lockout starts.</summary>
    internal const int DefaultLoginMaxFailures = 5;

    /// <summary>Default lockout length in seconds.</summary>
    internal const int DefaultLoginLockoutSeconds = 300;

    /// <summary>Default interval between background sweeps of expired sessions, in seconds.</summary>
    internal const int DefaultSessionSweepSeconds = 60;

    /// <summary>Default largest string value the cache tool reads, in bytes (1 MiB).</summary>
    internal const long DefaultMaxValueBytes = 1_048_576;

    internal TimeSpan SessionLifetime { get; init; } = TimeSpan.FromHours(DefaultSessionHours);

    internal int LoginMaxFailures { get; init; } = DefaultLoginMaxFailures;

    internal TimeSpan LoginLockout { get; init; } = TimeSpan.FromSeconds(DefaultLoginLockoutSeconds);

    internal TimeSpan SessionSweepInterval { get; init; } = TimeSpan.FromSeconds(DefaultSessionSweepSeconds);

    internal long MaxValueBytes { get; init; } = DefaultMaxValueBytes;

    /// <summary>
    /// Gets the hosts the connection form may target, as <c>host</c> or <c>host:port</c> (IPv6 as <c>[addr]:port</c>).
    /// Empty means any host. Matching is on the typed text, not on the resolved address.
    /// </summary>
    internal IReadOnlyList<string> AllowedHosts { get; init; } = [];

    /// <summary>Gets the only folder tree contract assemblies may be loaded from. Empty means loading is refused.</summary>
    internal string? ContractRoot { get; init; }

    internal static DiagnosticsOptions From(IConfiguration configuration)
    {
        var root = configuration["Diagnostics:ContractRoot"];
        return new DiagnosticsOptions
        {
            SessionLifetime = TimeSpan.FromHours(Positive(configuration, "Diagnostics:SessionHours", DefaultSessionHours)),
            LoginMaxFailures = (int)Positive(configuration, "Diagnostics:LoginMaxFailures", DefaultLoginMaxFailures),
            LoginLockout = TimeSpan.FromSeconds(Positive(configuration, "Diagnostics:LoginLockoutSeconds", DefaultLoginLockoutSeconds)),
            SessionSweepInterval = TimeSpan.FromSeconds(Positive(configuration, "Diagnostics:SessionSweepSeconds", DefaultSessionSweepSeconds)),
            MaxValueBytes = Positive(configuration, "Diagnostics:MaxValueBytes", DefaultMaxValueBytes),
            AllowedHosts = HostList(configuration.GetSection("Diagnostics:AllowedHosts")),
            ContractRoot = string.IsNullOrWhiteSpace(root) ? null : root.Trim(),
        };
    }

    /// <summary>Checks a typed host and port against <see cref="AllowedHosts"/>.</summary>
    /// <param name="host">Host from the connection form.</param>
    /// <param name="port">Port from the connection form.</param>
    /// <returns><see langword="true"/> when the list is empty or an entry matches.</returns>
    internal bool IsHostAllowed(string host, int port)
    {
        if (AllowedHosts.Count == 0)
        {
            return true;
        }

        var typed = Unbracket(host.Trim());
        return AllowedHosts
            .Select(SplitEntry)
            .Any(entry => string.Equals(entry.Host, typed, StringComparison.OrdinalIgnoreCase) && (entry.Port is null || entry.Port == port));
    }

    // Accepts a comma or semicolon separated string, or an array section (Diagnostics:AllowedHosts:0, :1, ...).
    private static string[] HostList(IConfigurationSection section)
    {
        IEnumerable<string> values = section.Value is not null ? [section.Value] : section.GetChildren().Select(child => child.Value ?? string.Empty);
        return values
            .SelectMany(value => value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray();
    }

    private static (string Host, int? Port) SplitEntry(string entry)
    {
        var close = entry.IndexOf(']', StringComparison.Ordinal);
        if (entry.StartsWith('[') && close > 0)
        {
            var rest = entry[(close + 1)..];
            return (entry[1..close], rest.StartsWith(':') ? ParsePort(rest[1..]) : null);
        }

        // One colon is host:port. None, or several (a bare IPv6 address), is a host alone.
        var colon = entry.IndexOf(':', StringComparison.Ordinal);
        if (colon > 0 && colon == entry.LastIndexOf(':'))
        {
            return (entry[..colon], ParsePort(entry[(colon + 1)..]));
        }

        return (entry, null);
    }

    private static int? ParsePort(string text)
    {
        // An unreadable port must not widen the entry to every port, so it matches no port.
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : -1;
    }

    private static string Unbracket(string host)
    {
        return host.Length > 1 && host.StartsWith('[') && host.EndsWith(']') ? host[1..^1] : host;
    }

    private static long Positive(IConfiguration configuration, string key, long fallback)
    {
        return long.TryParse(configuration[key], NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : fallback;
    }
}

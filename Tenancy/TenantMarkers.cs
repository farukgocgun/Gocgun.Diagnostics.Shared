using System.Text;

namespace Gocgun.Diagnostics.Shared.Tenancy;

/// <summary>Reads the tenant segment operators filter on. Packages do not enforce tenancy.</summary>
internal static class TenantMarkers
{
    internal const string AmqpHeaderName = "tenant-id";

    internal static string? FromCacheKey(string key)
    {
        const string prefix = "t:";
        if (!key.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = key.AsSpan(prefix.Length);
        var end = rest.IndexOf(':');
        if (end <= 0)
        {
            return null;
        }

        return rest[..end].ToString();
    }

    internal static string? FromHeaders(IReadOnlyDictionary<string, string> headers)
    {
        return headers.FirstOrDefault(pair => pair.Key.Equals(AmqpHeaderName, StringComparison.OrdinalIgnoreCase)).Value;
    }

    internal static string HeaderToString(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is byte[] bytes)
        {
            return Encoding.UTF8.GetString(bytes);
        }

        if (value is ReadOnlyMemory<byte> memory)
        {
            return Encoding.UTF8.GetString(memory.Span);
        }

        return value.ToString() ?? string.Empty;
    }
}

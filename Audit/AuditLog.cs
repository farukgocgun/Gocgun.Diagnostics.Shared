using System.Text.Json;

namespace Gocgun.Diagnostics.Shared.Audit;

/// <summary>Append-only local audit log. Each line is operator, time, profile, target, contract, and action.</summary>
internal sealed class AuditLog
{
    private readonly Lock gate = new();
    private readonly string path;
    private readonly TimeProvider time;

    internal AuditLog(string path, TimeProvider time)
    {
        this.path = path;
        this.time = time;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    internal static IReadOnlyList<string> FieldNames { get; } = ["at", "operator", "profile", "target", "contract", "action"];

    internal void Append(AuditEntry entry)
    {
        var line = JsonSerializer.Serialize(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["at"] = time.GetUtcNow().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["operator"] = entry.OperatorName,
            ["profile"] = entry.Profile,
            ["target"] = entry.Target,
            ["contract"] = entry.Contract,
            ["action"] = entry.Action,
        });
        lock (gate)
        {
            File.AppendAllText(path, line + "\n");
        }
    }

    internal string ReadAll()
    {
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        lock (gate)
        {
            return File.ReadAllText(path);
        }
    }
}

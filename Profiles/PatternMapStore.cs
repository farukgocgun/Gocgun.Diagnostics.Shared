using System.Text.Json;
using Gocgun.Diagnostics.Shared.Auth;
using Gocgun.Diagnostics.Shared.Decoding;

namespace Gocgun.Diagnostics.Shared.Profiles;

/// <summary>Pattern maps on disk. The file lists patterns and contract names only.</summary>
internal sealed class PatternMapStore
{
    private readonly string path;

    internal PatternMapStore(string path)
    {
        this.path = path;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    internal void Save(IReadOnlyList<PatternMap> maps)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var map in maps)
            {
                writer.WriteStartObject();
                writer.WriteString("pattern", map.Pattern);
                writer.WriteString("contractType", map.ContractType);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        OwnerOnlyFile.WriteAllBytes(path, stream.ToArray());
    }

    internal IReadOnlyList<PatternMap> Load()
    {
        if (!File.Exists(path))
        {
            return [];
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var maps = new List<PatternMap>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var pattern = item.GetProperty("pattern").GetString() ?? string.Empty;
            var contract = item.GetProperty("contractType").GetString() ?? string.Empty;
            maps.Add(new PatternMap(pattern, contract));
        }

        return maps;
    }

    internal string Export()
    {
        return File.Exists(path) ? File.ReadAllText(path) : "[]";
    }
}

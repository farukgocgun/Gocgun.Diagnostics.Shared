using System.Reflection;
using System.Runtime.Loader;
using ProtoBuf;
using ProtoBuf.Meta;

namespace Gocgun.Diagnostics.Shared.Decoding;

/// <summary>protobuf-net contracts loaded from a folder the operator chooses. The tool never downloads code.</summary>
internal sealed class ContractCatalog
{
    private readonly Dictionary<string, Type> types;
    private readonly RuntimeTypeModel model;

    private ContractCatalog(Dictionary<string, Type> types)
    {
        this.types = types;

        // One model per catalog: contracts from different folders never share serializer state.
        model = RuntimeTypeModel.Create();
    }

    internal static ContractCatalog Empty { get; } = new(new Dictionary<string, Type>(StringComparer.Ordinal));

    internal IReadOnlyCollection<string> TypeNames => types.Keys;

    internal static ContractCatalog FromTypes(IEnumerable<Type> types)
    {
        var found = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            if (TryContractName(type, out var name))
            {
                found[type.FullName ?? type.Name] = type;
                if (name is not null)
                {
                    found[name] = type;
                }
            }
        }

        return new ContractCatalog(found);
    }

    /// <summary>Canonicalizes a folder and demands it lies inside the contract root, with no symbolic link or junction below the root.</summary>
    /// <param name="folder">Folder typed by the operator. A relative path is read against the root.</param>
    /// <param name="contractRoot">Configured <c>Diagnostics:ContractRoot</c>.</param>
    /// <returns>The full folder path.</returns>
    internal static string ResolveFolder(string folder, string? contractRoot)
    {
        if (string.IsNullOrWhiteSpace(contractRoot))
        {
            throw new InvalidOperationException("Contract loading is disabled. Set Diagnostics:ContractRoot to enable it.");
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(contractRoot));
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder.Trim(), root + Path.DirectorySeparatorChar));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = root + Path.DirectorySeparatorChar;
        if (!(full + Path.DirectorySeparatorChar).StartsWith(prefix, comparison))
        {
            throw new InvalidOperationException("Contract folder must be inside Diagnostics:ContractRoot.");
        }

        // The path check above is textual; a link below the root would still lead outside it.
        for (var current = full; current.Length > root.Length; current = Path.GetDirectoryName(current) ?? root)
        {
            var directory = new DirectoryInfo(current);
            if (directory.Exists && IsLink(directory))
            {
                throw new InvalidOperationException("Contract folder must not pass through a symbolic link or junction.");
            }
        }

        return full;
    }

    internal static ContractCatalog LoadFolder(string folder, string? contractRoot)
    {
        var full = ResolveFolder(folder, contractRoot);
        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException("Contract folder does not exist.");
        }

        var context = new ContractLoadContext();
        var loaded = new List<Type>();

        // A linked DLL can point anywhere on disk, so only regular files are loaded.
        foreach (var file in new DirectoryInfo(full).EnumerateFiles("*.dll", SearchOption.TopDirectoryOnly).Where(candidate => !IsLink(candidate)))
        {
            TryLoad(context, file.FullName, loaded);
        }

        return FromTypes(loaded);
    }

    internal bool TryParse(string fullName, ReadOnlyMemory<byte> payload, out object? message, out string? error)
    {
        message = null;
        if (!types.TryGetValue(fullName, out var type))
        {
            error = "Contract is not loaded.";
            return false;
        }

        try
        {
            message = model.Deserialize(type, payload);
            error = null;
            return message is not null;
        }
        catch (Exception ex) when (ex is ProtoException or EndOfStreamException or OverflowException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            error = "Contract parse failed.";
            return false;
        }
    }

    // Catches symlinks, junctions and other reparse points. A hard link cannot be told apart from a regular file,
    // but making one needs write access to the target already. The check is not atomic with the load (TOCTOU);
    // whoever can swap entries below the root can already drop a DLL there.
    private static bool IsLink(FileSystemInfo entry)
    {
        return entry.LinkTarget is not null || entry.Attributes.HasFlag(FileAttributes.ReparsePoint);
    }

    private static void TryLoad(ContractLoadContext context, string file, List<Type> types)
    {
        try
        {
            var assembly = context.LoadFromAssemblyPath(file);
            types.AddRange(assembly.GetExportedTypes());
        }
        catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or FileNotFoundException or ReflectionTypeLoadException)
        {
            _ = ex.HResult;
        }
    }

    private static bool TryContractName(Type type, out string? name)
    {
        name = null;
        if (!type.IsClass || type.IsAbstract || type.ContainsGenericParameters)
        {
            return false;
        }

        var contract = type.GetCustomAttribute<ProtoContractAttribute>(inherit: false);
        if (contract is null)
        {
            return false;
        }

        var trimmed = contract.Name?.TrimStart('.');
        name = string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
        return true;
    }

    // Not collectible: protobuf-net builds serializers for these types, and a collectible assembly cannot be referenced from them.
    private sealed class ContractLoadContext : AssemblyLoadContext
    {
        internal ContractLoadContext()
            : base(isCollectible: false)
        {
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            return null;
        }
    }
}

using System.Collections.Frozen;
using System.Text.Json;
using System.Text.RegularExpressions;
using Gocgun.Diagnostics.Shared.Auth;
using Microsoft.AspNetCore.DataProtection;

namespace Gocgun.Diagnostics.Shared.Profiles;

/// <summary>Saved connection profile. The password is Data Protection ciphertext, never the original text.</summary>
internal sealed partial class ProtectedProfileStore
{
    /// <summary>The name rule, shown when a name is refused.</summary>
    internal const string NameRule = "Profile name must be 1 to 64 letters, digits, dots, underscores or hyphens, and not a Windows device name such as CON or COM1.";

    private const int NameTimeoutMilliseconds = 1000;

    // Windows opens a device, not a file, for these names even with an extension ("nul.json", "CON.dev.json").
    private static readonly FrozenSet<string> WindowsDeviceNames = new[]
    {
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly string directory;
    private readonly IDataProtector protector;

    internal ProtectedProfileStore(string directory, IDataProtector protector)
    {
        this.directory = directory;
        this.protector = protector;
        Directory.CreateDirectory(directory);
    }

    /// <summary>Allows 1 to 64 letters, digits, dots, underscores and hyphens, not only dots, and no Windows device name before the first dot.</summary>
    /// <param name="profileName">Candidate name.</param>
    /// <returns><see langword="true"/> when the name is a safe single file name.</returns>
    internal static bool IsValidName(string? profileName)
    {
        return !string.IsNullOrEmpty(profileName)
            && NameRegex().IsMatch(profileName)
            && profileName.Trim('.').Length > 0
            && !WindowsDeviceNames.Contains(profileName.Split('.')[0]);
    }

    internal void Save(string profileName, string password, IReadOnlyDictionary<string, string> fields)
    {
        var name = SafeName(profileName);
        var payload = new StoredProfile(protector.Protect(password), new Dictionary<string, string>(fields, StringComparer.Ordinal));
        var json = JsonSerializer.Serialize(payload);
        OwnerOnlyFile.WriteAllText(Path.Combine(directory, name + ".json"), json);
    }

    internal LoadedProfile? TryLoad(string profileName)
    {
        var file = Path.Combine(directory, SafeName(profileName) + ".json");
        if (!File.Exists(file))
        {
            return null;
        }

        var stored = JsonSerializer.Deserialize<StoredProfile>(File.ReadAllText(file));
        if (stored is null)
        {
            return null;
        }

        return new LoadedProfile(protector.Unprotect(stored.Password), stored.Fields);
    }

    internal string ReadRaw(string profileName)
    {
        var file = Path.Combine(directory, SafeName(profileName) + ".json");
        return File.Exists(file) ? File.ReadAllText(file) : string.Empty;
    }

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,64}\z", RegexOptions.CultureInvariant, NameTimeoutMilliseconds)]
    private static partial Regex NameRegex();

    private static string SafeName(string profileName)
    {
        if (!IsValidName(profileName))
        {
            throw new ArgumentException(NameRule, nameof(profileName));
        }

        return profileName;
    }

    private sealed record StoredProfile(string Password, Dictionary<string, string> Fields);
}

namespace Gocgun.Diagnostics.Shared.Profiles;

/// <summary>Profile fields plus the unprotected password, kept in memory only.</summary>
internal sealed record LoadedProfile(string Password, IReadOnlyDictionary<string, string> Fields);

namespace Gocgun.Diagnostics.Shared.Decoding;

/// <summary>Maps a key pattern to a protobuf full name. The file stores no values.</summary>
internal sealed record PatternMap(string Pattern, string ContractType);

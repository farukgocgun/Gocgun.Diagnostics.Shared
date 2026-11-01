namespace Gocgun.Diagnostics.Shared.Decoding;

/// <summary>Decoded broker body. The audit log is not this type and has no payload field.</summary>
internal sealed record BusDecodeResult(
    bool IsEnvelope,
    bool IsUserError,
    string? Error,
    string? MessageId,
    string? CorrelationId,
    string? CausationId,
    string? TraceParent,
    string? SentAt,
    string? Tenant,
    string? PayloadType,
    string Display,
    bool TenantHidden);

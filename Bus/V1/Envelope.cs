using ProtoBuf;

namespace Gocgun.Diagnostics.Shared.Bus.V1;

/// <summary>
/// Read-only copy of the <c>Gocgun.Bus</c> broker body (ADR 0018, ADR 0021). The payload type is the type URL.
/// Field numbers and types match the proto3 <c>gocgun.bus.v1.Envelope</c> layout, so bytes from either side decode.
/// </summary>
[ProtoContract(Name = "gocgun.bus.v1.Envelope")]
[CompatibilityLevel(CompatibilityLevel.Level300)]
internal sealed class Envelope
{
    /// <summary>Gets or sets the message id.</summary>
    [ProtoMember(1, Name = "message_id")]
    public string MessageId { get; set; } = string.Empty;

    /// <summary>Gets or sets the correlation id.</summary>
    [ProtoMember(2, Name = "correlation_id")]
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>Gets or sets the causation id.</summary>
    [ProtoMember(3, Name = "causation_id")]
    public string CausationId { get; set; } = string.Empty;

    /// <summary>Gets or sets the W3C traceparent.</summary>
    [ProtoMember(4, Name = "traceparent")]
    public string Traceparent { get; set; } = string.Empty;

    /// <summary>Gets or sets the sent time, UTC, as <c>google.protobuf.Timestamp</c>.</summary>
    [ProtoMember(5, Name = "sent_at")]
    public DateTime? SentAt { get; set; }

    /// <summary>Gets the headers, a proto3 <c>map&lt;string, string&gt;</c>.</summary>
    [ProtoMember(6, Name = "headers")]
    public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets or sets the payload, with the <c>google.protobuf.Any</c> layout.</summary>
    [ProtoMember(7, Name = "payload")]
    public EnvelopePayload? Payload { get; set; }
}

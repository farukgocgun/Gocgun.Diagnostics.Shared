using ProtoBuf;

namespace Gocgun.Diagnostics.Shared.Bus.V1;

/// <summary>Payload with the <c>google.protobuf.Any</c> layout: type URL, then the message bytes.</summary>
[ProtoContract(Name = "gocgun.bus.v1.EnvelopePayload")]
internal sealed class EnvelopePayload
{
    /// <summary>Gets or sets the type URL, <c>type.googleapis.com/</c> plus the contract name.</summary>
    [ProtoMember(1, Name = "type_url")]
    public string TypeUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the serialized message.</summary>
    [ProtoMember(2, Name = "value")]
    public ReadOnlyMemory<byte> Value { get; set; }
}

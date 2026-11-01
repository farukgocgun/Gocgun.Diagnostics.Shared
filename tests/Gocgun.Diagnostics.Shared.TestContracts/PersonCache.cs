using ProtoBuf;

namespace Gocgun.Diagnostics.Fixture;

/// <summary>Cache value fixture with a ciphertext field.</summary>
[ProtoContract(Name = "gocgun.diagnostics.fixture.v1.PersonCache")]
public sealed class PersonCache
{
    /// <summary>Gets or sets the display name.</summary>
    [ProtoMember(1, Name = "display_name")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Gets or sets the encrypted e-mail.</summary>
    [ProtoMember(2, Name = "email_cipher")]
    public ReadOnlyMemory<byte> EmailCipher { get; set; }

    /// <summary>Gets or sets the key id.</summary>
    [ProtoMember(3, Name = "key_id")]
    public string KeyId { get; set; } = string.Empty;
}

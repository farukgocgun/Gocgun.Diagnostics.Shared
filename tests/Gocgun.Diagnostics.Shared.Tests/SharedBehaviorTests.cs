using System.Reflection;
using System.Text;
using Gocgun.Diagnostics.Fixture;
using Gocgun.Diagnostics.Shared.Audit;
using Gocgun.Diagnostics.Shared.Auth;
using Gocgun.Diagnostics.Shared.Bus.V1;
using Gocgun.Diagnostics.Shared.Decoding;
using Gocgun.Diagnostics.Shared.Profiles;
using Gocgun.Diagnostics.Shared.Tenancy;
using Microsoft.AspNetCore.DataProtection;
using ProtoBuf;

namespace Gocgun.Diagnostics.Tooling.Tests;

public sealed class SharedBehaviorTests
{
    [Fact]
    public void Fallback_KnownVarint_ShowsField()
    {
        var view = FallbackDecoder.Describe([0x08, 0x96, 0x01]);
        Assert.Contains("varint 150", view.ToText(), StringComparison.Ordinal);
        Assert.Equal(3, view.Length);
    }

    [Fact]
    public void Fallback_Utf8AndJson_AreShownWithoutAContract()
    {
        var text = FallbackDecoder.Describe(Encoding.UTF8.GetBytes("merhaba"));
        Assert.Contains("utf8 merhaba", text.ToText(), StringComparison.Ordinal);
        Assert.Null(text.Json);

        var json = FallbackDecoder.Describe(Encoding.UTF8.GetBytes("{\"a\":1}"));
        Assert.Contains("json {\"a\":1}", json.ToText(), StringComparison.Ordinal);
        Assert.DoesNotContain("contract", json.ToText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Fallback_InvalidUtf8AndTruncatedWire_DoNotThrow()
    {
        var binary = FallbackDecoder.Describe([0xFF]);
        Assert.Null(binary.Utf8);
        Assert.Contains("ff", binary.Hex, StringComparison.Ordinal);
        Assert.NotNull(binary.WireError);

        var truncated = FallbackDecoder.Describe([0x08, 0x80]);
        Assert.NotNull(truncated.WireError);
    }

    [Fact]
    public void Pattern_Glob_MatchesOnlyThePrefix()
    {
        Assert.True(PatternMatcher.IsMatch("app:cache:post:*", "app:cache:post:1"));
        Assert.False(PatternMatcher.IsMatch("app:cache:post:*", "app:cache:user:1"));
        Assert.Equal("demo.Type", PatternMatcher.FindContract([new PatternMap("t:*:post:*", "demo.Type")], "t:abc:post:9"));
    }

    [Fact]
    public void Tenant_KeyAndHeader_AreRead()
    {
        Assert.Equal("abc", TenantMarkers.FromCacheKey("t:abc:post:1"));
        Assert.Null(TenantMarkers.FromCacheKey("post:1"));
        var headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["tenant-id"] = "tenant-a" };
        Assert.Equal("tenant-a", TenantMarkers.FromHeaders(headers));
    }

    [Fact]
    public void Cipher_BytesStayBase64_AndNoDecryptMethodExists()
    {
        var person = new PersonCache
        {
            DisplayName = "Ada",
            KeyId = "key-9",
            EmailCipher = Encoding.UTF8.GetBytes("alice@example.com"),
        };
        var catalog = ContractCatalog.FromTypes([typeof(PersonCache)]);
        Assert.True(catalog.TryParse("gocgun.diagnostics.fixture.v1.PersonCache", Bytes(person), out var message, out var error));
        Assert.Null(error);
        var json = CipherDisplay.Format(message!);
        Assert.Contains("Ada", json, StringComparison.Ordinal);
        Assert.Contains("key-9", json, StringComparison.Ordinal);
        Assert.Contains("YWxpY2VAZXhhbXBsZS5jb20=", json, StringComparison.Ordinal);
        Assert.DoesNotContain("alice@example.com", json, StringComparison.Ordinal);
        Assert.Contains("email_cipher remains ciphertext", json, StringComparison.Ordinal);
        Assert.Contains("\"displayName\":\"Ada\"", json, StringComparison.Ordinal);
        var names = typeof(CipherDisplay).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
        Assert.DoesNotContain(names, method => method.Name.Contains("Decrypt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Paste_WrappedBase64Envelope_DecodesMetadataAndHidesOtherTenants()
    {
        var person = new PersonCache { DisplayName = "Ada", KeyId = "key-9", EmailCipher = Encoding.UTF8.GetBytes("alice@example.com") };
        var envelope = new Envelope
        {
            MessageId = "m1",
            CorrelationId = "c1",
            CausationId = "cause",
            Traceparent = "00-trace",
            SentAt = DateTime.UnixEpoch.AddSeconds(5),
            Payload = new EnvelopePayload { TypeUrl = "type.googleapis.com/gocgun.diagnostics.fixture.v1.PersonCache", Value = Bytes(person) },
        };
        var raw = Convert.ToBase64String(Bytes(envelope));
        var wrapped = string.Join('\n', Split(raw, 76));
        var catalog = ContractCatalog.FromTypes([typeof(PersonCache)]);
        var headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["tenant-id"] = "tenant-a" };
        var decoded = BusBodyDecoder.DecodePaste(wrapped, catalog, null, headers, null);
        Assert.False(decoded.IsUserError);
        Assert.True(decoded.IsEnvelope);
        Assert.Equal("m1", decoded.MessageId);
        Assert.Equal("1970-01-01T00:00:05.0000000Z", decoded.SentAt);
        Assert.Equal("gocgun.diagnostics.fixture.v1.PersonCache", decoded.PayloadType);
        Assert.Contains("Ada", decoded.Display, StringComparison.Ordinal);
        Assert.DoesNotContain("alice@example.com", decoded.Display, StringComparison.Ordinal);

        var hidden = BusBodyDecoder.DecodePaste(wrapped, catalog, null, headers, "tenant-b");
        Assert.True(hidden.TenantHidden);
        Assert.DoesNotContain("Ada", hidden.Display, StringComparison.Ordinal);
    }

    [Fact]
    public void Envelope_Proto3WireLayout_Decodes()
    {
        byte[] body =
        [
            0x0A, 0x02, (byte)'m', (byte)'1', // 1: message_id
            0x2A, 0x02, 0x08, 0x01, // 5: sent_at { seconds: 1 }
            0x32, 0x06, 0x0A, 0x01, (byte)'k', 0x12, 0x01, (byte)'v', // 6: headers entry
            0x3A, 0x07, 0x0A, 0x01, (byte)'t', 0x12, 0x02, 0x08, 0x01, // 7: payload { type_url, value }
        ];
        var decoded = BusBodyDecoder.Decode(body, ContractCatalog.Empty, null, null, null);
        Assert.True(decoded.IsEnvelope);
        Assert.Equal("m1", decoded.MessageId);
        Assert.Equal("1970-01-01T00:00:01.0000000Z", decoded.SentAt);
        Assert.Equal("t", decoded.PayloadType);
    }

    [Fact]
    public void Catalog_CorruptPayload_FailsWithoutThrowing()
    {
        var catalog = ContractCatalog.FromTypes([typeof(PersonCache), typeof(string)]);
        Assert.DoesNotContain(catalog.TypeNames, name => name.Contains("String", StringComparison.Ordinal));
        byte[] truncated = [0x0A, 0x05, 0x01];
        Assert.False(catalog.TryParse("gocgun.diagnostics.fixture.v1.PersonCache", truncated, out _, out var error));
        Assert.Equal("Contract parse failed.", error);
        Assert.False(catalog.TryParse("unknown.v1.Type", truncated, out _, out var missing));
        Assert.Equal("Contract is not loaded.", missing);
    }

    [Fact]
    public void Paste_UserErrors_AreRejected()
    {
        var empty = BusBodyDecoder.DecodePaste("  ", ContractCatalog.Empty, null, null, null);
        Assert.True(empty.IsUserError);
        Assert.Equal("Body is empty.", empty.Error);
        var bad = BusBodyDecoder.DecodePaste("!!!!", ContractCatalog.Empty, null, null, null);
        Assert.True(bad.IsUserError);
        Assert.Equal("Body is not base64.", bad.Error);
    }

    [Fact]
    public void Audit_LineHasNoPayloadField()
    {
        var directory = NewDir();
        var log = new AuditLog(Path.Combine(directory, "audit.log"), TimeProvider.System);
        log.Append(new AuditEntry("local-admin", "session", "cache-key", "PersonCache", "view"));
        var text = log.ReadAll();
        Assert.Contains("\"target\":\"cache-key\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PAYLOADSECRET", text, StringComparison.Ordinal);
        var names = typeof(AuditEntry).GetProperties().Select(property => property.Name).ToArray();
        Assert.DoesNotContain(names, name => name.Contains("Payload", StringComparison.OrdinalIgnoreCase) || name.Contains("Password", StringComparison.OrdinalIgnoreCase) || name.Contains("Value", StringComparison.OrdinalIgnoreCase));
        foreach (var field in AuditLog.FieldNames)
        {
            Assert.Contains(field, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Password_HashRoundTrip_RejectsShortAndSecondSetup()
    {
        var store = new LocalAdminStore(Path.Combine(NewDir(), "admin.hash"));
        Assert.Throws<ArgumentException>(() => store.SetPassword("short"));
        const string password = "correct-horse-battery";
        store.SetPassword(password);
        Assert.DoesNotContain(password, store.ReadRaw(), StringComparison.Ordinal);
        Assert.True(store.Verify(password));
        Assert.False(store.Verify("wrong-password-value"));
        Assert.Throws<InvalidOperationException>(() => store.SetPassword(password));
    }

    [Fact]
    public void Profile_FileDoesNotContainThePassword()
    {
        var provider = new EphemeralDataProtectionProvider();
        var store = new ProtectedProfileStore(NewDir(), provider.CreateProtector("profiles"));
        const string password = "profile-password-1";
        store.Save("dev", password, new Dictionary<string, string>(StringComparer.Ordinal) { ["host"] = "127.0.0.1" });
        Assert.DoesNotContain(password, store.ReadRaw("dev"), StringComparison.Ordinal);
        var loaded = store.TryLoad("dev");
        Assert.NotNull(loaded);
        Assert.Equal(password, loaded.Password);
        Assert.Throws<ArgumentException>(() => store.Save("../etc", password, new Dictionary<string, string>(StringComparer.Ordinal)));
        Assert.Throws<ArgumentException>(() => store.Save("..", password, new Dictionary<string, string>(StringComparer.Ordinal)));
    }

    [Fact]
    public void Maps_RoundTrip_WithoutValues()
    {
        var path = Path.Combine(NewDir(), "maps.json");
        var store = new PatternMapStore(path);
        store.Save([new PatternMap("t:*", "gocgun.diagnostics.fixture.v1.PersonCache")]);
        var raw = store.Export();
        Assert.DoesNotContain("alice@example.com", raw, StringComparison.Ordinal);
        Assert.Equal("gocgun.diagnostics.fixture.v1.PersonCache", store.Load()[0].ContractType);
    }

    [Fact]
    public void Binding_NonLoopback_IsRejected()
    {
        BindingRules.EnsureSafe(null);
        BindingRules.EnsureSafe(string.Empty);
        BindingRules.EnsureSafe("http://127.0.0.1:0");
        BindingRules.EnsureSafe("https://127.0.0.1:443");
        BindingRules.EnsureSafe("http://localhost:5000");
        Assert.Throws<InvalidOperationException>(() => BindingRules.EnsureSafe("https://example.test:443"));
        Assert.Throws<InvalidOperationException>(() => BindingRules.EnsureSafe("http://0.0.0.0:80"));
        Assert.Throws<InvalidOperationException>(() => BindingRules.EnsureSafe("not a url"));
    }

    [Fact]
    public void LoadFolder_FindsTheFixtureContract()
    {
        var folder = NewDir();
        var source = typeof(PersonCache).Assembly.Location;
        File.Copy(source, Path.Combine(folder, Path.GetFileName(source)));
        var catalog = ContractCatalog.LoadFolder(folder, folder);
        Assert.Contains(catalog.TypeNames, name => name.Contains("PersonCache", StringComparison.Ordinal));
        var person = new PersonCache { DisplayName = "Ada", EmailCipher = Encoding.UTF8.GetBytes("alice@example.com") };
        Assert.True(catalog.TryParse("gocgun.diagnostics.fixture.v1.PersonCache", Bytes(person), out var message, out _));
        Assert.Contains("Ada", CipherDisplay.Format(message!), StringComparison.Ordinal);
        Assert.Throws<DirectoryNotFoundException>(() => ContractCatalog.LoadFolder(Path.Combine(folder, "missing"), folder));
    }

    [Fact]
    public void Assembly_DoesNotReferenceGocgunIdentity()
    {
        var names = typeof(LocalAdminStore).Assembly.GetReferencedAssemblies().Select(name => name.Name);
        Assert.DoesNotContain(names, name => name != null && name.Contains("Gocgun.Identity", StringComparison.Ordinal));
        var project = FindUp("Gocgun.Diagnostics.Shared.csproj");
        var text = File.ReadAllText(project);
        Assert.Contains("<IsPackable>false</IsPackable>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Gocgun.Identity", text, StringComparison.Ordinal);
    }

    private static byte[] Bytes<T>(T value)
    {
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, value);
        return stream.ToArray();
    }

    private static string FindUp(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(fileName);
    }

    private static string NewDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "gocgun-shared-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static IEnumerable<string> Split(string value, int size)
    {
        for (var i = 0; i < value.Length; i += size)
        {
            yield return value.Substring(i, Math.Min(size, value.Length - i));
        }
    }
}

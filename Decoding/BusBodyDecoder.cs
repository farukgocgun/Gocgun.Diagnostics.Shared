using System.Globalization;
using System.Text;
using Gocgun.Diagnostics.Shared.Bus.V1;
using Gocgun.Diagnostics.Shared.Tenancy;
using ProtoBuf;

namespace Gocgun.Diagnostics.Shared.Decoding;

/// <summary>Decodes a management-UI base64 body as <c>gocgun.bus.v1.Envelope</c> or a fallback.</summary>
internal static class BusBodyDecoder
{
    internal static BusDecodeResult DecodePaste(
        string? base64,
        ContractCatalog catalog,
        string? typeOverride,
        IReadOnlyDictionary<string, string>? amqpHeaders,
        string? tenantFilter)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return UserError("Body is empty.");
        }

        byte[] bytes;
        try
        {
            var compact = new string(base64.Where(ch => !char.IsWhiteSpace(ch)).ToArray());
            bytes = Convert.FromBase64String(compact);
        }
        catch (FormatException)
        {
            return UserError("Body is not base64.");
        }

        return Decode(bytes, catalog, typeOverride, amqpHeaders, tenantFilter);
    }

    internal static BusDecodeResult Decode(
        ReadOnlyMemory<byte> body,
        ContractCatalog catalog,
        string? typeOverride,
        IReadOnlyDictionary<string, string>? amqpHeaders,
        string? tenantFilter)
    {
        Envelope? envelope;
        try
        {
            envelope = Serializer.Deserialize<Envelope>(body);
        }
        catch (Exception ex) when (ex is ProtoException or EndOfStreamException or OverflowException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            envelope = null;
        }

        if (envelope is null || !LooksLikeEnvelope(envelope))
        {
            var fallback = FallbackDecoder.Describe(body.Span);
            return new BusDecodeResult(
                false,
                false,
                null,
                null,
                null,
                null,
                null,
                null,
                TenantMarkers.FromHeaders(amqpHeaders ?? Empty()),
                typeOverride,
                Hide(fallback.ToText(), tenantFilter, TenantMarkers.FromHeaders(amqpHeaders ?? Empty())),
                IsHidden(tenantFilter, TenantMarkers.FromHeaders(amqpHeaders ?? Empty())));
        }

        var headers = envelope.Headers.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var tenant = TenantMarkers.FromHeaders(amqpHeaders ?? Empty()) ?? TenantMarkers.FromHeaders(headers);
        var typeUrl = envelope.Payload?.TypeUrl ?? string.Empty;
        var payloadType = string.IsNullOrWhiteSpace(typeOverride) ? TypeFromUrl(typeUrl) : typeOverride.Trim();
        var payload = envelope.Payload?.Value.ToArray() ?? [];
        var display = RenderPayload(catalog, payloadType, payload);
        var hidden = IsHidden(tenantFilter, tenant);
        return new BusDecodeResult(
            true,
            false,
            null,
            envelope.MessageId,
            envelope.CorrelationId,
            envelope.CausationId,
            envelope.Traceparent,
            SentAt(envelope),
            tenant,
            payloadType,
            Metadata(envelope, tenant, payloadType, hidden, display),
            hidden);
    }

    private static string Metadata(Envelope envelope, string? tenant, string payloadType, bool hidden, string display)
    {
        var meta = new StringBuilder();
        meta.Append("message_id ");
        meta.Append(envelope.MessageId);
        meta.Append("\ncorrelation_id ");
        meta.Append(envelope.CorrelationId);
        meta.Append("\ncausation_id ");
        meta.Append(envelope.CausationId);
        meta.Append("\ntraceparent ");
        meta.Append(envelope.Traceparent);
        meta.Append("\nsent_at ");
        meta.Append(SentAt(envelope) ?? string.Empty);
        meta.Append("\ntenant ");
        meta.Append(tenant ?? string.Empty);
        meta.Append("\ntype ");
        meta.Append(payloadType);
        meta.Append('\n');
        meta.Append(hidden ? "tenant filter hid the payload" : display);
        return meta.ToString();
    }

    private static string? SentAt(Envelope envelope)
    {
        return envelope.SentAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private static string RenderPayload(ContractCatalog catalog, string payloadType, byte[] payload)
    {
        if (payloadType.Length > 0 && catalog.TryParse(payloadType, payload, out var message, out _) && message is not null)
        {
            return CipherDisplay.Format(message);
        }

        return FallbackDecoder.Describe(payload).ToText();
    }

    private static bool LooksLikeEnvelope(Envelope envelope)
    {
        return envelope.Payload is not null
            || !string.IsNullOrEmpty(envelope.MessageId)
            || !string.IsNullOrEmpty(envelope.CorrelationId)
            || envelope.Headers.Count > 0;
    }

    private static string TypeFromUrl(string typeUrl)
    {
        const string prefix = "type.googleapis.com/";
        if (typeUrl.StartsWith(prefix, StringComparison.Ordinal))
        {
            return typeUrl[prefix.Length..];
        }

        var slash = typeUrl.LastIndexOf('/');
        return slash >= 0 ? typeUrl[(slash + 1)..] : typeUrl;
    }

    private static bool IsHidden(string? tenantFilter, string? tenant)
    {
        return !string.IsNullOrWhiteSpace(tenantFilter)
            && !string.Equals(tenantFilter.Trim(), tenant, StringComparison.Ordinal);
    }

    private static string Hide(string display, string? tenantFilter, string? tenant)
    {
        return IsHidden(tenantFilter, tenant) ? "tenant filter hid the payload" : display;
    }

    private static BusDecodeResult UserError(string error)
    {
        return new BusDecodeResult(false, true, error, null, null, null, null, null, null, null, string.Empty, false);
    }

    private static Dictionary<string, string> Empty()
    {
        return new Dictionary<string, string>(StringComparer.Ordinal);
    }
}

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Gocgun.Diagnostics.Shared.Decoding;

/// <summary>Describes bytes without guessing a contract.</summary>
internal static class FallbackDecoder
{
    private const int HexLimit = 4096;

    internal static FallbackView Describe(ReadOnlySpan<byte> bytes)
    {
        var utf8 = TryUtf8(bytes);
        string? json = null;
        if (utf8 is not null && LooksLikeJson(utf8))
        {
            json = TryJson(utf8);
        }

        var wireError = default(string);
        var wire = TryWire(bytes, out wireError);
        return new FallbackView(bytes.Length, ToHex(bytes), utf8, json, wire, wireError);
    }

    private static string? TryUtf8(ReadOnlySpan<byte> bytes)
    {
        try
        {
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            return encoding.GetString(bytes);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool LooksLikeJson(string text)
    {
        var trimmed = text.TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[');
    }

    private static string? TryJson(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 32 });
            return text.Trim();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ToHex(ReadOnlySpan<byte> bytes)
    {
        var take = Math.Min(bytes.Length, HexLimit);
        var builder = new StringBuilder(take * 3);
        for (var i = 0; i < take; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            builder.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
        }

        if (bytes.Length > HexLimit)
        {
            builder.Append(" truncated");
        }

        return builder.ToString();
    }

    private static string? TryWire(ReadOnlySpan<byte> bytes, out string? error)
    {
        try
        {
            var builder = new StringBuilder();
            var offset = 0;
            if (!ReadMessage(bytes, ref offset, builder, depth: 0) || offset != bytes.Length)
            {
                error = "wire parse did not consume the buffer";
                return builder.Length == 0 ? null : builder.ToString().TrimEnd();
            }

            error = null;
            return builder.ToString().TrimEnd();
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private static bool ReadMessage(ReadOnlySpan<byte> bytes, ref int offset, StringBuilder builder, int depth)
    {
        if (depth > 8)
        {
            throw new InvalidOperationException("wire nesting is too deep");
        }

        var start = offset;
        while (offset < bytes.Length)
        {
            if (!TryVarint(bytes, ref offset, out var tag))
            {
                offset = start;
                return false;
            }

            var field = tag >> 3;
            var wire = tag & 7;
            if (field == 0)
            {
                offset = start;
                return false;
            }

            builder.Append("field ");
            builder.Append(field.ToString(CultureInfo.InvariantCulture));
            builder.Append(' ');
            if (!ReadField(bytes, ref offset, builder, (int)wire, depth))
            {
                offset = start;
                return false;
            }
        }

        return offset > start || bytes.Length == start;
    }

    private static bool ReadField(ReadOnlySpan<byte> bytes, ref int offset, StringBuilder builder, int wire, int depth)
    {
        switch (wire)
        {
            case 0:
                if (!TryVarint(bytes, ref offset, out var varint))
                {
                    return false;
                }

                builder.Append("varint ");
                builder.Append(varint.ToString(CultureInfo.InvariantCulture));
                builder.Append('\n');
                return true;
            case 1:
                return ReadFixed(bytes, ref offset, builder, 8, "fixed64");
            case 5:
                return ReadFixed(bytes, ref offset, builder, 4, "fixed32");
            case 2:
                return ReadLength(bytes, ref offset, builder, depth);
            default:
                return false;
        }
    }

    private static bool ReadFixed(ReadOnlySpan<byte> bytes, ref int offset, StringBuilder builder, int size, string name)
    {
        if (offset + size > bytes.Length)
        {
            return false;
        }

        builder.Append(name);
        builder.Append(' ');
        builder.Append(ToHex(bytes.Slice(offset, size)));
        builder.Append('\n');
        offset += size;
        return true;
    }

    private static bool ReadLength(ReadOnlySpan<byte> bytes, ref int offset, StringBuilder builder, int depth)
    {
        if (!TryVarint(bytes, ref offset, out var length) || length > int.MaxValue)
        {
            return false;
        }

        var size = (int)length;
        if (offset + size > bytes.Length)
        {
            return false;
        }

        var slice = bytes.Slice(offset, size);
        offset += size;
        var nested = new StringBuilder();
        var nestedOffset = 0;
        if (size > 0 && ReadMessage(slice, ref nestedOffset, nested, depth + 1) && nestedOffset == slice.Length)
        {
            builder.Append("message\n");
            builder.Append(nested);
            return true;
        }

        builder.Append("bytes ");
        builder.Append(size.ToString(CultureInfo.InvariantCulture));
        builder.Append(' ');
        builder.Append(ToHex(slice));
        builder.Append('\n');
        return true;
    }

    private static bool TryVarint(ReadOnlySpan<byte> bytes, ref int offset, out ulong value)
    {
        value = 0;
        var shift = 0;
        while (offset < bytes.Length && shift < 64)
        {
            var current = bytes[offset++];
            value |= (ulong)(current & 0x7F) << shift;
            if ((current & 0x80) == 0)
            {
                return true;
            }

            shift += 7;
        }

        return false;
    }
}

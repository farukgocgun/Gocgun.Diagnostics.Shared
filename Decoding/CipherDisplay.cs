using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using ProtoBuf;

namespace Gocgun.Diagnostics.Shared.Decoding;

/// <summary>Formats a protobuf-net contract as JSON. Byte fields whose names end with cipher stay base64.</summary>
internal static class CipherDisplay
{
    private const int MaxDepth = 32;

    internal static string Format(object message)
    {
        ArgumentNullException.ThrowIfNull(message);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteContract(writer, message, 0);
        }

        var json = Encoding.UTF8.GetString(buffer.ToArray());
        var notes = new StringBuilderNotes();
        foreach (var member in Members(message.GetType()))
        {
            if (!member.ProtoName.EndsWith("cipher", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            notes.Add(member.ProtoName + (IsBytes(member.Type) ? " remains ciphertext" : " is not a ciphertext byte field"));
        }

        if (notes.IsEmpty)
        {
            return json;
        }

        return json + "\n" + notes.Text;
    }

    private static void WriteContract(Utf8JsonWriter writer, object message, int depth)
    {
        writer.WriteStartObject();
        foreach (var member in Members(message.GetType()))
        {
            var value = member.Read(message);
            if (value is null)
            {
                continue;
            }

            writer.WritePropertyName(member.JsonName);
            WriteValue(writer, value, depth + 1);
        }

        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, object value, int depth)
    {
        if (depth > MaxDepth)
        {
            writer.WriteStringValue("(depth limit)");
            return;
        }

        if (TryBytes(value, out var bytes))
        {
            writer.WriteBase64StringValue(bytes.Span);
            return;
        }

        if (TryScalar(writer, value))
        {
            return;
        }

        if (value.GetType().GetCustomAttribute<ProtoContractAttribute>(inherit: false) is not null)
        {
            WriteContract(writer, value, depth);
            return;
        }

        WriteSequence(writer, value, depth);
    }

    private static bool TryScalar(Utf8JsonWriter writer, object value)
    {
        switch (value)
        {
            case string text:
                writer.WriteStringValue(text);
                return true;
            case bool flag:
                writer.WriteBooleanValue(flag);
                return true;
            case Enum:
                writer.WriteStringValue(EnumName(value));
                return true;
            case DateTime time:
                writer.WriteStringValue(time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                return true;
            case DateTimeOffset offset:
                writer.WriteStringValue(offset.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                return true;
            case IFormattable formattable when value is not IEnumerable:
                writer.WriteStringValue(formattable.ToString(null, CultureInfo.InvariantCulture));
                return true;
            default:
                return false;
        }
    }

    // Proto3 JSON mapping: the enum value name from [ProtoEnum], else the C# name.
    private static string EnumName(object value)
    {
        var name = value.ToString() ?? string.Empty;
        var field = value.GetType().GetField(name);
        return field?.GetCustomAttribute<ProtoEnumAttribute>(inherit: false)?.Name ?? name;
    }

    private static void WriteSequence(Utf8JsonWriter writer, object value, int depth)
    {
        if (value is IDictionary map)
        {
            writer.WriteStartObject();
            foreach (DictionaryEntry entry in map)
            {
                writer.WritePropertyName(Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty);
                WriteNullable(writer, entry.Value, depth + 1);
            }

            writer.WriteEndObject();
            return;
        }

        if (value is IEnumerable items)
        {
            writer.WriteStartArray();
            foreach (var item in items)
            {
                WriteNullable(writer, item, depth + 1);
            }

            writer.WriteEndArray();
            return;
        }

        writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture));
    }

    private static void WriteNullable(Utf8JsonWriter writer, object? value, int depth)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        WriteValue(writer, value, depth);
    }

    private static bool TryBytes(object value, out ReadOnlyMemory<byte> bytes)
    {
        switch (value)
        {
            case byte[] array:
                bytes = array;
                return true;
            case ReadOnlyMemory<byte> memory:
                bytes = memory;
                return true;
            case Memory<byte> writable:
                bytes = writable;
                return true;
            case ArraySegment<byte> segment:
                bytes = segment;
                return true;
            default:
                bytes = default;
                return false;
        }
    }

    private static bool IsBytes(Type type) =>
        type == typeof(byte[]) || type == typeof(ReadOnlyMemory<byte>) || type == typeof(Memory<byte>) || type == typeof(ArraySegment<byte>);

    private static IEnumerable<ContractMember> Members(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
        var members = new List<ContractMember>();
        foreach (var property in type.GetProperties(flags))
        {
            if (property.GetIndexParameters().Length == 0 && property.GetCustomAttribute<ProtoMemberAttribute>(inherit: true) is { } attribute)
            {
                members.Add(new ContractMember(attribute.Tag, attribute.Name ?? property.Name, property.PropertyType, property.GetValue));
            }
        }

        foreach (var field in type.GetFields(flags))
        {
            if (field.GetCustomAttribute<ProtoMemberAttribute>(inherit: true) is { } attribute)
            {
                members.Add(new ContractMember(attribute.Tag, attribute.Name ?? field.Name, field.FieldType, field.GetValue));
            }
        }

        return members.OrderBy(static member => member.Tag);
    }

    private sealed record ContractMember(int Tag, string ProtoName, Type Type, Func<object?, object?> Read)
    {
        // Proto3 JSON mapping: lowerCamelCase of the field name.
        internal string JsonName
        {
            get
            {
                var builder = new StringBuilder(ProtoName.Length);
                var upper = false;
                foreach (var character in ProtoName)
                {
                    if (character == '_')
                    {
                        upper = builder.Length > 0;
                        continue;
                    }

                    var next = character;
                    if (upper)
                    {
                        next = char.ToUpperInvariant(character);
                    }
                    else if (builder.Length == 0)
                    {
                        next = char.ToLowerInvariant(character);
                    }

                    builder.Append(next);
                    upper = false;
                }

                return builder.ToString();
            }
        }
    }

    private sealed class StringBuilderNotes
    {
        private readonly StringBuilder builder = new();

        internal bool IsEmpty => builder.Length == 0;

        internal string Text => builder.ToString().TrimEnd();

        internal void Add(string line)
        {
            builder.Append(line);
            builder.Append('\n');
        }
    }
}

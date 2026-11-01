using System.Globalization;
using System.Text;

namespace Gocgun.Diagnostics.Shared.Decoding;

/// <summary>Schema-less view used when no contract is mapped or parsing fails.</summary>
internal sealed record FallbackView(int Length, string Hex, string? Utf8, string? Json, string? Wire, string? WireError)
{
    internal string ToText()
    {
        var builder = new StringBuilder();
        builder.Append("length ");
        builder.Append(Length.ToString(CultureInfo.InvariantCulture));
        builder.Append('\n');
        builder.Append("hex ");
        builder.Append(Hex);
        if (Utf8 is not null)
        {
            builder.Append("\nutf8 ");
            builder.Append(Utf8);
        }

        if (Json is not null)
        {
            builder.Append("\njson ");
            builder.Append(Json);
        }

        if (Wire is not null)
        {
            builder.Append("\nwire\n");
            builder.Append(Wire);
        }

        if (WireError is not null)
        {
            builder.Append("\nwire-error ");
            builder.Append(WireError);
        }

        return builder.ToString();
    }
}

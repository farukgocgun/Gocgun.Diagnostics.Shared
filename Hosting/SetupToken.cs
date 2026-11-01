using System.Security.Cryptography;
using System.Text;

namespace Gocgun.Diagnostics.Shared.Hosting;

/// <summary>One-time token printed to the console at startup. First-run <c>/Setup</c> needs it, so a local web page alone cannot claim the admin account.</summary>
internal sealed class SetupToken
{
    private const int TokenBytes = 32;

    private readonly byte[] expected;

    public SetupToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenBytes);
        Value = Base64Url(bytes);
        expected = Encoding.UTF8.GetBytes(Value);
    }

    internal string Value { get; }

    internal bool Matches(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expected, Encoding.UTF8.GetBytes(candidate.Trim()));
    }

    private static string Base64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}

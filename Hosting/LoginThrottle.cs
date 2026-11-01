using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Gocgun.Diagnostics.Shared.Hosting;

/// <summary>
/// Per-process failed-login counter. After too many failures every login is refused until the lockout ends.
/// Any local process can trigger the lockout, so each lockout prints a one-time unlock code to the console:
/// the operator, who sees the console, can still log in with the password and that code.
/// </summary>
internal sealed class LoginThrottle
{
    private const int UnlockCodeBytes = 16;

    private readonly Lock gate = new();
    private readonly DiagnosticsOptions options;
    private readonly TimeProvider time;
    private readonly Action<string> announce;
    private int failures;
    private DateTimeOffset lockedUntil = DateTimeOffset.MinValue;
    private byte[]? unlockCode;

    public LoginThrottle(DiagnosticsOptions options, TimeProvider time)
        : this(options, time, Console.WriteLine)
    {
    }

    internal LoginThrottle(DiagnosticsOptions options, TimeProvider time, Action<string> announce)
    {
        this.options = options;
        this.time = time;
        this.announce = announce;
    }

    internal bool IsLocked()
    {
        lock (gate)
        {
            return time.GetUtcNow() < lockedUntil;
        }
    }

    /// <summary>Counts one failed login.</summary>
    /// <returns><see langword="true"/> when this failure started a lockout.</returns>
    internal bool RecordFailure()
    {
        string code;
        DateTimeOffset until;
        lock (gate)
        {
            failures++;
            if (failures < options.LoginMaxFailures)
            {
                return false;
            }

            failures = 0;
            until = time.GetUtcNow() + options.LoginLockout;
            lockedUntil = until;
            code = Base64Url(RandomNumberGenerator.GetBytes(UnlockCodeBytes));
            unlockCode = Encoding.UTF8.GetBytes(code);
        }

        announce(string.Create(CultureInfo.InvariantCulture, $"Login locked until {until:u}. Unlock code (password still needed): {code}"));
        return true;
    }

    /// <summary>Ends the current lockout when the code matches. The code works once.</summary>
    /// <param name="candidate">Code typed on the login form.</param>
    /// <returns><see langword="true"/> when a lockout was active and the code matched.</returns>
    internal bool TryUnlock(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var typed = Encoding.UTF8.GetBytes(candidate.Trim());
        lock (gate)
        {
            if (unlockCode is null || time.GetUtcNow() >= lockedUntil || !CryptographicOperations.FixedTimeEquals(unlockCode, typed))
            {
                return false;
            }

            unlockCode = null;
            lockedUntil = DateTimeOffset.MinValue;
            failures = 0;
            return true;
        }
    }

    internal void Reset()
    {
        lock (gate)
        {
            failures = 0;
        }
    }

    private static string Base64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}

using Gocgun.Diagnostics.Shared.Audit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Gocgun.Diagnostics.Shared.Hosting;

/// <summary>Sign-in, sign-out and the audit lines for them. Shared by both tools.</summary>
internal static class SessionLifecycle
{
    internal static Task SignInAsync(HttpContext http)
    {
        var principal = DiagnosticsHost.Principal();
        http.RequestServices.GetRequiredService<SignInRegistry>().Add(DiagnosticsHost.Sid(principal)!);

        // Not persistent: the cookie dies with the browser session, and ExpireTimeSpan caps it anyway.
        return http.SignInAsync(DiagnosticsHost.Scheme, principal, new AuthenticationProperties { IsPersistent = false });
    }

    internal static async Task SignOutAsync<T>(HttpContext http, SessionStore<T> sessions, AuditLog audit)
        where T : class
    {
        var sid = DiagnosticsHost.Sid(http.User);
        if (sid is not null)
        {
            http.RequestServices.GetRequiredService<SignInRegistry>().Remove(sid);
            await sessions.RemoveAsync(sid);
        }

        Record(audit, http, "logout");
        await http.SignOutAsync(DiagnosticsHost.Scheme);
    }

    /// <summary>Appends an authentication event. The target is the caller address; no password is ever passed here.</summary>
    internal static void Record(AuditLog audit, HttpContext http, string action)
    {
        var remote = http.Connection.RemoteIpAddress?.ToString() ?? "-";
        audit.Append(new AuditEntry(DiagnosticsHost.OperatorName, "-", remote, null, action));
    }
}

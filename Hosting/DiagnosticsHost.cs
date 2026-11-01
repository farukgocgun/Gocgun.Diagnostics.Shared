using System.Net;
using System.Security.Claims;
using Gocgun.Diagnostics.Shared.Audit;
using Gocgun.Diagnostics.Shared.Auth;
using Gocgun.Diagnostics.Shared.Profiles;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Gocgun.Diagnostics.Shared.Hosting;

/// <summary>Cookie login, antiforgery, no-store responses, and a loopback Kestrel bind.</summary>
internal static class DiagnosticsHost
{
    internal const string Scheme = "GocgunDiagnostics";
    internal const string SidClaim = "gocgun-sid";
    internal const string OperatorName = "local-admin";

    internal static void Add(WebApplicationBuilder builder, string applicationName)
    {
        var urls = builder.Configuration["urls"] ?? builder.Configuration["URLS"];
        BindingRules.EnsureSafe(urls);
        var data = builder.Configuration["Diagnostics:DataDirectory"];
        if (string.IsNullOrWhiteSpace(data))
        {
            data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), applicationName);
        }

        Directory.CreateDirectory(data);
        if (string.IsNullOrWhiteSpace(urls))
        {
            var loopback = new UriBuilder(Uri.UriSchemeHttp, IPAddress.Loopback.ToString(), 0).Uri.AbsoluteUri;
            builder.WebHost.UseUrls(loopback);
        }

        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(data, "keys")))
            .SetApplicationName(applicationName);
        var diagnostics = DiagnosticsOptions.From(builder.Configuration);
        builder.Services.AddSingleton(diagnostics);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<LoginThrottle>();
        builder.Services.AddSingleton<SignInRegistry>();
        builder.Services.AddSingleton<SetupToken>();
        builder.Services.AddHostedService<SessionSweepService>();
        builder.Services.AddSingleton(new LocalAdminStore(Path.Combine(data, "admin.hash")));
        builder.Services.AddSingleton(new AuditLog(Path.Combine(data, "audit.log"), TimeProvider.System));
        builder.Services.AddSingleton<ProtectedProfileStore>(services =>
            new ProtectedProfileStore(
                Path.Combine(data, "profiles"),
                services.GetRequiredService<IDataProtectionProvider>().CreateProtector("gocgun-diagnostics-profiles")));
        builder.Services.AddAuthentication(Scheme)
            .AddCookie(Scheme, cookie => ConfigureCookie(cookie, applicationName, diagnostics));
        builder.Services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder(Scheme).RequireAuthenticatedUser().Build();
        });
        builder.Services.AddAntiforgery(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });
        builder.Services.AddRazorPages(options =>
        {
            options.Conventions.AllowAnonymousToPage("/Setup");
            options.Conventions.AllowAnonymousToPage("/Login");
            options.Conventions.AllowAnonymousToPage("/Error");
        });
    }

    internal static void Use(WebApplication app)
    {
        app.UseExceptionHandler("/Error");
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.Pragma = "no-cache";
                return Task.CompletedTask;
            });
            await next(context);
        });
        app.Use(async (context, next) =>
        {
            var store = context.RequestServices.GetRequiredService<LocalAdminStore>();
            var path = context.Request.Path;
            var firstRunPage = path.StartsWithSegments("/Setup", StringComparison.Ordinal)
                || path.StartsWithSegments("/Error", StringComparison.Ordinal);
            if (!store.IsConfigured && !firstRunPage)
            {
                context.Response.Redirect("/Setup");
                return;
            }

            await next(context);
        });
        app.UseAuthentication();
        app.UseAuthorization();
        app.Lifetime.ApplicationStarted.Register(() => OnStarted(app));
    }

    internal static ClaimsPrincipal Principal()
    {
        var sid = Guid.NewGuid().ToString("N");
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, OperatorName),
            new(SidClaim, sid),
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
    }

    internal static string? Sid(ClaimsPrincipal user)
    {
        return user.FindFirst(SidClaim)?.Value;
    }

    private static void OnStarted(WebApplication app)
    {
        // Kestrel:Endpoints can bind past the "urls" check, so verify what the server really bound.
        var bound = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        var offending = BindingRules.FindNonLoopback(bound);
        if (offending.Count > 0)
        {
            if (app.Logger.IsEnabled(LogLevel.Critical))
            {
                app.Logger.LogCritical("Bound to a non-loopback address ({Addresses}). Stopping.", string.Join(", ", offending));
            }

            Environment.ExitCode = 1;
            app.Lifetime.StopApplication();
            return;
        }

        foreach (var url in app.Urls)
        {
            Console.WriteLine(url);
        }

        if (!app.Services.GetRequiredService<LocalAdminStore>().IsConfigured)
        {
            Console.WriteLine("Setup token (needed on /Setup): " + app.Services.GetRequiredService<SetupToken>().Value);
        }
    }

    private static void ConfigureCookie(CookieAuthenticationOptions cookie, string applicationName, DiagnosticsOptions diagnostics)
    {
        cookie.LoginPath = "/Login";
        cookie.Cookie.Name = applicationName + ".auth";
        cookie.Cookie.HttpOnly = true;
        cookie.Cookie.SameSite = SameSiteMode.Strict;
        cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        cookie.ExpireTimeSpan = diagnostics.SessionLifetime;
        cookie.SlidingExpiration = false;

        // A cookie only works while its session id is registered; logout removes the id.
        cookie.Events.OnValidatePrincipal = context =>
        {
            var sid = context.Principal is null ? null : Sid(context.Principal);
            var registry = context.HttpContext.RequestServices.GetRequiredService<SignInRegistry>();
            if (sid is not null && registry.IsActive(sid))
            {
                return Task.CompletedTask;
            }

            context.RejectPrincipal();
            return context.HttpContext.SignOutAsync(Scheme);
        };
    }
}

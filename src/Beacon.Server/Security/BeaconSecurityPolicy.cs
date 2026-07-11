using System.Net;
using Microsoft.AspNetCore.Hosting.Server;

namespace Beacon.Server.Security;

public sealed record BeaconSecurityOptions(
    bool? TestHost,
    string IdentityPath,
    string CredentialsPath);

public sealed class BeaconSecurityPolicy(BeaconSecurityOptions options)
{
    public bool IsTestHost(HttpContext context)
    {
        if (options.TestHost.HasValue)
        {
            return options.TestHost.Value;
        }
        IServer server = context.RequestServices.GetRequiredService<IServer>();
        return string.Equals(
            server.GetType().FullName,
            "Microsoft.AspNetCore.TestHost.TestServer",
            StringComparison.Ordinal);
    }

    public bool IsTrustedLocalRequest(HttpContext context)
    {
        IPAddress? address = context.Connection.RemoteIpAddress;
        return address is null || IPAddress.IsLoopback(address);
    }
}

public static class BeaconSecurityMiddleware
{
    public static IApplicationBuilder UseBeaconSecurity(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            BeaconSecurityPolicy policy = context.RequestServices.GetRequiredService<BeaconSecurityPolicy>();
            if (policy.IsTestHost(context))
            {
                await next(context).ConfigureAwait(false);
                return;
            }

            if (!context.Request.IsHttps)
            {
                context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
                await context.Response.WriteAsJsonAsync(new { error = "Beacon requires HTTPS." })
                    .ConfigureAwait(false);
                return;
            }

            if (context.Request.Path.StartsWithSegments("/admin")
                && !policy.IsTrustedLocalRequest(context))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = "Beacon admin access is local-only." })
                    .ConfigureAwait(false);
                return;
            }

            string? scopedClientId = GetScopedClientId(context.Request);
            if (scopedClientId is null)
            {
                await next(context).ConfigureAwait(false);
                return;
            }

            string? credential = ReadCredential(context.Request);
            if (credential is null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "Beacon client credential is required." })
                    .ConfigureAwait(false);
                return;
            }

            ClientCredentialService credentials =
                context.RequestServices.GetRequiredService<ClientCredentialService>();
            string? authenticatedClientId = credentials.Authenticate(credential);
            if (authenticatedClientId is null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "Beacon client credential is invalid." })
                    .ConfigureAwait(false);
                return;
            }
            if (!string.Equals(authenticatedClientId, scopedClientId, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = "Beacon credential is not valid for this client." })
                    .ConfigureAwait(false);
                return;
            }

            context.Items["Beacon.AuthenticatedClientId"] = authenticatedClientId;
            await next(context).ConfigureAwait(false);
        });

    internal static string? ReadCredential(HttpRequest request)
    {
        string value = request.Headers.Authorization.ToString();
        const string prefix = "Beacon ";
        return value.StartsWith(prefix, StringComparison.Ordinal)
            ? value[prefix.Length..].Trim()
            : null;
    }

    private static string? GetScopedClientId(HttpRequest request)
    {
        if (request.Path.Equals("/games"))
        {
            string value = request.Headers["X-Beacon-Client-Id"].ToString();
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value;
        }
        string[] segments = request.Path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (segments.Length < 2 || !string.Equals(segments[0], "clients", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        if (string.Equals(segments[1], "hello", StringComparison.OrdinalIgnoreCase)
            || string.Equals(segments[1], "registrations", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return segments[1];
    }
}

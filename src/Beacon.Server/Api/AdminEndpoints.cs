using Beacon.Core.Clients;
using Beacon.Core.Displays;
using Beacon.Core.Games;
using Beacon.Core.Sessions;
using Beacon.Core.Streaming;
using Beacon.Server.Hosting;
using Beacon.Server.State;

namespace Beacon.Server.Api;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder admin = endpoints.MapGroup("/admin");

        admin.MapGet("/snapshot", async (
            InMemoryClientStore clients,
            InMemorySessionStore sessions,
            IStreamingBackend streaming,
            ISessionOwnershipTracker ownership,
            GameLibraryService games,
            BeaconHostOptions hostOptions,
            CancellationToken cancellationToken) =>
        {
            GameLibrarySnapshot gameSnapshot = await games.ScanAsync(cancellationToken);
            IReadOnlyList<SessionOwnershipSnapshot> ownershipSnapshots = await ownership.GetSnapshotsAsync(cancellationToken);
            return Results.Ok(new
            {
                clients = clients.GetProfiles().Select(profile => new
                {
                    clientId = profile.ClientId.Value,
                    profile,
                    capabilities = clients.GetCapabilities(profile.ClientId.Value),
                    telemetry = clients.GetTelemetry(profile.ClientId.Value)
                }),
                sessions = sessions.GetAll(),
                streams = streaming.GetSessions(),
                ownership = ownershipSnapshots,
                host = new
                {
                    mode = hostOptions.ModeName,
                    streamingBackendMode = hostOptions.StreamingBackendModeName,
                    displayBackend = hostOptions.DisplayBackendName,
                    gameLauncher = hostOptions.GameLauncherName,
                    activityInspector = hostOptions.ActivityInspectorName,
                    streamingBackend = hostOptions.StreamingBackendName
                },
                games = new
                {
                    total = gameSnapshot.Games.Count,
                    diagnostics = gameSnapshot.Diagnostics
                }
            });
        });

        admin.MapPost("/recovery/restore-physical", async (
            IDisplayBackend displayBackend,
            CancellationToken cancellationToken) =>
        {
            await displayBackend.RestorePhysicalPrimaryAsync(cancellationToken);
            return Results.Ok(new { restoreRequested = true });
        });

        admin.MapPost("/clients/{clientId}/display/recover", async (
            string clientId,
            DisplayLeaseManager leases,
            CancellationToken cancellationToken) =>
        {
            string displayId = DisplayLease.CreateDisplayId(new ClientId(clientId));
            DisplayRecoveryResult result = await leases.RecoverDisplayAsync(displayId, cancellationToken);

            return result.Success
                ? Results.Ok(new { clientId, displayId, recovered = true })
                : Results.Problem(result.Error, statusCode: StatusCodes.Status503ServiceUnavailable);
        });

        return endpoints;
    }
}

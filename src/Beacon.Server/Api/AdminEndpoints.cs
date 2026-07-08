using Beacon.Core.Clients;
using Beacon.Core.Displays;
using Beacon.Core.Games;
using Beacon.Core.Recovery;
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
            ClientPairingOptions pairingOptions,
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
                profiles = new
                {
                    store = clients.ProfileStoreKind,
                    location = clients.ProfileStoreLocation,
                    pairingEnabled = pairingOptions.Enabled
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

        admin.MapPost("/recovery/move-windows-back", async (
            MoveWindowsBackRequest request,
            IRecoveryBackend recovery,
            CancellationToken cancellationToken) =>
        {
            RecoveryActionResult result = await recovery.MoveWindowsBackAsync(request.Minimize, cancellationToken);
            return result.Success
                ? Results.Ok(result)
                : Results.Problem(result.Error, statusCode: StatusCodes.Status503ServiceUnavailable);
        });

        admin.MapPost("/recovery/close-virtual-windows", async (
            IRecoveryBackend recovery,
            CancellationToken cancellationToken) =>
        {
            RecoveryActionResult result = await recovery.CloseVirtualWindowsAsync(cancellationToken);
            return result.Success
                ? Results.Ok(result)
                : Results.Problem(result.Error, statusCode: StatusCodes.Status503ServiceUnavailable);
        });

        admin.MapPost("/recovery/terminate-virtual-processes", async (
            IRecoveryBackend recovery,
            CancellationToken cancellationToken) =>
        {
            RecoveryActionResult result = await recovery.TerminateVirtualProcessesAsync(cancellationToken);
            return result.Success
                ? Results.Ok(result)
                : Results.Problem(result.Error, statusCode: StatusCodes.Status503ServiceUnavailable);
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

        admin.MapPatch("/clients/{clientId}/profile", (
            string clientId,
            ClientProfileAdminPatch patch,
            InMemoryClientStore clients) =>
        {
            ClientProfile? profile = clients.GetProfile(clientId);
            if (profile is null)
            {
                return Results.NotFound(new { error = $"Client '{clientId}' is not registered." });
            }

            try
            {
                ClientProfile updated = ClientProfilePatcher.ApplyAdminPatch(profile, patch);
                clients.SaveProfile(updated);
                return Results.Ok(updated);
            }
            catch (InvalidClientProfilePatchException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        return endpoints;
    }
}

public sealed record MoveWindowsBackRequest(bool Minimize = true);

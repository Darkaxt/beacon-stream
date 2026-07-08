using System.Text.Json;
using Beacon.Core.Clients;
using Beacon.Core.Displays;
using Beacon.Core.Games;
using Beacon.Core.Input;
using Beacon.Core.Sessions;
using Beacon.Core.Streaming;
using Beacon.Server.State;

namespace Beacon.Server.Api;

public static class ClientEndpoints
{
    private static readonly string[] EditableFields =
    [
        "preferredWidth",
        "preferredHeight",
        "preferredRefreshHz",
        "hdrPreference",
        "codecPreference",
        "qualityMode",
        "bitrateCapMbps",
        "audioMode",
        "keepAppRunningOnDisconnect"
    ];

    private static readonly HashSet<string> EditableFieldSet = new(EditableFields, StringComparer.OrdinalIgnoreCase);

    public static IEndpointRouteBuilder MapClientEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder clients = endpoints.MapGroup("/clients");

        clients.MapPost("/hello", (ClientHelloRequest request, InMemoryClientStore store, ClientPairingOptions pairing) =>
        {
            ClientProfile? existingProfile = store.GetProfile(request.ClientId);
            if (existingProfile is null && !pairing.Allows(request.PairingToken))
            {
                return Results.Json(
                    new { error = $"Client '{request.ClientId}' is not registered. Pairing is required before this client can connect." },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            ClientProfile profile = existingProfile ?? store.RegisterProfile(request.ClientId, request.Name);

            return Results.Ok(new
            {
                clientId = profile.ClientId.Value,
                profile,
                editableFields = EditableFields
            });
        });

        clients.MapGet("/{clientId}/profile", (string clientId, InMemoryClientStore store) =>
            store.GetProfile(clientId) is { } profile
                ? Results.Ok(profile)
                : Results.NotFound(new { error = $"Client '{clientId}' is not registered." }));

        clients.MapPatch("/{clientId}/profile", (string clientId, JsonElement body, InMemoryClientStore store) =>
        {
            ClientProfile? profile = store.GetProfile(clientId);
            if (profile is null)
            {
                return Results.NotFound(new { error = $"Client '{clientId}' is not registered." });
            }

            foreach (JsonProperty property in body.EnumerateObject())
            {
                if (!EditableFieldSet.Contains(property.Name))
                {
                    return Results.BadRequest(new { error = $"Field '{property.Name}' is not editable from the client." });
                }
            }

            ClientProfilePatch patch = CreatePatch(body);
            try
            {
                ClientProfile updated = ClientProfilePatcher.ApplyApkPatch(profile, patch);
                store.SaveProfile(updated);
                return Results.Ok(updated);
            }
            catch (InvalidClientProfilePatchException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        clients.MapPost("/{clientId}/capabilities", (string clientId, EndpointCapabilities capabilities, InMemoryClientStore store) =>
        {
            store.SaveCapabilities(clientId, capabilities);
            return Results.Ok(new { clientId, accepted = true });
        });

        clients.MapPost("/{clientId}/telemetry", (string clientId, TelemetrySnapshot telemetry, InMemoryClientStore store) =>
        {
            store.SaveTelemetry(clientId, telemetry);
            return Results.Ok(new { clientId, accepted = true });
        });

        clients.MapPost("/{clientId}/plan", async (
            string clientId,
            PlanRequest request,
            InMemoryClientStore clients,
            InMemorySessionStore sessions,
            GameLibraryService games,
            CancellationToken cancellationToken) =>
        {
            ClientProfile? profile = clients.GetProfile(clientId);
            if (profile is null)
            {
                return Results.NotFound(new { error = $"Client '{clientId}' is not registered." });
            }

            GameResolution resolution = await ResolveRequestedGameAsync(request, games, cancellationToken);
            if (resolution.Error is not null)
            {
                return resolution.Error;
            }

            SessionPlanResult result = SessionPlanner.CreatePlan(
                profile,
                clients.GetCapabilities(clientId),
                clients.GetTelemetry(clientId),
                resolution.Game!);

            if (!result.Success || result.Plan is null)
            {
                return Results.BadRequest(new { error = result.Error });
            }

            sessions.Save(result.Plan);
            return Results.Ok(CreatePlanResponse(result.Plan, profile));
        });

        clients.MapPost("/{clientId}/launch", async (
            string clientId,
            PlanRequest request,
            InMemoryClientStore clients,
            InMemorySessionStore sessions,
            GameLibraryService games,
            DisplayLeaseManager leases,
            IDisplayBackend displayBackend,
            IGameLauncher launcher,
            ISessionOwnershipTracker ownership,
            IStreamingBackend streaming,
            CancellationToken cancellationToken) =>
        {
            ClientProfile? profile = clients.GetProfile(clientId);
            if (profile is null)
            {
                return Results.NotFound(new { error = $"Client '{clientId}' is not registered." });
            }

            GameResolution resolution = await ResolveRequestedGameAsync(request, games, cancellationToken);
            if (resolution.Error is not null)
            {
                return resolution.Error;
            }

            SessionPlanResult planResult = SessionPlanner.CreatePlan(
                profile,
                clients.GetCapabilities(clientId),
                clients.GetTelemetry(clientId),
                resolution.Game!);

            if (!planResult.Success || planResult.Plan is null)
            {
                return Results.BadRequest(new { error = planResult.Error });
            }

            StreamingPreflightResult streamingPreflight = await streaming.CheckReadinessAsync(planResult.Plan, cancellationToken);
            if (!streamingPreflight.Success)
            {
                return Results.Problem(
                    streamingPreflight.Error,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            DisplayLeaseResult leaseResult = await leases.EnsureLeaseAsync(profile, cancellationToken);
            if (!leaseResult.Success || leaseResult.Lease is null)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            sessions.Save(planResult.Plan);

            GameLaunchResult launchResult = await launcher.LaunchAsync(
                new GameLaunchRequest(resolution.Game!, planResult.Plan, leaseResult.Lease.DisplayId),
                cancellationToken);
            if (!launchResult.Success || launchResult.State is null)
            {
                DisplayRestoreResult restore = await displayBackend.RestorePhysicalPrimaryAsync(cancellationToken);
                string restoreStatus = restore.Success
                    ? "Physical primary restore requested after game launch failure."
                    : $"Physical primary restore failed after game launch failure: {restore.Error}";

                return Results.Problem(
                    $"{launchResult.Error} {restoreStatus}",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            await ownership.RecordLaunchAsync(planResult.Plan, launchResult.State, cancellationToken);

            StreamingStartResult streamResult = await streaming.StartAsync(planResult.Plan, cancellationToken);
            if (!streamResult.Success || streamResult.Session is null)
            {
                DisplayRestoreResult restore = await displayBackend.RestorePhysicalPrimaryAsync(cancellationToken);
                string restoreStatus = restore.Success
                    ? "Physical primary restore requested after stream start failure."
                    : $"Physical primary restore failed after stream start failure: {restore.Error}";

                return Results.Problem(
                    $"{streamResult.Error} {restoreStatus}",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Ok(new
            {
                clientId,
                displayId = leaseResult.Lease.DisplayId,
                state = "streaming",
                launch = launchResult.State,
                stream = streamResult.Session
            });
        });

        clients.MapGet("/{clientId}/stream", async (
            string clientId,
            InMemorySessionStore sessions,
            IStreamingBackend streaming,
            CancellationToken cancellationToken) =>
        {
            SessionPlan? plan = sessions.Get(clientId);
            if (plan is null)
            {
                return Results.NotFound(new { error = $"Client '{clientId}' has no session plan." });
            }

            StreamingSessionState? stream = await streaming.GetSessionAsync(plan.SessionId, cancellationToken);
            return stream is null
                ? Results.NotFound(new { error = $"Stream session '{plan.SessionId}' is not running." })
                : Results.Ok(new { clientId, stream });
        });

        clients.MapPost("/{clientId}/stream/stop", async (
            string clientId,
            InMemorySessionStore sessions,
            IStreamingBackend streaming,
            CancellationToken cancellationToken) =>
        {
            SessionPlan? plan = sessions.Get(clientId);
            if (plan is null)
            {
                return Results.NotFound(new { error = $"Client '{clientId}' has no session plan." });
            }

            StreamingStopResult stop = await streaming.StopAsync(plan.SessionId, cancellationToken);
            return stop.Success && stop.Session is not null
                ? Results.Ok(new { clientId, stream = stop.Session })
                : Results.Problem(stop.Error, statusCode: StatusCodes.Status503ServiceUnavailable);
        });

        clients.MapPost("/{clientId}/input", async (
            string clientId,
            ClientInputRequest request,
            InMemorySessionStore sessions,
            IStreamingBackend streaming,
            IClientInputSink input,
            CancellationToken cancellationToken) =>
        {
            SessionPlan? plan = sessions.Get(clientId);
            if (plan is null)
            {
                return Results.NotFound(new { error = $"Client '{clientId}' has no session plan." });
            }

            StreamingSessionState? stream = await streaming.GetSessionAsync(plan.SessionId, cancellationToken);
            if (stream is null || !stream.State.Equals("running", StringComparison.OrdinalIgnoreCase))
            {
                return Results.NotFound(new { error = $"Stream session '{plan.SessionId}' is not running." });
            }

            if (request.Events is not { Count: > 0 })
            {
                return Results.BadRequest(new { error = "Input request must include at least one event." });
            }

            var batch = new ClientInputBatch(
                clientId,
                plan.SessionId,
                plan.Display.DisplayId,
                request.Sequence,
                request.Events);
            ClientInputResult result = await input.ForwardAsync(batch, cancellationToken);
            return result.Success
                ? Results.Ok(new
                {
                    clientId,
                    sessionId = plan.SessionId,
                    displayId = plan.Display.DisplayId,
                    accepted = true,
                    eventCount = result.EventCount
                })
                : Results.Problem(result.Error, statusCode: StatusCodes.Status503ServiceUnavailable);
        });

        clients.MapPost("/{clientId}/disconnect", async (
            string clientId,
            InMemorySessionStore sessions,
            DisplayLeaseManager leases,
            IStreamingBackend streaming,
            CancellationToken cancellationToken) =>
        {
            await leases.DisconnectAsync(DisplayLease.CreateDisplayId(new ClientId(clientId)), cancellationToken);
            SessionPlan? plan = sessions.Get(clientId);
            StreamingSessionState? stream = null;
            if (plan is not null)
            {
                StreamingStopResult stop = await streaming.StopAsync(plan.SessionId, cancellationToken);
                if (!stop.Success)
                {
                    return Results.Problem(stop.Error, statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                stream = stop.Session;
            }

            return Results.Ok(new { clientId, leaseRetained = true, stream });
        });

        clients.MapPost("/{clientId}/reconnect", async (
            string clientId,
            InMemoryClientStore clients,
            DisplayLeaseManager leases,
            CancellationToken cancellationToken) =>
        {
            ClientProfile? profile = clients.GetProfile(clientId);
            if (profile is null)
            {
                return Results.NotFound(new { error = $"Client '{clientId}' is not registered." });
            }

            DisplayLeaseResult leaseResult = await leases.EnsureLeaseAsync(profile, cancellationToken);
            if (!leaseResult.Success || leaseResult.Lease is null)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Ok(new { clientId, displayId = leaseResult.Lease.DisplayId, state = "reconnected" });
        });

        clients.MapPost("/{clientId}/quit", async (
            string clientId,
            QuitRequest request,
            InMemorySessionStore sessions,
            DisplayLeaseManager leases,
            ISessionOwnershipTracker ownership,
            IStreamingBackend streaming,
            CancellationToken cancellationToken) =>
        {
            SessionPlan? plan = sessions.Get(clientId);
            StreamingSessionState? stream = null;
            SessionOwnershipSnapshot? ownershipSnapshot = null;
            if (plan is not null)
            {
                StreamingStopResult stop = await streaming.StopAsync(plan.SessionId, cancellationToken);
                if (!stop.Success)
                {
                    return Results.Problem(stop.Error, statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                stream = stop.Session;
                ownershipSnapshot = await ownership.GetSnapshotAsync(plan.SessionId, cancellationToken);
            }

            bool removed = await leases.CleanupIfAllowedAsync(
                DisplayLease.CreateDisplayId(new ClientId(clientId)),
                request.ClientActive,
                ownershipSnapshot?.LaunchedProcessRunning == true || ownershipSnapshot?.ChildProcessRunning == true,
                ownershipSnapshot?.OwnedWindowRemaining == true,
                cancellationToken);

            if (removed && plan is not null)
            {
                await ownership.ClearAsync(plan.SessionId, cancellationToken);
            }

            return Results.Ok(new { clientId, cleanupEvaluated = true, displayRemoved = removed, stream, ownership = ownershipSnapshot });
        });

        clients.MapPost("/{clientId}/emergency-restore", async (
            string clientId,
            InMemoryClientStore clients,
            IDisplayBackend displayBackend,
            CancellationToken cancellationToken) =>
        {
            ClientProfile? profile = clients.GetProfile(clientId);
            if (profile is null)
            {
                return Results.NotFound(new { error = $"Client '{clientId}' is not registered." });
            }

            if (!profile.Session.AllowEmergencyRestoreFromClient)
            {
                return Results.Problem(
                    "Emergency restore is disabled for this client profile.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            DisplayRestoreResult restore = await displayBackend.RestorePhysicalPrimaryAsync(cancellationToken);
            if (!restore.Success)
            {
                return Results.Problem(
                    restore.Error ?? "Physical primary restore failed.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Ok(new { clientId, restoreRequested = true });
        });

        return endpoints;
    }

    private static object CreatePlanResponse(SessionPlan plan, ClientProfile profile) =>
        new
        {
            sessionId = plan.SessionId,
            clientId = plan.ClientId.Value,
            appId = plan.AppId,
            display = plan.Display,
            stream = plan.Stream,
            recovery = new
            {
                restorePhysicalDisplayOnEnd = profile.Display.RestorePhysicalDisplayOnEnd,
                allowClientAbort = profile.Session.AllowEmergencyRestoreFromClient,
                terminateOwnedAppOnQuit = !profile.Session.KeepAppRunningOnDisconnect
            }
        };

    private static async Task<GameResolution> ResolveRequestedGameAsync(
        PlanRequest request,
        GameLibraryService games,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.GameId))
        {
            GameLibrarySnapshot snapshot = await games.ScanAsync(cancellationToken);
            GameDescriptor? game = snapshot.Games.FirstOrDefault(game =>
                game.Id.Equals(request.GameId, StringComparison.OrdinalIgnoreCase));

            return game is null
                ? new GameResolution(null, Results.NotFound(new { error = $"Game '{request.GameId}' is not available in the normalized library." }))
                : new GameResolution(game, null);
        }

        if (string.IsNullOrWhiteSpace(request.AppId) ||
            string.IsNullOrWhiteSpace(request.Title) ||
            string.IsNullOrWhiteSpace(request.Source))
        {
            return new GameResolution(null, Results.BadRequest(new { error = "Provide either gameId or appId, title, and source." }));
        }

        return new GameResolution(CreateRequestedGame(request), null);
    }

    private static GameDescriptor CreateRequestedGame(PlanRequest request) =>
        new(
            request.AppId!,
            request.Title!,
            request.Source!,
            new GameLaunchIntent("manual-request", request.AppId!),
            new GameArtwork(null, "none"),
            Installed: true,
            new GameProcessHints(null, null));

    private static ClientProfilePatch CreatePatch(JsonElement body) =>
        new(
            PreferredWidth: ReadInt(body, "preferredWidth"),
            PreferredHeight: ReadInt(body, "preferredHeight"),
            PreferredRefreshHz: ReadInt(body, "preferredRefreshHz"),
            HdrPreference: ReadHdrPreference(body, "hdrPreference"),
            CodecPreference: ReadString(body, "codecPreference"),
            QualityMode: ReadString(body, "qualityMode"),
            BitrateCapMbps: ReadInt(body, "bitrateCapMbps"),
            AudioMode: ReadString(body, "audioMode"),
            KeepAppRunningOnDisconnect: ReadBool(body, "keepAppRunningOnDisconnect"));

    private static int? ReadInt(JsonElement body, string propertyName) =>
        body.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    private static bool? ReadBool(JsonElement body, string propertyName) =>
        body.TryGetProperty(propertyName, out JsonElement value)
            ? value.GetBoolean()
            : null;

    private static string? ReadString(JsonElement body, string propertyName) =>
        body.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static HdrPreference? ReadHdrPreference(JsonElement body, string propertyName)
    {
        if (!body.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number)
        {
            return (HdrPreference)value.GetInt32();
        }

        return value.ValueKind == JsonValueKind.String && Enum.TryParse(value.GetString(), ignoreCase: true, out HdrPreference preference)
            ? preference
            : null;
    }
}

public sealed record ClientHelloRequest(string ClientId, string? Name, string? PairingToken = null);

internal sealed record GameResolution(GameDescriptor? Game, IResult? Error);

public sealed record PlanRequest(string? AppId = null, string? Title = null, string? Source = null, string? GameId = null);

public sealed record QuitRequest(bool ClientActive, bool? OwnedProcessRunning = null, bool? OwnedWindowRemaining = null);

public sealed record ClientInputRequest(long Sequence, IReadOnlyList<ClientInputEvent> Events);

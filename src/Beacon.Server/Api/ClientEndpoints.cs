using System.Text.Json;
using Beacon.Core.Clients;
using Beacon.Core.Displays;
using Beacon.Core.Games;
using Beacon.Core.Sessions;
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

        clients.MapPost("/hello", (ClientHelloRequest request, InMemoryClientStore store) =>
        {
            ClientProfile profile = store.GetProfile(request.ClientId) ?? ClientProfile.CreateZFold7Default();
            store.SaveProfile(profile);

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

        clients.MapPost("/{clientId}/plan", (
            string clientId,
            PlanRequest request,
            InMemoryClientStore clients,
            InMemorySessionStore sessions) =>
        {
            ClientProfile? profile = clients.GetProfile(clientId);
            if (profile is null)
            {
                return Results.NotFound(new { error = $"Client '{clientId}' is not registered." });
            }

            SessionPlanResult result = SessionPlanner.CreatePlan(
                profile,
                clients.GetCapabilities(clientId),
                clients.GetTelemetry(clientId),
                new GameDescriptor(request.AppId, request.Title, request.Source));

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
            DisplayLeaseManager leases,
            CancellationToken cancellationToken) =>
        {
            ClientProfile? profile = clients.GetProfile(clientId);
            if (profile is null)
            {
                return Results.NotFound(new { error = $"Client '{clientId}' is not registered." });
            }

            SessionPlanResult planResult = SessionPlanner.CreatePlan(
                profile,
                clients.GetCapabilities(clientId),
                clients.GetTelemetry(clientId),
                new GameDescriptor(request.AppId, request.Title, request.Source));

            if (!planResult.Success || planResult.Plan is null)
            {
                return Results.BadRequest(new { error = planResult.Error });
            }

            DisplayLeaseResult leaseResult = await leases.EnsureLeaseAsync(profile, cancellationToken);
            if (!leaseResult.Success || leaseResult.Lease is null)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            sessions.Save(planResult.Plan);
            return Results.Ok(new { clientId, displayId = leaseResult.Lease.DisplayId, state = "started" });
        });

        clients.MapPost("/{clientId}/disconnect", async (string clientId, DisplayLeaseManager leases, CancellationToken cancellationToken) =>
        {
            await leases.DisconnectAsync(DisplayLease.CreateDisplayId(new ClientId(clientId)), cancellationToken);
            return Results.Ok(new { clientId, leaseRetained = true });
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
            DisplayLeaseManager leases,
            CancellationToken cancellationToken) =>
        {
            bool removed = await leases.CleanupIfAllowedAsync(
                DisplayLease.CreateDisplayId(new ClientId(clientId)),
                request.ClientActive,
                request.OwnedProcessRunning,
                request.OwnedWindowRemaining,
                cancellationToken);

            return Results.Ok(new { clientId, cleanupEvaluated = true, displayRemoved = removed });
        });

        clients.MapPost("/{clientId}/display/recover", async (
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

        clients.MapPost("/{clientId}/emergency-restore", async (
            string clientId,
            IDisplayBackend displayBackend,
            CancellationToken cancellationToken) =>
        {
            await displayBackend.RestorePhysicalPrimaryAsync(cancellationToken);
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

public sealed record ClientHelloRequest(string ClientId, string? Name);

public sealed record PlanRequest(string AppId, string Title, string Source);

public sealed record QuitRequest(bool ClientActive, bool OwnedProcessRunning, bool OwnedWindowRemaining);

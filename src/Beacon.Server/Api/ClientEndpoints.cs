using System.Text.Json;
using System.Text.Json.Serialization;
using Beacon.Core.Benchmarks;
using Beacon.Core.Clients;
using Beacon.Core.Displays;
using Beacon.Core.Diagnostics;
using Beacon.Core.Games;
using Beacon.Core.Input;
using Beacon.Core.Sessions;
using Beacon.Core.Streaming;
using Beacon.Server.State;
using Beacon.Server.Security;

namespace Beacon.Server.Api;

public static class ClientEndpoints
{
    private static readonly JsonSerializerOptions WebJsonOptions = CreateWebJsonOptions();
    private static readonly TimeSpan MaximumBenchmarkEvidenceAge = TimeSpan.FromDays(7);

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

        clients.MapPost("/hello", (
            ClientHelloRequest request,
            HttpContext context,
            InMemoryClientStore store,
            ClientCredentialService credentials,
            BeaconSecurityPolicy security) =>
        {
            ClientProfile? existingProfile = store.GetProfile(request.ClientId);
            if (security.IsTestHost(context))
            {
                ClientProfile testProfile = existingProfile ?? store.RegisterProfile(request.ClientId, request.Name);
                return Results.Ok(CreateHelloResponse(testProfile));
            }

            string? submitted = BeaconSecurityMiddleware.ReadCredential(context.Request);
            if (existingProfile is not null && credentials.Authenticate(request.ClientId, submitted))
            {
                return Results.Ok(CreateHelloResponse(existingProfile));
            }

            PendingClientRegistration pending = credentials.RequestRegistration(request.ClientId, request.Name);
            return Results.Accepted($"/clients/registrations/{pending.RegistrationId}/completion", new
            {
                registrationId = pending.RegistrationId,
                clientId = pending.ClientId,
                state = "pending",
            });
        });

        clients.MapGet("/registrations/{registrationId}/completion", async (
            string registrationId,
            ClientCredentialService credentials,
            InMemoryClientStore store,
            CancellationToken cancellationToken) =>
        {
            PendingClientRegistration? registration = credentials.GetRegistration(registrationId);
            if (registration is null)
            {
                return Results.NotFound(new { error = "Client registration was not found." });
            }
            ApprovedClientCredential approved = await credentials.WaitForApprovalAsync(
                registrationId,
                cancellationToken);
            ClientProfile profile = store.RegisterProfile(registration.ClientId, registration.Name);
            return Results.Ok(new
            {
                clientId = approved.ClientId,
                credential = approved.Credential,
                profile,
                editableFields = EditableFields,
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

        clients.MapGet("/{clientId}/benchmarks", (string clientId, InMemoryClientStore store) =>
            store.GetProfile(clientId) is null
                ? Results.NotFound(new { error = $"Client '{clientId}' is not registered." })
                : Results.Ok(new { clientId, runs = store.GetBenchmarkEvidence(clientId) }));

        clients.MapPost("/{clientId}/benchmarks/prepare", (
            string clientId,
            JsonElement body,
            InMemoryClientStore store) =>
        {
            if (store.GetProfile(clientId) is null)
            {
                return Results.NotFound(new { error = $"Client '{clientId}' is not registered." });
            }

            if (ContainsRawNetworkIdentity(body))
            {
                return Results.BadRequest(new { error = "Raw network identity fields are not accepted." });
            }

            BenchmarkPrepareRequest? request;
            try
            {
                request = body.Deserialize<BenchmarkPrepareRequest>(WebJsonOptions);
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "Benchmark preparation JSON is invalid." });
            }

            if (request is null)
            {
                return Results.BadRequest(new { error = "Complete network and hardware fingerprints are required." });
            }

            try
            {
                BenchmarkEvidenceValidator.Validate(request.Fingerprints);
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new { error = "Benchmark fingerprints are invalid." });
            }

            BenchmarkPreparationResult preparation = store.PrepareBenchmarkRun(
                new ClientId(clientId),
                request.Trigger,
                request.Fingerprints,
                DateTimeOffset.UtcNow,
                MaximumBenchmarkEvidenceAge);
            BenchmarkTransportPlan transportPlan = BenchmarkSuitePolicy.Create(request.Trigger);
            return Results.Ok(new
            {
                disposition = preparation.Disposition switch
                {
                    BenchmarkPreparationDisposition.Reuse => "reuse",
                    BenchmarkPreparationDisposition.Continue => "continue",
                    _ => "start-new"
                },
                runId = preparation.Evidence.RunId,
                evidenceRevision = preparation.Evidence.CompletedAt is null
                    ? null
                    : preparation.Evidence.Revision,
                selectedResult = preparation.Evidence.SelectedResult,
                transportPlan,
                networkCoverage = BenchmarkSuitePolicy.Coverage(transportPlan),
                reason = preparation.Reason
            });
        });

        clients.MapPost("/{clientId}/benchmarks/{runId:guid}/complete", (
            string clientId,
            Guid runId,
            BenchmarkCompletionRequest request,
            InMemoryClientStore store) =>
        {
            BenchmarkEvidence? pending = store.GetBenchmarkEvidence(runId);
            if (pending is null || !pending.ClientId.Value.Equals(clientId, StringComparison.OrdinalIgnoreCase))
            {
                return Results.NotFound(new { error = "Benchmark run was not found for this client." });
            }

            if (pending.CompletedAt is not null)
            {
                return Results.Conflict(new { error = "Benchmark run is already complete." });
            }

            if (request.NetworkSamples is null ||
                request.DecoderSamples is null ||
                request.PowerSamples is null)
            {
                return Results.BadRequest(new { error = "Benchmark sample collections are required." });
            }

            ClientProfile? profile = store.GetProfile(clientId);
            if (profile is null)
            {
                return Results.NotFound(new { error = $"Client '{clientId}' is not registered." });
            }

            try
            {
                BenchmarkTransportPlan transportPlan = BenchmarkSuitePolicy.Create(pending.Trigger);
                NetworkBenchmarkCoverage coverage = BenchmarkSuitePolicy.Coverage(transportPlan);
                var scoringInput = new BenchmarkScoringInput(
                    request.NetworkSamples,
                    request.DecoderSamples,
                    request.PowerSamples,
                    profile.Stream.CodecPreference,
                    coverage);
                SelectedBenchmarkResult selected = BenchmarkScorer.Select(scoringInput);
                BenchmarkEvidence completed = pending with
                {
                    CompletedAt = DateTimeOffset.UtcNow,
                    NetworkSamples = request.NetworkSamples.ToArray(),
                    DecoderSamples = request.DecoderSamples.ToArray(),
                    PowerSamples = request.PowerSamples.ToArray(),
                    SelectedResult = selected,
                    NetworkCoverage = coverage
                };
                if (!store.TryCompleteBenchmarkEvidence(runId, clientId, completed, out BenchmarkEvidence? committed))
                {
                    return Results.Conflict(new { error = "Benchmark run was completed or replaced concurrently." });
                }

                return Results.Ok(new
                {
                    runId = committed!.RunId,
                    evidenceRevision = committed.Revision,
                    selectedResult = committed.SelectedResult
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new { error = "Benchmark evidence is invalid." });
            }
        });

        clients.MapPost("/{clientId}/plan", async (
            string clientId,
            PlanRequest request,
            InMemoryClientStore clients,
            InMemorySessionStore sessions,
            GameLibraryService games,
            CancellationToken cancellationToken) =>
        {
            PlanResolutionResult resolution = await ResolvePlanAsync(
                clientId,
                request,
                clients,
                games,
                "Completed benchmark evidence is required before session planning.",
                cancellationToken);
            if (resolution is PlanResolutionFailure failure)
            {
                return failure.Error;
            }

            var resolved = (ResolvedPlan)resolution;
            sessions.Save(resolved.Plan);
            return Results.Ok(CreatePlanResponse(resolved.Plan, resolved.Profile));
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
            StreamTicketProvisioningService ticketProvisioning,
            BeaconServerIdentity serverIdentity,
            CancellationToken cancellationToken) =>
        {
            PlanResolutionResult resolution = await ResolvePlanAsync(
                clientId,
                request,
                clients,
                games,
                "Completed benchmark evidence is required before launch.",
                cancellationToken);
            if (resolution is PlanResolutionFailure failure)
            {
                return failure.Error;
            }

            var resolved = (ResolvedPlan)resolution;

            StreamingPreflightResult streamingPreflight = await streaming.CheckReadinessAsync(resolved.Plan, cancellationToken);
            if (!streamingPreflight.Success)
            {
                return Results.Problem(
                    streamingPreflight.Error,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            DisplayLeaseResult leaseResult = await leases.EnsureLeaseAsync(resolved.Profile, cancellationToken);
            if (!leaseResult.Success || leaseResult.Lease is null)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            sessions.Save(resolved.Plan);

            GameLaunchResult launchResult = await launcher.LaunchAsync(
                new GameLaunchRequest(resolved.Game, resolved.Plan, leaseResult.Lease.DisplayId),
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

            await ownership.RecordLaunchAsync(resolved.Plan, launchResult.State, cancellationToken);

            StreamTicketProvisioningResult ticketResult = await ticketProvisioning.ProvisionAsync(
                clientId,
                resolved.Plan.SessionId,
                resolved.Plan.Revision,
                cancellationToken);
            if (!ticketResult.Success || ticketResult.Ticket is null)
            {
                return Results.Problem(
                    ticketResult.Error,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            IssuedStreamTicket issuedTicket = ticketResult.Ticket;

            StreamingStartResult streamResult = await streaming.StartAsync(resolved.Plan, cancellationToken);
            if (!streamResult.Success
                || streamResult.Session is null
                || !string.Equals(streamResult.Session.State, "running", StringComparison.Ordinal)
                || streamResult.Session.ActiveListenerPort is not (> 0 and <= 65_535)
                || streamResult.Session.RuntimeGeneration == Guid.Empty)
            {
                string streamError = streamResult.Error
                    ?? $"Stream session '{resolved.Plan.SessionId}' has no active streaming runtime.";
                string stopStatus = string.Empty;
                if (streamResult.Success)
                {
                    StreamingStopResult stopped = await streaming.StopRuntimeAsync(
                        resolved.Plan.SessionId,
                        streamResult.Session?.RuntimeGeneration ?? Guid.Empty,
                        cancellationToken);
                    stopStatus = stopped.Success
                        ? " Streaming runtime stopped after invalid metadata."
                        : $" Streaming runtime stop compensation failed after invalid metadata: {stopped.Error}.";
                }
                StreamTicketProvisioningResult revoked = await ticketProvisioning.RevokeSessionAsync(
                    clientId,
                    resolved.Plan.SessionId,
                    cancellationToken);
                string revocationStatus = revoked.Success
                    ? "Stream ticket revoked after stream start failure."
                    : $"Stream ticket revocation failed after stream start failure: {revoked.Error}";
                DisplayRestoreResult restore = await displayBackend.RestorePhysicalPrimaryAsync(cancellationToken);
                string restoreStatus = restore.Success
                    ? "Physical primary restore requested after stream start failure."
                    : $"Physical primary restore failed after stream start failure: {restore.Error}";

                return Results.Problem(
                    $"{streamError}{stopStatus} {revocationStatus} {restoreStatus}",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Ok(new
            {
                clientId,
                displayId = leaseResult.Lease.DisplayId,
                state = "streaming",
                launch = launchResult.State,
                stream = streamResult.Session,
                connection = CreateConnectionGrant(
                    resolved.Plan,
                    streamResult.Session,
                    issuedTicket,
                    serverIdentity),
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

            StreamingSessionState? streamSession = await streaming.GetSessionAsync(
                plan.SessionId,
                cancellationToken);
            return streamSession is null
                ? Results.NotFound(new { error = $"Stream session '{plan.SessionId}' is not running." })
                : Results.Ok(new
                {
                    clientId,
                    stream = streamSession
                });
        });

        clients.MapPost("/{clientId}/stream/stop", async (
            string clientId,
            InMemorySessionStore sessions,
            IStreamingBackend streaming,
            StreamTicketProvisioningService ticketProvisioning,
            CancellationToken cancellationToken) =>
        {
            SessionPlan? plan = sessions.Get(clientId);
            if (plan is null)
            {
                return Results.NotFound(new { error = $"Client '{clientId}' has no session plan." });
            }

            StreamingStopResult stop = await streaming.StopAsync(plan.SessionId, cancellationToken);
            if (!stop.Success || stop.Session is null)
            {
                return Results.Problem(stop.Error, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            StreamTicketProvisioningResult revoked = await ticketProvisioning.RevokeSessionAsync(
                clientId,
                plan.SessionId,
                cancellationToken);
            return revoked.Success
                ? Results.Ok(new { clientId, stream = stop.Session })
                : Results.Problem(revoked.Error, statusCode: StatusCodes.Status503ServiceUnavailable);
        });

        clients.MapPost("/{clientId}/beacon", async (
            string clientId,
            HttpRequest httpRequest,
            InMemoryClientStore clients,
            InMemorySessionStore sessions,
            DisplayLeaseManager leases,
            ISessionOwnershipTracker ownership,
            CancellationToken cancellationToken) =>
        {
            ClientProfile? profile = clients.GetProfile(clientId);
            if (profile is null)
            {
                return Results.NotFound(new { error = $"Client '{clientId}' is not registered." });
            }

            BeaconRequest request = await ReadBeaconRequestAsync(httpRequest, cancellationToken);
            string displayId = DisplayLease.CreateDisplayId(new ClientId(clientId));
            if (request.Active)
            {
                DisplayLeaseResult leaseResult = await leases.PrepareLeaseAsync(profile, cancellationToken);
                if (!leaseResult.Success || leaseResult.Lease is null)
                {
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                }

                return Results.Ok(new
                {
                    clientId,
                    state = "active",
                    displayId = leaseResult.Lease.DisplayId,
                    leasePrepared = true,
                    displayRemoved = false
                });
            }

            SessionPlan? plan = sessions.Get(clientId);
            SessionOwnershipSnapshot? ownershipSnapshot = plan is null
                ? null
                : await ownership.GetSnapshotAsync(plan.SessionId, cancellationToken);
            bool displayRemoved = await leases.CleanupIfAllowedAsync(
                displayId,
                clientActive: false,
                ownershipSnapshot?.LaunchedProcessRunning == true || ownershipSnapshot?.ChildProcessRunning == true,
                ownershipSnapshot?.OwnedWindowRemaining == true,
                cancellationToken);

            if (displayRemoved && plan is not null)
            {
                await ownership.ClearAsync(plan.SessionId, cancellationToken);
            }

            return Results.Ok(new
            {
                clientId,
                state = "inactive",
                displayId,
                leasePrepared = false,
                displayRemoved,
                ownership = ownershipSnapshot
            });
        });

        clients.MapPost("/{clientId}/input", async (
            string clientId,
            HttpClientInputTransportRequest request,
            InMemorySessionStore sessions,
            IStreamingBackend streaming,
            IClientInputSink input,
            IDiagnosticEventSink diagnostics,
            CancellationToken cancellationToken) =>
        {
            SessionPlan? plan = sessions.Get(clientId);
            if (plan is null)
            {
                PublishInputDiagnostic(
                    diagnostics,
                    DiagnosticSeverity.Warning,
                    "input.reject",
                    "Input rejected because the client has no session plan.",
                    clientId,
                    sessionId: null,
                    displayId: null,
                    request.Sequence,
                    request.Events?.Count ?? 0);
                return Results.NotFound(new { error = $"Client '{clientId}' has no session plan." });
            }

            StreamingSessionState? stream = await streaming.GetSessionAsync(plan.SessionId, cancellationToken);
            if (stream is null || !stream.State.Equals("running", StringComparison.OrdinalIgnoreCase))
            {
                PublishInputDiagnostic(
                    diagnostics,
                    DiagnosticSeverity.Warning,
                    "input.reject",
                    $"Input rejected because stream session '{plan.SessionId}' is not running.",
                    clientId,
                    plan.SessionId,
                    plan.Display.DisplayId,
                    request.Sequence,
                    request.Events?.Count ?? 0);
                return Results.NotFound(new { error = $"Stream session '{plan.SessionId}' is not running." });
            }

            if (request.Events is not { Count: > 0 })
            {
                PublishInputDiagnostic(
                    diagnostics,
                    DiagnosticSeverity.Warning,
                    "input.reject",
                    "Input rejected because the request did not include events.",
                    clientId,
                    plan.SessionId,
                    plan.Display.DisplayId,
                    request.Sequence,
                    0);
                return Results.BadRequest(new { error = "Input request must include at least one event." });
            }

            ClientInputEvent[] events = request.Events
                .Select(inputEvent => inputEvent.ToCoreEvent())
                .ToArray();

            var batch = new ClientInputBatch(
                clientId,
                plan.SessionId,
                plan.Display.DisplayId,
                request.Sequence,
                events);
            ClientInputResult result = await input.ForwardAsync(batch, cancellationToken);
            if (!result.Success)
            {
                PublishInputDiagnostic(
                    diagnostics,
                    DiagnosticSeverity.Error,
                    "input.forward",
                    "Input forwarding failed.",
                    clientId,
                    plan.SessionId,
                    plan.Display.DisplayId,
                    request.Sequence,
                    request.Events.Count);
                return Results.Problem(result.Error, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            PublishInputDiagnostic(
                diagnostics,
                DiagnosticSeverity.Information,
                "input.forward",
                $"Forwarded {result.EventCount} input event(s).",
                clientId,
                plan.SessionId,
                plan.Display.DisplayId,
                request.Sequence,
                result.EventCount);
            return Results.Ok(new
            {
                clientId,
                sessionId = plan.SessionId,
                displayId = plan.Display.DisplayId,
                accepted = true,
                eventCount = result.EventCount
            });
        });

        clients.MapPost("/{clientId}/disconnect", async (
            string clientId,
            HttpRequest httpRequest,
            InMemorySessionStore sessions,
            DisplayLeaseManager leases,
            IStreamingBackend streaming,
            ISessionOwnershipTracker ownership,
            CancellationToken cancellationToken) =>
        {
            DisconnectRequest request = await ReadDisconnectRequestAsync(httpRequest, cancellationToken);
            await leases.DisconnectAsync(DisplayLease.CreateDisplayId(new ClientId(clientId)), cancellationToken);
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
                if (!request.ClientActive)
                {
                    ownershipSnapshot = await ownership.GetSnapshotAsync(plan.SessionId, cancellationToken);
                }
            }

            bool displayRemoved = false;
            if (!request.ClientActive)
            {
                displayRemoved = await leases.CleanupIfAllowedAsync(
                    DisplayLease.CreateDisplayId(new ClientId(clientId)),
                    clientActive: false,
                    ownershipSnapshot?.LaunchedProcessRunning == true || ownershipSnapshot?.ChildProcessRunning == true,
                    ownershipSnapshot?.OwnedWindowRemaining == true,
                    cancellationToken);

                if (displayRemoved && plan is not null)
                {
                    await ownership.ClearAsync(plan.SessionId, cancellationToken);
                }
            }

            return Results.Ok(new
            {
                clientId,
                leaseRetained = !displayRemoved,
                displayRemoved,
                stream,
                ownership = ownershipSnapshot
            });
        });

        clients.MapPost("/{clientId}/reconnect", async (
            string clientId,
            InMemoryClientStore clients,
            InMemorySessionStore sessions,
            DisplayLeaseManager leases,
            IStreamingBackend streaming,
            StreamTicketProvisioningService ticketProvisioning,
            BeaconServerIdentity serverIdentity,
            CancellationToken cancellationToken) =>
        {
            ClientProfile? profile = clients.GetProfile(clientId);
            if (profile is null)
            {
                return Results.NotFound(new { error = $"Client '{clientId}' is not registered." });
            }

            SessionPlan? plan = sessions.Get(clientId);
            if (plan is null)
            {
                return Results.Problem(
                    $"Client '{clientId}' has no session plan or active streaming runtime.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            StreamingSessionState? streamingSession = await streaming.GetSessionAsync(
                plan.SessionId,
                cancellationToken);
            if (streamingSession is null
                || !string.Equals(streamingSession.State, "running", StringComparison.Ordinal)
                || streamingSession.ActiveListenerPort is not (> 0 and <= 65_535)
                || streamingSession.RuntimeGeneration == Guid.Empty)
            {
                return Results.Problem(
                    $"Stream session '{plan.SessionId}' has no active streaming runtime.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            DisplayLeaseResult leaseResult = await leases.EnsureLeaseAsync(profile, cancellationToken);
            if (!leaseResult.Success || leaseResult.Lease is null)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            StreamTicketProvisioningResult ticketResult = await ticketProvisioning.ProvisionAsync(
                clientId,
                plan.SessionId,
                plan.Revision,
                cancellationToken);
            if (!ticketResult.Success || ticketResult.Ticket is null)
            {
                return Results.Problem(
                    ticketResult.Error,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            IssuedStreamTicket replacement = ticketResult.Ticket;
            StreamingSessionState? confirmedStreamingSession = await streaming.GetSessionAsync(
                plan.SessionId,
                cancellationToken);
            if (!IsSameActiveRuntime(streamingSession, confirmedStreamingSession))
            {
                StreamTicketProvisioningResult revoked = await ticketProvisioning.RevokeSessionAsync(
                    clientId,
                    plan.SessionId,
                    cancellationToken);
                string revocationStatus = revoked.Success
                    ? "Reconnect ticket revoked."
                    : $"Reconnect ticket revocation failed: {revoked.Error}";
                return Results.Problem(
                    $"Stream session '{plan.SessionId}' changed while provisioning reconnect ticket. " +
                    revocationStatus,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            return Results.Ok(new
            {
                clientId,
                displayId = leaseResult.Lease.DisplayId,
                state = "reconnected",
                connection = CreateConnectionGrant(
                    plan,
                    confirmedStreamingSession!,
                    replacement,
                    serverIdentity),
            });
        });

        clients.MapPost("/{clientId}/quit", async (
            string clientId,
            QuitRequest request,
            InMemorySessionStore sessions,
            DisplayLeaseManager leases,
            ISessionOwnershipTracker ownership,
            IStreamingBackend streaming,
            StreamTicketProvisioningService ticketProvisioning,
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
                StreamTicketProvisioningResult revoked = await ticketProvisioning.RevokeSessionAsync(
                    clientId,
                    plan.SessionId,
                    cancellationToken);
                if (!revoked.Success)
                {
                    return Results.Problem(
                        revoked.Error,
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }
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
            DisplayLeaseManager leases,
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

            string displayId = DisplayLease.CreateDisplayId(new ClientId(clientId));
            DisplayRecoveryResult recovery = await leases.RecoverDisplayAsync(displayId, cancellationToken);
            if (!recovery.Success)
            {
                return Results.Problem(
                    recovery.Error ?? "Client-scoped emergency display recovery failed.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Ok(new { clientId, displayId, recovered = true });
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

    private static JsonSerializerOptions CreateWebJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static bool ContainsRawNetworkIdentity(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (property.Name.Equals("ssid", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("bssid", StringComparison.OrdinalIgnoreCase) ||
                    ContainsRawNetworkIdentity(property.Value))
                {
                    return true;
                }
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            return value.EnumerateArray().Any(ContainsRawNetworkIdentity);
        }

        return false;
    }

    private static ConnectionGrant CreateConnectionGrant(
        SessionPlan plan,
        StreamingSessionState streamingSession,
        IssuedStreamTicket ticket,
        BeaconServerIdentity serverIdentity) =>
        new(
            ProtocolVersion: 1,
            Ticket: ticket.Ticket,
            ExpiresAt: ticket.ExpiresAt,
            PlanRevision: plan.Revision,
            PlanExplanation: $"{plan.Display.Reason} {plan.Stream.Reason}",
            SessionId: plan.SessionId,
            Port: streamingSession.ActiveListenerPort!.Value,
            PublicKeyFingerprint: serverIdentity.PublicKeyFingerprint,
            SelectedVideo: new SelectedVideoGrant(
                Codec: plan.Stream.Codec,
                Width: plan.Display.Width,
                Height: plan.Display.Height,
                FramesPerSecondNumerator: plan.Stream.Fps,
                FramesPerSecondDenominator: 1,
                DynamicRange: plan.Display.HdrMode));

    private static bool IsSameActiveRuntime(
        StreamingSessionState expected,
        StreamingSessionState? actual) =>
        actual is not null
        && string.Equals(actual.State, "running", StringComparison.Ordinal)
        && actual.ActiveListenerPort is > 0 and <= 65_535
        && actual.RuntimeGeneration != Guid.Empty
        && string.Equals(actual.SessionId, expected.SessionId, StringComparison.Ordinal)
        && actual.ActiveListenerPort == expected.ActiveListenerPort
        && actual.RuntimeGeneration == expected.RuntimeGeneration;

    private sealed record ConnectionGrant(
        int ProtocolVersion,
        string Ticket,
        DateTimeOffset ExpiresAt,
        ulong PlanRevision,
        string PlanExplanation,
        string SessionId,
        int Port,
        string PublicKeyFingerprint,
        SelectedVideoGrant SelectedVideo);

    private sealed record SelectedVideoGrant(
        string Codec,
        int Width,
        int Height,
        int FramesPerSecondNumerator,
        int FramesPerSecondDenominator,
        string DynamicRange);

    private static async Task<PlanResolutionResult> ResolvePlanAsync(
        string clientId,
        PlanRequest request,
        InMemoryClientStore clients,
        GameLibraryService games,
        string missingBenchmarkError,
        CancellationToken cancellationToken)
    {
        ClientProfile? profile = clients.GetProfile(clientId);
        if (profile is null)
        {
            return new PlanResolutionFailure(
                Results.NotFound(new { error = $"Client '{clientId}' is not registered." }));
        }

        GameResolution gameResolution = await ResolveRequestedGameAsync(request, games, cancellationToken);
        if (gameResolution.Error is not null)
        {
            return new PlanResolutionFailure(gameResolution.Error);
        }

        BenchmarkPlanEvidence? benchmark;
        try
        {
            benchmark = clients.GetLatestBenchmarkPlanEvidence(
                clientId,
                DateTimeOffset.UtcNow,
                MaximumBenchmarkEvidenceAge,
                profile.Stream.CodecPreference);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        {
            return new PlanResolutionFailure(Results.Conflict(new
            {
                error = $"Benchmark evidence does not certify codec preference " +
                    $"'{profile.Stream.CodecPreference}': {error.Message}"
            }));
        }

        if (benchmark is null)
        {
            return new PlanResolutionFailure(Results.Conflict(new { error = missingBenchmarkError }));
        }

        SessionPlanResult planResult = SessionPlanner.CreatePlan(
            profile,
            clients.GetCapabilities(clientId),
            benchmark,
            gameResolution.Game!);
        if (!planResult.Success || planResult.Plan is null)
        {
            return new PlanResolutionFailure(Results.BadRequest(new { error = planResult.Error }));
        }

        return new ResolvedPlan(profile, gameResolution.Game!, planResult.Plan);
    }

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

    private static object CreateHelloResponse(ClientProfile profile) => new
    {
        clientId = profile.ClientId.Value,
        profile,
        editableFields = EditableFields,
    };

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

    private static async Task<DisconnectRequest> ReadDisconnectRequestAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        return await ReadOptionalJsonRequestAsync(
            request,
            static () => new DisconnectRequest(),
            "Disconnect request JSON is invalid.",
            cancellationToken);
    }

    private static async Task<BeaconRequest> ReadBeaconRequestAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        return await ReadOptionalJsonRequestAsync(
            request,
            static () => new BeaconRequest(),
            "Beacon request JSON is invalid.",
            cancellationToken);
    }

    private static async Task<TRequest> ReadOptionalJsonRequestAsync<TRequest>(
        HttpRequest request,
        Func<TRequest> createDefault,
        string invalidJsonMessage,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength is <= 0 ||
            (request.ContentLength is null && string.IsNullOrWhiteSpace(request.ContentType)))
        {
            return createDefault();
        }

        try
        {
            TRequest? parsed = await JsonSerializer.DeserializeAsync<TRequest>(
                request.Body,
                WebJsonOptions,
                cancellationToken: cancellationToken);
            return parsed ?? createDefault();
        }
        catch (JsonException ex)
        {
            throw new BadHttpRequestException(invalidJsonMessage, ex);
        }
    }

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

    private static void PublishInputDiagnostic(
        IDiagnosticEventSink diagnostics,
        string severity,
        string operation,
        string message,
        string clientId,
        string? sessionId,
        string? displayId,
        long sequence,
        int eventCount)
    {
        diagnostics.Publish(DiagnosticEvent.Create(
            severity,
            "input",
            operation,
            message,
            clientId,
            sessionId,
            displayId,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["sequence"] = sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["eventCount"] = eventCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }));
    }

    private abstract record PlanResolutionResult;

    private sealed record ResolvedPlan(
        ClientProfile Profile,
        GameDescriptor Game,
        SessionPlan Plan) : PlanResolutionResult;

    private sealed record PlanResolutionFailure(IResult Error) : PlanResolutionResult;
}

public sealed record BenchmarkPrepareRequest(
    BenchmarkTrigger Trigger,
    BenchmarkFingerprintSet Fingerprints);

public sealed record BenchmarkCompletionRequest(
    IReadOnlyList<NetworkBenchmarkSample> NetworkSamples,
    IReadOnlyList<DecoderBenchmarkSample> DecoderSamples,
    IReadOnlyList<EndpointPowerSample> PowerSamples);

public sealed record ClientHelloRequest(string ClientId, string? Name);

internal sealed record GameResolution(GameDescriptor? Game, IResult? Error);

public sealed record PlanRequest(string? AppId = null, string? Title = null, string? Source = null, string? GameId = null);

public sealed record QuitRequest(bool ClientActive, bool? OwnedProcessRunning = null, bool? OwnedWindowRemaining = null);

public sealed record DisconnectRequest(bool ClientActive = true);

public sealed record BeaconRequest(bool Active = true);

public sealed record ClientInputRequest(long Sequence, IReadOnlyList<ClientInputEvent> Events);

internal sealed record HttpClientInputTransportRequest(
    long Sequence,
    IReadOnlyList<HttpClientInputTransportEvent> Events);

internal sealed record HttpClientInputTransportEvent(
    string Type,
    string Action,
    int? PointerId = null,
    double? X = null,
    double? Y = null,
    int? Buttons = null,
    string? Key = null,
    string? Code = null,
    double? Value = null)
{
    public ClientInputEvent ToCoreEvent() =>
        new(Type, Action, PointerId, X, Y, Buttons, Key, Code, Value);
}

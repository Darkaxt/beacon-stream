using Beacon.Core.Benchmarks;
using Beacon.Server.Security;

namespace Beacon.Server.Benchmarks;

public sealed record BenchmarkRuntimeGrantResult(
    bool Success,
    BenchmarkConnectionGrant? Connection,
    string? Error)
{
    public static BenchmarkRuntimeGrantResult Granted(BenchmarkConnectionGrant connection) =>
        new(true, connection, null);

    public static BenchmarkRuntimeGrantResult Fail(string error) => new(false, null, error);
}

public sealed record BenchmarkConnectionGrant(
    int ProtocolVersion,
    string Ticket,
    DateTimeOffset ExpiresAt,
    ulong PlanRevision,
    string PlanExplanation,
    string SessionId,
    int Port,
    string PublicKeyFingerprint,
    BenchmarkGrant Benchmark);

public sealed record BenchmarkGrant(
    Guid RunId,
    int SchemaVersion,
    BenchmarkRoundGrant ReliableRound,
    BenchmarkRoundGrant DatagramRound,
    byte[] RunToken);

public sealed record BenchmarkRoundGrant(
    int PacketCount,
    int PayloadBytes,
    long MeasurementIntervalUs);

public sealed class BenchmarkRuntimeOrchestrator(
    IBenchmarkRuntime runtime,
    StreamTicketProvisioningService ticketProvisioning,
    BeaconServerIdentity serverIdentity)
{
    public async Task<BenchmarkRuntimeGrantResult> StartAsync(
        BenchmarkRuntimePlan plan,
        IReadOnlyList<Guid> supersededRunIds,
        CancellationToken cancellationToken)
    {
        foreach (Guid supersededRunId in supersededRunIds)
        {
            string? cleanupError = await StopAsync(
                plan.ClientId.Value,
                supersededRunId,
                cancellationToken).ConfigureAwait(false);
            if (cleanupError is not null)
            {
                return BenchmarkRuntimeGrantResult.Fail(cleanupError);
            }
        }

        BenchmarkRuntimeState? active = await runtime.GetAsync(
            plan.SessionId,
            cancellationToken).ConfigureAwait(false);
        bool startedHere = active is null || active.State != "running";
        if (startedHere)
        {
            BenchmarkRuntimeStartResult started = await runtime.StartAsync(
                plan,
                cancellationToken).ConfigureAwait(false);
            if (!started.Success || started.Runtime is null)
            {
                return BenchmarkRuntimeGrantResult.Fail(
                    started.Error ?? "Benchmark runtime failed to start.");
            }
            active = started.Runtime;
        }

        if (!IsSameActiveRuntime(plan, active))
        {
            await StopStartedRuntimeAsync(plan, startedHere, cancellationToken).ConfigureAwait(false);
            return BenchmarkRuntimeGrantResult.Fail(
                "Benchmark runtime returned invalid connection metadata.");
        }

        StreamTicketProvisioningResult ticketResult = await ticketProvisioning.ProvisionAsync(
            plan.ClientId.Value,
            plan.SessionId,
            plan.Revision,
            cancellationToken).ConfigureAwait(false);
        if (!ticketResult.Success || ticketResult.Ticket is null)
        {
            await StopStartedRuntimeAsync(plan, startedHere, cancellationToken).ConfigureAwait(false);
            return BenchmarkRuntimeGrantResult.Fail(
                ticketResult.Error ?? "Benchmark ticket provisioning failed.");
        }

        BenchmarkRuntimeState? confirmed = await runtime.GetAsync(
            plan.SessionId,
            cancellationToken).ConfigureAwait(false);
        if (!IsSameActiveRuntime(plan, confirmed)
            || confirmed!.RuntimeGeneration != active!.RuntimeGeneration)
        {
            _ = await ticketProvisioning.RevokeSessionAsync(
                plan.ClientId.Value,
                plan.SessionId,
                cancellationToken).ConfigureAwait(false);
            await StopStartedRuntimeAsync(plan, startedHere, cancellationToken).ConfigureAwait(false);
            return BenchmarkRuntimeGrantResult.Fail(
                "Benchmark runtime changed while provisioning its connection ticket.");
        }

        return BenchmarkRuntimeGrantResult.Granted(CreateConnectionGrant(
            plan,
            confirmed,
            ticketResult.Ticket));
    }

    public async Task<string?> StopAsync(
        string clientId,
        Guid runId,
        CancellationToken cancellationToken)
    {
        string sessionId = $"benchmark:{runId:D}";
        BenchmarkRuntimeState? active = await runtime.GetAsync(
            sessionId,
            cancellationToken).ConfigureAwait(false);
        if (active is not null && active.State == "running")
        {
            BenchmarkRuntimeStopResult stopped = await runtime.StopAsync(
                sessionId,
                cancellationToken).ConfigureAwait(false);
            if (!stopped.Success)
            {
                return stopped.Error ?? "Benchmark runtime failed to stop.";
            }
        }

        StreamTicketProvisioningResult revoked = await ticketProvisioning.RevokeSessionAsync(
            clientId,
            sessionId,
            cancellationToken).ConfigureAwait(false);
        return revoked.Success
            ? null
            : revoked.Error ?? "Benchmark ticket revocation failed.";
    }

    private async Task StopStartedRuntimeAsync(
        BenchmarkRuntimePlan plan,
        bool startedHere,
        CancellationToken cancellationToken)
    {
        if (startedHere)
        {
            _ = await runtime.StopAsync(plan.SessionId, cancellationToken).ConfigureAwait(false);
        }
    }

    private BenchmarkConnectionGrant CreateConnectionGrant(
        BenchmarkRuntimePlan plan,
        BenchmarkRuntimeState active,
        IssuedStreamTicket ticket) =>
        new(
            ProtocolVersion: 1,
            Ticket: ticket.Ticket,
            ExpiresAt: ticket.ExpiresAt,
            PlanRevision: plan.Revision,
            PlanExplanation: $"{plan.Trigger} benchmark suite selected by server policy.",
            SessionId: plan.SessionId,
            Port: active.ActiveListenerPort!.Value,
            PublicKeyFingerprint: serverIdentity.PublicKeyFingerprint,
            Benchmark: new BenchmarkGrant(
                RunId: plan.RunId,
                SchemaVersion: plan.SchemaVersion,
                ReliableRound: new BenchmarkRoundGrant(
                    plan.TransportPlan.ReliablePacketCount,
                    plan.TransportPlan.ReliablePayloadBytes,
                    plan.TransportPlan.MeasurementIntervalUs),
                DatagramRound: new BenchmarkRoundGrant(
                    plan.TransportPlan.DatagramPacketCount,
                    plan.TransportPlan.DatagramPayloadBytes,
                    plan.TransportPlan.MeasurementIntervalUs),
                RunToken: (byte[])active.RunToken.Clone()));

    private static bool IsSameActiveRuntime(
        BenchmarkRuntimePlan plan,
        BenchmarkRuntimeState? active) =>
        active is not null
        && active.State == "running"
        && active.ActiveListenerPort is > 0 and <= 65_535
        && active.RuntimeGeneration != Guid.Empty
        && active.RunToken.Length == 16
        && active.RunId == plan.RunId
        && active.SessionId == plan.SessionId
        && active.ClientId == plan.ClientId.Value
        && active.PlanRevision == plan.Revision
        && active.SchemaVersion == plan.SchemaVersion
        && active.TransportPlan == plan.TransportPlan;
}

using Beacon.Core.Sessions;

namespace Beacon.Core.Streaming;

public sealed class UnavailableStreamingBackend : IStreamingBackend
{
    public const string Diagnostic =
        "Beacon StreamWorker is not implemented during architecture recovery.";

    public Task<StreamingBackendHealth> GetHealthAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new StreamingBackendHealth(
            Ready: false,
            State: "unavailable",
            Diagnostic,
            Capabilities: new([], [], [], null, null, false),
            ActiveSessions: 0,
            Diagnostics: []));
    }

    public Task<StreamingPreflightResult> CheckReadinessAsync(
        SessionPlan plan,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(StreamingPreflightResult.Fail(Diagnostic));
    }

    public Task<StreamingStartResult> StartAsync(
        SessionPlan plan,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(StreamingStartResult.Fail(Diagnostic));
    }

    public Task<StreamingStopResult> StopAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(StreamingStopResult.Fail(Diagnostic));
    }

    public Task<StreamingSessionState?> GetSessionAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<StreamingSessionState?>(null);
    }

    public IReadOnlyList<StreamingSessionState> GetSessions() => [];
}

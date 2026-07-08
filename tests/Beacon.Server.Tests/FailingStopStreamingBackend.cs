using Beacon.Core.Sessions;
using Beacon.Core.Streaming;

namespace Beacon.Server.Tests;

internal sealed class FailingStopStreamingBackend(string stopError) : IStreamingBackend
{
    private readonly FakeStreamingBackend inner = new();

    public Task<StreamingPreflightResult> CheckReadinessAsync(SessionPlan plan, CancellationToken cancellationToken) =>
        inner.CheckReadinessAsync(plan, cancellationToken);

    public Task<StreamingStartResult> StartAsync(SessionPlan plan, CancellationToken cancellationToken) =>
        inner.StartAsync(plan, cancellationToken);

    public Task<StreamingStopResult> StopAsync(string sessionId, CancellationToken cancellationToken)
    {
        inner.StopCalls.Add(sessionId);
        return Task.FromResult(StreamingStopResult.Fail(stopError));
    }

    public Task<StreamingSessionState?> GetSessionAsync(string sessionId, CancellationToken cancellationToken) =>
        inner.GetSessionAsync(sessionId, cancellationToken);

    public IReadOnlyList<StreamingSessionState> GetSessions() => inner.GetSessions();
}

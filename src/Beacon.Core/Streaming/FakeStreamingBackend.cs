using Beacon.Core.Sessions;

namespace Beacon.Core.Streaming;

public sealed class FakeStreamingBackend : IStreamingBackend
{
    private readonly Dictionary<string, StreamingSessionState> sessions = new(StringComparer.OrdinalIgnoreCase);

    public string? NextStartError { get; set; }

    public string? NextPreflightError { get; set; }

    public List<string> StartCalls { get; } = [];

    public List<string> StopCalls { get; } = [];

    public Task<StreamingPreflightResult> CheckReadinessAsync(SessionPlan plan, CancellationToken cancellationToken) =>
        Task.FromResult(string.IsNullOrWhiteSpace(NextPreflightError)
            ? StreamingPreflightResult.Ok()
            : StreamingPreflightResult.Fail(NextPreflightError));

    public Task<StreamingStartResult> StartAsync(SessionPlan plan, CancellationToken cancellationToken)
    {
        StartCalls.Add(plan.SessionId);

        if (!string.IsNullOrWhiteSpace(NextStartError))
        {
            string error = NextStartError;
            NextStartError = null;
            return Task.FromResult(StreamingStartResult.Fail(error));
        }

        string launchUri = $"beacon-fake://stream/{plan.SessionId}";
        var connection = new StreamingConnectionDescriptor(
            "beacon-fake",
            launchUri,
            [new StreamingEndpointDescriptor("control", launchUri)],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["displayId"] = plan.Display.DisplayId,
                ["transport"] = plan.Stream.Transport
            });

        var session = new StreamingSessionState(
            plan.SessionId,
            plan.ClientId.Value,
            plan.AppId,
            plan.Display.DisplayId,
            plan.Stream.Codec,
            plan.Stream.Fps,
            plan.Stream.InitialBitrateMbps,
            plan.Stream.Transport,
            State: "running",
            Error: null,
            connection);

        sessions[plan.SessionId] = session;
        return Task.FromResult(StreamingStartResult.Ok(session));
    }

    public Task<StreamingStopResult> StopAsync(string sessionId, CancellationToken cancellationToken)
    {
        StopCalls.Add(sessionId);

        if (!sessions.TryGetValue(sessionId, out StreamingSessionState? session))
        {
            return Task.FromResult(StreamingStopResult.Fail($"Stream session '{sessionId}' is not running."));
        }

        StreamingSessionState stopped = session with { State = "stopped" };
        sessions[sessionId] = stopped;
        return Task.FromResult(StreamingStopResult.Ok(stopped));
    }

    public Task<StreamingSessionState?> GetSessionAsync(string sessionId, CancellationToken cancellationToken) =>
        Task.FromResult(sessions.GetValueOrDefault(sessionId));

    public IReadOnlyList<StreamingSessionState> GetSessions() =>
        sessions.Values
            .OrderBy(session => session.ClientId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(session => session.SessionId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

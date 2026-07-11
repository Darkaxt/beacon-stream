using System.Buffers.Binary;
using System.Collections.Concurrent;
using Beacon.Core.Sessions;

namespace Beacon.Core.Streaming;

public sealed class FakeStreamingBackend : IStreamingBackend
{
    private readonly ConcurrentDictionary<string, StreamingSessionState> sessions =
        new(StringComparer.OrdinalIgnoreCase);
    private long nextRuntimeGeneration;

    public string? NextStartError { get; set; }

    public string? NextPreflightError { get; set; }

    public string? NextStopError { get; set; }

    public int ActiveListenerPort { get; set; } = 47998;

    public List<string> StartCalls { get; } = [];

    public List<string> StopCalls { get; } = [];

    public Task<StreamingBackendHealth> GetHealthAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new StreamingBackendHealth(
            Ready: string.IsNullOrWhiteSpace(NextPreflightError),
            State: string.IsNullOrWhiteSpace(NextPreflightError) ? "ready" : "unavailable",
            Diagnostic: string.IsNullOrWhiteSpace(NextPreflightError)
                ? "Fake streaming backend ready."
                : NextPreflightError,
            Capabilities: new(
                Codecs: ["h264", "hevc", "av1"],
                Encoders: ["fake"],
                CaptureMethods: ["fake"],
                MaxFps: 120,
                MaxBitrateMbps: null,
                Hdr10: false),
            ActiveSessions: sessions.Count,
            Diagnostics: []));

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

        var session = new StreamingSessionState(
            plan.SessionId,
            plan.ClientId.Value,
            plan.AppId,
            plan.Display.DisplayId,
            plan.Stream.Codec,
            plan.Stream.Fps,
            plan.Stream.InitialBitrateMbps,
            State: "running",
            Error: null,
            ActiveListenerPort: ActiveListenerPort,
            RuntimeGeneration: CreateRuntimeGeneration());

        sessions[plan.SessionId] = session;
        return Task.FromResult(StreamingStartResult.Ok(session));
    }

    public Task<StreamingStopResult> StopAsync(
        string sessionId,
        CancellationToken cancellationToken) =>
        StopCore(sessionId, expectedGeneration: null);

    public Task<StreamingStopResult> StopRuntimeAsync(
        string sessionId,
        Guid expectedGeneration,
        CancellationToken cancellationToken) =>
        StopCore(sessionId, expectedGeneration);

    private Task<StreamingStopResult> StopCore(string sessionId, Guid? expectedGeneration)
    {
        StopCalls.Add(sessionId);

        if (!string.IsNullOrWhiteSpace(NextStopError))
        {
            string error = NextStopError;
            NextStopError = null;
            return Task.FromResult(StreamingStopResult.Fail(error));
        }

        if (!sessions.TryGetValue(sessionId, out StreamingSessionState? session))
        {
            return Task.FromResult(StreamingStopResult.Fail($"Stream session '{sessionId}' is not running."));
        }
        if (expectedGeneration.HasValue
            && session.RuntimeGeneration != expectedGeneration.Value)
        {
            return Task.FromResult(StreamingStopResult.Fail(
                $"Stream session '{sessionId}' runtime generation changed before compensation."));
        }

        StreamingSessionState stopped = session with { State = "stopped", ActiveListenerPort = null };
        if (!sessions.TryUpdate(sessionId, stopped, session))
        {
            return Task.FromResult(StreamingStopResult.Fail(
                $"Stream session '{sessionId}' runtime changed while stopping."));
        }
        return Task.FromResult(StreamingStopResult.Ok(stopped));
    }

    public Task<StreamingSessionState?> GetSessionAsync(string sessionId, CancellationToken cancellationToken) =>
        Task.FromResult(sessions.GetValueOrDefault(sessionId));

    public IReadOnlyList<StreamingSessionState> GetSessions() =>
        sessions.Values
            .OrderBy(session => session.ClientId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(session => session.SessionId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private Guid CreateRuntimeGeneration()
    {
        long generation = Interlocked.Increment(ref nextRuntimeGeneration);
        Span<byte> value = stackalloc byte[16];
        BinaryPrimitives.WriteInt64BigEndian(value[8..], generation);
        return new Guid(value);
    }
}

using Beacon.Core.Sessions;

namespace Beacon.Core.Streaming;

public interface IStreamingBackend
{
    Task<StreamingBackendHealth> GetHealthAsync(CancellationToken cancellationToken);

    Task<StreamingPreflightResult> CheckReadinessAsync(SessionPlan plan, CancellationToken cancellationToken);

    Task<StreamingStartResult> StartAsync(SessionPlan plan, CancellationToken cancellationToken);

    Task<StreamingStopResult> StopAsync(string sessionId, CancellationToken cancellationToken);

    Task<StreamingStopResult> StopRuntimeAsync(
        string sessionId,
        Guid expectedGeneration,
        CancellationToken cancellationToken) =>
        Task.FromResult(StreamingStopResult.Fail(
            $"Stream session '{sessionId}' generation-aware stop is unavailable."));

    Task<StreamingSessionState?> GetSessionAsync(string sessionId, CancellationToken cancellationToken);

    IReadOnlyList<StreamingSessionState> GetSessions();
}

public sealed record StreamingCapabilities(
    IReadOnlyList<string> Codecs,
    IReadOnlyList<string> Encoders,
    IReadOnlyList<string> CaptureMethods,
    int? MaxFps,
    int? MaxBitrateMbps,
    bool Hdr10);

public sealed record StreamingBackendHealth(
    bool Ready,
    string State,
    string Diagnostic,
    StreamingCapabilities Capabilities,
    int ActiveSessions,
    IReadOnlyList<string> Diagnostics)
{
    public static StreamingBackendHealth Unknown(string diagnostic) =>
        new(
            Ready: false,
            State: "unknown",
            Diagnostic: diagnostic,
            Capabilities: new([], [], [], null, null, false),
            ActiveSessions: 0,
            Diagnostics: []);
}

public sealed record StreamingPreflightResult(bool Success, string? Error)
{
    public static StreamingPreflightResult Ok() => new(true, null);

    public static StreamingPreflightResult Fail(string error) => new(false, error);
}

public sealed record StreamingStartResult(bool Success, StreamingSessionState? Session, string? Error)
{
    public static StreamingStartResult Ok(StreamingSessionState session) => new(true, session, null);

    public static StreamingStartResult Fail(string error) => new(false, null, error);
}

public sealed record StreamingStopResult(bool Success, StreamingSessionState? Session, string? Error)
{
    public static StreamingStopResult Ok(StreamingSessionState session) => new(true, session, null);

    public static StreamingStopResult Fail(string error) => new(false, null, error);
}

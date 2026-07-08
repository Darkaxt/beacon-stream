using Beacon.Core.Sessions;

namespace Beacon.Core.Streaming;

public interface IStreamingBackend
{
    Task<StreamingPreflightResult> CheckReadinessAsync(SessionPlan plan, CancellationToken cancellationToken);

    Task<StreamingStartResult> StartAsync(SessionPlan plan, CancellationToken cancellationToken);

    Task<StreamingStopResult> StopAsync(string sessionId, CancellationToken cancellationToken);

    Task<StreamingSessionState?> GetSessionAsync(string sessionId, CancellationToken cancellationToken);

    IReadOnlyList<StreamingSessionState> GetSessions();
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

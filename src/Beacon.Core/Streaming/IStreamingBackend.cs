using Beacon.Core.Sessions;

namespace Beacon.Core.Streaming;

public interface IStreamingBackend
{
    Task<StreamingBackendHealth> GetHealthAsync(CancellationToken cancellationToken);

    Task<StreamingPreflightResult> CheckReadinessAsync(SessionPlan plan, CancellationToken cancellationToken);

    Task<StreamingStartResult> StartAsync(SessionPlan plan, CancellationToken cancellationToken);

    Task<StreamingStopResult> StopAsync(string sessionId, CancellationToken cancellationToken);

    Task<StreamingSessionState?> GetSessionAsync(string sessionId, CancellationToken cancellationToken);

    IReadOnlyList<StreamingSessionState> GetSessions();
}

public sealed record StreamingBackendHealth(
    bool Ready,
    string Backend,
    string Diagnostic,
    bool ExecutableConfigured,
    bool ExecutableAvailable,
    string? ExecutablePath,
    bool WrapperChildExecutableConfigured,
    bool WrapperChildExecutableAvailable,
    string? WrapperChildExecutablePath,
    bool WrapperChildArgumentsConfigured,
    bool ManifestConfigured,
    bool ManifestAvailable,
    string? ManifestPath,
    string? ManifestName,
    string? Protocol,
    string? LaunchUri,
    IReadOnlyList<StreamingEndpointDescriptor> Endpoints,
    IReadOnlyList<string> Codecs,
    IReadOnlyList<string> Transports,
    IReadOnlyList<string> Encoders,
    IReadOnlyList<string> Capture,
    int? MaxFps,
    int? MaxBitrateMbps,
    bool Hdr10,
    int ActiveSessions,
    IReadOnlyList<string> Diagnostics)
{
    public static StreamingBackendHealth Unknown(string diagnostic) =>
        new(
            Ready: false,
            Backend: "unknown",
            Diagnostic: diagnostic,
            ExecutableConfigured: false,
            ExecutableAvailable: false,
            ExecutablePath: null,
            WrapperChildExecutableConfigured: false,
            WrapperChildExecutableAvailable: false,
            WrapperChildExecutablePath: null,
            WrapperChildArgumentsConfigured: false,
            ManifestConfigured: false,
            ManifestAvailable: false,
            ManifestPath: null,
            ManifestName: null,
            Protocol: null,
            LaunchUri: null,
            Endpoints: [],
            Codecs: [],
            Transports: [],
            Encoders: [],
            Capture: [],
            MaxFps: null,
            MaxBitrateMbps: null,
            Hdr10: false,
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

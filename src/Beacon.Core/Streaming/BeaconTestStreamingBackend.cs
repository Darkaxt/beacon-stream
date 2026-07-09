using Beacon.Core.Sessions;

namespace Beacon.Core.Streaming;

public sealed class BeaconTestStreamingBackend : IStreamingBackend
{
    public const string BackendName = "beacon-test";
    public const string Protocol = "beacon-test";
    public const string ColorBarsEndpoint = "beacon-test://pattern/color-bars";

    private readonly Dictionary<string, StreamingSessionState> sessions = new(StringComparer.OrdinalIgnoreCase);

    public Task<StreamingBackendHealth> GetHealthAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new StreamingBackendHealth(
            Ready: true,
            Backend: BackendName,
            Diagnostic: "Beacon test pattern streaming backend ready.",
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
            Protocol: Protocol,
            LaunchUri: null,
            Endpoints: [new StreamingEndpointDescriptor("video", ColorBarsEndpoint)],
            Codecs: ["beacon-test"],
            Transports: ["in-app-test"],
            Encoders: ["beacon-test"],
            Capture: ["beacon-test"],
            MaxFps: 120,
            MaxBitrateMbps: null,
            Hdr10: false,
            ActiveSessions: sessions.Count,
            Diagnostics: []));

    public Task<StreamingPreflightResult> CheckReadinessAsync(SessionPlan plan, CancellationToken cancellationToken) =>
        Task.FromResult(StreamingPreflightResult.Ok());

    public Task<StreamingStartResult> StartAsync(SessionPlan plan, CancellationToken cancellationToken)
    {
        var connection = new StreamingConnectionDescriptor(
            Protocol,
            LaunchUri: null,
            [new StreamingEndpointDescriptor("video", ColorBarsEndpoint)],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["displayId"] = plan.Display.DisplayId,
                ["transport"] = plan.Stream.Transport,
                ["pattern"] = "color-bars"
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

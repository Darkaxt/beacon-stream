using Beacon.Core.Sessions;
using System.Globalization;

namespace Beacon.Core.Streaming;

public enum BeaconTestStreamKind
{
    ColorBars,
    EncodedVideo
}

public sealed record BeaconTestStreamingOptions(BeaconTestStreamKind StreamKind)
{
    public static BeaconTestStreamingOptions Default { get; } = new(BeaconTestStreamKind.ColorBars);
}

public sealed class BeaconTestStreamingBackend : IStreamingBackend
{
    public const string BackendName = "beacon-test";
    public const string Protocol = "beacon-test";
    public const string ColorBarsEndpoint = "beacon-test://pattern/color-bars";
    public const string EncodedVideoEndpoint = "beacon-test://video/color-bars.h264";

    private const string EncodedVideoCodec = "h264";
    private const string EncodedVideoContainer = "annex-b";

    private readonly Dictionary<string, StreamingSessionState> sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly BeaconTestStreamingOptions options;

    public BeaconTestStreamingBackend()
        : this(BeaconTestStreamingOptions.Default)
    {
    }

    public BeaconTestStreamingBackend(BeaconTestStreamingOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public Task<StreamingBackendHealth> GetHealthAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new StreamingBackendHealth(
            Ready: true,
            Backend: BackendName,
            Diagnostic: options.StreamKind switch
            {
                BeaconTestStreamKind.ColorBars => "Beacon test pattern streaming backend ready.",
                BeaconTestStreamKind.EncodedVideo => "Beacon encoded-video test streaming backend ready.",
                _ => "Beacon test streaming backend ready."
            },
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
            Endpoints: [CreateVideoEndpoint()],
            Codecs: options.StreamKind == BeaconTestStreamKind.EncodedVideo ? [EncodedVideoCodec] : ["beacon-test"],
            Transports: ["in-app-test"],
            Encoders: ["beacon-test"],
            Capture: options.StreamKind == BeaconTestStreamKind.EncodedVideo ? ["beacon-test-encoded-video"] : ["beacon-test"],
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
            [CreateVideoEndpoint()],
            CreateMetadata(plan));

        var session = new StreamingSessionState(
            plan.SessionId,
            plan.ClientId.Value,
            plan.AppId,
            plan.Display.DisplayId,
            options.StreamKind == BeaconTestStreamKind.EncodedVideo ? EncodedVideoCodec : plan.Stream.Codec,
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

    private StreamingEndpointDescriptor CreateVideoEndpoint() =>
        options.StreamKind == BeaconTestStreamKind.EncodedVideo
            ? new StreamingEndpointDescriptor("video", EncodedVideoEndpoint)
            : new StreamingEndpointDescriptor("video", ColorBarsEndpoint);

    private Dictionary<string, string> CreateMetadata(SessionPlan plan)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["displayId"] = plan.Display.DisplayId,
            ["transport"] = plan.Stream.Transport
        };

        if (options.StreamKind == BeaconTestStreamKind.EncodedVideo)
        {
            metadata["streamKind"] = "encoded-video";
            metadata["codec"] = EncodedVideoCodec;
            metadata["container"] = EncodedVideoContainer;
            metadata["width"] = plan.Display.Width.ToString(CultureInfo.InvariantCulture);
            metadata["height"] = plan.Display.Height.ToString(CultureInfo.InvariantCulture);
            metadata["fps"] = plan.Stream.Fps.ToString(CultureInfo.InvariantCulture);
            return metadata;
        }

        metadata["pattern"] = "color-bars";
        return metadata;
    }
}

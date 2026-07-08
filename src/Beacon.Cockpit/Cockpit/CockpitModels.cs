namespace Beacon.Cockpit.Cockpit;

public sealed record CockpitSnapshot(
    IReadOnlyList<CockpitClientSummary> Clients,
    IReadOnlyList<CockpitSessionSummary> Sessions,
    IReadOnlyList<CockpitStreamSummary> Streams,
    IReadOnlyList<CockpitOwnershipSummary> Ownership,
    CockpitDisplayHealth Display,
    CockpitStreamingHealth StreamingHealth,
    CockpitInputHealth InputHealth,
    CockpitGameSummary Games,
    IReadOnlyList<CockpitDiagnosticEvent> Diagnostics);

public sealed record CockpitClientSummary(string ClientId, CockpitClientProfile Profile);

public sealed record CockpitClientProfile(
    string Name,
    CockpitDisplayProfile Display,
    CockpitStreamProfile Stream,
    CockpitAudioProfile Audio,
    CockpitSessionProfile Session);

public sealed record CockpitDisplayProfile(
    int PreferredWidth,
    int PreferredHeight,
    int PreferredRefreshHz,
    string HdrPreference,
    string Mode,
    bool RestorePhysicalDisplayOnEnd,
    bool ForbidMirrorMode);

public sealed record CockpitStreamProfile(string QualityMode, string CodecPreference, int? BitrateCapMbps);

public sealed record CockpitAudioProfile(string Mode);

public sealed record CockpitSessionProfile(bool KeepAppRunningOnDisconnect, bool AllowEmergencyRestoreFromClient);

public sealed class CockpitClientProfilePatch
{
    public int? PreferredWidth { get; set; }

    public int? PreferredHeight { get; set; }

    public int? PreferredRefreshHz { get; set; }

    public string? HdrPreference { get; set; }

    public string? Mode { get; set; }

    public bool? RestorePhysicalDisplayOnEnd { get; set; }

    public bool? ForbidMirrorMode { get; set; }

    public string? CodecPreference { get; set; }

    public string? QualityMode { get; set; }

    public int? BitrateCapMbps { get; set; }

    public string? AudioMode { get; set; }

    public bool? KeepAppRunningOnDisconnect { get; set; }

    public bool? AllowEmergencyRestoreFromClient { get; set; }
}

public sealed record CockpitSessionSummary(string AppId);

public sealed record CockpitStreamSummary(
    string SessionId,
    string ClientId,
    string AppId,
    string DisplayId,
    string Codec,
    int Fps,
    int InitialBitrateMbps,
    string Transport,
    string State,
    string? Error,
    CockpitStreamConnection? Connection);

public sealed record CockpitStreamConnection(
    string Protocol,
    string? LaunchUri,
    IReadOnlyList<CockpitStreamEndpoint> Endpoints,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record CockpitStreamEndpoint(string Role, string Uri);

public sealed record CockpitOwnershipSummary(
    string SessionId,
    string AppId,
    int? LaunchedProcessId,
    bool LaunchedProcessRunning,
    bool ChildProcessRunning,
    bool OwnedWindowRemaining,
    IReadOnlyList<string> Reasons);

public sealed record CockpitDisplayHealth(
    bool DriverReady,
    string Diagnostic,
    bool TopologyAvailable,
    bool MirrorMode,
    bool PhysicalPrimaryVerified,
    IReadOnlyList<CockpitDisplayPath> Paths)
{
    public static CockpitDisplayHealth Unknown { get; } = new(
        DriverReady: false,
        Diagnostic: "Display health unavailable.",
        TopologyAvailable: false,
        MirrorMode: false,
        PhysicalPrimaryVerified: false,
        Paths: []);
}

public sealed record CockpitDisplayPath(
    string DisplayId,
    string Kind,
    int Width,
    int Height,
    int RefreshHz,
    bool IsPrimary,
    int X,
    int Y);

public sealed record CockpitStreamingHealth(
    bool Ready,
    string Backend,
    string Diagnostic,
    bool ExecutableConfigured,
    bool ExecutableAvailable,
    string? ExecutablePath,
    bool ManifestConfigured,
    bool ManifestAvailable,
    string? ManifestPath,
    string? ManifestName,
    string? Protocol,
    string? LaunchUri,
    IReadOnlyList<CockpitStreamEndpoint> Endpoints,
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
    public static CockpitStreamingHealth Unknown { get; } = new(
        Ready: false,
        Backend: "unknown",
        Diagnostic: "Streaming health unavailable.",
        ExecutableConfigured: false,
        ExecutableAvailable: false,
        ExecutablePath: null,
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

public sealed record CockpitInputHealth(
    bool Ready,
    string Backend,
    string Diagnostic,
    IReadOnlyList<string> SupportedEventTypes,
    IReadOnlyList<string> SupportedPointerActions,
    IReadOnlyList<string> SupportedKeyboardActions)
{
    public static CockpitInputHealth Unknown { get; } = new(
        Ready: false,
        Backend: "unknown",
        Diagnostic: "Input health unavailable.",
        SupportedEventTypes: [],
        SupportedPointerActions: [],
        SupportedKeyboardActions: []);
}

public sealed record CockpitGameSummary(int Total, IReadOnlyList<string> Diagnostics);

public sealed record CockpitDiagnosticEvent(
    string Id,
    DateTimeOffset TimestampUtc,
    string Severity,
    string Category,
    string Operation,
    string Message,
    string? ClientId,
    string? SessionId,
    string? DisplayId,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record CockpitRecoveryResult(bool RestoreRequested, bool Recovered, string? DisplayId);

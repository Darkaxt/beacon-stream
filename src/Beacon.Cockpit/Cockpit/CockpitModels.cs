namespace Beacon.Cockpit.Cockpit;

public sealed record CockpitSnapshot(
    IReadOnlyList<CockpitClientSummary> Clients,
    IReadOnlyList<CockpitSessionSummary> Sessions,
    IReadOnlyList<CockpitStreamSummary> Streams,
    IReadOnlyList<CockpitOwnershipSummary> Ownership,
    CockpitGameSummary Games);

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
    string? Error);

public sealed record CockpitOwnershipSummary(
    string SessionId,
    string AppId,
    int? LaunchedProcessId,
    bool LaunchedProcessRunning,
    bool ChildProcessRunning,
    bool OwnedWindowRemaining,
    IReadOnlyList<string> Reasons);

public sealed record CockpitGameSummary(int Total, IReadOnlyList<string> Diagnostics);

public sealed record CockpitRecoveryResult(bool RestoreRequested, bool Recovered, string? DisplayId);

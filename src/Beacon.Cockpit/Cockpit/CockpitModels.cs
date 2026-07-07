namespace Beacon.Cockpit.Cockpit;

public sealed record CockpitSnapshot(
    IReadOnlyList<CockpitClientSummary> Clients,
    IReadOnlyList<CockpitSessionSummary> Sessions,
    IReadOnlyList<CockpitStreamSummary> Streams,
    IReadOnlyList<CockpitOwnershipSummary> Ownership,
    CockpitGameSummary Games);

public sealed record CockpitClientSummary(string ClientId);

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

namespace Beacon.Cockpit.Cockpit;

public sealed record CockpitSnapshot(
    IReadOnlyList<CockpitClientSummary> Clients,
    IReadOnlyList<CockpitSessionSummary> Sessions,
    CockpitGameSummary Games);

public sealed record CockpitClientSummary(string ClientId);

public sealed record CockpitSessionSummary(string AppId);

public sealed record CockpitGameSummary(int Total, IReadOnlyList<string> Diagnostics);

public sealed record CockpitRecoveryResult(bool RestoreRequested, bool Recovered, string? DisplayId);

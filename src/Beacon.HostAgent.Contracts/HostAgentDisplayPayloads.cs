namespace Beacon.HostAgent.Contracts;

public sealed record HostAgentLeaseSnapshotPayload(
    int LeaseCount,
    uint? WatchdogTimeoutSeconds,
    bool HeartbeatActive,
    bool Healthy,
    string Diagnostic);

public sealed record HostAgentStatusPayload(
    bool DriverReady,
    string DriverDiagnostic,
    HostAgentLeaseSnapshotPayload Lease);

public sealed record HoldDisplayLeaseResultPayload(
    bool Acquired,
    HostAgentLeaseSnapshotPayload Lease);

public sealed record CreateVirtualDisplayPayload(
    string DisplayId,
    int Width,
    int Height,
    int RefreshHz);

public sealed record CreateVirtualDisplayResultPayload(string DisplayName);

public sealed record DisplayIdPayload(string DisplayId);

public sealed record DisplayTopologyPayload(
    IReadOnlyList<DisplayPathPayload> Paths,
    bool IsMirrorMode,
    IReadOnlyDictionary<string, string> DisplayNames);

public sealed record DisplayPathPayload(
    string DisplayId,
    HostAgentDisplayKind Kind,
    int Width,
    int Height,
    int RefreshHz,
    bool IsPrimary,
    int X,
    int Y);

public enum HostAgentDisplayKind
{
    Physical,
    Virtual
}

public sealed record DisplayHdrCapabilityPayload(
    bool Supported,
    bool Enabled,
    string Reason);

public sealed record SetHdrStatePayload(string DisplayId, bool Enabled);

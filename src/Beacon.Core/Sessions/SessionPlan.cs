using Beacon.Core.Clients;
using Beacon.Core.Displays;

namespace Beacon.Core.Sessions;

public sealed record PlannedDisplay(
    string DisplayId,
    int Width,
    int Height,
    int RefreshHz,
    string Mode,
    HdrPreference HdrPreference,
    bool HdrEnabled,
    string HdrMode,
    string Reason);

public sealed record PlannedStream(
    string Codec,
    int Fps,
    int InitialBitrateMbps,
    string Transport,
    string CongestionPolicy,
    string Reason,
    Guid BenchmarkRunId,
    string BenchmarkEvidenceRevision)
{
    public string CodecProfile { get; init; } = "";

    public int BitDepth { get; init; }

    public bool TenBitPresentationVerified { get; init; }

    public bool HdrPresentationVerified { get; init; }
}

public sealed record SessionPlan(
    string SessionId,
    ClientId ClientId,
    string AppId,
    PlannedDisplay Display,
    PlannedStream Stream,
    ulong Revision = 1);

public sealed record SessionPlanResult(bool Success, SessionPlan? Plan, string? Error);

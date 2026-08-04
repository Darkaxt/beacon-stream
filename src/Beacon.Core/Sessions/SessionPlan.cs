using Beacon.Core.Clients;
using Beacon.Core.Displays;

namespace Beacon.Core.Sessions;

public static class Hdr10StaticMetadata
{
    public static ReadOnlySpan<byte> Cta8613Descriptor =>
    [
        0x00, 0x48, 0x8A, 0x08, 0x39, 0x34, 0x21, 0xAA, 0x9B, 0x96,
        0x19, 0xFC, 0x08, 0x13, 0x3D, 0x42, 0x40, 0xE8, 0x03, 0x32,
        0x00, 0xE8, 0x03, 0x90, 0x01
    ];

    public static byte[] CreateCta8613Descriptor() => Cta8613Descriptor.ToArray();
}

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
    int Width,
    int Height,
    int Fps,
    int InitialBitrateMbps,
    string Transport,
    string CongestionPolicy,
    string Reason,
    Guid BenchmarkRunId,
    string BenchmarkEvidenceRevision)
{
    public string CodecProfile { get; init; } = "high";

    public int BitDepth { get; init; } = 8;

    public string ColorPrimaries { get; init; } = "bt709";

    public string TransferFunction { get; init; } = "bt709";

    public string MatrixCoefficients { get; init; } = "bt709";

    public string ColorRange { get; init; } = "limited";

    public byte[] HdrStaticInfo { get; init; } = [];

    public bool HdrStaticInfoInBitstream { get; init; }

    public bool TenBitPresentationVerified { get; init; }

    public bool HdrPresentationVerified { get; init; }
}

public sealed record PlannedAudio(
    string Codec,
    int SampleRateHz,
    int ChannelCount,
    int FrameDurationUs,
    int BitrateBps,
    string Reason);

public sealed record SessionPlan(
    string SessionId,
    ClientId ClientId,
    string AppId,
    PlannedDisplay Display,
    PlannedStream Stream,
    PlannedAudio Audio,
    ulong Revision = 1);

public sealed record SessionPlanResult(bool Success, SessionPlan? Plan, string? Error);

using Beacon.Core.Clients;

namespace Beacon.Core.Benchmarks;

public enum BenchmarkTrigger
{
    Automatic,
    Manual,
    SessionPreflight
}

public sealed record NetworkBenchmarkSample(
    long Sequence,
    int PayloadBytes,
    double RttMs,
    double JitterMs,
    bool Received,
    double ThroughputMbps,
    int ReorderDistance);

public sealed record DecoderBenchmarkSample(
    string Codec,
    string Profile,
    int BitDepth,
    int Width,
    int Height,
    int TargetFps,
    bool Configured,
    double SustainedFps,
    double P95DecodeLatencyMs,
    double? P95PresentationLatencyMs,
    int DroppedFrames,
    int OutputErrors);

public sealed record EndpointPowerSample(
    int? BatteryPercent,
    bool IsCharging,
    string ThermalState);

public sealed record BenchmarkScoringInput(
    IReadOnlyList<NetworkBenchmarkSample> NetworkSamples,
    IReadOnlyList<DecoderBenchmarkSample> DecoderSamples,
    IReadOnlyList<EndpointPowerSample> PowerSamples);

public sealed record SelectedBenchmarkResult(
    string Codec,
    int MaxSustainableFps,
    int InitialBitrateMbps,
    double SustainableThroughputMbps,
    double RttMs,
    double JitterMs,
    double PacketLossPercent,
    IReadOnlyList<string> Reasons);

public sealed record BenchmarkEvidence(
    Guid RunId,
    ClientId ClientId,
    BenchmarkTrigger Trigger,
    BenchmarkFingerprintSet Fingerprints,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<NetworkBenchmarkSample> NetworkSamples,
    IReadOnlyList<DecoderBenchmarkSample> DecoderSamples,
    IReadOnlyList<EndpointPowerSample> PowerSamples,
    SelectedBenchmarkResult? SelectedResult);

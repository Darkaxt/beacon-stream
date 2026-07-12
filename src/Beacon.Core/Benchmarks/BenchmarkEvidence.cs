using System.Globalization;
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

public sealed record NetworkBenchmarkCoverage(
    long FirstSequence,
    int ExpectedPacketCount);

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
    int OutputErrors,
    bool TenBitPresentationVerified = false,
    bool HdrPresentationVerified = false);

public sealed record EndpointPowerSample(
    int? BatteryPercent,
    bool IsCharging,
    string ThermalState);

public sealed record BenchmarkScoringInput(
    IReadOnlyList<NetworkBenchmarkSample> NetworkSamples,
    IReadOnlyList<DecoderBenchmarkSample> DecoderSamples,
    IReadOnlyList<EndpointPowerSample> PowerSamples,
    string CodecPreference = "auto",
    NetworkBenchmarkCoverage? NetworkCoverage = null);

public sealed record SelectedBenchmarkResult(
    string Codec,
    int MaxSustainableFps,
    int InitialBitrateMbps,
    double SustainableThroughputMbps,
    double RttMs,
    double JitterMs,
    double PacketLossPercent,
    bool PowerConstrained,
    IReadOnlyList<string> Reasons,
    string Profile = "",
    int BitDepth = 0,
    int Width = 0,
    int Height = 0,
    bool TenBitPresentationVerified = false,
    bool HdrPresentationVerified = false,
    double P95DecodeLatencyMs = 0,
    double? P95PresentationLatencyMs = null);

public sealed record BenchmarkPlanEvidence(
    Guid RunId,
    string Revision,
    SelectedBenchmarkResult SelectedResult);

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
    SelectedBenchmarkResult? SelectedResult,
    NetworkBenchmarkCoverage? NetworkCoverage = null)
{
    public string Revision => FingerprintRevision.Create(
        RunId.ToString("D"),
        ClientId.Value,
        ((int)Trigger).ToString(CultureInfo.InvariantCulture),
        Fingerprints.Network.Revision,
        Fingerprints.Hardware.Revision,
        CompletedAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        SelectedResult?.Codec,
        SelectedResult?.MaxSustainableFps.ToString(CultureInfo.InvariantCulture),
        SelectedResult?.InitialBitrateMbps.ToString(CultureInfo.InvariantCulture),
        SelectedResult?.SustainableThroughputMbps.ToString("R", CultureInfo.InvariantCulture),
        SelectedResult?.RttMs.ToString("R", CultureInfo.InvariantCulture),
        SelectedResult?.JitterMs.ToString("R", CultureInfo.InvariantCulture),
        SelectedResult?.PacketLossPercent.ToString("R", CultureInfo.InvariantCulture),
        SelectedResult?.PowerConstrained.ToString(CultureInfo.InvariantCulture),
        SelectedResult?.Profile,
        SelectedResult?.BitDepth.ToString(CultureInfo.InvariantCulture),
        SelectedResult?.Width.ToString(CultureInfo.InvariantCulture),
        SelectedResult?.Height.ToString(CultureInfo.InvariantCulture),
        SelectedResult?.TenBitPresentationVerified.ToString(CultureInfo.InvariantCulture),
        SelectedResult?.HdrPresentationVerified.ToString(CultureInfo.InvariantCulture),
        SelectedResult?.P95DecodeLatencyMs.ToString("R", CultureInfo.InvariantCulture),
        SelectedResult?.P95PresentationLatencyMs?.ToString("R", CultureInfo.InvariantCulture),
        NetworkCoverage?.FirstSequence.ToString(CultureInfo.InvariantCulture),
        NetworkCoverage?.ExpectedPacketCount.ToString(CultureInfo.InvariantCulture));

    public BenchmarkPlanEvidence ToPlanEvidence(string codecPreference)
    {
        if (CompletedAt is null || SelectedResult is null)
        {
            throw new InvalidOperationException("Only completed benchmark evidence can be used for session planning.");
        }

        BenchmarkEvidenceValidator.Validate(this);
        SelectedBenchmarkResult selected = BenchmarkScorer.Select(new BenchmarkScoringInput(
            NetworkSamples,
            DecoderSamples,
            PowerSamples,
            codecPreference,
            NetworkCoverage));
        BenchmarkEvidence rescored = this with { SelectedResult = selected };
        BenchmarkEvidenceValidator.Validate(rescored);
        return new BenchmarkPlanEvidence(RunId, rescored.Revision, selected);
    }
}

using Beacon.Core.Benchmarks;

namespace Beacon.Core.Tests.Benchmarks;

public sealed class BenchmarkScorerTests
{
    [Fact]
    public void SelectsSustainableCandidateFromRawSamplesAndExplainsDecision()
    {
        BenchmarkScoringInput input = new(
            NetworkSamples:
            [
                new(1, PayloadBytes: 1200, RttMs: 8, JitterMs: 1.0, Received: true, ThroughputMbps: 120, ReorderDistance: 0),
                new(2, PayloadBytes: 1200, RttMs: 9, JitterMs: 1.4, Received: true, ThroughputMbps: 110, ReorderDistance: 0),
                new(3, PayloadBytes: 1200, RttMs: 8, JitterMs: 1.1, Received: true, ThroughputMbps: 100, ReorderDistance: 0),
            ],
            DecoderSamples:
            [
                new("av1", "main", 10, 2560, 1600, 120, Configured: true, SustainedFps: 93, P95DecodeLatencyMs: 12, P95PresentationLatencyMs: 18, DroppedFrames: 40, OutputErrors: 0),
                new("h264", "high", 8, 2560, 1600, 120, Configured: true, SustainedFps: 120, P95DecodeLatencyMs: 5, P95PresentationLatencyMs: 9, DroppedFrames: 0, OutputErrors: 0),
            ],
            PowerSamples:
            [
                new(BatteryPercent: 80, IsCharging: false, ThermalState: "nominal"),
                new(BatteryPercent: 77, IsCharging: false, ThermalState: "nominal"),
            ],
            NetworkCoverage: new(FirstSequence: 1, ExpectedPacketCount: 3));

        SelectedBenchmarkResult selected = BenchmarkScorer.Select(input);

        Assert.Equal("h264", selected.Codec);
        Assert.Equal(120, selected.MaxSustainableFps);
        Assert.Equal(70, selected.InitialBitrateMbps);
        Assert.Equal(100, selected.SustainableThroughputMbps);
        Assert.Equal(0, selected.PacketLossPercent);
        Assert.Equal("high", selected.Profile);
        Assert.Equal(8, selected.BitDepth);
        Assert.Equal(2560, selected.Width);
        Assert.Equal(1600, selected.Height);
        Assert.False(selected.TenBitPresentationVerified);
        Assert.False(selected.HdrPresentationVerified);
        Assert.Contains(selected.Reasons, reason => reason.Contains("AV1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(selected.Reasons, reason => reason.Contains("H.264", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LossAndThermalPressureReduceSelectedLimits()
    {
        BenchmarkScoringInput input = new(
            NetworkSamples:
            [
                new(1, PayloadBytes: 1200, RttMs: 30, JitterMs: 4, Received: true, ThroughputMbps: 80, ReorderDistance: 0),
                new(2, PayloadBytes: 1200, RttMs: 35, JitterMs: 6, Received: false, ThroughputMbps: 0, ReorderDistance: 0),
                new(3, PayloadBytes: 1200, RttMs: 40, JitterMs: 8, Received: true, ThroughputMbps: 70, ReorderDistance: 1),
                new(4, PayloadBytes: 1200, RttMs: 35, JitterMs: 6, Received: true, ThroughputMbps: 75, ReorderDistance: 0),
            ],
            DecoderSamples:
            [
                new("h264", "high", 8, 2560, 1600, 60, Configured: true, SustainedFps: 60, P95DecodeLatencyMs: 8, P95PresentationLatencyMs: 12, DroppedFrames: 0, OutputErrors: 0),
            ],
            PowerSamples:
            [
                new(BatteryPercent: 20, IsCharging: false, ThermalState: "nominal"),
                new(BatteryPercent: 16, IsCharging: false, ThermalState: "hot"),
            ],
            NetworkCoverage: new(FirstSequence: 1, ExpectedPacketCount: 4));

        SelectedBenchmarkResult selected = BenchmarkScorer.Select(input);

        Assert.Equal(60, selected.MaxSustainableFps);
        Assert.True(selected.InitialBitrateMbps < 49);
        Assert.Equal(25, selected.PacketLossPercent);
        Assert.True(selected.PowerConstrained);
        Assert.Contains(selected.Reasons, reason => reason.Contains("thermal", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(selected.Reasons, reason => reason.Contains("loss", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ThrowsWhenNoDecoderCandidateIsSustainable()
    {
        BenchmarkScoringInput input = new(
            NetworkSamples:
            [
                new(1, PayloadBytes: 1200, RttMs: 8, JitterMs: 1, Received: true, ThroughputMbps: 100, ReorderDistance: 0),
            ],
            DecoderSamples:
            [
                new("h264", "high", 8, 2560, 1600, 120, Configured: false, SustainedFps: 0, P95DecodeLatencyMs: 0, P95PresentationLatencyMs: null, DroppedFrames: 0, OutputErrors: 1),
            ],
            PowerSamples: [],
            NetworkCoverage: new(FirstSequence: 1, ExpectedPacketCount: 1));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => BenchmarkScorer.Select(input));

        Assert.Contains("sustainable decoder", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HighRttIsPreservedInServerSelectionReasons()
    {
        BenchmarkScoringInput input = new(
            NetworkSamples:
            [
                new(1, PayloadBytes: 1200, RttMs: 95, JitterMs: 4, Received: true, ThroughputMbps: 40, ReorderDistance: 0)
            ],
            DecoderSamples:
            [
                new("hevc", "main", 8, 2560, 1600, 60, Configured: true, SustainedFps: 60, P95DecodeLatencyMs: 8, P95PresentationLatencyMs: 12, DroppedFrames: 0, OutputErrors: 0)
            ],
            PowerSamples: [],
            NetworkCoverage: new(FirstSequence: 1, ExpectedPacketCount: 1));

        SelectedBenchmarkResult selected = BenchmarkScorer.Select(input);

        Assert.Contains(selected.Reasons, reason => reason.Contains("RTT", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(selected.Reasons, reason => reason.Contains("95", StringComparison.Ordinal));
    }

    [Fact]
    public void ExplicitServerCodecPreferenceRestrictsCandidateSelection()
    {
        BenchmarkScoringInput input = new(
            NetworkSamples:
            [
                new(1, PayloadBytes: 1200, RttMs: 8, JitterMs: 1, Received: true, ThroughputMbps: 100, ReorderDistance: 0)
            ],
            DecoderSamples:
            [
                new("av1", "main", 10, 2560, 1600, 120, Configured: true, SustainedFps: 120, P95DecodeLatencyMs: 5, P95PresentationLatencyMs: 9, DroppedFrames: 0, OutputErrors: 0),
                new("hevc", "main10", 10, 2560, 1600, 120, Configured: true, SustainedFps: 120, P95DecodeLatencyMs: 5, P95PresentationLatencyMs: 9, DroppedFrames: 0, OutputErrors: 0)
            ],
            PowerSamples: [],
            CodecPreference: "hevc",
            NetworkCoverage: new(FirstSequence: 1, ExpectedPacketCount: 1));

        SelectedBenchmarkResult selected = BenchmarkScorer.Select(input);

        Assert.Equal("hevc", selected.Codec);
        Assert.Contains(selected.Reasons, reason => reason.Contains("profile", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PreservesExactCertifiedModeAndHdrPresentationEvidence()
    {
        BenchmarkScoringInput input = CreateInput(
            new DecoderBenchmarkSample(
                "hevc",
                "Main10",
                10,
                3840,
                2160,
                60,
                Configured: true,
                SustainedFps: 60,
                P95DecodeLatencyMs: 8,
                P95PresentationLatencyMs: 14,
                DroppedFrames: 0,
                OutputErrors: 0,
                TenBitPresentationVerified: true,
                HdrPresentationVerified: true));

        SelectedBenchmarkResult selected = BenchmarkScorer.Select(input);

        Assert.Equal("hevc", selected.Codec);
        Assert.Equal("Main10", selected.Profile);
        Assert.Equal(10, selected.BitDepth);
        Assert.Equal(3840, selected.Width);
        Assert.Equal(2160, selected.Height);
        Assert.Equal(60, selected.MaxSustainableFps);
        Assert.Equal(8, selected.P95DecodeLatencyMs);
        Assert.Equal(14, selected.P95PresentationLatencyMs);
        Assert.True(selected.TenBitPresentationVerified);
        Assert.True(selected.HdrPresentationVerified);
    }

    [Theory]
    [MemberData(nameof(UnsafeDecoderSamples))]
    public void RejectsDecoderCandidatesWithoutExactSafePresentationEvidence(DecoderBenchmarkSample decoder)
    {
        BenchmarkScoringInput input = CreateInput(decoder);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => BenchmarkScorer.Select(input));

        Assert.Contains("sustainable decoder", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PacketLossUsesServerExpectedSequenceCoverageIncludingGapsAndTrailingLoss()
    {
        BenchmarkScoringInput input = new(
            NetworkSamples:
            [
                new(10, 1200, 8, 1, Received: true, ThroughputMbps: 100, ReorderDistance: 0),
                new(11, 1200, 8, 1, Received: false, ThroughputMbps: 0, ReorderDistance: 0),
                new(12, 1200, 8, 1, Received: true, ThroughputMbps: 100, ReorderDistance: 0),
            ],
            DecoderSamples: [CreateSafeDecoder()],
            PowerSamples: [],
            NetworkCoverage: new(FirstSequence: 10, ExpectedPacketCount: 5));

        SelectedBenchmarkResult selected = BenchmarkScorer.Select(input);

        Assert.Equal(60, selected.PacketLossPercent);
    }

    [Fact]
    public void LowThroughputFailsInsteadOfFabricatingMinimumBitrate()
    {
        BenchmarkScoringInput input = new(
            NetworkSamples:
            [
                new(1, 1200, 8, 1, Received: true, ThroughputMbps: 6, ReorderDistance: 0),
            ],
            DecoderSamples: [CreateSafeDecoder()],
            PowerSamples: [],
            NetworkCoverage: new(FirstSequence: 1, ExpectedPacketCount: 1));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => BenchmarkScorer.Select(input));

        Assert.Contains("throughput", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    public static TheoryData<DecoderBenchmarkSample> UnsafeDecoderSamples =>
        new()
        {
            CreateSafeDecoder() with { SustainedFps = 59.9 },
            CreateSafeDecoder() with { DroppedFrames = 1 },
            CreateSafeDecoder() with { OutputErrors = 1 },
            CreateSafeDecoder() with { P95DecodeLatencyMs = 17 },
            CreateSafeDecoder() with { P95PresentationLatencyMs = 34 },
        };

    private static BenchmarkScoringInput CreateInput(DecoderBenchmarkSample decoder) =>
        new(
            NetworkSamples:
            [
                new(1, 1200, 8, 1, Received: true, ThroughputMbps: 100, ReorderDistance: 0),
            ],
            DecoderSamples: [decoder],
            PowerSamples: [],
            NetworkCoverage: new(FirstSequence: 1, ExpectedPacketCount: 1));

    private static DecoderBenchmarkSample CreateSafeDecoder() =>
        new(
            "h264",
            "high",
            8,
            2560,
            1600,
            60,
            Configured: true,
            SustainedFps: 60,
            P95DecodeLatencyMs: 8,
            P95PresentationLatencyMs: 12,
            DroppedFrames: 0,
            OutputErrors: 0);
}

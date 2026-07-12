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
            ]);

        SelectedBenchmarkResult selected = BenchmarkScorer.Select(input);

        Assert.Equal("h264", selected.Codec);
        Assert.Equal(120, selected.MaxSustainableFps);
        Assert.Equal(70, selected.InitialBitrateMbps);
        Assert.Equal(100, selected.SustainableThroughputMbps);
        Assert.Equal(0, selected.PacketLossPercent);
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
                new("h264", "high", 8, 2560, 1600, 120, Configured: true, SustainedFps: 120, P95DecodeLatencyMs: 8, P95PresentationLatencyMs: 12, DroppedFrames: 0, OutputErrors: 0),
            ],
            PowerSamples:
            [
                new(BatteryPercent: 20, IsCharging: false, ThermalState: "nominal"),
                new(BatteryPercent: 16, IsCharging: false, ThermalState: "hot"),
            ]);

        SelectedBenchmarkResult selected = BenchmarkScorer.Select(input);

        Assert.Equal(60, selected.MaxSustainableFps);
        Assert.True(selected.InitialBitrateMbps < 49);
        Assert.Equal(25, selected.PacketLossPercent);
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
            PowerSamples: []);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => BenchmarkScorer.Select(input));

        Assert.Contains("sustainable decoder", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}

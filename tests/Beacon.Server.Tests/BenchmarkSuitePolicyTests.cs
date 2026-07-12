using Beacon.Core.Benchmarks;
using Beacon.Core.Clients;
using Beacon.Server.State;

namespace Beacon.Server.Tests;

public sealed class BenchmarkSuitePolicyTests
{
    [Fact]
    public void FullAndPreflightSuitesUseExplicitServerOwnedTrafficCounts()
    {
        BenchmarkTransportPlan full = BenchmarkSuitePolicy.Create(BenchmarkTrigger.Manual);
        BenchmarkTransportPlan automatic = BenchmarkSuitePolicy.Create(BenchmarkTrigger.Automatic);
        BenchmarkTransportPlan preflight = BenchmarkSuitePolicy.Create(BenchmarkTrigger.SessionPreflight);

        Assert.Equal(full, automatic);
        Assert.True(full.ReliablePacketCount > preflight.ReliablePacketCount);
        Assert.True(full.DatagramPacketCount > preflight.DatagramPacketCount);
        Assert.InRange(full.DatagramPayloadBytes, 1, 1_155);
        Assert.True(full.MeasurementIntervalUs > 0);
    }

    [Theory]
    [InlineData(BenchmarkTrigger.Automatic)]
    [InlineData(BenchmarkTrigger.Manual)]
    [InlineData(BenchmarkTrigger.SessionPreflight)]
    public void NetworkCoverageIsDerivedFromTheIssuedDatagramPlan(BenchmarkTrigger trigger)
    {
        BenchmarkTransportPlan plan = BenchmarkSuitePolicy.Create(trigger);

        Assert.Equal(
            new NetworkBenchmarkCoverage(0, plan.DatagramPacketCount),
            BenchmarkSuitePolicy.Coverage(plan));
    }

    [Fact]
    public void FullSuiteIssuesVersionedDecoderVectorsFromEndpointCapabilities()
    {
        var capabilities = new EndpointCapabilities(
            Av1: false,
            Hevc: false,
            H264: true,
            Hdr10: false,
            VirtualDisplayHdrSupported: false,
            MaxFps: 60,
            LowLatencyDecode: true,
            CurrentScreenMode: "1280x720@60");

        BenchmarkHardwarePlan plan = BenchmarkSuitePolicy.CreateHardware(
            BenchmarkTrigger.Manual,
            capabilities);

        Assert.Equal(1, plan.SchemaVersion);
        DecoderBenchmarkRoundPlan round = Assert.Single(plan.DecoderRounds);
        Assert.Equal("beacon-h264-high-8-1280x720-60-v1", round.VectorId);
        Assert.Equal("h264", round.Codec);
        Assert.Equal("high", round.Profile);
        Assert.Equal(8, round.BitDepth);
        Assert.Equal(1280, round.Width);
        Assert.Equal(720, round.Height);
        Assert.Equal(60, round.TargetFps);
        Assert.True(round.RepetitionCount > 0);
        Assert.True(plan.SamplePowerBeforeAndAfterEachRound);
    }

    [Fact]
    public void DecoderPlanOmitsUnadvertisedCodecsInsteadOfInventingSupport()
    {
        var capabilities = new EndpointCapabilities(
            Av1: false,
            Hevc: false,
            H264: false,
            Hdr10: false,
            VirtualDisplayHdrSupported: false);

        BenchmarkHardwarePlan plan = BenchmarkSuitePolicy.CreateHardware(
            BenchmarkTrigger.Automatic,
            capabilities);

        Assert.Empty(plan.DecoderRounds);
    }
}

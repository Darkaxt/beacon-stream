using Beacon.Core.Benchmarks;
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
}

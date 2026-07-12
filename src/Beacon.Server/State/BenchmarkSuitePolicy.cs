using Beacon.Core.Benchmarks;

namespace Beacon.Server.State;

public static class BenchmarkSuitePolicy
{
    public static BenchmarkTransportPlan Create(BenchmarkTrigger trigger) =>
        trigger == BenchmarkTrigger.SessionPreflight
            ? new BenchmarkTransportPlan(16, 32 * 1024, 64, 1000, 250_000)
            : new BenchmarkTransportPlan(64, 64 * 1024, 256, 1000, 1_000_000);

    public static NetworkBenchmarkCoverage Coverage(BenchmarkTransportPlan plan) =>
        new(FirstSequence: 0, ExpectedPacketCount: plan.DatagramPacketCount);
}

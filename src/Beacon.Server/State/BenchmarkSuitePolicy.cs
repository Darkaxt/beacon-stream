using Beacon.Core.Benchmarks;

namespace Beacon.Server.State;

internal static class BenchmarkSuitePolicy
{
    public static NetworkBenchmarkCoverage NetworkCoverage { get; } = new(
        FirstSequence: 1,
        ExpectedPacketCount: 1);
}

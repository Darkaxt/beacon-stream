using Beacon.Core.Benchmarks;
using Beacon.Core.Clients;

namespace Beacon.Server.State;

internal static class FakeBenchmarkEvidence
{
    public static BenchmarkEvidence CreateZFold7(DateTimeOffset completedAt)
    {
        NetworkBenchmarkSample[] networkSamples =
        [
            new(1, 1200, 8, 1, Received: true, ThroughputMbps: 100, ReorderDistance: 0)
        ];
        DecoderBenchmarkSample[] decoderSamples =
        [
            new("av1", "main", 10, 2560, 1600, 120, true, 120, 5, 9, 0, 0),
            new("hevc", "main10", 10, 2560, 1600, 120, true, 120, 5, 9, 0, 0),
            new("h264", "high", 8, 2560, 1600, 120, true, 120, 5, 9, 0, 0)
        ];
        EndpointPowerSample[] powerSamples = [new(80, false, "nominal")];
        SelectedBenchmarkResult selected = BenchmarkScorer.Select(new(
            networkSamples,
            decoderSamples,
            powerSamples));

        return new BenchmarkEvidence(
            RunId: Guid.Parse("80b224b6-d499-4e59-912d-c5575459c356"),
            ClientId: new ClientId("z-fold-7"),
            Trigger: BenchmarkTrigger.Automatic,
            Fingerprints: new BenchmarkFingerprintSet(
                new NetworkFingerprint(3, "fake-host", "in-memory", "127.0.0.0/8", null, null, "fake", null),
                new HardwareFingerprint(3, "z-fold-7-fake", "emulator", "test", "2560x1600-120", "av1-hevc-h264")),
            StartedAt: completedAt.AddMinutes(-1),
            CompletedAt: completedAt,
            NetworkSamples: networkSamples,
            DecoderSamples: decoderSamples,
            PowerSamples: powerSamples,
            SelectedResult: selected);
    }
}

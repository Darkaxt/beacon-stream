using Beacon.Core.Benchmarks;
using Beacon.Core.Clients;
using Beacon.Server.State;

namespace Beacon.Server.Tests;

public sealed class BenchmarkEvidenceRepositoryTests
{
    [Fact]
    public void FileRepositoryRoundTripsRawSamplesAndSelectedResult()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"beacon-benchmark-repository-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "benchmark-evidence.json");

        try
        {
            BenchmarkEvidence evidence = CreateEvidence(
                Guid.Parse("e84a03c5-e089-4a60-adba-f61a114c74c1"),
                new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero));
            var repository = new FileBenchmarkEvidenceRepository(path);

            repository.SaveEvidence([evidence]);
            BenchmarkEvidence loaded = Assert.Single(repository.LoadEvidence());

            Assert.Equal(evidence.RunId, loaded.RunId);
            Assert.Equal(evidence.Fingerprints, loaded.Fingerprints);
            Assert.Equal(evidence.NetworkSamples, loaded.NetworkSamples);
            Assert.Equal(evidence.DecoderSamples, loaded.DecoderSamples);
            Assert.Equal(evidence.PowerSamples, loaded.PowerSamples);
            SelectedBenchmarkResult expected = Assert.IsType<SelectedBenchmarkResult>(evidence.SelectedResult);
            SelectedBenchmarkResult actual = Assert.IsType<SelectedBenchmarkResult>(loaded.SelectedResult);
            Assert.Equal(expected.Codec, actual.Codec);
            Assert.Equal(expected.MaxSustainableFps, actual.MaxSustainableFps);
            Assert.Equal(expected.InitialBitrateMbps, actual.InitialBitrateMbps);
            Assert.Equal(expected.SustainableThroughputMbps, actual.SustainableThroughputMbps);
            Assert.Equal(expected.RttMs, actual.RttMs);
            Assert.Equal(expected.JitterMs, actual.JitterMs);
            Assert.Equal(expected.PacketLossPercent, actual.PacketLossPercent);
            Assert.Equal(expected.PowerConstrained, actual.PowerConstrained);
            Assert.Equal(expected.Reasons, actual.Reasons);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void ClientStoreSelectsNewestCompletedEvidenceAndPreservesHistory()
    {
        var repository = new InMemoryBenchmarkEvidenceRepository();
        var store = new InMemoryClientStore(new InMemoryClientProfileRepository(), repository);
        BenchmarkEvidence older = CreateEvidence(
            Guid.Parse("e84a03c5-e089-4a60-adba-f61a114c74c1"),
            new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero));
        BenchmarkEvidence newer = CreateEvidence(
            Guid.Parse("f4e51e63-58f3-45b2-ab71-bafbd30dc957"),
            new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero));
        BenchmarkEvidence incomplete = newer with
        {
            RunId = Guid.Parse("4da32eb9-e1de-4196-9dce-f07ca86d21b4"),
            CompletedAt = null,
            SelectedResult = null
        };

        store.SaveBenchmarkEvidence(newer);
        store.SaveBenchmarkEvidence(older);
        store.SaveBenchmarkEvidence(incomplete);

        BenchmarkPlanEvidence selected = Assert.IsType<BenchmarkPlanEvidence>(
            store.GetLatestBenchmarkPlanEvidence(
                "z-fold-7",
                new DateTimeOffset(2026, 7, 12, 13, 0, 0, TimeSpan.Zero),
                TimeSpan.FromDays(7)));
        Assert.Equal(newer.RunId, selected.RunId);
        Assert.Equal(newer.Revision, selected.Revision);
        Assert.Equal(3, repository.LoadEvidence().Count);
    }

    internal static BenchmarkEvidence CreateEvidence(Guid runId, DateTimeOffset completedAt)
    {
        var fingerprints = new BenchmarkFingerprintSet(
            new NetworkFingerprint(3, "192.168.1.10", "wifi", "192.168.1.0/24", "6-ghz", 37, "500-999-mbps", "salted-network-a"),
            new HardwareFingerprint(3, "caps-a", "16", "1.0.0", "display-a", "codec-a"));
        NetworkBenchmarkSample[] networkSamples =
        [
            new(1, 1200, 8, 1, Received: true, ThroughputMbps: 100, ReorderDistance: 0)
        ];
        DecoderBenchmarkSample[] decoderSamples =
        [
            new("h264", "high", 8, 2560, 1600, 120, true, 120, 5, 9, 0, 0)
        ];
        EndpointPowerSample[] powerSamples = [new(80, false, "nominal")];
        SelectedBenchmarkResult selected = BenchmarkScorer.Select(new(networkSamples, decoderSamples, powerSamples));

        return new BenchmarkEvidence(
            runId,
            new ClientId("z-fold-7"),
            BenchmarkTrigger.Automatic,
            fingerprints,
            completedAt.AddMinutes(-5),
            completedAt,
            networkSamples,
            decoderSamples,
            powerSamples,
            selected);
    }
}

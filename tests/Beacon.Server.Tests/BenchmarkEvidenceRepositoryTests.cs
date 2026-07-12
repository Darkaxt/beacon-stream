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
            using var repository = new FileBenchmarkEvidenceRepository(path);

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
    public void FileRepositoryRejectsUnsupportedDocumentVersion()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"beacon-benchmark-version-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "benchmark-evidence.json");

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, "{\"version\":99,\"evidence\":[]}");
            using var repository = new FileBenchmarkEvidenceRepository(path);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => repository.LoadEvidence());

            Assert.Contains("version", error.Message, StringComparison.OrdinalIgnoreCase);
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
    public void FileRepositoryRejectsSecondLiveWriterUntilTheFirstIsDisposed()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"beacon-benchmark-lease-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "benchmark-evidence.json");

        try
        {
            using (var repository = new FileBenchmarkEvidenceRepository(path))
            {
                InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                    () => new FileBenchmarkEvidenceRepository(path));

                Assert.Contains("exclusive writer lease", error.Message, StringComparison.OrdinalIgnoreCase);
                Assert.Contains(path, error.Message, StringComparison.Ordinal);
            }

            using var replacement = new FileBenchmarkEvidenceRepository(path);
            Assert.Equal(path, replacement.Location);
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
            SelectedResult = null,
            NetworkSamples = [],
            DecoderSamples = [],
            PowerSamples = [],
            NetworkCoverage = null
        };
        var repository = new InMemoryBenchmarkEvidenceRepository([newer, older, incomplete]);
        var store = new InMemoryClientStore(new InMemoryClientProfileRepository(), repository);

        BenchmarkPlanEvidence selected = Assert.IsType<BenchmarkPlanEvidence>(
            store.GetLatestBenchmarkPlanEvidence(
                "z-fold-7",
                new DateTimeOffset(2026, 7, 12, 13, 0, 0, TimeSpan.Zero),
                TimeSpan.FromDays(7),
                "auto"));
        Assert.Equal(newer.RunId, selected.RunId);
        Assert.Equal(newer.Revision, selected.Revision);
        Assert.Equal(3, repository.LoadEvidence().Count);
    }

    [Fact]
    public void FailedPersistenceDoesNotPublishEvidenceInMemory()
    {
        var repository = new FailingBenchmarkEvidenceRepository();
        var store = new InMemoryClientStore(new InMemoryClientProfileRepository(), repository);
        BenchmarkEvidence evidence = CreateEvidence(
            Guid.Parse("da1f9e17-38c0-4568-be44-18ca59a9beb3"),
            new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero));

        Assert.Throws<IOException>(() => store.PrepareBenchmarkRun(
            evidence.ClientId,
            BenchmarkTrigger.Manual,
            evidence.Fingerprints,
            evidence.StartedAt,
            TimeSpan.FromDays(7)));

        Assert.Empty(store.GetBenchmarkEvidence(evidence.ClientId.Value));
    }

    [Fact]
    public void FailedPreparationDoesNotReplaceTheCurrentPlanningFingerprint()
    {
        DateTimeOffset now = new(2026, 7, 12, 13, 0, 0, TimeSpan.Zero);
        BenchmarkEvidence existing = CreateEvidence(
            Guid.Parse("3f32b673-d1eb-4dc0-8d15-249490a28a7f"),
            now.AddHours(-1));
        var repository = new SwitchableBenchmarkEvidenceRepository([existing]);
        var store = new InMemoryClientStore(new InMemoryClientProfileRepository(), repository);
        BenchmarkPreparationResult reused = store.PrepareBenchmarkRun(
            existing.ClientId,
            BenchmarkTrigger.Automatic,
            existing.Fingerprints,
            now,
            TimeSpan.FromDays(7));
        Assert.Equal(BenchmarkPreparationDisposition.Reuse, reused.Disposition);
        repository.FailWrites = true;
        BenchmarkFingerprintSet changed = existing.Fingerprints with
        {
            Network = existing.Fingerprints.Network with { WifiChannel = 44 }
        };

        Assert.Throws<IOException>(() => store.PrepareBenchmarkRun(
            existing.ClientId,
            BenchmarkTrigger.Automatic,
            changed,
            now,
            TimeSpan.FromDays(7)));

        BenchmarkPlanEvidence planEvidence = Assert.IsType<BenchmarkPlanEvidence>(
            store.GetLatestBenchmarkPlanEvidence(
                existing.ClientId.Value,
                now,
                TimeSpan.FromDays(7),
                "auto"));
        Assert.Equal(existing.RunId, planEvidence.RunId);
    }

    [Fact]
    public async Task ConcurrentCompletionCommitsExactlyOneResult()
    {
        BenchmarkEvidence completed = CreateEvidence(
            Guid.Parse("2148288c-a071-42e7-9e60-5f2926ec70a0"),
            new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero));
        BenchmarkEvidence pending = completed with
        {
            CompletedAt = null,
            SelectedResult = null,
            NetworkSamples = [],
            DecoderSamples = [],
            PowerSamples = [],
            NetworkCoverage = null
        };
        var repository = new InMemoryBenchmarkEvidenceRepository([pending]);
        var store = new InMemoryClientStore(new InMemoryClientProfileRepository(), repository);

        Task<bool>[] attempts = Enumerable.Range(0, 2)
            .Select(index => Task.Run(() => store.TryCompleteBenchmarkEvidence(
                pending.RunId,
                pending.ClientId.Value,
                completed,
                out _)))
            .ToArray();
        bool[] results = await Task.WhenAll(attempts);

        Assert.Single(results, result => result);
        BenchmarkEvidence stored = Assert.Single(repository.LoadEvidence());
        Assert.NotNull(stored.CompletedAt);
        Assert.NotNull(stored.SelectedResult);
    }

    [Fact]
    public void PreparationReusesMatchingHistoryAndPendingRunsByFingerprint()
    {
        DateTimeOffset now = new(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);
        BenchmarkEvidence networkA = CreateEvidence(
            Guid.Parse("e84a03c5-e089-4a60-adba-f61a114c74c1"),
            now.AddHours(-2));
        BenchmarkEvidence networkB = CreateEvidence(
            Guid.Parse("f4e51e63-58f3-45b2-ab71-bafbd30dc957"),
            now.AddHours(-1)) with
        {
            Fingerprints = networkA.Fingerprints with
            {
                Network = networkA.Fingerprints.Network with { WifiChannel = 44 }
            }
        };
        var repository = new InMemoryBenchmarkEvidenceRepository([networkA, networkB]);
        var store = new InMemoryClientStore(new InMemoryClientProfileRepository(), repository);

        BenchmarkPreparationResult returnedToA = store.PrepareBenchmarkRun(
            networkA.ClientId,
            BenchmarkTrigger.Automatic,
            networkA.Fingerprints,
            now,
            TimeSpan.FromDays(7));
        BenchmarkFingerprintSet networkC = networkA.Fingerprints with
        {
            Network = networkA.Fingerprints.Network with { WifiChannel = 149 }
        };
        BenchmarkPreparationResult firstC = store.PrepareBenchmarkRun(
            networkA.ClientId,
            BenchmarkTrigger.Automatic,
            networkC,
            now,
            TimeSpan.FromDays(7));
        BenchmarkPreparationResult repeatedC = store.PrepareBenchmarkRun(
            networkA.ClientId,
            BenchmarkTrigger.Automatic,
            networkC,
            now.AddSeconds(1),
            TimeSpan.FromDays(7));

        Assert.Equal(BenchmarkPreparationDisposition.Reuse, returnedToA.Disposition);
        Assert.Equal(networkA.RunId, returnedToA.Evidence.RunId);
        Assert.Equal(BenchmarkPreparationDisposition.StartNew, firstC.Disposition);
        Assert.Equal(BenchmarkPreparationDisposition.Continue, repeatedC.Disposition);
        Assert.Equal(firstC.Evidence.RunId, repeatedC.Evidence.RunId);
        Assert.Null(store.GetLatestBenchmarkPlanEvidence(
            networkA.ClientId.Value,
            now.AddSeconds(1),
            TimeSpan.FromDays(7),
            "auto"));
    }

    internal static BenchmarkEvidence CreateEvidence(Guid runId, DateTimeOffset completedAt)
    {
        var fingerprints = new BenchmarkFingerprintSet(
            new NetworkFingerprint(3, "192.168.1.10", "wifi", "192.168.1.0/24", "6-ghz", 37, "500-999-mbps", new string('a', 64)),
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
        var coverage = new NetworkBenchmarkCoverage(FirstSequence: 1, ExpectedPacketCount: 1);
        SelectedBenchmarkResult selected = BenchmarkScorer.Select(new(
            networkSamples,
            decoderSamples,
            powerSamples,
            NetworkCoverage: coverage));

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
            selected,
            coverage);
    }

    private sealed class FailingBenchmarkEvidenceRepository : IBenchmarkEvidenceRepository
    {
        public string Kind => "failing";

        public string? Location => null;

        public IReadOnlyList<BenchmarkEvidence> LoadEvidence() => [];

        public void SaveEvidence(IReadOnlyList<BenchmarkEvidence> evidence) =>
            throw new IOException("Persistence failed.");
    }

    private sealed class SwitchableBenchmarkEvidenceRepository(
        IEnumerable<BenchmarkEvidence>? initialEvidence = null) : IBenchmarkEvidenceRepository
    {
        private BenchmarkEvidence[] evidence = initialEvidence?.ToArray() ?? [];

        public bool FailWrites { get; set; }

        public string Kind => "switchable";

        public string? Location => null;

        public IReadOnlyList<BenchmarkEvidence> LoadEvidence() => evidence;

        public void SaveEvidence(IReadOnlyList<BenchmarkEvidence> values)
        {
            if (FailWrites)
            {
                throw new IOException("Persistence failed.");
            }

            evidence = values.ToArray();
        }
    }
}

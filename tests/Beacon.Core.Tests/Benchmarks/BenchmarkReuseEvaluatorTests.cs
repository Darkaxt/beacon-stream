using Beacon.Core.Benchmarks;
using Beacon.Core.Clients;

namespace Beacon.Core.Tests.Benchmarks;

public sealed class BenchmarkReuseEvaluatorTests
{
    private static readonly DateTimeOffset EvaluatedAt = new(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AutomaticRunReusesFreshCompletedEvidenceForSameFingerprints()
    {
        BenchmarkEvidence evidence = CreateEvidence(EvaluatedAt.AddHours(-2));

        BenchmarkReuseDecision decision = BenchmarkReuseEvaluator.Decide(
            BenchmarkTrigger.Automatic,
            evidence.Fingerprints,
            evidence,
            EvaluatedAt,
            TimeSpan.FromDays(7));

        Assert.Equal(BenchmarkRunDisposition.Reuse, decision.Disposition);
        Assert.Equal(evidence.RunId, decision.ReusedRunId);
        Assert.Contains("matches", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ManualRunAlwaysStartsNewEvidence()
    {
        BenchmarkEvidence evidence = CreateEvidence(EvaluatedAt.AddMinutes(-1));

        BenchmarkReuseDecision decision = BenchmarkReuseEvaluator.Decide(
            BenchmarkTrigger.Manual,
            evidence.Fingerprints,
            evidence,
            EvaluatedAt,
            TimeSpan.FromDays(7));

        Assert.Equal(BenchmarkRunDisposition.StartNew, decision.Disposition);
        Assert.Null(decision.ReusedRunId);
        Assert.Contains("manual", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SessionPreflightAlwaysStartsNewEvidence()
    {
        BenchmarkEvidence evidence = CreateEvidence(EvaluatedAt.AddMinutes(-1));

        BenchmarkReuseDecision decision = BenchmarkReuseEvaluator.Decide(
            BenchmarkTrigger.SessionPreflight,
            evidence.Fingerprints,
            evidence,
            EvaluatedAt,
            TimeSpan.FromDays(7));

        Assert.Equal(BenchmarkRunDisposition.StartNew, decision.Disposition);
        Assert.Null(decision.ReusedRunId);
        Assert.Contains("preflight", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MaterialFingerprintChangeStartsNewAutomaticRun()
    {
        BenchmarkEvidence evidence = CreateEvidence(EvaluatedAt.AddHours(-2));
        BenchmarkFingerprintSet changed = evidence.Fingerprints with
        {
            Network = evidence.Fingerprints.Network with { WifiChannel = 44 }
        };

        BenchmarkReuseDecision decision = BenchmarkReuseEvaluator.Decide(
            BenchmarkTrigger.Automatic,
            changed,
            evidence,
            EvaluatedAt,
            TimeSpan.FromDays(7));

        Assert.Equal(BenchmarkRunDisposition.StartNew, decision.Disposition);
        Assert.Contains("fingerprint", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StaleOrIncompleteEvidenceIsNeverReused()
    {
        BenchmarkEvidence stale = CreateEvidence(EvaluatedAt.AddDays(-8));
        BenchmarkEvidence incomplete = stale with
        {
            CompletedAt = null,
            SelectedResult = null
        };

        BenchmarkReuseDecision staleDecision = BenchmarkReuseEvaluator.Decide(
            BenchmarkTrigger.Automatic,
            stale.Fingerprints,
            stale,
            EvaluatedAt,
            TimeSpan.FromDays(7));
        BenchmarkReuseDecision incompleteDecision = BenchmarkReuseEvaluator.Decide(
            BenchmarkTrigger.Automatic,
            incomplete.Fingerprints,
            incomplete,
            EvaluatedAt,
            TimeSpan.FromDays(7));

        Assert.Equal(BenchmarkRunDisposition.StartNew, staleDecision.Disposition);
        Assert.Contains("stale", staleDecision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(BenchmarkRunDisposition.StartNew, incompleteDecision.Disposition);
        Assert.Contains("incomplete", incompleteDecision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private static BenchmarkEvidence CreateEvidence(DateTimeOffset completedAt)
    {
        var selected = new SelectedBenchmarkResult(
            Codec: "h264",
            MaxSustainableFps: 120,
            InitialBitrateMbps: 70,
            SustainableThroughputMbps: 100,
            RttMs: 8,
            JitterMs: 1.2,
            PacketLossPercent: 0,
            PowerConstrained: false,
            Reasons: ["H.264 120 FPS passed active decode validation."]);

        return new BenchmarkEvidence(
            RunId: Guid.Parse("d16c4121-f8e8-442f-891e-4bb260bf3b9a"),
            ClientId: new ClientId("z-fold-7"),
            Trigger: BenchmarkTrigger.Automatic,
            Fingerprints: new BenchmarkFingerprintSet(
                BenchmarkFingerprintTests.CreateNetworkFingerprint(),
                BenchmarkFingerprintTests.CreateHardwareFingerprint()),
            StartedAt: completedAt.AddMinutes(-5),
            CompletedAt: completedAt,
            NetworkSamples: [],
            DecoderSamples: [],
            PowerSamples: [],
            SelectedResult: selected);
    }
}

namespace Beacon.Core.Benchmarks;

public enum BenchmarkRunDisposition
{
    StartNew,
    Reuse
}

public sealed record BenchmarkReuseDecision(
    BenchmarkRunDisposition Disposition,
    Guid? ReusedRunId,
    string Reason);

public static class BenchmarkReuseEvaluator
{
    public static BenchmarkReuseDecision Decide(
        BenchmarkTrigger trigger,
        BenchmarkFingerprintSet currentFingerprints,
        BenchmarkEvidence? existing,
        DateTimeOffset evaluatedAt,
        TimeSpan maximumEvidenceAge)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximumEvidenceAge, TimeSpan.Zero);

        if (trigger == BenchmarkTrigger.Manual)
        {
            return StartNew("Manual benchmark requests always create a new run.");
        }

        if (existing is null)
        {
            return StartNew("No prior benchmark evidence is available.");
        }

        if (existing.CompletedAt is null || existing.SelectedResult is null)
        {
            return StartNew("Prior benchmark evidence is incomplete.");
        }

        if (existing.Fingerprints != currentFingerprints)
        {
            return StartNew("The current network or hardware fingerprint differs from prior evidence.");
        }

        TimeSpan age = evaluatedAt - existing.CompletedAt.Value;
        if (age < TimeSpan.Zero || age > maximumEvidenceAge)
        {
            return StartNew("Prior benchmark evidence is stale.");
        }

        return new BenchmarkReuseDecision(
            BenchmarkRunDisposition.Reuse,
            existing.RunId,
            "Completed evidence matches the current network and hardware fingerprints.");
    }

    private static BenchmarkReuseDecision StartNew(string reason) =>
        new(BenchmarkRunDisposition.StartNew, ReusedRunId: null, reason);
}

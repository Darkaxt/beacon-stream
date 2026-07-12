using Beacon.Core.Benchmarks;

namespace Beacon.Server.State;

public enum BenchmarkPreparationDisposition
{
    StartNew,
    Reuse,
    Continue
}

public sealed record BenchmarkPreparationResult(
    BenchmarkPreparationDisposition Disposition,
    BenchmarkEvidence Evidence,
    string Reason);

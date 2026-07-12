using Beacon.Core.Benchmarks;

namespace Beacon.Server.State;

public sealed class InMemoryBenchmarkEvidenceRepository : IBenchmarkEvidenceRepository
{
    private readonly object gate = new();
    private BenchmarkEvidence[] evidence;

    public InMemoryBenchmarkEvidenceRepository(IEnumerable<BenchmarkEvidence>? initialEvidence = null)
    {
        evidence = initialEvidence?.ToArray() ?? [];
    }

    public string Kind => "memory";

    public string? Location => null;

    public IReadOnlyList<BenchmarkEvidence> LoadEvidence()
    {
        lock (gate)
        {
            return evidence.ToArray();
        }
    }

    public void SaveEvidence(IReadOnlyList<BenchmarkEvidence> values)
    {
        lock (gate)
        {
            evidence = values.ToArray();
        }
    }
}

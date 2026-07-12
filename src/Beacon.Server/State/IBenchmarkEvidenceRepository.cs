using Beacon.Core.Benchmarks;

namespace Beacon.Server.State;

public interface IBenchmarkEvidenceRepository
{
    string Kind { get; }

    string? Location { get; }

    IReadOnlyList<BenchmarkEvidence> LoadEvidence();

    void SaveEvidence(IReadOnlyList<BenchmarkEvidence> evidence);
}

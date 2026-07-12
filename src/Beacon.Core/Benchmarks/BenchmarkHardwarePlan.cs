namespace Beacon.Core.Benchmarks;

public sealed record DecoderBenchmarkRoundPlan(
    string VectorId,
    string Codec,
    string Profile,
    int BitDepth,
    int Width,
    int Height,
    int TargetFps,
    int RepetitionCount);

public sealed record BenchmarkHardwarePlan(
    int SchemaVersion,
    IReadOnlyList<DecoderBenchmarkRoundPlan> DecoderRounds,
    bool SamplePowerBeforeAndAfterEachRound);

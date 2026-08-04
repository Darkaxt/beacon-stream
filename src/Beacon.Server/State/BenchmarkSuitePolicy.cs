using Beacon.Core.Benchmarks;
using Beacon.Core.Clients;

namespace Beacon.Server.State;

public static class BenchmarkSuitePolicy
{
    public static BenchmarkTransportPlan Create(BenchmarkTrigger trigger) =>
        trigger == BenchmarkTrigger.SessionPreflight
            ? new BenchmarkTransportPlan(16, 32 * 1024, 64, 1000, 250_000)
            : new BenchmarkTransportPlan(64, 64 * 1024, 256, 1000, 1_000_000);

    public static NetworkBenchmarkCoverage Coverage(BenchmarkTransportPlan plan) =>
        new(FirstSequence: 0, ExpectedPacketCount: plan.DatagramPacketCount);

    public static BenchmarkHardwarePlan CreateHardware(
        BenchmarkTrigger trigger,
        EndpointCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        var rounds = new List<DecoderBenchmarkRoundPlan>();
        if (trigger != BenchmarkTrigger.SessionPreflight && capabilities.H264)
        {
            if (capabilities.MaxFps >= 60)
            {
                rounds.Add(new DecoderBenchmarkRoundPlan(
                    VectorId: "beacon-h264-high-8-1280x720-60-v1",
                    Codec: "h264",
                    Profile: "high",
                    BitDepth: 8,
                    Width: 1280,
                    Height: 720,
                    TargetFps: 60,
                    RepetitionCount: 3));
            }

            if (capabilities.MaxFps >= 30)
            {
                rounds.Add(new DecoderBenchmarkRoundPlan(
                    VectorId: "beacon-h264-high-8-640x360-30-v1",
                    Codec: "h264",
                    Profile: "high",
                    BitDepth: 8,
                    Width: 640,
                    Height: 360,
                    TargetFps: 30,
                    RepetitionCount: 3));
            }
        }

        return new BenchmarkHardwarePlan(
            SchemaVersion: 1,
            DecoderRounds: rounds,
            SamplePowerBeforeAndAfterEachRound: true);
    }
}

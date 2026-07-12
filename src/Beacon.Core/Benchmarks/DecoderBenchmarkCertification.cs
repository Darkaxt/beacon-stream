namespace Beacon.Core.Benchmarks;

internal static class DecoderBenchmarkCertification
{
    public static bool IsCertified(DecoderBenchmarkSample sample)
    {
        double frameDurationMs = FrameDurationMs(sample.TargetFps);
        return sample.Configured &&
            sample.DroppedFrames == 0 &&
            sample.OutputErrors == 0 &&
            sample.SustainedFps >= sample.TargetFps &&
            sample.P95DecodeLatencyMs <= frameDurationMs &&
            sample.P95PresentationLatencyMs is not null &&
            sample.P95PresentationLatencyMs.Value <= 2 * frameDurationMs;
    }

    public static double FrameDurationMs(int targetFps) => 1000.0 / targetFps;
}

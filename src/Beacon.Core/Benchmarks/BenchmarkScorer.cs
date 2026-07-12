namespace Beacon.Core.Benchmarks;

public static class BenchmarkScorer
{
    private const double DecoderSustainabilityRatio = 0.95;
    private const double ThroughputBudgetRatio = 0.70;
    private const double LossProtectionRatio = 0.70;
    private const double ThermalProtectionRatio = 0.80;

    public static SelectedBenchmarkResult Select(BenchmarkScoringInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.NetworkSamples.Count == 0)
        {
            throw new InvalidOperationException("Benchmark scoring requires network samples.");
        }

        NetworkBenchmarkSample[] received = input.NetworkSamples
            .Where(sample => sample.Received && sample.ThroughputMbps > 0)
            .ToArray();
        if (received.Length == 0)
        {
            throw new InvalidOperationException("Benchmark scoring requires at least one received network sample.");
        }

        var reasons = new List<string>();
        DecoderBenchmarkSample[] sustainable = input.DecoderSamples
            .Where(IsSustainable)
            .ToArray();

        foreach (DecoderBenchmarkSample rejected in input.DecoderSamples.Except(sustainable))
        {
            reasons.Add($"{DisplayCodec(rejected.Codec)} {rejected.TargetFps} FPS was rejected by active decode evidence.");
        }

        string codecPreference = input.CodecPreference.Trim().ToLowerInvariant();
        DecoderBenchmarkSample[] eligible = codecPreference is "" or "auto"
            ? sustainable
            : sustainable
                .Where(sample => sample.Codec.Equals(codecPreference, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        if (codecPreference is not ("" or "auto"))
        {
            reasons.Add($"Server profile codec preference {codecPreference} constrained candidate selection.");
        }

        DecoderBenchmarkSample? selectedDecoder = eligible
            .OrderByDescending(sample => CodecPriority(sample.Codec))
            .ThenByDescending(sample => sample.BitDepth)
            .ThenByDescending(sample => sample.TargetFps)
            .FirstOrDefault();
        if (selectedDecoder is null)
        {
            throw new InvalidOperationException("No sustainable decoder candidate is available.");
        }

        reasons.Add($"{DisplayCodec(selectedDecoder.Codec)} {selectedDecoder.TargetFps} FPS passed active decode validation.");

        double lossPercent = 100.0 * (input.NetworkSamples.Count - received.Length) / input.NetworkSamples.Count;
        double sustainableThroughput = received.Min(sample => sample.ThroughputMbps);
        double rttMs = received.Average(sample => sample.RttMs);
        double jitterMs = received.Average(sample => sample.JitterMs);
        double bitrate = sustainableThroughput * ThroughputBudgetRatio;
        int selectedFps = selectedDecoder.TargetFps;

        if (rttMs >= 80)
        {
            reasons.Add($"Measured RTT {rttMs:0.#}ms requires latency protection.");
        }

        if (lossPercent >= 2)
        {
            bitrate *= LossProtectionRatio;
            reasons.Add($"Measured packet loss {lossPercent:0.#}% reduced the initial bitrate budget.");
        }

        bool powerConstrained = IsThermallyConstrained(input.PowerSamples);
        if (powerConstrained)
        {
            bitrate *= ThermalProtectionRatio;
            selectedFps = Math.Min(selectedFps, 60);
            reasons.Add("Measured thermal pressure limited the selected frame rate and bitrate.");
        }

        return new SelectedBenchmarkResult(
            Codec: selectedDecoder.Codec.ToLowerInvariant(),
            MaxSustainableFps: selectedFps,
            InitialBitrateMbps: Math.Max(5, checked((int)Math.Floor(bitrate))),
            SustainableThroughputMbps: sustainableThroughput,
            RttMs: rttMs,
            JitterMs: jitterMs,
            PacketLossPercent: lossPercent,
            PowerConstrained: powerConstrained,
            Reasons: reasons);
    }

    private static bool IsSustainable(DecoderBenchmarkSample sample) =>
        sample.Configured &&
        sample.OutputErrors == 0 &&
        sample.SustainedFps >= sample.TargetFps * DecoderSustainabilityRatio;

    private static bool IsThermallyConstrained(IReadOnlyList<EndpointPowerSample> samples) =>
        samples.Any(sample =>
            sample.ThermalState.Equals("hot", StringComparison.OrdinalIgnoreCase) ||
            sample.ThermalState.Equals("critical", StringComparison.OrdinalIgnoreCase));

    private static int CodecPriority(string codec) =>
        codec.ToLowerInvariant() switch
        {
            "av1" => 3,
            "hevc" => 2,
            "h264" => 1,
            _ => 0
        };

    private static string DisplayCodec(string codec) =>
        codec.ToLowerInvariant() switch
        {
            "av1" => "AV1",
            "hevc" => "HEVC",
            "h264" => "H.264",
            _ => codec
        };
}

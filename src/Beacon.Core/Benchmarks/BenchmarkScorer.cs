namespace Beacon.Core.Benchmarks;

public static class BenchmarkScorer
{
    private const double ThroughputBudgetRatio = 0.70;
    private const double LossProtectionRatio = 0.70;
    private const double ThermalProtectionRatio = 0.80;
    private const int MinimumInitialBitrateMbps = 5;

    public static SelectedBenchmarkResult Select(BenchmarkScoringInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        BenchmarkEvidenceValidator.Validate(input);

        return SelectValidated(input);
    }

    internal static SelectedBenchmarkResult SelectValidated(BenchmarkScoringInput input)
    {
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
            .Where(DecoderBenchmarkCertification.IsCertified)
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
            .ThenByDescending(sample => (long)sample.Width * sample.Height)
            .ThenBy(sample => sample.P95PresentationLatencyMs!.Value)
            .ThenBy(sample => sample.P95DecodeLatencyMs)
            .ThenByDescending(sample => sample.Width)
            .ThenByDescending(sample => sample.Height)
            .ThenBy(sample => sample.Codec, StringComparer.OrdinalIgnoreCase)
            .ThenBy(sample => sample.Codec, StringComparer.Ordinal)
            .ThenBy(sample => sample.Profile, StringComparer.OrdinalIgnoreCase)
            .ThenBy(sample => sample.Profile, StringComparer.Ordinal)
            .ThenByDescending(sample => sample.SustainedFps)
            .ThenByDescending(sample => sample.TenBitPresentationVerified)
            .ThenByDescending(sample => sample.HdrPresentationVerified)
            .ThenByDescending(sample => sample.Configured)
            .ThenBy(sample => sample.DroppedFrames)
            .ThenBy(sample => sample.OutputErrors)
            .FirstOrDefault();
        if (selectedDecoder is null)
        {
            throw new InvalidOperationException("No sustainable decoder candidate is available.");
        }

        reasons.Add($"{DisplayCodec(selectedDecoder.Codec)} {selectedDecoder.TargetFps} FPS passed active decode validation.");

        NetworkBenchmarkCoverage coverage = input.NetworkCoverage!;
        int receivedSequenceCount = received
            .Select(sample => sample.Sequence)
            .Distinct()
            .Count();
        double lossPercent = 100.0 * (coverage.ExpectedPacketCount - receivedSequenceCount) / coverage.ExpectedPacketCount;
        double sustainableThroughput = received.Min(sample => sample.ThroughputMbps);
        double rttMs = received.Average(sample => sample.RttMs);
        double jitterMs = received.Average(sample => sample.JitterMs);
        double bitrate = sustainableThroughput * ThroughputBudgetRatio;

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
            reasons.Add("Measured thermal pressure reduced the initial bitrate budget.");
        }

        int initialBitrate = checked((int)Math.Floor(bitrate));
        if (initialBitrate < MinimumInitialBitrateMbps)
        {
            throw new InvalidOperationException(
                $"Measured throughput cannot sustain the minimum {MinimumInitialBitrateMbps} Mbps initial bitrate.");
        }

        return new SelectedBenchmarkResult(
            Codec: selectedDecoder.Codec.ToLowerInvariant(),
            MaxSustainableFps: selectedDecoder.TargetFps,
            InitialBitrateMbps: initialBitrate,
            SustainableThroughputMbps: sustainableThroughput,
            RttMs: rttMs,
            JitterMs: jitterMs,
            PacketLossPercent: lossPercent,
            PowerConstrained: powerConstrained,
            Reasons: reasons,
            Profile: selectedDecoder.Profile,
            BitDepth: selectedDecoder.BitDepth,
            Width: selectedDecoder.Width,
            Height: selectedDecoder.Height,
            TenBitPresentationVerified: selectedDecoder.TenBitPresentationVerified,
            HdrPresentationVerified: selectedDecoder.HdrPresentationVerified,
            P95DecodeLatencyMs: selectedDecoder.P95DecodeLatencyMs,
            P95PresentationLatencyMs: selectedDecoder.P95PresentationLatencyMs);
    }

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

namespace Beacon.Core.Benchmarks;

public static class BenchmarkEvidenceValidator
{
    public const int SupportedFingerprintSchemaVersion = 3;

    private const int MaximumDimension = 16384;
    private const int MaximumTargetFps = 1000;
    private const int MaximumDatagramPayloadBytes = 65507;

    private static readonly HashSet<string> SupportedCodecs =
        new(StringComparer.OrdinalIgnoreCase) { "av1", "hevc", "h264" };

    private static readonly HashSet<string> SupportedThermalStates =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "",
            "none",
            "nominal",
            "light",
            "moderate",
            "hot",
            "critical"
        };

    public static void Validate(BenchmarkEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        if (evidence.RunId == Guid.Empty)
        {
            throw Invalid("Benchmark run id must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(evidence.ClientId.Value))
        {
            throw Invalid("Benchmark client id is required.");
        }

        if (!Enum.IsDefined(evidence.Trigger))
        {
            throw Invalid("Benchmark trigger is not supported.");
        }

        Validate(evidence.Fingerprints);
        if (evidence.StartedAt == default)
        {
            throw Invalid("Benchmark start time is required.");
        }

        if (evidence.CompletedAt is null)
        {
            if (evidence.SelectedResult is not null)
            {
                throw Invalid("Incomplete benchmark evidence cannot contain a selected result.");
            }

            if (evidence.NetworkSamples.Count > 0 ||
                evidence.DecoderSamples.Count > 0 ||
                evidence.PowerSamples.Count > 0 ||
                evidence.NetworkCoverage is not null)
            {
                throw Invalid("Pending benchmark evidence cannot contain samples or network coverage.");
            }

            return;
        }

        if (evidence.CompletedAt == default || evidence.CompletedAt < evidence.StartedAt)
        {
            throw Invalid("Benchmark completion time must not precede the start time.");
        }

        if (evidence.SelectedResult is null)
        {
            throw Invalid("Completed benchmark evidence requires a selected result.");
        }

        var scoringInput = new BenchmarkScoringInput(
            evidence.NetworkSamples,
            evidence.DecoderSamples,
            evidence.PowerSamples,
            NetworkCoverage: evidence.NetworkCoverage);
        Validate(scoringInput);
        Validate(evidence.SelectedResult);

        SelectedBenchmarkResult rescored;
        try
        {
            rescored = BenchmarkScorer.SelectValidated(scoringInput with
            {
                CodecPreference = evidence.SelectedResult.Codec
            });
        }
        catch (InvalidOperationException error)
        {
            throw Invalid("Selected benchmark result cannot be reproduced from measured evidence.", error);
        }

        if (evidence.SelectedResult.InitialBitrateMbps != rescored.InitialBitrateMbps ||
            evidence.SelectedResult.SustainableThroughputMbps != rescored.SustainableThroughputMbps ||
            evidence.SelectedResult.RttMs != rescored.RttMs ||
            evidence.SelectedResult.JitterMs != rescored.JitterMs ||
            evidence.SelectedResult.PacketLossPercent != rescored.PacketLossPercent ||
            evidence.SelectedResult.PowerConstrained != rescored.PowerConstrained)
        {
            throw Invalid("Selected benchmark network result does not match measured network and power evidence.");
        }

        bool matchesDecoder = evidence.DecoderSamples.Any(sample =>
            sample.Codec.Equals(evidence.SelectedResult.Codec, StringComparison.OrdinalIgnoreCase) &&
            sample.Profile.Equals(evidence.SelectedResult.Profile, StringComparison.Ordinal) &&
            sample.BitDepth == evidence.SelectedResult.BitDepth &&
            sample.Width == evidence.SelectedResult.Width &&
            sample.Height == evidence.SelectedResult.Height &&
            sample.TargetFps == evidence.SelectedResult.MaxSustainableFps &&
            sample.TenBitPresentationVerified == evidence.SelectedResult.TenBitPresentationVerified &&
            sample.HdrPresentationVerified == evidence.SelectedResult.HdrPresentationVerified &&
            sample.P95DecodeLatencyMs == evidence.SelectedResult.P95DecodeLatencyMs &&
            sample.P95PresentationLatencyMs == evidence.SelectedResult.P95PresentationLatencyMs &&
            DecoderBenchmarkCertification.IsCertified(sample));
        if (!matchesDecoder)
        {
            throw Invalid("Selected benchmark result does not match certified decoder evidence.");
        }
    }

    public static void Validate(BenchmarkScoringInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.NetworkSamples);
        ArgumentNullException.ThrowIfNull(input.DecoderSamples);
        ArgumentNullException.ThrowIfNull(input.PowerSamples);

        if (input.NetworkCoverage is null)
        {
            throw Invalid("Server-defined network sequence coverage is required.");
        }

        Validate(input.NetworkCoverage);
        long lastSequence = GetLastSequence(input.NetworkCoverage);
        var observedSequences = new HashSet<long>();
        foreach (NetworkBenchmarkSample sample in input.NetworkSamples)
        {
            if (sample.Sequence < input.NetworkCoverage.FirstSequence || sample.Sequence > lastSequence)
            {
                throw Invalid("Network sample sequence falls outside server-defined coverage.");
            }

            if (!observedSequences.Add(sample.Sequence))
            {
                throw Invalid("Network sample sequences must be unique.");
            }

            if (sample.PayloadBytes is <= 0 or > MaximumDatagramPayloadBytes)
            {
                throw Invalid("Network sample payload size is invalid.");
            }

            RequireFiniteNonNegative(sample.RttMs, "Network sample RTT");
            RequireFiniteNonNegative(sample.JitterMs, "Network sample jitter");
            RequireFiniteNonNegative(sample.ThroughputMbps, "Network sample throughput");
            if (sample.Received && sample.ThroughputMbps <= 0)
            {
                throw Invalid("Received network samples require positive throughput.");
            }

            if (!sample.Received && sample.ThroughputMbps != 0)
            {
                throw Invalid("Missing network samples cannot report throughput.");
            }

            if (sample.ReorderDistance < 0)
            {
                throw Invalid("Network sample reorder distance cannot be negative.");
            }
        }

        foreach (DecoderBenchmarkSample sample in input.DecoderSamples)
        {
            Validate(sample);
        }

        foreach (EndpointPowerSample sample in input.PowerSamples)
        {
            Validate(sample);
        }

        string codecPreference = input.CodecPreference.Trim();
        if (codecPreference.Length > 0 &&
            !codecPreference.Equals("auto", StringComparison.OrdinalIgnoreCase) &&
            !SupportedCodecs.Contains(codecPreference))
        {
            throw Invalid("Benchmark codec preference is not supported.");
        }
    }

    public static void Validate(BenchmarkFingerprintSet fingerprints)
    {
        ArgumentNullException.ThrowIfNull(fingerprints);
        ArgumentNullException.ThrowIfNull(fingerprints.Network);
        ArgumentNullException.ThrowIfNull(fingerprints.Hardware);

        if (fingerprints.Network.SchemaVersion != SupportedFingerprintSchemaVersion ||
            fingerprints.Hardware.SchemaVersion != SupportedFingerprintSchemaVersion)
        {
            throw Invalid($"Benchmark fingerprint schema must be exactly version {SupportedFingerprintSchemaVersion}.");
        }

        RequireText(fingerprints.Network.ServerRoute, "Network server route");
        RequireText(fingerprints.Network.Transport, "Network transport");
        RequireText(fingerprints.Network.LocalNetworkPrefix, "Local network prefix");
        RequireText(fingerprints.Network.LinkSpeedBucket, "Network link speed bucket");
        if (fingerprints.Network.SaltedNetworkIdHash is { } saltedHash &&
            (saltedHash.Length != 64 || saltedHash.Any(character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))))
        {
            throw Invalid("Salted network identity hash must be 64 lowercase hexadecimal characters.");
        }

        if (fingerprints.Network.WifiChannel is <= 0 or > 233)
        {
            throw Invalid("Wi-Fi channel is outside the supported range.");
        }

        RequireText(fingerprints.Hardware.DeviceCapabilityRevision, "Device capability revision");
        RequireText(fingerprints.Hardware.AndroidVersion, "Android version");
        RequireText(fingerprints.Hardware.ApkVersion, "APK version");
        RequireText(fingerprints.Hardware.DisplayModeInventoryRevision, "Display mode inventory revision");
        RequireText(fingerprints.Hardware.CodecInventoryRevision, "Codec inventory revision");
    }

    public static void Validate(SelectedBenchmarkResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        ValidateCodec(result.Codec);
        ValidateProfile(result.Codec, result.Profile, result.BitDepth);
        ValidateBitDepth(result.BitDepth, result.TenBitPresentationVerified, result.HdrPresentationVerified);
        ValidateDimension(result.Width, "Selected decoder width");
        ValidateDimension(result.Height, "Selected decoder height");
        ValidateTargetFps(result.MaxSustainableFps);
        RequireFiniteNonNegative(result.P95DecodeLatencyMs, "Selected decoder P95 latency");
        if (result.P95PresentationLatencyMs is null)
        {
            throw Invalid("Selected presentation P95 latency is required.");
        }

        RequireFiniteNonNegative(result.P95PresentationLatencyMs.Value, "Selected presentation P95 latency");
        double frameDurationMs = DecoderBenchmarkCertification.FrameDurationMs(result.MaxSustainableFps);
        if (result.P95DecodeLatencyMs > frameDurationMs ||
            result.P95PresentationLatencyMs.Value > 2 * frameDurationMs)
        {
            throw Invalid("Selected decoder latency exceeds the certified frame budget.");
        }

        if (result.InitialBitrateMbps < 5)
        {
            throw Invalid("Selected initial bitrate is below the supported minimum.");
        }

        RequireFinitePositive(result.SustainableThroughputMbps, "Selected sustainable throughput");
        if (result.InitialBitrateMbps > result.SustainableThroughputMbps)
        {
            throw Invalid("Selected initial bitrate cannot exceed measured sustainable throughput.");
        }

        RequireFiniteNonNegative(result.RttMs, "Selected RTT");
        RequireFiniteNonNegative(result.JitterMs, "Selected jitter");
        if (!double.IsFinite(result.PacketLossPercent) || result.PacketLossPercent is < 0 or > 100)
        {
            throw Invalid("Selected packet loss percentage must be between 0 and 100.");
        }

        ArgumentNullException.ThrowIfNull(result.Reasons);
        if (result.Reasons.Any(string.IsNullOrWhiteSpace))
        {
            throw Invalid("Selected benchmark reasons cannot contain empty values.");
        }
    }

    private static void Validate(NetworkBenchmarkCoverage coverage)
    {
        if (coverage.FirstSequence < 0)
        {
            throw Invalid("Network coverage first sequence cannot be negative.");
        }

        if (coverage.ExpectedPacketCount <= 0)
        {
            throw Invalid("Network coverage expected packet count must be positive.");
        }

        _ = GetLastSequence(coverage);
    }

    private static void Validate(DecoderBenchmarkSample sample)
    {
        ValidateCodec(sample.Codec);
        ValidateProfile(sample.Codec, sample.Profile, sample.BitDepth);
        ValidateBitDepth(sample.BitDepth, sample.TenBitPresentationVerified, sample.HdrPresentationVerified);
        ValidateDimension(sample.Width, "Decoder width");
        ValidateDimension(sample.Height, "Decoder height");
        ValidateTargetFps(sample.TargetFps);
        RequireFiniteNonNegative(sample.SustainedFps, "Decoder sustained FPS");
        RequireFiniteNonNegative(sample.P95DecodeLatencyMs, "Decoder P95 latency");
        if (sample.P95PresentationLatencyMs is not null)
        {
            RequireFiniteNonNegative(sample.P95PresentationLatencyMs.Value, "Presentation P95 latency");
        }

        if (sample.DroppedFrames < 0 || sample.OutputErrors < 0)
        {
            throw Invalid("Decoder drop and error counts cannot be negative.");
        }
    }

    private static void Validate(EndpointPowerSample sample)
    {
        if (sample.BatteryPercent is < 0 or > 100)
        {
            throw Invalid("Battery percentage must be between 0 and 100.");
        }

        if (!SupportedThermalStates.Contains(sample.ThermalState))
        {
            throw Invalid("Thermal state is not supported.");
        }
    }

    private static void ValidateBitDepth(int bitDepth, bool tenBitVerified, bool hdrVerified)
    {
        if (bitDepth is not (8 or 10))
        {
            throw Invalid("Decoder bit depth must be 8 or 10.");
        }

        if ((tenBitVerified || hdrVerified) && bitDepth != 10)
        {
            throw Invalid("10-bit or HDR presentation evidence requires a 10-bit decoder mode.");
        }

        if (hdrVerified && !tenBitVerified)
        {
            throw Invalid("HDR presentation evidence requires verified 10-bit presentation.");
        }
    }

    private static void ValidateCodec(string codec)
    {
        if (!SupportedCodecs.Contains(codec))
        {
            throw Invalid("Decoder codec is not supported.");
        }
    }

    private static void ValidateProfile(string codec, string profile, int bitDepth)
    {
        RequireText(profile, "Decoder profile");
        bool supported = codec.ToLowerInvariant() switch
        {
            "h264" => profile.Equals("baseline", StringComparison.OrdinalIgnoreCase) ||
                profile.Equals("main", StringComparison.OrdinalIgnoreCase) ||
                profile.Equals("high", StringComparison.OrdinalIgnoreCase),
            "hevc" => profile.Equals("main", StringComparison.OrdinalIgnoreCase) ||
                profile.Equals("main10", StringComparison.OrdinalIgnoreCase),
            "av1" => profile.Equals("main", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
        if (!supported)
        {
            throw Invalid($"Decoder profile '{profile}' is not supported for {codec}.");
        }

        bool profileMatchesBitDepth = codec.ToLowerInvariant() switch
        {
            "h264" => bitDepth == 8,
            "hevc" when profile.Equals("main", StringComparison.OrdinalIgnoreCase) => bitDepth == 8,
            "hevc" when profile.Equals("main10", StringComparison.OrdinalIgnoreCase) => bitDepth == 10,
            _ => true
        };
        if (!profileMatchesBitDepth)
        {
            throw Invalid($"Decoder profile '{profile}' does not support {bitDepth}-bit depth for {codec}.");
        }
    }

    private static void ValidateDimension(int value, string name)
    {
        if (value is <= 0 or > MaximumDimension)
        {
            throw Invalid($"{name} is outside the supported range.");
        }
    }

    private static void ValidateTargetFps(int targetFps)
    {
        if (targetFps is <= 0 or > MaximumTargetFps)
        {
            throw Invalid("Decoder target FPS is outside the supported range.");
        }
    }

    private static long GetLastSequence(NetworkBenchmarkCoverage coverage)
    {
        try
        {
            return checked(coverage.FirstSequence + coverage.ExpectedPacketCount - 1L);
        }
        catch (OverflowException error)
        {
            throw Invalid("Network coverage sequence range overflows.", error);
        }
    }

    private static void RequireFinitePositive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw Invalid($"{name} must be finite and positive.");
        }
    }

    private static void RequireFiniteNonNegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw Invalid($"{name} must be finite and non-negative.");
        }
    }

    private static void RequireText(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Invalid($"{name} is required.");
        }
    }

    private static ArgumentException Invalid(string message, Exception? innerException = null) =>
        new(message, innerException);
}

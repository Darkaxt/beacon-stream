using Beacon.Core.Benchmarks;
using Beacon.Core.Clients;

namespace Beacon.Core.Tests.Benchmarks;

public sealed class BenchmarkEvidenceValidatorTests
{
    [Fact]
    public void RejectsNonHashedNetworkIdentityValue()
    {
        BenchmarkEvidence evidence = CreateEvidence();
        evidence = evidence with
        {
            Fingerprints = evidence.Fingerprints with
            {
                Network = evidence.Fingerprints.Network with { SaltedNetworkIdHash = "raw-network-name" }
            }
        };

        ArgumentException error = Assert.Throws<ArgumentException>(() => BenchmarkEvidenceValidator.Validate(evidence));

        Assert.Contains("salted", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hash", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsUnsupportedCodecProfile()
    {
        BenchmarkEvidence evidence = CreateEvidence();
        evidence = evidence with
        {
            DecoderSamples =
            [
                evidence.DecoderSamples[0] with { Profile = "made-up-profile" }
            ],
            SelectedResult = evidence.SelectedResult! with { Profile = "made-up-profile" }
        };

        ArgumentException error = Assert.Throws<ArgumentException>(() => BenchmarkEvidenceValidator.Validate(evidence));

        Assert.Contains("profile", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsCodecProfileBitDepthMismatch()
    {
        BenchmarkEvidence evidence = CreateEvidence();
        evidence = evidence with
        {
            DecoderSamples =
            [
                evidence.DecoderSamples[0] with
                {
                    BitDepth = 8,
                    TenBitPresentationVerified = false,
                    HdrPresentationVerified = false
                }
            ],
            SelectedResult = evidence.SelectedResult! with
            {
                BitDepth = 8,
                TenBitPresentationVerified = false,
                HdrPresentationVerified = false
            }
        };

        ArgumentException error = Assert.Throws<ArgumentException>(() => BenchmarkEvidenceValidator.Validate(evidence));

        Assert.Contains("bit depth", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsInitialBitrateAboveMeasuredThroughput()
    {
        BenchmarkEvidence evidence = CreateEvidence() with
        {
            SelectedResult = CreateEvidence().SelectedResult! with { InitialBitrateMbps = 101 }
        };

        ArgumentException error = Assert.Throws<ArgumentException>(() => BenchmarkEvidenceValidator.Validate(evidence));

        Assert.Contains("throughput", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsSelectedNetworkMetricsThatDoNotMatchSamples()
    {
        BenchmarkEvidence evidence = CreateEvidence() with
        {
            SelectedResult = CreateEvidence().SelectedResult! with { RttMs = 99 }
        };

        ArgumentException error = Assert.Throws<ArgumentException>(() => BenchmarkEvidenceValidator.Validate(evidence));

        Assert.Contains("network", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AcceptsExactSupportedSchemaAndPossibleValues()
    {
        BenchmarkEvidence evidence = CreateEvidence();

        BenchmarkEvidenceValidator.Validate(evidence);
    }

    [Fact]
    public void RejectsUnsupportedFingerprintSchemaVersion()
    {
        BenchmarkEvidence evidence = CreateEvidence() with
        {
            Fingerprints = CreateEvidence().Fingerprints with
            {
                Network = CreateEvidence().Fingerprints.Network with { SchemaVersion = 4 }
            }
        };

        ArgumentException error = Assert.Throws<ArgumentException>(() => BenchmarkEvidenceValidator.Validate(evidence));

        Assert.Contains("schema", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsUndefinedTriggerValue()
    {
        BenchmarkEvidence evidence = CreateEvidence() with { Trigger = (BenchmarkTrigger)999 };

        ArgumentException error = Assert.Throws<ArgumentException>(() => BenchmarkEvidenceValidator.Validate(evidence));

        Assert.Contains("trigger", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsImpossibleNumericSampleValues()
    {
        BenchmarkEvidence evidence = CreateEvidence();
        evidence = evidence with
        {
            NetworkSamples = [evidence.NetworkSamples[0] with { RttMs = double.NaN }]
        };

        ArgumentException error = Assert.Throws<ArgumentException>(() => BenchmarkEvidenceValidator.Validate(evidence));

        Assert.Contains("rtt", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsImpossibleEnumLikeSampleValues()
    {
        BenchmarkEvidence evidence = CreateEvidence();
        evidence = evidence with
        {
            PowerSamples = [evidence.PowerSamples[0] with { ThermalState = "volcanic" }]
        };

        ArgumentException error = Assert.Throws<ArgumentException>(() => BenchmarkEvidenceValidator.Validate(evidence));

        Assert.Contains("thermal", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsSelectedResultThatDoesNotMatchCertifiedDecoderMode()
    {
        BenchmarkEvidence evidence = CreateEvidence();
        evidence = evidence with
        {
            SelectedResult = evidence.SelectedResult! with { Width = 3840 }
        };

        ArgumentException error = Assert.Throws<ArgumentException>(() => BenchmarkEvidenceValidator.Validate(evidence));

        Assert.Contains("decoder", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompletedEvidenceMustValidateBeforeBecomingPlanEvidence()
    {
        BenchmarkEvidence evidence = CreateEvidence() with { Trigger = (BenchmarkTrigger)999 };

        Assert.Throws<ArgumentException>(() => evidence.ToPlanEvidence("auto"));
    }

    [Fact]
    public void PlanEvidenceRescoresStoredRawSamplesForCurrentServerPolicy()
    {
        BenchmarkEvidence evidence = CreateEvidence();
        DecoderBenchmarkSample h264 = evidence.DecoderSamples[0] with
        {
            Codec = "h264",
            Profile = "high",
            BitDepth = 8,
            TenBitPresentationVerified = false,
            HdrPresentationVerified = false
        };
        evidence = evidence with { DecoderSamples = [.. evidence.DecoderSamples, h264] };

        BenchmarkPlanEvidence planEvidence = evidence.ToPlanEvidence("h264");

        Assert.Equal("h264", planEvidence.SelectedResult.Codec);
        Assert.NotEqual(evidence.Revision, planEvidence.Revision);
        Assert.Contains(
            planEvidence.SelectedResult.Reasons,
            reason => reason.Contains("profile", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PendingEvidenceCannotContainUncoveredSamples()
    {
        BenchmarkEvidence evidence = CreateEvidence() with
        {
            CompletedAt = null,
            SelectedResult = null,
            NetworkCoverage = null
        };

        ArgumentException error = Assert.Throws<ArgumentException>(() => BenchmarkEvidenceValidator.Validate(evidence));

        Assert.Contains("pending", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static BenchmarkEvidence CreateEvidence()
    {
        NetworkBenchmarkSample network = new(1, 1200, 8, 1, true, 100, 0);
        DecoderBenchmarkSample decoder = new(
            "hevc",
            "main10",
            10,
            2560,
            1600,
            60,
            true,
            60,
            8,
            12,
            0,
            0,
            TenBitPresentationVerified: true,
            HdrPresentationVerified: true);
        SelectedBenchmarkResult selected = BenchmarkScorer.Select(new(
            NetworkSamples: [network],
            DecoderSamples: [decoder],
            PowerSamples: [new(80, false, "nominal")],
            NetworkCoverage: new(1, 1)));

        return new BenchmarkEvidence(
            RunId: Guid.Parse("a7f0a3d4-f144-49fb-8f58-a3892f15da6d"),
            ClientId: new ClientId("z-fold-7"),
            Trigger: BenchmarkTrigger.Automatic,
            Fingerprints: new BenchmarkFingerprintSet(
                BenchmarkFingerprintTests.CreateNetworkFingerprint(),
                BenchmarkFingerprintTests.CreateHardwareFingerprint()),
            StartedAt: new DateTimeOffset(2026, 7, 12, 11, 55, 0, TimeSpan.Zero),
            CompletedAt: new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero),
            NetworkSamples: [network],
            DecoderSamples: [decoder],
            PowerSamples: [new(80, false, "nominal")],
            SelectedResult: selected,
            NetworkCoverage: new(1, 1));
    }
}

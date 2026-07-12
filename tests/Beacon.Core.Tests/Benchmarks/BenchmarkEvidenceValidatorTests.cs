using Beacon.Core.Benchmarks;
using Beacon.Core.Clients;

namespace Beacon.Core.Tests.Benchmarks;

public sealed class BenchmarkEvidenceValidatorTests
{
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

        Assert.Throws<ArgumentException>(() => evidence.ToPlanEvidence());
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

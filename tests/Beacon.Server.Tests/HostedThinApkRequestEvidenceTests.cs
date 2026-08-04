using Beacon.Server.TestHost;

namespace Beacon.Server.Tests;

public sealed class HostedThinApkRequestEvidenceTests
{
    [Fact]
    public void SnapshotPreservesOrderedSanitizedRequestFacts()
    {
        var evidence = new HostedThinApkRequestEvidence();

        evidence.Record("POST", "/clients/hello", 200);
        evidence.Record("POST", "/clients/z-fold-7/beacon", 200);

        Assert.Equal(
            [
                new HostedThinApkRequestEvent(1, "POST", "/clients/hello", 200),
                new HostedThinApkRequestEvent(2, "POST", "/clients/z-fold-7/beacon", 200),
            ],
            evidence.Snapshot());
    }
}

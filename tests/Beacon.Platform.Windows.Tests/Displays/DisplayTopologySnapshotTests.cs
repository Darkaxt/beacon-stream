using Beacon.Platform.Windows.Displays;

namespace Beacon.Platform.Windows.Tests.Displays;

public sealed class DisplayTopologySnapshotTests
{
    [Fact]
    public void FromPathsDetectsMirrorModeWhenPhysicalAndVirtualDisplaysShareBounds()
    {
        DisplayTopologySnapshot topology = DisplayTopologySnapshot.FromPaths(
            [
                new DisplayPathSnapshot(
                    "physical-laptop-panel",
                    DisplayPathKind.Physical,
                    2560,
                    1600,
                    120,
                    IsPrimary: true,
                    X: 0,
                    Y: 0),
                new DisplayPathSnapshot(
                    "client-z-fold-7",
                    DisplayPathKind.Virtual,
                    2560,
                    1600,
                    120,
                    IsPrimary: false,
                    X: 0,
                    Y: 0)
            ]);

        Assert.True(topology.IsMirrorMode);
    }

    [Theory]
    [InlineData("SudoMaker Virtual Display Adapter", "ROOT\\SUDOMAKER\\SUDOVDA", DisplayPathKind.Virtual)]
    [InlineData("Generic PnP Monitor", "MONITOR\\BOE0BCA", DisplayPathKind.Physical)]
    public void ClassifyDisplayKindUsesVirtualDriverEvidence(
        string deviceString,
        string deviceId,
        DisplayPathKind expectedKind)
    {
        Assert.Equal(expectedKind, WindowsDisplayApi.ClassifyDisplayKind(deviceString, deviceId));
    }
}

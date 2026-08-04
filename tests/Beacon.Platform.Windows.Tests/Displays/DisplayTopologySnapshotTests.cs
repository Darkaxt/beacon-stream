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

    [Fact]
    public void PhysicalPrimaryVerifiedRequiresPhysicalDisplayAtPrimary()
    {
        DisplayTopologySnapshot topology = DisplayTopologySnapshot.Extended(
            physicalDisplayId: "physical-laptop-panel",
            virtualDisplayId: "client-z-fold-7",
            width: 2560,
            height: 1600,
            refreshHz: 120,
            virtualPrimary: true);

        Assert.False(topology.PhysicalPrimaryVerified);
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

    [Fact]
    public void BuildSudoVdaControlCodeMatchesDriverContract()
    {
        Assert.Equal(0x00222000u, WindowsDisplayApi.BuildSudoVdaControlCode(0x800));
        Assert.Equal(0x00222004u, WindowsDisplayApi.BuildSudoVdaControlCode(0x801));
        Assert.Equal(0x0022200Cu, WindowsDisplayApi.BuildSudoVdaControlCode(0x803));
        Assert.Equal(0x00222220u, WindowsDisplayApi.BuildSudoVdaControlCode(0x888));
        Assert.Equal(0x002223FCu, WindowsDisplayApi.BuildSudoVdaControlCode(0x8FF));
    }

    [Fact]
    public void CreateDeterministicDisplayGuidIsStablePerDisplayId()
    {
        Guid first = WindowsDisplayApi.CreateDeterministicDisplayGuid("client-z-fold-7");
        Guid second = WindowsDisplayApi.CreateDeterministicDisplayGuid("client-z-fold-7");
        Guid other = WindowsDisplayApi.CreateDeterministicDisplayGuid("client-other");

        Assert.Equal(first, second);
        Assert.NotEqual(first, other);
    }

    [Fact]
    public void SelectAddedDisplayNameReturnsSingleNewDisplayName()
    {
        string? added = WindowsDisplayApi.SelectAddedDisplayName(
            before: [@"\\.\DISPLAY5"],
            after: [@"\\.\DISPLAY5", @"\\.\DISPLAY7"]);

        Assert.Equal(@"\\.\DISPLAY7", added);
    }

    [Fact]
    public void SelectSingleVirtualDisplayNameReturnsInactiveSudoVdaDisplayName()
    {
        string? selected = WindowsDisplayApi.SelectSingleVirtualDisplayName(
            [
                new DisplayPathSnapshot(@"\\.\DISPLAY5", DisplayPathKind.Physical, 0, 0, 0, IsPrimary: true),
                new DisplayPathSnapshot(@"\\.\DISPLAY9", DisplayPathKind.Virtual, 0, 0, 0, IsPrimary: false)
            ]);

        Assert.Equal(@"\\.\DISPLAY9", selected);
    }
}

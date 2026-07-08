using Beacon.DisplayProbe;
using Beacon.Core.Displays;
using Beacon.Platform.Windows.Displays;

namespace Beacon.DisplayProbe.Tests;

public sealed class DisplayProbeFormatterTests
{
    [Fact]
    public void FormatStatusIncludesDriverAndDisplayTopology()
    {
        DisplayTopologySnapshot topology = DisplayTopologySnapshot.Extended(
            physicalDisplayId: @"\\.\DISPLAY1",
            virtualDisplayId: "client-z-fold-7",
            width: 2560,
            height: 1600,
            refreshHz: 120,
            virtualPrimary: true);
        var driverStatus = new DisplayDriverStatus(Ready: false, Diagnostic: "SudoVDA not ready");

        string output = DisplayProbeFormatter.FormatStatus(driverStatus, topology);

        Assert.Contains("SudoVDA not ready", output);
        Assert.Contains(@"\\.\DISPLAY1", output);
        Assert.Contains("client-z-fold-7", output);
        Assert.Contains("2560x1600@120", output);
        Assert.Contains("primary=True", output);
    }

    [Fact]
    public void FormatRestoreResultReportsVerifiedSuccess()
    {
        string output = DisplayProbeFormatter.FormatRestoreResult(DisplayRestoreResult.Ok());

        Assert.Equal("restore-physical: success verified=True", output);
    }

    [Fact]
    public void FormatRestoreResultReportsVerificationFailure()
    {
        string output = DisplayProbeFormatter.FormatRestoreResult(
            DisplayRestoreResult.Fail("physical primary was not verified"));

        Assert.Contains("restore-physical: failed", output);
        Assert.Contains("physical primary was not verified", output);
    }
}

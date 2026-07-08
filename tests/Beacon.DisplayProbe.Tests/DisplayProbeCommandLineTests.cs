using Beacon.DisplayProbe;

namespace Beacon.DisplayProbe.Tests;

public sealed class DisplayProbeCommandLineTests
{
    [Fact]
    public void ParseEnsureCommandPreservesSixteenByTenAndRefresh()
    {
        DisplayProbeCommand command = DisplayProbeCommandLine.Parse(
            [
                "ensure",
                "--client",
                "z-fold-7",
                "--width",
                "2560",
                "--height",
                "1600",
                "--refresh",
                "120",
                "--hdr",
                "prefer"
            ]);

        var ensure = Assert.IsType<EnsureDisplayProbeCommand>(command);
        Assert.Equal("z-fold-7", ensure.ClientId);
        Assert.Equal(2560, ensure.Width);
        Assert.Equal(1600, ensure.Height);
        Assert.Equal(120, ensure.RefreshHz);
        Assert.Equal("prefer", ensure.Hdr);
    }

    [Fact]
    public void ParsePrepareCommandPreservesSixteenByTenAndRefresh()
    {
        DisplayProbeCommand command = DisplayProbeCommandLine.Parse(
            [
                "prepare",
                "--client",
                "z-fold-7",
                "--width",
                "2560",
                "--height",
                "1600",
                "--refresh",
                "120",
                "--hdr",
                "prefer"
            ]);

        var prepare = Assert.IsType<PrepareDisplayProbeCommand>(command);
        Assert.Equal("z-fold-7", prepare.ClientId);
        Assert.Equal(2560, prepare.Width);
        Assert.Equal(1600, prepare.Height);
        Assert.Equal(120, prepare.RefreshHz);
        Assert.Equal("prefer", prepare.Hdr);
    }

    [Fact]
    public void ParseRestorePhysicalCommandHasNoClientRequirement()
    {
        DisplayProbeCommand command = DisplayProbeCommandLine.Parse(["restore-physical"]);

        Assert.IsType<RestorePhysicalDisplayProbeCommand>(command);
    }
}

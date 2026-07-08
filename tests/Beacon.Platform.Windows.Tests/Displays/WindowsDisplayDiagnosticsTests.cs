using Beacon.Platform.Windows.Displays;

namespace Beacon.Platform.Windows.Tests.Displays;

public sealed class WindowsDisplayDiagnosticsTests
{
    [Fact]
    public void AdvancedColorInfo2ReportsHdrActiveOnlyWhenSupportedUserEnabledAndActiveModeHdr()
    {
        DisplayHdrCapability capability = WindowsDisplayDiagnostics.FromAdvancedColorInfo2(
            WindowsDisplayDiagnostics.HighDynamicRangeSupported |
            WindowsDisplayDiagnostics.HighDynamicRangeUserEnabled,
            bitsPerColorChannel: 10,
            activeColorMode: WindowsDisplayDiagnostics.AdvancedColorModeHdr);

        Assert.True(capability.Supported);
        Assert.True(capability.Enabled);
        Assert.Contains("HDR active", capability.Reason);
        Assert.Contains("10", capability.Reason);
    }

    [Fact]
    public void AdvancedColorInfo2ReportsSupportedButDisabledWhenHdrIsNotUserEnabled()
    {
        DisplayHdrCapability capability = WindowsDisplayDiagnostics.FromAdvancedColorInfo2(
            WindowsDisplayDiagnostics.HighDynamicRangeSupported,
            bitsPerColorChannel: 8,
            activeColorMode: 0);

        Assert.True(capability.Supported);
        Assert.False(capability.Enabled);
        Assert.Contains("not user-enabled", capability.Reason);
    }

    [Fact]
    public void AdvancedColorInfo2ReportsUnsupportedWhenHdrSupportFlagIsMissing()
    {
        DisplayHdrCapability capability = WindowsDisplayDiagnostics.FromAdvancedColorInfo2(
            WindowsDisplayDiagnostics.AdvancedColorSupported,
            bitsPerColorChannel: 8,
            activeColorMode: 0);

        Assert.False(capability.Supported);
        Assert.False(capability.Enabled);
        Assert.Contains("unsupported", capability.Reason);
    }

    [Fact]
    public void LegacyAdvancedColorInfoReportsEnabledWithCaveat()
    {
        DisplayHdrCapability capability = WindowsDisplayDiagnostics.FromLegacyAdvancedColorInfo(
            WindowsDisplayDiagnostics.AdvancedColorSupported |
            WindowsDisplayDiagnostics.AdvancedColorEnabled,
            bitsPerColorChannel: 10);

        Assert.True(capability.Supported);
        Assert.True(capability.Enabled);
        Assert.Contains("HDR and WCG are not distinguished", capability.Reason);
    }

    [Fact]
    public void VerifyPhysicalRestoreFailsWhenApplySucceedsButPhysicalPrimaryIsMissing()
    {
        DisplayTopologySnapshot topology = DisplayTopologySnapshot.Extended(
            physicalDisplayId: "physical-laptop-panel",
            virtualDisplayId: "client-z-fold-7",
            width: 2560,
            height: 1600,
            refreshHz: 120,
            virtualPrimary: true);

        DisplayApiResult result = WindowsDisplayDiagnostics.VerifyPhysicalRestore(
            "physical-laptop-panel",
            topology);

        Assert.False(result.Success);
        Assert.Contains("physical-laptop-panel", result.Error ?? string.Empty);
        Assert.Contains("not verified", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("client-z-fold-7", result.Error ?? string.Empty);
    }

    [Fact]
    public void VerifyPhysicalRestoreSucceedsWhenPhysicalPrimaryIsPresent()
    {
        DisplayTopologySnapshot topology = DisplayTopologySnapshot.PhysicalOnly(
            physicalDisplayId: "physical-laptop-panel",
            width: 2560,
            height: 1600,
            refreshHz: 120);

        DisplayApiResult result = WindowsDisplayDiagnostics.VerifyPhysicalRestore(
            "physical-laptop-panel",
            topology);

        Assert.True(result.Success, result.Error);
    }
}

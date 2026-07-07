using Beacon.Core.Displays;
using Beacon.Platform.Windows.Displays;

namespace Beacon.Platform.Windows.Tests.Displays;

public sealed class WindowsDisplayBackendTests
{
    [Fact]
    public async Task EnsureVirtualDisplayAsync_WhenDriverIsUnavailable_ReturnsDiagnosticAndDoesNotChangeTopology()
    {
        var api = new FakeWindowsDisplayApi
        {
            DriverReady = false,
            DriverDiagnostic = "SudoVDA driver is not installed."
        };
        var backend = new WindowsDisplayBackend(api);

        DisplayEnsureResult result = await backend.EnsureVirtualDisplayAsync(
            "client-z-fold-7",
            2560,
            1600,
            120,
            HdrPreference.Prefer,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("SudoVDA driver is not installed", result.Error ?? string.Empty);
        Assert.Empty(api.CreatedDisplays);
        Assert.Empty(api.PrimaryRequests);
    }

    [Fact]
    public async Task EnsureVirtualDisplayAsync_CreatesDisplayAndRejectsMirrorTopology()
    {
        var api = new FakeWindowsDisplayApi
        {
            AfterCreateTopology = DisplayTopologySnapshot.Mirrored(
                physicalDisplayId: "physical-laptop-panel",
                virtualDisplayId: "client-z-fold-7",
                width: 2560,
                height: 1600,
                refreshHz: 120)
        };
        var backend = new WindowsDisplayBackend(api);

        DisplayEnsureResult result = await backend.EnsureVirtualDisplayAsync(
            "client-z-fold-7",
            2560,
            1600,
            120,
            HdrPreference.Prefer,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("mirror mode", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(("client-z-fold-7", 2560, 1600, 120), Assert.Single(api.CreatedDisplays));
        Assert.Empty(api.PrimaryRequests);
    }

    [Fact]
    public async Task EnsureVirtualDisplayAsync_VerifiesExactModeAndMakesVirtualDisplayPrimary()
    {
        var api = FakeWindowsDisplayApi.ReadyWithGoodTopology();
        var backend = new WindowsDisplayBackend(api);

        DisplayEnsureResult result = await backend.EnsureVirtualDisplayAsync(
            "client-z-fold-7",
            2560,
            1600,
            120,
            HdrPreference.Prefer,
            CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal(("client-z-fold-7", 2560, 1600, 120), Assert.Single(api.CreatedDisplays));
        Assert.Equal("client-z-fold-7", Assert.Single(api.PrimaryRequests));
    }

    [Fact]
    public async Task EnsureVirtualDisplayAsync_WhenHdrPreferredAndDriverReportsSdr_ReturnsSdrWithReason()
    {
        var api = FakeWindowsDisplayApi.ReadyWithGoodTopology();
        api.HdrCapability = new DisplayHdrCapability(
            Supported: false,
            Enabled: false,
            Reason: "Windows Advanced Color reports SDR only.");
        var backend = new WindowsDisplayBackend(api);

        DisplayEnsureResult result = await backend.EnsureVirtualDisplayAsync(
            "client-z-fold-7",
            2560,
            1600,
            120,
            HdrPreference.Prefer,
            CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.False(result.HdrEnabled);
        Assert.Equal("Windows Advanced Color reports SDR only.", result.HdrReason);
    }

    [Fact]
    public async Task EnsureVirtualDisplayAsync_WhenHdrRequiredAndDriverReportsSdr_FailsBeforeLaunch()
    {
        var api = FakeWindowsDisplayApi.ReadyWithGoodTopology();
        api.HdrCapability = new DisplayHdrCapability(
            Supported: false,
            Enabled: false,
            Reason: "virtual display exposes no HDR metadata.");
        var backend = new WindowsDisplayBackend(api);

        DisplayEnsureResult result = await backend.EnsureVirtualDisplayAsync(
            "client-z-fold-7",
            2560,
            1600,
            120,
            HdrPreference.Require,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("HDR required", result.Error ?? string.Empty);
        Assert.Contains("virtual display exposes no HDR metadata", result.Error ?? string.Empty);
    }
}

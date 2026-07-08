using Beacon.Core.Displays;
using Beacon.Platform.Windows.Displays;

namespace Beacon.Platform.Windows.Tests.Displays;

public sealed class WindowsDisplayBackendTests
{
    [Fact]
    public async Task GetHealthAsyncReportsDriverAndTopologyWithoutMutatingDisplays()
    {
        var api = new FakeWindowsDisplayApi
        {
            CurrentTopology = DisplayTopologySnapshot.Extended(
                physicalDisplayId: "physical-laptop-panel",
                virtualDisplayId: "client-z-fold-7",
                width: 2560,
                height: 1600,
                refreshHz: 120,
                virtualPrimary: false)
        };
        var backend = new WindowsDisplayBackend(api);

        DisplayHealth health = await backend.GetHealthAsync(CancellationToken.None);

        Assert.True(health.DriverReady);
        Assert.Equal("SudoVDA driver is ready.", health.Diagnostic);
        Assert.True(health.TopologyAvailable);
        Assert.False(health.MirrorMode);
        Assert.True(health.PhysicalPrimaryVerified);
        Assert.Equal(2, health.Paths.Count);
        Assert.Contains(health.Paths, path => path.DisplayId == "physical-laptop-panel" && path.Kind == "Physical" && path.IsPrimary);
        Assert.Contains(health.Paths, path => path.DisplayId == "client-z-fold-7" && path.Kind == "Virtual" && !path.IsPrimary);
        Assert.Empty(api.CreatedDisplays);
        Assert.Empty(api.PrimaryRequests);
        Assert.Empty(api.RestoreRequests);
        Assert.Empty(api.RemovedDisplays);
    }

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
    public async Task PrepareVirtualDisplayAsync_CreatesDisplayWithoutPrimaryRequest()
    {
        var api = FakeWindowsDisplayApi.ReadyWithGoodTopology();
        var backend = new WindowsDisplayBackend(api);

        DisplayEnsureResult result = await backend.PrepareVirtualDisplayAsync(
            "client-z-fold-7",
            2560,
            1600,
            120,
            HdrPreference.Prefer,
            CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal(("client-z-fold-7", 2560, 1600, 120), Assert.Single(api.CreatedDisplays));
        Assert.Empty(api.PrimaryRequests);
        Assert.Empty(api.RestoreRequests);
    }

    [Fact]
    public async Task PrepareVirtualDisplayAsync_WhenCreateStealsPrimary_RestoresPhysicalPrimaryAndKeepsDisplay()
    {
        var api = new FakeWindowsDisplayApi
        {
            AfterCreateTopology = DisplayTopologySnapshot.Extended(
                physicalDisplayId: "physical-laptop-panel",
                virtualDisplayId: "client-z-fold-7",
                width: 2560,
                height: 1600,
                refreshHz: 120,
                virtualPrimary: true),
            AfterRestoreTopology = DisplayTopologySnapshot.Extended(
                physicalDisplayId: "physical-laptop-panel",
                virtualDisplayId: "client-z-fold-7",
                width: 2560,
                height: 1600,
                refreshHz: 120,
                virtualPrimary: false)
        };
        var backend = new WindowsDisplayBackend(api);

        DisplayEnsureResult result = await backend.PrepareVirtualDisplayAsync(
            "client-z-fold-7",
            2560,
            1600,
            120,
            HdrPreference.Prefer,
            CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal(("client-z-fold-7", 2560, 1600, 120), Assert.Single(api.CreatedDisplays));
        Assert.Empty(api.PrimaryRequests);
        Assert.Equal("physical-primary", Assert.Single(api.RestoreRequests));
        Assert.Empty(api.RemovedDisplays);
    }

    [Fact]
    public async Task PrepareVirtualDisplayAsync_WritesTopologyDecisionLog()
    {
        var api = FakeWindowsDisplayApi.ReadyWithGoodTopology();
        var backend = new WindowsDisplayBackend(api);

        DisplayEnsureResult result = await backend.PrepareVirtualDisplayAsync(
            "client-z-fold-7",
            2560,
            1600,
            120,
            HdrPreference.Prefer,
            CancellationToken.None);

        Assert.True(result.Success, result.Error);
        DisplayOperationLogEntry entry = Assert.Single(backend.OperationLog);
        Assert.Equal("prepare-virtual-display", entry.Operation);
        Assert.Equal("client-z-fold-7", entry.DisplayId);
        Assert.Equal(2560, entry.Width);
        Assert.Equal(1600, entry.Height);
        Assert.Equal(120, entry.RefreshHz);
        Assert.False(entry.Primary);
        Assert.False(entry.HdrEnabled);
        Assert.Contains("prepared", entry.Reason);
        Assert.NotNull(entry.Before);
        Assert.NotNull(entry.After);
        Assert.False(entry.After.IsPrimary("client-z-fold-7"));
        Assert.True(entry.After.PhysicalPrimaryVerified);
    }

    [Fact]
    public async Task EnsureVirtualDisplayAsync_WritesTopologyDecisionLog()
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
        DisplayOperationLogEntry entry = Assert.Single(backend.OperationLog);
        Assert.Equal("ensure-virtual-display", entry.Operation);
        Assert.Equal("client-z-fold-7", entry.DisplayId);
        Assert.Equal(2560, entry.Width);
        Assert.Equal(1600, entry.Height);
        Assert.Equal(120, entry.RefreshHz);
        Assert.False(entry.HdrEnabled);
        Assert.Contains("virtual-primary", entry.Reason);
        Assert.NotNull(entry.Before);
        Assert.NotNull(entry.After);
        Assert.True(entry.After.IsPrimary("client-z-fold-7"));
    }

    [Fact]
    public async Task EnsureVirtualDisplayAsync_WhenPrimaryApplyFails_RemovesCreatedDisplay()
    {
        var api = FakeWindowsDisplayApi.ReadyWithGoodTopology();
        api.PrimaryResult = DisplayApiResult.Fail("primary apply not available");
        var backend = new WindowsDisplayBackend(api);

        DisplayEnsureResult result = await backend.EnsureVirtualDisplayAsync(
            "client-z-fold-7",
            2560,
            1600,
            120,
            HdrPreference.Prefer,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("primary apply not available", result.Error ?? string.Empty);
        Assert.Equal("client-z-fold-7", Assert.Single(api.RemovedDisplays));
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

    [Fact]
    public async Task RestorePhysicalPrimaryAsync_WhenFirstTopologyIsStale_ReappliesUntilVerified()
    {
        var api = FakeWindowsDisplayApi.ReadyWithGoodTopology();
        api.RestoreTopologies.Enqueue(DisplayTopologySnapshot.Extended(
            physicalDisplayId: "physical-laptop-panel",
            virtualDisplayId: "client-z-fold-7",
            width: 2560,
            height: 1600,
            refreshHz: 120,
            virtualPrimary: true));
        api.RestoreTopologies.Enqueue(DisplayTopologySnapshot.PhysicalOnly(
            physicalDisplayId: "physical-laptop-panel",
            width: 2560,
            height: 1600,
            refreshHz: 120));
        var backend = new WindowsDisplayBackend(api);

        DisplayRestoreResult result = await backend.RestorePhysicalPrimaryAsync(CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal(2, api.RestoreRequests.Count);
    }

    [Fact]
    public async Task RestorePhysicalPrimaryAsync_WhenTopologyRepeatsWithoutPhysicalPrimary_FailsWithDiagnostic()
    {
        var api = FakeWindowsDisplayApi.ReadyWithGoodTopology();
        api.RestoreTopologies.Enqueue(DisplayTopologySnapshot.Extended(
            physicalDisplayId: "physical-laptop-panel",
            virtualDisplayId: "client-z-fold-7",
            width: 2560,
            height: 1600,
            refreshHz: 120,
            virtualPrimary: true));
        api.RestoreTopologies.Enqueue(DisplayTopologySnapshot.Extended(
            physicalDisplayId: "physical-laptop-panel",
            virtualDisplayId: "client-z-fold-7",
            width: 2560,
            height: 1600,
            refreshHz: 120,
            virtualPrimary: true));
        var backend = new WindowsDisplayBackend(api);

        DisplayRestoreResult result = await backend.RestorePhysicalPrimaryAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("not verified", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("physical-laptop-panel", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, api.RestoreRequests.Count);
        DisplayOperationLogEntry entry = Assert.Single(backend.OperationLog);
        Assert.False(entry.Primary);
        Assert.Contains("restore-verification-failed", entry.Reason);
    }

    [Fact]
    public async Task RestorePhysicalPrimaryAsync_WritesTopologyDecisionLog()
    {
        var api = FakeWindowsDisplayApi.ReadyWithGoodTopology();
        api.RestoreTopologies.Enqueue(DisplayTopologySnapshot.PhysicalOnly(
            physicalDisplayId: "physical-laptop-panel",
            width: 2560,
            height: 1600,
            refreshHz: 120));
        var backend = new WindowsDisplayBackend(api);

        DisplayRestoreResult result = await backend.RestorePhysicalPrimaryAsync(CancellationToken.None);

        Assert.True(result.Success, result.Error);
        DisplayOperationLogEntry entry = Assert.Single(backend.OperationLog);
        Assert.Equal("restore-physical-primary", entry.Operation);
        Assert.Equal("physical", entry.DisplayId);
        Assert.True(entry.Primary);
        Assert.Contains("verified", entry.Reason);
        Assert.NotNull(entry.After);
        Assert.True(entry.After.PhysicalPrimaryVerified);
    }
}

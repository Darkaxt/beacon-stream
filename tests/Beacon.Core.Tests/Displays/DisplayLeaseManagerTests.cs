using Beacon.Core.Clients;
using Beacon.Core.Displays;

namespace Beacon.Core.Tests.Displays;

public sealed class DisplayLeaseManagerTests
{
    [Fact]
    public void DisplayIdIsClientScopedAndStable()
    {
        Assert.Equal("client-z-fold-7", DisplayLease.CreateDisplayId(new ClientId("z-fold-7")));
    }

    [Fact]
    public async Task PreflightCreatesClientScopedLeaseBeforeLaunch()
    {
        var backend = new FakeDisplayBackend();
        var manager = new DisplayLeaseManager(backend);

        DisplayLeaseResult result = await manager.EnsureLeaseAsync(ClientProfile.CreateZFold7Default(), CancellationToken.None);

        Assert.True(result.Success);
        DisplayLease lease = Assert.IsType<DisplayLease>(result.Lease);
        Assert.Equal("client-z-fold-7", lease.DisplayId);
        Assert.Equal("client-z-fold-7:2560x1600@120:hdr=Prefer", Assert.Single(backend.EnsureCalls));
    }

    [Fact]
    public async Task DisconnectDoesNotTearDownLease()
    {
        var backend = new FakeDisplayBackend();
        var manager = new DisplayLeaseManager(backend);

        await manager.DisconnectAsync("client-z-fold-7", CancellationToken.None);

        Assert.Empty(backend.RemoveCalls);
    }

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public async Task CleanupKeepsLeaseUntilClientInactiveAndNoOwnedWorkRemains(
        bool clientActive,
        bool ownedProcessRunning,
        bool ownedWindowRemaining,
        bool expectedRemoved)
    {
        var backend = new FakeDisplayBackend();
        var manager = new DisplayLeaseManager(backend);

        bool removed = await manager.CleanupIfAllowedAsync(
            "client-z-fold-7",
            clientActive,
            ownedProcessRunning,
            ownedWindowRemaining,
            CancellationToken.None);

        Assert.Equal(expectedRemoved, removed);
        if (expectedRemoved)
        {
            Assert.Equal("client-z-fold-7", Assert.Single(backend.RemoveCalls));
        }
        else
        {
            Assert.Empty(backend.RemoveCalls);
        }
    }

    [Fact]
    public async Task CleanupRestoresPhysicalPrimaryWhenClientInactiveButOwnedWindowRemains()
    {
        var backend = new FakeDisplayBackend();
        var manager = new DisplayLeaseManager(backend);

        bool removed = await manager.CleanupIfAllowedAsync(
            "client-z-fold-7",
            clientActive: false,
            ownedProcessRunning: false,
            ownedWindowRemaining: true,
            CancellationToken.None);

        Assert.False(removed);
        Assert.Empty(backend.RemoveCalls);
        Assert.Equal("physical-primary", Assert.Single(backend.RestoreCalls));
    }

    [Fact]
    public async Task CleanupRestoresPhysicalPrimaryBeforeRemovingLeaseWhenNoOwnedWorkRemains()
    {
        var backend = new FakeDisplayBackend();
        var manager = new DisplayLeaseManager(backend);

        bool removed = await manager.CleanupIfAllowedAsync(
            "client-z-fold-7",
            clientActive: false,
            ownedProcessRunning: false,
            ownedWindowRemaining: false,
            CancellationToken.None);

        Assert.True(removed);
        Assert.Equal("physical-primary", Assert.Single(backend.RestoreCalls));
        Assert.Equal("client-z-fold-7", Assert.Single(backend.RemoveCalls));
    }

    [Fact]
    public async Task RecoverDisplayAsyncManualOverrideRestoresPhysicalPrimaryAndRemovesVirtualDisplay()
    {
        var backend = new FakeDisplayBackend();
        var manager = new DisplayLeaseManager(backend);

        DisplayRecoveryResult result = await manager.RecoverDisplayAsync("client-z-fold-7", CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal("physical-primary", Assert.Single(backend.RestoreCalls));
        Assert.Equal("client-z-fold-7", Assert.Single(backend.RemoveCalls));
    }

    [Fact]
    public async Task RecoverDisplayAsyncWhenRestoreFailsDoesNotRemoveVirtualDisplay()
    {
        var backend = new FakeDisplayBackend
        {
            NextRestoreResult = DisplayRestoreResult.Fail("physical primary could not be verified")
        };
        var manager = new DisplayLeaseManager(backend);

        DisplayRecoveryResult result = await manager.RecoverDisplayAsync("client-z-fold-7", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("physical primary could not be verified", result.Error ?? string.Empty);
        Assert.Empty(backend.RemoveCalls);
    }

    [Fact]
    public async Task MissingVirtualDisplayFailsInsteadOfFallingBackToPhysicalDisplay()
    {
        var backend = new FakeDisplayBackend { AllowEnsure = false };
        var manager = new DisplayLeaseManager(backend);

        DisplayLeaseResult result = await manager.EnsureLeaseAsync(ClientProfile.CreateZFold7Default(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.Lease);
        Assert.Contains("refusing to fall back", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(backend.RestoreCalls);
        Assert.Empty(backend.RemoveCalls);
    }
}

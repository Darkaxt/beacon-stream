using Beacon.Platform.Windows.Displays;

namespace Beacon.Platform.Windows.Tests.Displays;

public sealed class WindowsDisplayLeaseTopologyReconcilerTests
{
    [Fact]
    public async Task MissingVirtualPathReappliesExtendedTopology()
    {
        int applyCount = 0;
        int queryCount = 0;
        var reconciler = new WindowsDisplayLeaseTopologyReconciler(
            () => queryCount++ == 0
                ? DisplayTopologySnapshot.PhysicalOnly(@"\\.\DISPLAY1", 2560, 1600, 240)
                : DisplayTopologySnapshot.Extended(
                    @"\\.\DISPLAY1",
                    "client-z-fold-7",
                    2560,
                    1600,
                    120,
                    virtualPrimary: false),
            () =>
            {
                applyCount++;
                return DisplayApiResult.Ok();
            });

        await reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Equal(1, applyCount);
        Assert.Contains("reactivated", reconciler.Diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingPhysicalPathReappliesAndVerifiesExtendedTopology()
    {
        int applyCount = 0;
        int queryCount = 0;
        var virtualOnly = new DisplayTopologySnapshot(
            [
                new DisplayPathSnapshot(
                    "client-z-fold-7",
                    DisplayPathKind.Virtual,
                    2560,
                    1600,
                    120,
                    IsPrimary: true)
            ],
            IsMirrorMode: false);
        var reconciler = new WindowsDisplayLeaseTopologyReconciler(
            () => queryCount++ == 0
                ? virtualOnly
                : DisplayTopologySnapshot.Extended(
                    @"\\.\DISPLAY1",
                    "client-z-fold-7",
                    2560,
                    1600,
                    120,
                    virtualPrimary: false),
            () =>
            {
                applyCount++;
                return DisplayApiResult.Ok();
            });

        await reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Equal(1, applyCount);
        Assert.Contains("reactivated", reconciler.Diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnverifiedExtendedTopologyRepairFailsHeartbeat()
    {
        var virtualOnly = new DisplayTopologySnapshot(
            [
                new DisplayPathSnapshot(
                    "client-z-fold-7",
                    DisplayPathKind.Virtual,
                    2560,
                    1600,
                    120,
                    IsPrimary: true)
            ],
            IsMirrorMode: false);
        var reconciler = new WindowsDisplayLeaseTopologyReconciler(
            () => virtualOnly,
            () => DisplayApiResult.Ok());

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reconciler.ReconcileAsync(CancellationToken.None).AsTask());

        Assert.Contains("not verified", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ActiveVirtualPathDoesNotChangeTopology()
    {
        int applyCount = 0;
        var reconciler = new WindowsDisplayLeaseTopologyReconciler(
            () => DisplayTopologySnapshot.Extended(
                @"\\.\DISPLAY1",
                "client-z-fold-7",
                2560,
                1600,
                120,
                virtualPrimary: true),
            () =>
            {
                applyCount++;
                return DisplayApiResult.Ok();
            });

        await reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Equal(0, applyCount);
    }

    [Fact]
    public async Task FailedExtendedTopologyApplyIsReportedToHeartbeatSession()
    {
        var reconciler = new WindowsDisplayLeaseTopologyReconciler(
            () => DisplayTopologySnapshot.PhysicalOnly(@"\\.\DISPLAY1", 2560, 1600, 240),
            () => DisplayApiResult.Fail("SetDisplayConfig Result=87"));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reconciler.ReconcileAsync(CancellationToken.None).AsTask());

        Assert.Contains("Result=87", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}

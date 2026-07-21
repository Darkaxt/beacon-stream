using Beacon.Platform.Windows.Displays;

namespace Beacon.Platform.Windows.Tests.Displays;

public sealed class WindowsDisplayLeaseTopologyReconcilerTests
{
    private static readonly LeasedDisplayTopologyRequirement RequiredLease = new(
        "client-z-fold-7",
        Width: 2560,
        Height: 1600,
        RefreshHz: 120);

    [Fact]
    public void VirtualOnlyRecoveryReappliesCompleteLeasedTopology()
    {
        var topology = new DisplayTopologySnapshot(
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

        Assert.Equal(
            LeasedDisplayRecoveryAction.ReactivateLeasedDisplays,
            WindowsDisplayLeaseRecoveryPlanner.Plan(topology, [RequiredLease]));
    }

    [Fact]
    public void PhysicalOnlyRecoveryWaitsForPhysicalBeforeRequestingLeasedPath()
    {
        Assert.Equal(
            LeasedDisplayRecoveryAction.ReactivateLeasedDisplays,
            WindowsDisplayLeaseRecoveryPlanner.Plan(
                DisplayTopologySnapshot.PhysicalOnly(@"\\.\DISPLAY1", 2560, 1600, 240),
                [RequiredLease]));
    }

    [Fact]
    public void ExactExtendedTopologyRequiresNoRecoveryTransition()
    {
        Assert.Equal(
            LeasedDisplayRecoveryAction.None,
            WindowsDisplayLeaseRecoveryPlanner.Plan(
                DisplayTopologySnapshot.Extended(
                    @"\\.\DISPLAY1",
                    "client-z-fold-7",
                    2560,
                    1600,
                    120,
                    virtualPrimary: true),
                [RequiredLease]));
    }

    [Fact]
    public async Task MissingVirtualPathReappliesExactLeasedTopology()
    {
        int applyCount = 0;
        int queryCount = 0;
        IReadOnlyList<LeasedDisplayTopologyRequirement>? applied = null;
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
            () => [RequiredLease],
            requirements =>
            {
                applyCount++;
                applied = requirements;
                return DisplayApiResult.Ok();
            });

        await reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Equal(1, applyCount);
        Assert.Equal([RequiredLease], applied);
        Assert.Contains("next heartbeat", reconciler.Diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingPhysicalPathReappliesAndVerifiesExtendedTopology()
    {
        int applyCount = 0;
        int queryCount = 0;
        var diagnostics = new List<string>();
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
            () => [RequiredLease],
            _ =>
            {
                applyCount++;
                return DisplayApiResult.Ok();
            },
            diagnostics.Add);

        await reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Equal(1, applyCount);
        Assert.Contains(
            diagnostics,
            message => message.Contains("requirements=1", StringComparison.Ordinal)
                && message.Contains("physical=False", StringComparison.Ordinal));
        Assert.Contains(
            diagnostics,
            message => message.Contains("transition-completed success=True", StringComparison.Ordinal));
        Assert.Contains("next heartbeat", reconciler.Diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TopologyTransitionIsVerifiedOnNextHeartbeat()
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
        int queryCount = 0;
        int applyCount = 0;
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
            () => [RequiredLease],
            _ =>
            {
                applyCount++;
                return DisplayApiResult.Ok();
            });

        await reconciler.ReconcileAsync(CancellationToken.None);
        Assert.Equal(1, queryCount);
        Assert.Equal(1, applyCount);
        Assert.Contains("next heartbeat", reconciler.Diagnostic, StringComparison.OrdinalIgnoreCase);

        await reconciler.ReconcileAsync(CancellationToken.None);
        Assert.Equal(2, queryCount);
        Assert.Equal(1, applyCount);
        Assert.Contains("active", reconciler.Diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnchangedTopologyKeepsTransitionGateOwnedWithoutReapplying()
    {
        int applyCount = 0;
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
            () => [RequiredLease],
            _ =>
            {
                applyCount++;
                return DisplayApiResult.Ok();
            });

        await reconciler.ReconcileAsync(CancellationToken.None);
        await reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Equal(1, applyCount);
        Assert.Contains("awaiting Windows topology change", reconciler.Diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChangedInvalidTopologyMustStabilizeBeforeAnotherTransition()
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
        DisplayTopologySnapshot physicalOnly =
            DisplayTopologySnapshot.PhysicalOnly(@"\\.\DISPLAY1", 2560, 1600, 240);
        var reconciler = new WindowsDisplayLeaseTopologyReconciler(
            () => queryCount++ == 0 ? virtualOnly : physicalOnly,
            () => [RequiredLease],
            _ =>
            {
                applyCount++;
                return DisplayApiResult.Ok();
            });

        await reconciler.ReconcileAsync(CancellationToken.None);
        await reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Equal(1, applyCount);
        Assert.Contains("stabilize", reconciler.Diagnostic, StringComparison.OrdinalIgnoreCase);

        await reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Equal(2, applyCount);
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
            () => [RequiredLease],
            _ =>
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
            () => [RequiredLease],
            _ => DisplayApiResult.Fail("SetDisplayConfig Result=87"));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reconciler.ReconcileAsync(CancellationToken.None).AsTask());

        Assert.Contains("Result=87", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WrongLeasedModeReappliesExactLeasedTopology()
    {
        int applyCount = 0;
        int queryCount = 0;
        var reconciler = new WindowsDisplayLeaseTopologyReconciler(
            () => queryCount++ == 0
                ? DisplayTopologySnapshot.Extended(
                    @"\\.\DISPLAY1",
                    "client-z-fold-7",
                    2560,
                    1440,
                    60,
                    virtualPrimary: false)
                : DisplayTopologySnapshot.Extended(
                    @"\\.\DISPLAY1",
                    "client-z-fold-7",
                    2560,
                    1600,
                    120,
                    virtualPrimary: false),
            () => [RequiredLease],
            _ =>
            {
                applyCount++;
                return DisplayApiResult.Ok();
            });

        await reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Equal(1, applyCount);
    }

    [Fact]
    public async Task UnrelatedVirtualPathDoesNotSatisfyLeasedTopology()
    {
        int applyCount = 0;
        int queryCount = 0;
        var reconciler = new WindowsDisplayLeaseTopologyReconciler(
            () => queryCount++ == 0
                ? DisplayTopologySnapshot.Extended(
                    @"\\.\DISPLAY1",
                    "client-other",
                    2560,
                    1600,
                    120,
                    virtualPrimary: false)
                : DisplayTopologySnapshot.Extended(
                    @"\\.\DISPLAY1",
                    "client-z-fold-7",
                    2560,
                    1600,
                    120,
                    virtualPrimary: false),
            () => [RequiredLease],
            _ =>
            {
                applyCount++;
                return DisplayApiResult.Ok();
            });

        await reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Equal(1, applyCount);
    }
}

using Beacon.Platform.Windows.Displays;

namespace Beacon.Platform.Windows.Tests.Displays;

public sealed class WindowsVirtualDisplayArrivalGateTests
{
    [Fact]
    public async Task RequiresTwoMatchingPostAddHeartbeatObservations()
    {
        var signal = new ManualHeartbeatRevisionSignal();
        var snapshots = new ObservedSnapshotSequence(
        [
            new(true, @"\\.\DISPLAY34", "physical-only"),
            new(true, @"\\.\DISPLAY34", "virtual-only"),
            new(true, @"\\.\DISPLAY34", "virtual-only")
        ]);
        var gate = new WindowsVirtualDisplayArrivalGate(
            () => signal.Revision,
            signal.WaitAsync);

        Task<VirtualDisplayTargetArrivalSnapshot> wait = gate.WaitForStableTargetAsync(
            snapshots.Query,
            CancellationToken.None);

        Assert.False(wait.IsCompleted);
        signal.Pulse();
        await snapshots.WaitForObservationAsync(0);
        Assert.False(wait.IsCompleted);
        signal.Pulse();
        await snapshots.WaitForObservationAsync(1);
        Assert.False(wait.IsCompleted);
        signal.Pulse();

        VirtualDisplayTargetArrivalSnapshot result = await wait;
        Assert.Equal(@"\\.\DISPLAY34", result.DisplayName);
        Assert.Equal("virtual-only", result.TopologyFingerprint);
    }

    [Fact]
    public async Task UnavailableTargetCannotOpenArrivalGate()
    {
        var signal = new ManualHeartbeatRevisionSignal();
        var snapshots = new ObservedSnapshotSequence(
        [
            new(false, null, "physical-only"),
            new(true, @"\\.\DISPLAY34", "physical-only"),
            new(true, @"\\.\DISPLAY34", "physical-only")
        ]);
        var gate = new WindowsVirtualDisplayArrivalGate(
            () => signal.Revision,
            signal.WaitAsync);
        Task<VirtualDisplayTargetArrivalSnapshot> wait = gate.WaitForStableTargetAsync(
            snapshots.Query,
            CancellationToken.None);

        signal.Pulse();
        await snapshots.WaitForObservationAsync(0);
        Assert.False(wait.IsCompleted);
        signal.Pulse();
        await snapshots.WaitForObservationAsync(1);
        Assert.False(wait.IsCompleted);
        signal.Pulse();

        Assert.True((await wait).Available);
    }

    [Fact]
    public async Task StableWrongTopologyIsReappliedBeforeGateOpens()
    {
        var signal = new ManualHeartbeatRevisionSignal();
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var snapshots = new ObservedSnapshotSequence(
        [
            new(true, @"\\.\DISPLAY34", "virtual-only", ExtendedTopology: false, DesiredTopology: false),
            new(true, @"\\.\DISPLAY34", "virtual-only", ExtendedTopology: false, DesiredTopology: false),
            new(true, @"\\.\DISPLAY34", "extended", ExtendedTopology: true, DesiredTopology: true),
            new(true, @"\\.\DISPLAY34", "extended", ExtendedTopology: true, DesiredTopology: true)
        ]);
        int applyCount = 0;
        var gate = new WindowsVirtualDisplayArrivalGate(
            () => signal.Revision,
            signal.WaitAsync);

        Task<DisplayApiResult> wait = gate.WaitForStableDesiredTopologyAsync(
            snapshots.Query,
            snapshot =>
            {
                Assert.False(snapshot.ExtendedTopology);
                applyCount++;
                applied.SetResult();
                return DisplayApiResult.Ok();
            },
            CancellationToken.None);

        signal.Pulse();
        await snapshots.WaitForObservationAsync(0);
        Assert.False(wait.IsCompleted);
        signal.Pulse();
        await applied.Task;
        Assert.Equal(1, applyCount);
        Assert.False(wait.IsCompleted);

        signal.Pulse();
        await snapshots.WaitForObservationAsync(2);
        Assert.False(wait.IsCompleted);
        signal.Pulse();

        Assert.True((await wait).Success);
        Assert.Equal(1, applyCount);
    }

    [Fact]
    public async Task StableDesiredTopologyOpensWithoutReapplying()
    {
        var signal = new ManualHeartbeatRevisionSignal();
        var snapshots = new ObservedSnapshotSequence(
        [
            new(true, @"\\.\DISPLAY34", "extended", ExtendedTopology: true, DesiredTopology: true),
            new(true, @"\\.\DISPLAY34", "extended", ExtendedTopology: true, DesiredTopology: true)
        ]);
        int applyCount = 0;
        var gate = new WindowsVirtualDisplayArrivalGate(
            () => signal.Revision,
            signal.WaitAsync);

        Task<DisplayApiResult> wait = gate.WaitForStableDesiredTopologyAsync(
            snapshots.Query,
            _ =>
            {
                applyCount++;
                return DisplayApiResult.Ok();
            },
            CancellationToken.None);

        signal.Pulse();
        await snapshots.WaitForObservationAsync(0);
        Assert.False(wait.IsCompleted);
        signal.Pulse();

        Assert.True((await wait).Success);
        Assert.Equal(0, applyCount);
    }

    [Fact]
    public async Task FailedTopologyReapplyClosesGateWithDiagnostic()
    {
        var signal = new ManualHeartbeatRevisionSignal();
        var snapshots = new ObservedSnapshotSequence(
        [
            new(true, @"\\.\DISPLAY34", "virtual-only", ExtendedTopology: false, DesiredTopology: false),
            new(true, @"\\.\DISPLAY34", "virtual-only", ExtendedTopology: false, DesiredTopology: false)
        ]);
        var gate = new WindowsVirtualDisplayArrivalGate(
            () => signal.Revision,
            signal.WaitAsync);

        Task<DisplayApiResult> wait = gate.WaitForStableDesiredTopologyAsync(
            snapshots.Query,
            _ => DisplayApiResult.Fail("SetDisplayConfig Result=87"),
            CancellationToken.None);

        signal.Pulse();
        await snapshots.WaitForObservationAsync(0);
        signal.Pulse();

        DisplayApiResult result = await wait;
        Assert.False(result.Success);
        Assert.Contains("Result=87", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExtendedTopologyGateSeparatesPathCompositionFromModeApply()
    {
        var signal = new ManualHeartbeatRevisionSignal();
        var pathsApplied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var snapshots = new ObservedSnapshotSequence(
        [
            new(true, @"\\.\DISPLAY34", "virtual-only", ExtendedTopology: false),
            new(true, @"\\.\DISPLAY34", "virtual-only", ExtendedTopology: false),
            new(true, @"\\.\DISPLAY34", "extended-default-mode", ExtendedTopology: true),
            new(true, @"\\.\DISPLAY34", "extended-default-mode", ExtendedTopology: true)
        ]);
        int applyCount = 0;
        var gate = new WindowsVirtualDisplayArrivalGate(
            () => signal.Revision,
            signal.WaitAsync);

        Task<DisplayApiResult> wait = gate.WaitForStableExtendedTopologyAsync(
            snapshots.Query,
            snapshot =>
            {
                Assert.False(snapshot.ExtendedTopology);
                applyCount++;
                pathsApplied.SetResult();
                return DisplayApiResult.Ok();
            },
            CancellationToken.None);

        signal.Pulse();
        await snapshots.WaitForObservationAsync(0);
        signal.Pulse();
        await pathsApplied.Task;
        Assert.False(wait.IsCompleted);

        signal.Pulse();
        await snapshots.WaitForObservationAsync(2);
        signal.Pulse();

        Assert.True((await wait).Success);
        Assert.Equal(1, applyCount);
    }

    private sealed class ObservedSnapshotSequence(
        IReadOnlyList<VirtualDisplayTargetArrivalSnapshot> snapshots)
    {
        private readonly TaskCompletionSource[] observed = snapshots
            .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        private int index;

        public VirtualDisplayTargetArrivalSnapshot Query()
        {
            int current = index++;
            observed[current].SetResult();
            return snapshots[current];
        }

        public Task WaitForObservationAsync(int observationIndex) =>
            observed[observationIndex].Task;
    }

    private sealed class ManualHeartbeatRevisionSignal
    {
        private readonly object gate = new();
        private TaskCompletionSource<long> changed = NewSignal();
        private long revision;

        public long Revision => Volatile.Read(ref revision);

        public Task<long> WaitAsync(long afterRevision, CancellationToken cancellationToken)
        {
            lock (gate)
            {
                if (revision > afterRevision)
                {
                    return Task.FromResult(revision);
                }

                return changed.Task.WaitAsync(cancellationToken);
            }
        }

        public void Pulse()
        {
            TaskCompletionSource<long> previous;
            long next;
            lock (gate)
            {
                next = checked(++revision);
                previous = changed;
                changed = NewSignal();
            }

            previous.TrySetResult(next);
        }

        private static TaskCompletionSource<long> NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

using Beacon.Platform.Windows.Displays;

namespace Beacon.Platform.Windows.Tests.Displays;

public sealed class WindowsVirtualDisplayArrivalGateTests
{
    [Fact]
    public async Task RequiresTwoMatchingPostAddHeartbeatObservations()
    {
        var signal = new ManualHeartbeatRevisionSignal();
        var snapshots = new Queue<VirtualDisplayTargetArrivalSnapshot>(
        [
            new(true, @"\\.\DISPLAY34", "physical-only"),
            new(true, @"\\.\DISPLAY34", "virtual-only"),
            new(true, @"\\.\DISPLAY34", "virtual-only")
        ]);
        var gate = new WindowsVirtualDisplayArrivalGate(
            () => signal.Revision,
            signal.WaitAsync);

        Task<VirtualDisplayTargetArrivalSnapshot> wait = gate.WaitForStableTargetAsync(
            () => snapshots.Dequeue(),
            CancellationToken.None);

        Assert.False(wait.IsCompleted);
        signal.Pulse();
        await Task.Yield();
        Assert.False(wait.IsCompleted);
        signal.Pulse();
        await Task.Yield();
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
        var snapshots = new Queue<VirtualDisplayTargetArrivalSnapshot>(
        [
            new(false, null, "physical-only"),
            new(true, @"\\.\DISPLAY34", "physical-only"),
            new(true, @"\\.\DISPLAY34", "physical-only")
        ]);
        var gate = new WindowsVirtualDisplayArrivalGate(
            () => signal.Revision,
            signal.WaitAsync);
        Task<VirtualDisplayTargetArrivalSnapshot> wait = gate.WaitForStableTargetAsync(
            () => snapshots.Dequeue(),
            CancellationToken.None);

        signal.Pulse();
        await Task.Yield();
        Assert.False(wait.IsCompleted);
        signal.Pulse();
        await Task.Yield();
        Assert.False(wait.IsCompleted);
        signal.Pulse();

        Assert.True((await wait).Available);
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

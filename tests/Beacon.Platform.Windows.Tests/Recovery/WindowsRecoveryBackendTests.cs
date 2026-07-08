using Beacon.Platform.Windows.Displays;
using Beacon.Platform.Windows.Recovery;
using Beacon.Platform.Windows.Sessions;
using Beacon.Platform.Windows.Tests.Displays;

namespace Beacon.Platform.Windows.Tests.Recovery;

public sealed class WindowsRecoveryBackendTests
{
    [Fact]
    public async Task MoveWindowsBackMovesVisibleVirtualWindowsToPhysicalDisplayAndMinimizes()
    {
        var displayApi = new FakeWindowsDisplayApi
        {
            CurrentTopology = CreateTopology()
        };
        var recoveryApi = new FakeWindowsRecoveryApi();
        recoveryApi.Windows.Add(new WindowsRecoveryWindow(
            new IntPtr(101),
            1001,
            "Game",
            new WindowsRectangle(0, 0, 1280, 720),
            IsVisible: true));
        recoveryApi.Windows.Add(new WindowsRecoveryWindow(
            new IntPtr(102),
            1002,
            "Physical",
            new WindowsRectangle(2560, 0, 800, 600),
            IsVisible: true));
        var backend = new WindowsRecoveryBackend(displayApi, recoveryApi);

        var result = await backend.MoveWindowsBackAsync(minimize: true, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, result.AffectedCount);
        Assert.Equal(new IntPtr(101), Assert.Single(recoveryApi.MovedWindows).Handle);
        Assert.Equal(new IntPtr(101), Assert.Single(recoveryApi.MinimizedWindows));
    }

    [Fact]
    public async Task CloseVirtualWindowsClosesOnlyVisibleVirtualWindows()
    {
        var displayApi = new FakeWindowsDisplayApi
        {
            CurrentTopology = CreateTopology()
        };
        var recoveryApi = new FakeWindowsRecoveryApi();
        recoveryApi.Windows.Add(new WindowsRecoveryWindow(
            new IntPtr(201),
            2001,
            "Game",
            new WindowsRectangle(0, 0, 1280, 720),
            IsVisible: true));
        recoveryApi.Windows.Add(new WindowsRecoveryWindow(
            new IntPtr(202),
            2002,
            "Hidden",
            new WindowsRectangle(0, 0, 1280, 720),
            IsVisible: false));
        var backend = new WindowsRecoveryBackend(displayApi, recoveryApi);

        var result = await backend.CloseVirtualWindowsAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, result.AffectedCount);
        Assert.Equal(new IntPtr(201), Assert.Single(recoveryApi.ClosedWindows));
    }

    [Fact]
    public async Task TerminateVirtualProcessesTerminatesDistinctNonBeaconProcesses()
    {
        var displayApi = new FakeWindowsDisplayApi
        {
            CurrentTopology = CreateTopology()
        };
        var recoveryApi = new FakeWindowsRecoveryApi { CurrentProcessId = 3000 };
        recoveryApi.Windows.Add(new WindowsRecoveryWindow(
            new IntPtr(301),
            3001,
            "Game",
            new WindowsRectangle(0, 0, 1280, 720),
            IsVisible: true));
        recoveryApi.Windows.Add(new WindowsRecoveryWindow(
            new IntPtr(302),
            3001,
            "Game child",
            new WindowsRectangle(0, 0, 400, 300),
            IsVisible: true));
        recoveryApi.Windows.Add(new WindowsRecoveryWindow(
            new IntPtr(303),
            3000,
            "Beacon",
            new WindowsRectangle(0, 0, 400, 300),
            IsVisible: true));
        var backend = new WindowsRecoveryBackend(displayApi, recoveryApi);

        var result = await backend.TerminateVirtualProcessesAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, result.AffectedCount);
        Assert.Equal([3001], recoveryApi.TerminatedProcessIds);
    }

    [Fact]
    public async Task MoveWindowsBackFailsWhenNoPhysicalDisplayExists()
    {
        var displayApi = new FakeWindowsDisplayApi
        {
            CurrentTopology = new DisplayTopologySnapshot(
                [
                    new DisplayPathSnapshot("client-z-fold-7", DisplayPathKind.Virtual, 2560, 1600, 120, IsPrimary: true)
                ],
                IsMirrorMode: false)
        };
        var backend = new WindowsRecoveryBackend(displayApi, new FakeWindowsRecoveryApi());

        var result = await backend.MoveWindowsBackAsync(minimize: true, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("No active physical display", result.Error, StringComparison.Ordinal);
    }

    private static DisplayTopologySnapshot CreateTopology() =>
        new(
            [
                new DisplayPathSnapshot(
                    "client-z-fold-7",
                    DisplayPathKind.Virtual,
                    2560,
                    1600,
                    120,
                    IsPrimary: true,
                    X: 0,
                    Y: 0),
                new DisplayPathSnapshot(
                    "physical-laptop-panel",
                    DisplayPathKind.Physical,
                    2560,
                    1600,
                    120,
                    IsPrimary: false,
                    X: 2560,
                    Y: 0)
            ],
            IsMirrorMode: false);

    private sealed class FakeWindowsRecoveryApi : IWindowsRecoveryApi
    {
        public int CurrentProcessId { get; set; } = 9999;

        public List<WindowsRecoveryWindow> Windows { get; } = [];

        public List<(IntPtr Handle, int X, int Y, int Width, int Height)> MovedWindows { get; } = [];

        public List<IntPtr> MinimizedWindows { get; } = [];

        public List<IntPtr> ClosedWindows { get; } = [];

        public List<int> TerminatedProcessIds { get; } = [];

        public IReadOnlyList<WindowsRecoveryWindow> EnumerateTopLevelWindows() => Windows;

        public void MoveWindow(IntPtr handle, int x, int y, int width, int height) =>
            MovedWindows.Add((handle, x, y, width, height));

        public void MinimizeWindow(IntPtr handle) => MinimizedWindows.Add(handle);

        public void CloseWindow(IntPtr handle) => ClosedWindows.Add(handle);

        public void TerminateProcess(int processId) => TerminatedProcessIds.Add(processId);
    }
}

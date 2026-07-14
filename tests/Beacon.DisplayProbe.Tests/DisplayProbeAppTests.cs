using Beacon.DisplayProbe;
using Beacon.Platform.Windows.Displays;

namespace Beacon.DisplayProbe.Tests;

public sealed class DisplayProbeAppTests
{
    [Fact]
    public async Task PrepareCommandCreatesDisplayWithoutPrimaryRequest()
    {
        var api = new ProbeWindowsDisplayApi
        {
            AfterCreateTopology = DisplayTopologySnapshot.Extended(
                physicalDisplayId: "physical-laptop-panel",
                virtualDisplayId: "client-z-fold-7",
                width: 2560,
                height: 1600,
                refreshHz: 120,
                virtualPrimary: false)
        };
        using var output = new StringWriter();

        int exitCode = await DisplayProbeApp.RunAsync(
            api,
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
            ],
            output,
            TextWriter.Null);

        Assert.Equal(0, exitCode);
        Assert.Contains("prepare: success", output.ToString());
        Assert.Equal(("client-z-fold-7", 2560, 1600, 120), Assert.Single(api.CreatedDisplays));
        Assert.Equal(0, api.PrimaryCalls);
    }

    [Fact]
    public async Task RestorePhysicalCommandUsesVerifiedBackendResult()
    {
        var api = new ProbeWindowsDisplayApi
        {
            CurrentTopology = DisplayTopologySnapshot.Extended(
                physicalDisplayId: "physical-laptop-panel",
                virtualDisplayId: "client-z-fold-7",
                width: 2560,
                height: 1600,
                refreshHz: 120,
                virtualPrimary: true)
        };
        api.RestoreTopologies.Enqueue(api.CurrentTopology);
        api.RestoreTopologies.Enqueue(api.CurrentTopology);
        using var output = new StringWriter();

        int exitCode = await DisplayProbeApp.RunAsync(
            api,
            ["restore-physical"],
            output,
            TextWriter.Null);

        Assert.Equal(2, exitCode);
        Assert.Contains("restore-physical: failed", output.ToString());
        Assert.Contains("not verified", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, api.RestoreCalls);
    }

    [Fact]
    public async Task RecoverCommandRemovesDisplayAfterInitialRestoreFailure()
    {
        var api = new ProbeWindowsDisplayApi
        {
            CurrentTopology = DisplayTopologySnapshot.Extended(
                physicalDisplayId: "physical-laptop-panel",
                virtualDisplayId: "client-z-fold-7",
                width: 2560,
                height: 1600,
                refreshHz: 120,
                virtualPrimary: true),
            AfterRemoveTopology = DisplayTopologySnapshot.PhysicalOnly(
                physicalDisplayId: "physical-laptop-panel",
                width: 2560,
                height: 1600,
                refreshHz: 120)
        };
        api.RestoreResults.Enqueue(DisplayApiResult.Fail("physical primary was not verified"));
        api.RestoreResults.Enqueue(DisplayApiResult.Ok());
        using var output = new StringWriter();

        int exitCode = await DisplayProbeApp.RunAsync(
            api,
            ["recover", "--client", "z-fold-7"],
            output,
            TextWriter.Null);

        Assert.Equal(0, exitCode);
        Assert.Contains("recover: success", output.ToString());
        Assert.Equal(2, api.RestoreCalls);
        Assert.Equal("client-z-fold-7", Assert.Single(api.RemovedDisplays));
    }

    [Fact]
    public async Task DriverSessionCommandHoldsReportsAndReleasesDriverControlWithoutCreatingDisplay()
    {
        var api = new ProbeWindowsDisplayApi();
        using var output = new StringWriter();

        int exitCode = await DisplayProbeApp.RunAsync(
            api,
            ["driver-session"],
            output,
            TextWriter.Null);

        Assert.Equal(0, exitCode);
        Assert.Contains("driver-session: success", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("watchdog=3s", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("heartbeat=active", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("probe-driver-session", Assert.Single(api.HeldDisplayIds));
        Assert.Equal("probe-driver-session", Assert.Single(api.ReleasedDisplayIds));
        Assert.Empty(api.CreatedDisplays);
    }

    private sealed class ProbeWindowsDisplayApi : IWindowsDisplayApi, IWindowsDisplayLeaseSession
    {
        public DisplayTopologySnapshot CurrentTopology { get; set; } =
            DisplayTopologySnapshot.PhysicalOnly("physical-laptop-panel", 2560, 1600, 120);

        public DisplayTopologySnapshot? AfterCreateTopology { get; set; }

        public DisplayTopologySnapshot? AfterRemoveTopology { get; set; }

        public Queue<DisplayTopologySnapshot> RestoreTopologies { get; } = new();

        public Queue<DisplayApiResult> RestoreResults { get; } = new();

        public List<(string DisplayId, int Width, int Height, int RefreshHz)> CreatedDisplays { get; } = [];

        public List<string> RemovedDisplays { get; } = [];

        public List<string> HeldDisplayIds { get; } = [];

        public List<string> ReleasedDisplayIds { get; } = [];

        public int PrimaryCalls { get; private set; }

        public int RestoreCalls { get; private set; }

        public SudoVdaDriverLeaseSessionSnapshot Snapshot => new(
            LeaseCount: HeldDisplayIds.Count - ReleasedDisplayIds.Count,
            WatchdogTimeoutSeconds: 3,
            HeartbeatActive: HeldDisplayIds.Count > ReleasedDisplayIds.Count,
            Healthy: true,
            Diagnostic: "probe heartbeat healthy");

        public DisplayDriverStatus GetDriverStatus() => new(true, "SudoVDA driver is ready.");

        public Task<DisplayApiResult> CreateVirtualDisplayAsync(
            string displayId,
            int width,
            int height,
            int refreshHz,
            CancellationToken cancellationToken)
        {
            CreatedDisplays.Add((displayId, width, height, refreshHz));
            if (AfterCreateTopology is not null)
            {
                CurrentTopology = AfterCreateTopology;
            }

            return Task.FromResult(DisplayApiResult.Ok());
        }

        public Task<DisplayTopologySnapshot> QueryTopologyAsync(CancellationToken cancellationToken) =>
            Task.FromResult(CurrentTopology);

        public Task<DisplayApiResult> SetVirtualPrimaryAsync(string displayId, CancellationToken cancellationToken)
        {
            PrimaryCalls++;
            return Task.FromResult(DisplayApiResult.Ok());
        }

        public Task<DisplayApiResult> RestorePhysicalPrimaryAsync(CancellationToken cancellationToken)
        {
            RestoreCalls++;
            if (RestoreResults.Count > 0)
            {
                return Task.FromResult(RestoreResults.Dequeue());
            }

            if (RestoreTopologies.Count > 0)
            {
                CurrentTopology = RestoreTopologies.Dequeue();
            }

            return Task.FromResult(DisplayApiResult.Ok());
        }

        public Task<DisplayApiResult> RemoveVirtualDisplayAsync(string displayId, CancellationToken cancellationToken)
        {
            RemovedDisplays.Add(displayId);
            if (AfterRemoveTopology is not null)
            {
                CurrentTopology = AfterRemoveTopology;
            }

            return Task.FromResult(DisplayApiResult.Ok());
        }

        public Task<DisplayHdrCapability> QueryHdrCapabilityAsync(string displayId, CancellationToken cancellationToken) =>
            Task.FromResult(new DisplayHdrCapability(false, false, "SDR only."));

        public Task<SudoVdaDriverLeaseHoldResult> HoldAsync(
            string displayId,
            CancellationToken cancellationToken)
        {
            HeldDisplayIds.Add(displayId);
            return Task.FromResult(SudoVdaDriverLeaseHoldResult.Held());
        }

        public Task ReleaseAsync(string displayId, CancellationToken cancellationToken)
        {
            ReleasedDisplayIds.Add(displayId);
            return Task.CompletedTask;
        }
    }
}

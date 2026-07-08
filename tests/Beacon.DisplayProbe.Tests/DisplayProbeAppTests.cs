using Beacon.DisplayProbe;
using Beacon.Platform.Windows.Displays;

namespace Beacon.DisplayProbe.Tests;

public sealed class DisplayProbeAppTests
{
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

    private sealed class ProbeWindowsDisplayApi : IWindowsDisplayApi
    {
        public DisplayTopologySnapshot CurrentTopology { get; set; } =
            DisplayTopologySnapshot.PhysicalOnly("physical-laptop-panel", 2560, 1600, 120);

        public Queue<DisplayTopologySnapshot> RestoreTopologies { get; } = new();

        public int RestoreCalls { get; private set; }

        public DisplayDriverStatus GetDriverStatus() => new(true, "SudoVDA driver is ready.");

        public Task<DisplayApiResult> CreateVirtualDisplayAsync(
            string displayId,
            int width,
            int height,
            int refreshHz,
            CancellationToken cancellationToken) =>
            Task.FromResult(DisplayApiResult.Ok());

        public Task<DisplayTopologySnapshot> QueryTopologyAsync(CancellationToken cancellationToken) =>
            Task.FromResult(CurrentTopology);

        public Task<DisplayApiResult> SetVirtualPrimaryAsync(string displayId, CancellationToken cancellationToken) =>
            Task.FromResult(DisplayApiResult.Ok());

        public Task<DisplayApiResult> RestorePhysicalPrimaryAsync(CancellationToken cancellationToken)
        {
            RestoreCalls++;
            if (RestoreTopologies.Count > 0)
            {
                CurrentTopology = RestoreTopologies.Dequeue();
            }

            return Task.FromResult(DisplayApiResult.Ok());
        }

        public Task<DisplayApiResult> RemoveVirtualDisplayAsync(string displayId, CancellationToken cancellationToken) =>
            Task.FromResult(DisplayApiResult.Ok());

        public Task<DisplayHdrCapability> QueryHdrCapabilityAsync(string displayId, CancellationToken cancellationToken) =>
            Task.FromResult(new DisplayHdrCapability(false, false, "SDR only."));
    }
}

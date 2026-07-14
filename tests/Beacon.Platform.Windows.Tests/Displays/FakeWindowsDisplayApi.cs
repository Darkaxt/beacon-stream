using Beacon.Platform.Windows.Displays;

namespace Beacon.Platform.Windows.Tests.Displays;

internal sealed class FakeWindowsDisplayApi : IWindowsDisplayApi
{
    public bool DriverReady { get; set; } = true;

    public string DriverDiagnostic { get; set; } = "SudoVDA driver is ready.";

    public DisplayTopologySnapshot CurrentTopology { get; set; } =
        DisplayTopologySnapshot.PhysicalOnly("physical-laptop-panel", 2560, 1600, 120);

    public DisplayTopologySnapshot? AfterCreateTopology { get; set; }

    public DisplayTopologySnapshot? AfterPrimaryTopology { get; set; }

    public DisplayTopologySnapshot? AfterRestoreTopology { get; set; }

    public Queue<DisplayTopologySnapshot> RestoreTopologies { get; } = new();

    public DisplayHdrCapability HdrCapability { get; set; } =
        new(Supported: false, Enabled: false, Reason: "Windows Advanced Color reports SDR only.");

    public DisplayApiResult PrimaryResult { get; set; } = DisplayApiResult.Ok();

    public DisplayApiResult CreateResult { get; set; } = DisplayApiResult.Ok();

    public List<(string DisplayId, int Width, int Height, int RefreshHz)> CreatedDisplays { get; } = [];

    public List<string> PrimaryRequests { get; } = [];

    public List<string> RestoreRequests { get; } = [];

    public List<string> RemovedDisplays { get; } = [];

    public int TopologyQueryCount { get; private set; }

    public static FakeWindowsDisplayApi ReadyWithGoodTopology()
    {
        return new FakeWindowsDisplayApi
        {
            AfterCreateTopology = DisplayTopologySnapshot.Extended(
                physicalDisplayId: "physical-laptop-panel",
                virtualDisplayId: "client-z-fold-7",
                width: 2560,
                height: 1600,
                refreshHz: 120,
                virtualPrimary: false),
            AfterPrimaryTopology = DisplayTopologySnapshot.Extended(
                physicalDisplayId: "physical-laptop-panel",
                virtualDisplayId: "client-z-fold-7",
                width: 2560,
                height: 1600,
                refreshHz: 120,
                virtualPrimary: true)
        };
    }

    public DisplayDriverStatus GetDriverStatus() => new(DriverReady, DriverDiagnostic);

    public Task<DisplayApiResult> CreateVirtualDisplayAsync(
        string displayId,
        int width,
        int height,
        int refreshHz,
        CancellationToken cancellationToken)
    {
        CreatedDisplays.Add((displayId, width, height, refreshHz));
        if (!CreateResult.Success)
        {
            return Task.FromResult(CreateResult);
        }

        if (AfterCreateTopology is not null)
        {
            CurrentTopology = AfterCreateTopology;
        }

        return Task.FromResult(CreateResult);
    }

    public Task<DisplayTopologySnapshot> QueryTopologyAsync(CancellationToken cancellationToken)
    {
        TopologyQueryCount++;
        return Task.FromResult(CurrentTopology);
    }

    public Task<DisplayApiResult> SetVirtualPrimaryAsync(string displayId, CancellationToken cancellationToken)
    {
        PrimaryRequests.Add(displayId);
        if (!PrimaryResult.Success)
        {
            return Task.FromResult(PrimaryResult);
        }

        if (AfterPrimaryTopology is not null)
        {
            CurrentTopology = AfterPrimaryTopology;
        }

        return Task.FromResult(PrimaryResult);
    }

    public Task<DisplayApiResult> RestorePhysicalPrimaryAsync(CancellationToken cancellationToken)
    {
        RestoreRequests.Add("physical-primary");
        if (RestoreTopologies.Count > 0)
        {
            CurrentTopology = RestoreTopologies.Dequeue();
            return Task.FromResult(DisplayApiResult.Ok());
        }

        if (AfterRestoreTopology is not null)
        {
            CurrentTopology = AfterRestoreTopology;
        }

        return Task.FromResult(DisplayApiResult.Ok());
    }

    public Task<DisplayApiResult> RemoveVirtualDisplayAsync(string displayId, CancellationToken cancellationToken)
    {
        RemovedDisplays.Add(displayId);
        return Task.FromResult(DisplayApiResult.Ok());
    }

    public Task<DisplayHdrCapability> QueryHdrCapabilityAsync(string displayId, CancellationToken cancellationToken) =>
        Task.FromResult(HdrCapability);
}

using Beacon.Core.Clients;
using Beacon.Core.Displays;
using Beacon.Core.Sessions;
using Beacon.Core.Streaming;

namespace Beacon.Core.Tests.Streaming;

public sealed class BeaconTestStreamingBackendTests
{
    [Fact]
    public async Task StartCreatesEndpointOnlyColorBarsSession()
    {
        var backend = new BeaconTestStreamingBackend();
        SessionPlan plan = CreatePlan();

        StreamingStartResult result = await backend.StartAsync(plan, CancellationToken.None);

        Assert.True(result.Success);
        StreamingSessionState session = Assert.IsType<StreamingSessionState>(result.Session);
        Assert.Equal("z-fold-7-steam-shortcut:3767414131", session.SessionId);
        Assert.Equal("running", session.State);
        Assert.NotNull(session.Connection);
        Assert.Equal("beacon-test", session.Connection.Protocol);
        Assert.Null(session.Connection.LaunchUri);
        StreamingEndpointDescriptor endpoint = Assert.Single(session.Connection.Endpoints);
        Assert.Equal("video", endpoint.Role);
        Assert.Equal("beacon-test://pattern/color-bars", endpoint.Uri);
        Assert.Equal("client-z-fold-7", session.Connection.Metadata["displayId"]);
        Assert.Equal("color-bars", session.Connection.Metadata["pattern"]);
        Assert.Single(backend.GetSessions());
    }

    [Fact]
    public async Task HealthAdvertisesBeaconTestEndpoint()
    {
        var backend = new BeaconTestStreamingBackend();

        StreamingBackendHealth health = await backend.GetHealthAsync(CancellationToken.None);

        Assert.True(health.Ready);
        Assert.Equal("beacon-test", health.Backend);
        Assert.Equal("beacon-test", health.Protocol);
        Assert.Null(health.LaunchUri);
        StreamingEndpointDescriptor endpoint = Assert.Single(health.Endpoints);
        Assert.Equal("video", endpoint.Role);
        Assert.Equal("beacon-test://pattern/color-bars", endpoint.Uri);
        Assert.Contains("beacon-test", health.Capture);
    }

    [Fact]
    public async Task StopMarksSessionStoppedWithoutDeletingState()
    {
        var backend = new BeaconTestStreamingBackend();
        SessionPlan plan = CreatePlan();
        await backend.StartAsync(plan, CancellationToken.None);

        StreamingStopResult result = await backend.StopAsync(plan.SessionId, CancellationToken.None);

        Assert.True(result.Success);
        StreamingSessionState session = Assert.IsType<StreamingSessionState>(result.Session);
        Assert.Equal("stopped", session.State);
        Assert.Equal(session, await backend.GetSessionAsync(plan.SessionId, CancellationToken.None));
    }

    private static SessionPlan CreatePlan() =>
        new(
            SessionId: "z-fold-7-steam-shortcut:3767414131",
            ClientId: new ClientId("z-fold-7"),
            AppId: "steam-shortcut:3767414131",
            Display: new PlannedDisplay(
                "client-z-fold-7",
                2560,
                1600,
                120,
                "virtual-primary",
                HdrPreference.Prefer,
                HdrEnabled: false,
                "sdr",
                "HDR disabled because virtual display does not report HDR capability."),
            Stream: new PlannedStream("av1", 120, 65, "lan-direct", "adaptive"));
}

using Beacon.Core.Clients;
using Beacon.Core.Displays;
using Beacon.Core.Sessions;
using Beacon.Core.Streaming;

namespace Beacon.Core.Tests.Streaming;

public sealed class FakeStreamingBackendTests
{
    [Fact]
    public async Task StartCreatesRunningSessionFromPlan()
    {
        var backend = new FakeStreamingBackend();
        SessionPlan plan = CreatePlan();

        StreamingStartResult result = await backend.StartAsync(plan, CancellationToken.None);

        Assert.True(result.Success);
        StreamingSessionState session = Assert.IsType<StreamingSessionState>(result.Session);
        Assert.Equal("z-fold-7-steam-shortcut:3767414131", session.SessionId);
        Assert.Equal("z-fold-7", session.ClientId);
        Assert.Equal("client-z-fold-7", session.DisplayId);
        Assert.Equal("av1", session.Codec);
        Assert.Equal(120, session.Fps);
        Assert.Equal("running", session.State);
        Assert.NotNull(session.Connection);
        Assert.Equal("beacon-fake", session.Connection.Protocol);
        Assert.Equal("beacon-fake://stream/z-fold-7-steam-shortcut:3767414131", session.Connection.LaunchUri);
        StreamingEndpointDescriptor endpoint = Assert.Single(session.Connection.Endpoints);
        Assert.Equal("control", endpoint.Role);
        Assert.Equal("beacon-fake://stream/z-fold-7-steam-shortcut:3767414131", endpoint.Uri);
        Assert.Equal("client-z-fold-7", session.Connection.Metadata["displayId"]);
        Assert.Single(backend.GetSessions());
    }

    [Fact]
    public async Task PreflightCanFailBeforeStart()
    {
        var backend = new FakeStreamingBackend { NextPreflightError = "stream wrapper missing" };

        StreamingPreflightResult result = await backend.CheckReadinessAsync(CreatePlan(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("stream wrapper missing", result.Error);
        Assert.Empty(backend.GetSessions());
    }

    [Fact]
    public async Task StopMarksSessionStoppedWithoutDeletingState()
    {
        var backend = new FakeStreamingBackend();
        SessionPlan plan = CreatePlan();
        await backend.StartAsync(plan, CancellationToken.None);

        StreamingStopResult result = await backend.StopAsync(plan.SessionId, CancellationToken.None);

        Assert.True(result.Success);
        StreamingSessionState session = Assert.IsType<StreamingSessionState>(result.Session);
        Assert.Equal("stopped", session.State);
        Assert.Equal("client-z-fold-7", session.DisplayId);
        Assert.Equal(session, await backend.GetSessionAsync(plan.SessionId, CancellationToken.None));
    }

    [Fact]
    public async Task StartFailureReturnsDiagnostic()
    {
        var backend = new FakeStreamingBackend { NextStartError = "encoder unavailable" };

        StreamingStartResult result = await backend.StartAsync(CreatePlan(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.Session);
        Assert.Equal("encoder unavailable", result.Error);
        Assert.Empty(backend.GetSessions());
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

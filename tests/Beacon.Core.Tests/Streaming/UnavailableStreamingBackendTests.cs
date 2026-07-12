using Beacon.Core.Clients;
using Beacon.Core.Displays;
using Beacon.Core.Sessions;
using Beacon.Core.Streaming;

namespace Beacon.Core.Tests.Streaming;

public sealed class UnavailableStreamingBackendTests
{
    [Fact]
    public async Task FailsPreflightUntilBeaconStreamWorkerExists()
    {
        var backend = new UnavailableStreamingBackend();

        StreamingBackendHealth health = await backend.GetHealthAsync(CancellationToken.None);
        StreamingPreflightResult preflight = await backend.CheckReadinessAsync(
            CreatePlan(),
            CancellationToken.None);

        Assert.False(health.Ready);
        Assert.Equal("unavailable", health.State);
        Assert.Contains("StreamWorker", health.Diagnostic, StringComparison.Ordinal);
        Assert.False(preflight.Success);
        Assert.Equal(health.Diagnostic, preflight.Error);
        Assert.Empty(backend.GetSessions());
    }

    [Fact]
    public async Task NeverFabricatesAStreamSession()
    {
        var backend = new UnavailableStreamingBackend();

        StreamingStartResult start = await backend.StartAsync(CreatePlan(), CancellationToken.None);

        Assert.False(start.Success);
        Assert.Null(start.Session);
        Assert.Contains("StreamWorker", start.Error, StringComparison.Ordinal);
        Assert.Null(await backend.GetSessionAsync("session", CancellationToken.None));
    }

    private static SessionPlan CreatePlan() => new(
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
            "HDR unavailable during the recovery fixture."),
        Stream: new PlannedStream(
            "h264",
            120,
            65,
            "beacon",
            "measured",
            "Test benchmark evidence.",
            Guid.Parse("33acde60-b29f-4f03-b2b2-f51337bdb9a5"),
            "test-benchmark-revision"));
}

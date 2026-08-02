using System.Text.Json;
using Beacon.Core.Clients;
using Beacon.Core.Displays;
using Beacon.Core.Sessions;
using Beacon.Core.Streaming;

namespace Beacon.Core.Tests.Streaming;

public sealed class FakeStreamingBackendTests
{
    [Fact]
    public async Task HealthReportsOnlyBeaconCapabilitiesAndState()
    {
        var backend = new FakeStreamingBackend();

        StreamingBackendHealth health = await backend.GetHealthAsync(CancellationToken.None);

        Assert.True(health.Ready);
        Assert.Equal("ready", health.State);
        Assert.Equal("Fake streaming backend ready.", health.Diagnostic);
        Assert.Equal(["h264", "hevc", "av1"], health.Capabilities.Codecs);
        Assert.Equal(["fake"], health.Capabilities.Encoders);
        Assert.Equal(["fake"], health.Capabilities.CaptureMethods);
        Assert.Equal(120, health.Capabilities.MaxFps);
        Assert.Null(health.Capabilities.MaxBitrateMbps);
        Assert.False(health.Capabilities.Hdr10);
        Assert.Equal(0, health.ActiveSessions);
        Assert.Empty(health.Diagnostics);
    }

    [Fact]
    public async Task StartCreatesRunningSessionFromPlan()
    {
        var backend = new FakeStreamingBackend { ActiveListenerPort = 51234 };
        SessionPlan plan = CreatePlan();

        StreamingStartResult result = await backend.StartAsync(plan, CancellationToken.None);

        Assert.True(result.Success);
        StreamingSessionState session = Assert.IsType<StreamingSessionState>(result.Session);
        Assert.Equal("z-fold-7-steam-shortcut:3767414131", session.SessionId);
        Assert.Equal("z-fold-7", session.ClientId);
        Assert.Equal("client-z-fold-7", session.DisplayId);
        Assert.Equal("av1", session.Codec);
        Assert.Equal(120, session.Fps);
        Assert.Equal(65, session.InitialBitrateMbps);
        Assert.Equal(51234, session.ActiveListenerPort);
        Assert.Equal("running", session.State);
        Assert.Single(backend.GetSessions());
    }

    [Fact]
    public async Task PreflightCanFailBeforeStart()
    {
        var backend = new FakeStreamingBackend { NextPreflightError = "stream unavailable" };

        StreamingPreflightResult result = await backend.CheckReadinessAsync(CreatePlan(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("stream unavailable", result.Error);
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

    [Fact]
    public async Task StopFailureReturnsDiagnosticAndKeepsRunningState()
    {
        var backend = new FakeStreamingBackend
        {
            ActiveListenerPort = 51234,
            NextStopError = "worker stop unavailable",
        };
        SessionPlan plan = CreatePlan();
        Assert.True((await backend.StartAsync(plan, CancellationToken.None)).Success);

        StreamingStopResult result = await backend.StopAsync(plan.SessionId, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("worker stop unavailable", result.Error);
        Assert.Equal("running", (await backend.GetSessionAsync(plan.SessionId, CancellationToken.None))?.State);
    }

    [Fact]
    public async Task SuccessfulStartsUseDeterministicPerInstanceRuntimeGenerations()
    {
        var backend = new FakeStreamingBackend();
        var secondBackend = new FakeStreamingBackend();
        SessionPlan plan = CreatePlan();

        StreamingSessionState first = Assert.IsType<StreamingSessionState>(
            (await backend.StartAsync(plan, CancellationToken.None)).Session);
        Assert.Equal(47998, first.ActiveListenerPort);
        Assert.Equal(Guid.Parse("00000000-0000-0000-0000-000000000001"), first.RuntimeGeneration);
        Assert.DoesNotContain(
            "runtimeGeneration",
            JsonSerializer.Serialize(first),
            StringComparison.OrdinalIgnoreCase);
        Assert.True((await backend.StopAsync(plan.SessionId, CancellationToken.None)).Success);

        StreamingSessionState second = Assert.IsType<StreamingSessionState>(
            (await backend.StartAsync(plan, CancellationToken.None)).Session);
        StreamingSessionState otherFirst = Assert.IsType<StreamingSessionState>(
            (await secondBackend.StartAsync(plan, CancellationToken.None)).Session);

        Assert.Equal(Guid.Parse("00000000-0000-0000-0000-000000000002"), second.RuntimeGeneration);
        Assert.Equal(first.RuntimeGeneration, otherFirst.RuntimeGeneration);
    }

    [Fact]
    public async Task ConditionalStopRejectsReplacementRuntimeGeneration()
    {
        var backend = new FakeStreamingBackend();
        SessionPlan plan = CreatePlan();
        StreamingSessionState runtimeA = Assert.IsType<StreamingSessionState>(
            (await backend.StartAsync(plan, CancellationToken.None)).Session);
        StreamingSessionState runtimeB = Assert.IsType<StreamingSessionState>(
            (await backend.StartAsync(plan, CancellationToken.None)).Session);

        StreamingStopResult stopped = await backend.StopRuntimeAsync(
            plan.SessionId,
            runtimeA.RuntimeGeneration,
            CancellationToken.None);

        StreamingSessionState current = Assert.IsType<StreamingSessionState>(
            await backend.GetSessionAsync(plan.SessionId, CancellationToken.None));
        Assert.False(stopped.Success);
        Assert.Contains("runtime generation changed", stopped.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(runtimeB.RuntimeGeneration, current.RuntimeGeneration);
        Assert.Equal("running", current.State);
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
            Stream: new PlannedStream(
                "av1",
                2560,
                1600,
                120,
                65,
                "beacon",
                "measured",
                "Test benchmark evidence.",
                Guid.Parse("33acde60-b29f-4f03-b2b2-f51337bdb9a5"),
                "test-benchmark-revision"),
            Audio: new PlannedAudio("opus", 48_000, 2, 20_000, 96_000, "R2 test audio."));
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Beacon.Core.Displays;
using Beacon.Core.Recovery;
using Beacon.Core.Sessions;
using Beacon.Core.Streaming;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Beacon.Server.Tests;

public sealed class AdminApiTests(BeaconServerTestFactory factory) : IClassFixture<BeaconServerTestFactory>
{
    [Fact]
    public async Task SnapshotReturnsClientsGamesAndSessions()
    {
        HttpClient client = factory.CreateClient();

        await client.PostAsJsonAsync("/clients/z-fold-7/launch", new { gameId = "steam-shortcut:3767414131" });
        HttpResponseMessage response = await client.GetAsync("/admin/snapshot");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;

        Assert.True(root.GetProperty("clients").GetArrayLength() > 0);
        Assert.True(root.GetProperty("games").GetProperty("total").GetInt32() > 0);
        Assert.Equal("z-fold-7", root.GetProperty("clients")[0].GetProperty("clientId").GetString());
        JsonElement benchmark = Assert.Single(root.GetProperty("clients")[0].GetProperty("benchmarks").EnumerateArray());
        Assert.Equal("av1", benchmark.GetProperty("selectedResult").GetProperty("codec").GetString());
        Assert.Equal(256, benchmark.GetProperty("networkSamples").GetArrayLength());
        Assert.Equal("steam-shortcut:3767414131", root.GetProperty("sessions")[0].GetProperty("appId").GetString());
        Assert.Equal("running", root.GetProperty("streams")[0].GetProperty("state").GetString());
        Assert.Equal("client-z-fold-7", root.GetProperty("streams")[0].GetProperty("displayId").GetString());
        Assert.Equal("steam-shortcut:3767414131", root.GetProperty("ownership")[0].GetProperty("appId").GetString());
        Assert.False(root.GetProperty("ownership")[0].GetProperty("launchedProcessRunning").GetBoolean());
        Assert.Equal("fake", root.GetProperty("host").GetProperty("mode").GetString());
        Assert.False(root.GetProperty("host").TryGetProperty("streamingBackendMode", out _));
        Assert.Equal("FakeDisplayBackend", root.GetProperty("host").GetProperty("displayBackend").GetString());
        Assert.Equal("FakeStreamingBackend", root.GetProperty("host").GetProperty("streamingBackend").GetString());
        Assert.Equal("memory", root.GetProperty("profiles").GetProperty("store").GetString());
        Assert.Equal("memory", root.GetProperty("benchmarks").GetProperty("store").GetString());
        Assert.False(root.GetProperty("profiles").TryGetProperty("pairingEnabled", out _));
        Assert.True(root.GetProperty("inputHealth").GetProperty("ready").GetBoolean());
        Assert.Equal("no-op", root.GetProperty("inputHealth").GetProperty("backend").GetString());
        Assert.Contains(
            "pointer",
            root.GetProperty("inputHealth").GetProperty("supportedEventTypes").EnumerateArray()
                .Select(value => value.GetString()));
        Assert.Contains(
            "keyboard",
            root.GetProperty("inputHealth").GetProperty("supportedEventTypes").EnumerateArray()
                .Select(value => value.GetString()));
        Assert.Contains(
            "tap",
            root.GetProperty("inputHealth").GetProperty("supportedPointerActions").EnumerateArray()
                .Select(value => value.GetString()));
        Assert.Contains(
            "press",
            root.GetProperty("inputHealth").GetProperty("supportedKeyboardActions").EnumerateArray()
                .Select(value => value.GetString()));
        Assert.True(root.GetProperty("display").GetProperty("driverReady").GetBoolean());
        Assert.True(root.GetProperty("display").GetProperty("topologyAvailable").GetBoolean());
        Assert.False(root.GetProperty("display").GetProperty("mirrorMode").GetBoolean());
        Assert.True(root.GetProperty("display").GetProperty("physicalPrimaryVerified").GetBoolean());
        Assert.True(root.GetProperty("display").GetProperty("paths").GetArrayLength() > 0);
        Assert.True(root.GetProperty("streamingHealth").GetProperty("ready").GetBoolean());
        Assert.Equal("ready", root.GetProperty("streamingHealth").GetProperty("state").GetString());
        Assert.Equal(1, root.GetProperty("streamingHealth").GetProperty("activeSessions").GetInt32());
        Assert.Contains(
            "av1",
            root.GetProperty("streamingHealth").GetProperty("capabilities").GetProperty("codecs")
                .EnumerateArray()
                .Select(value => value.GetString()));
        Assert.False(root.GetProperty("streamingHealth").TryGetProperty("endpoints", out _));
    }

    [Fact]
    public async Task AdminCanRequestPhysicalRestoreAndClientRecovery()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage restore = await client.PostAsJsonAsync("/admin/recovery/restore-physical", new { });
        HttpResponseMessage move = await client.PostAsJsonAsync("/admin/recovery/move-windows-back", new { minimize = true });
        HttpResponseMessage close = await client.PostAsJsonAsync("/admin/recovery/close-virtual-windows", new { });
        HttpResponseMessage terminate = await client.PostAsJsonAsync("/admin/recovery/terminate-virtual-processes", new { });
        HttpResponseMessage recover = await client.PostAsJsonAsync("/admin/clients/z-fold-7/display/recover", new { });

        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        Assert.Equal(HttpStatusCode.OK, move.StatusCode);
        Assert.Equal(HttpStatusCode.OK, close.StatusCode);
        Assert.Equal(HttpStatusCode.OK, terminate.StatusCode);
        Assert.Equal(HttpStatusCode.OK, recover.StatusCode);

        using JsonDocument restoreJson = await JsonDocument.ParseAsync(await restore.Content.ReadAsStreamAsync());
        using JsonDocument moveJson = await JsonDocument.ParseAsync(await move.Content.ReadAsStreamAsync());
        using JsonDocument closeJson = await JsonDocument.ParseAsync(await close.Content.ReadAsStreamAsync());
        using JsonDocument terminateJson = await JsonDocument.ParseAsync(await terminate.Content.ReadAsStreamAsync());
        using JsonDocument recoverJson = await JsonDocument.ParseAsync(await recover.Content.ReadAsStreamAsync());

        Assert.True(restoreJson.RootElement.GetProperty("restoreRequested").GetBoolean());
        Assert.True(moveJson.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("move-windows-back", moveJson.RootElement.GetProperty("action").GetString());
        Assert.True(closeJson.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("close-virtual-windows", closeJson.RootElement.GetProperty("action").GetString());
        Assert.True(terminateJson.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("terminate-virtual-processes", terminateJson.RootElement.GetProperty("action").GetString());
        Assert.True(recoverJson.RootElement.GetProperty("recovered").GetBoolean());
    }

    [Fact]
    public async Task AdminResetTopologyRestoresPhysicalAndMovesWindowsBackMinimized()
    {
        var display = new FakeDisplayBackend();
        var recovery = new FakeRecoveryBackend
        {
            MoveWindowsBackResult = RecoveryActionResult.Ok("move-windows-back", 2, ["moved windows"])
        };
        WebApplicationFactory<Program> recoveryFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDisplayBackend>();
                services.RemoveAll<IRecoveryBackend>();
                services.AddSingleton<IDisplayBackend>(display);
                services.AddSingleton<IRecoveryBackend>(recovery);
            }));
        HttpClient client = recoveryFactory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/admin/recovery/reset-topology", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("reset-topology", root.GetProperty("action").GetString());
        Assert.Equal(2, root.GetProperty("affectedCount").GetInt32());
        Assert.Equal(["physical-primary"], display.RestoreCalls);
        Assert.Equal([true], recovery.MoveWindowsBackCalls);
    }

    [Fact]
    public async Task AdminPhysicalRestoreReturnsServiceUnavailableWhenRestoreFails()
    {
        var display = new FakeDisplayBackend
        {
            NextRestoreResult = DisplayRestoreResult.Fail("physical primary was not verified")
        };
        WebApplicationFactory<Program> failingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDisplayBackend>();
                services.AddSingleton<IDisplayBackend>(display);
            }));
        HttpClient client = failingFactory.CreateClient();

        HttpResponseMessage restore = await client.PostAsJsonAsync("/admin/recovery/restore-physical", new { });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, restore.StatusCode);
        string body = await restore.Content.ReadAsStringAsync();
        Assert.Contains("physical primary was not verified", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("physical-primary", Assert.Single(display.RestoreCalls));
    }

    [Fact]
    public async Task SnapshotIncludesRecentOperationalDiagnostics()
    {
        HttpClient client = factory.CreateClient();

        await client.PostAsJsonAsync("/admin/recovery/restore-physical", new { });
        HttpResponseMessage response = await client.GetAsync("/admin/snapshot");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement diagnostic = document.RootElement.GetProperty("diagnostics")
            .EnumerateArray()
            .First(evt => evt.GetProperty("operation").GetString() == "restore-physical");

        Assert.Equal("recovery", diagnostic.GetProperty("category").GetString());
        Assert.Equal("restore-physical", diagnostic.GetProperty("operation").GetString());
        Assert.Contains("Physical display restore", diagnostic.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SnapshotIncludesClientInputDiagnostics()
    {
        using WebApplicationFactory<Program> isolatedFactory = factory.WithWebHostBuilder(_ => { });
        HttpClient client = isolatedFactory.CreateClient();

        HttpResponseMessage launch = await client.PostAsJsonAsync(
            "/clients/z-fold-7/launch",
            new { gameId = "steam-shortcut:3767414131" });
        HttpResponseMessage input = await client.PostAsJsonAsync("/clients/z-fold-7/input", new
        {
            sequence = 42,
            events = new[]
            {
                new { type = "pointer", action = "tap", pointerId = 1, x = 0.5, y = 0.5, buttons = 1 }
            }
        });
        HttpResponseMessage response = await client.GetAsync("/admin/snapshot");

        Assert.Equal(HttpStatusCode.OK, launch.StatusCode);
        Assert.Equal(HttpStatusCode.OK, input.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement diagnostic = document.RootElement.GetProperty("diagnostics")
            .EnumerateArray()
            .First(evt => evt.GetProperty("operation").GetString() == "input.forward");

        Assert.Equal("input", diagnostic.GetProperty("category").GetString());
        Assert.Equal("information", diagnostic.GetProperty("severity").GetString());
        Assert.Equal("z-fold-7", diagnostic.GetProperty("clientId").GetString());
        Assert.Equal("z-fold-7-steam-shortcut:3767414131", diagnostic.GetProperty("sessionId").GetString());
        Assert.Equal("client-z-fold-7", diagnostic.GetProperty("displayId").GetString());
        Assert.Contains("1 input event", diagnostic.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        JsonElement metadata = diagnostic.GetProperty("metadata");
        Assert.Equal("42", metadata.GetProperty("sequence").GetString());
        Assert.Equal("1", metadata.GetProperty("eventCount").GetString());
    }

    [Fact]
    public async Task SnapshotReportsDisplayUnavailableWhenHealthCheckThrows()
    {
        WebApplicationFactory<Program> failingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDisplayBackend>();
                services.AddSingleton<IDisplayBackend>(new ThrowingDisplayBackend("display api exploded"));
            }));
        HttpClient client = failingFactory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/admin/snapshot");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement display = document.RootElement.GetProperty("display");
        Assert.False(display.GetProperty("driverReady").GetBoolean());
        Assert.False(display.GetProperty("topologyAvailable").GetBoolean());
        Assert.Contains("display api exploded", display.GetProperty("diagnostic").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SnapshotReportsStreamingUnavailableWhenHealthCheckThrows()
    {
        WebApplicationFactory<Program> failingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStreamingBackend>();
                services.AddSingleton<IStreamingBackend>(new ThrowingStreamingBackend("stream api exploded"));
            }));
        HttpClient client = failingFactory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/admin/snapshot");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement streaming = document.RootElement.GetProperty("streamingHealth");
        Assert.False(streaming.GetProperty("ready").GetBoolean());
        Assert.Contains("stream api exploded", streaming.GetProperty("diagnostic").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SnapshotIncludesStreamWorkerCapabilities()
    {
        var health = new StreamingBackendHealth(
            Ready: true,
            State: "ready",
            Diagnostic: "Beacon StreamWorker ready.",
            Capabilities: new(
                Codecs: ["av1"],
                Encoders: ["nvenc"],
                CaptureMethods: ["dxgi"],
                MaxFps: 120,
                MaxBitrateMbps: 150,
                Hdr10: true),
            ActiveSessions: 0,
            Diagnostics: ["encoder verified"]);
        WebApplicationFactory<Program> childHealthFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStreamingBackend>();
                services.AddSingleton<IStreamingBackend>(new StaticStreamingBackend(health));
            }));
        HttpClient client = childHealthFactory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/admin/snapshot");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement streaming = document.RootElement.GetProperty("streamingHealth");
        Assert.True(streaming.GetProperty("ready").GetBoolean());
        Assert.Equal("ready", streaming.GetProperty("state").GetString());
        Assert.Equal("Beacon StreamWorker ready.", streaming.GetProperty("diagnostic").GetString());
        JsonElement capabilities = streaming.GetProperty("capabilities");
        Assert.Equal("av1", capabilities.GetProperty("codecs")[0].GetString());
        Assert.Equal("nvenc", capabilities.GetProperty("encoders")[0].GetString());
        Assert.Equal("dxgi", capabilities.GetProperty("captureMethods")[0].GetString());
        Assert.Equal(120, capabilities.GetProperty("maxFps").GetInt32());
        Assert.Equal(150, capabilities.GetProperty("maxBitrateMbps").GetInt32());
        Assert.True(capabilities.GetProperty("hdr10").GetBoolean());
        Assert.Equal("encoder verified", streaming.GetProperty("diagnostics")[0].GetString());
        Assert.False(streaming.TryGetProperty("protocol", out _));
        Assert.False(streaming.TryGetProperty("endpoints", out _));
    }

    [Fact]
    public async Task AdminCanStopSelectedClientStream()
    {
        HttpClient client = factory.CreateClient();
        await client.PostAsJsonAsync("/clients/z-fold-7/launch", new { gameId = "steam-shortcut:3767414131" });
        FakeStreamSessionAuthorizer authorizer = Assert.IsType<FakeStreamSessionAuthorizer>(
            factory.Services.GetRequiredService<IStreamSessionAuthorizer>());
        int revocationsBeforeStop = authorizer.Revocations.Count;

        HttpResponseMessage response = await client.PostAsJsonAsync("/admin/clients/z-fold-7/stream/stop", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;

        Assert.Equal("z-fold-7", root.GetProperty("clientId").GetString());
        Assert.Equal("stopped", root.GetProperty("stream").GetProperty("state").GetString());
        Assert.Equal(revocationsBeforeStop + 1, authorizer.Revocations.Count);
    }

    [Fact]
    public async Task AdminStopSelectedClientStreamReturnsServiceUnavailableWhenBackendStopFails()
    {
        WebApplicationFactory<Program> failingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStreamingBackend>();
                services.AddSingleton<IStreamingBackend>(new FailingStopStreamingBackend("stream stop failed"));
            }));
        HttpClient client = failingFactory.CreateClient();

        HttpResponseMessage launch = await client.PostAsJsonAsync("/clients/z-fold-7/launch", new { gameId = "steam-shortcut:3767414131" });
        HttpResponseMessage stop = await client.PostAsJsonAsync("/admin/clients/z-fold-7/stream/stop", new { });

        Assert.Equal(HttpStatusCode.OK, launch.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, stop.StatusCode);
        string body = await stop.Content.ReadAsStringAsync();
        Assert.Contains("stream stop failed", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdminStopSelectedClientStreamReportsMissingSessionPlan()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/admin/clients/no-session-client/stream/stop", new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("no session plan", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdminCanRemoveSelectedClientDisplayLease()
    {
        var display = new FakeDisplayBackend();
        WebApplicationFactory<Program> displayFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDisplayBackend>();
                services.AddSingleton<IDisplayBackend>(display);
            }));
        HttpClient client = displayFactory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/admin/clients/z-fold-7/display/remove", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;

        Assert.Equal("client-z-fold-7", root.GetProperty("displayId").GetString());
        Assert.True(root.GetProperty("removed").GetBoolean());
        Assert.Equal(["physical-primary"], display.RestoreCalls);
        Assert.Equal(["client-z-fold-7"], display.RemoveCalls);
    }

    [Fact]
    public async Task AdminCanPatchClientProfilePolicyFields()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PatchAsJsonAsync("/admin/clients/z-fold-7/profile", new
        {
            preferredRefreshHz = 90,
            mode = "physical-blackout",
            restorePhysicalDisplayOnEnd = false,
            forbidMirrorMode = false,
            keepAppRunningOnDisconnect = true,
            allowEmergencyRestoreFromClient = false
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;

        Assert.Equal(2560, root.GetProperty("display").GetProperty("preferredWidth").GetInt32());
        Assert.Equal(1600, root.GetProperty("display").GetProperty("preferredHeight").GetInt32());
        Assert.Equal(90, root.GetProperty("display").GetProperty("preferredRefreshHz").GetInt32());
        Assert.Equal("physical-blackout", root.GetProperty("display").GetProperty("mode").GetString());
        Assert.False(root.GetProperty("display").GetProperty("restorePhysicalDisplayOnEnd").GetBoolean());
        Assert.False(root.GetProperty("display").GetProperty("forbidMirrorMode").GetBoolean());
        Assert.True(root.GetProperty("session").GetProperty("keepAppRunningOnDisconnect").GetBoolean());
        Assert.False(root.GetProperty("session").GetProperty("allowEmergencyRestoreFromClient").GetBoolean());
    }

    [Fact]
    public async Task AdminProfilePatchRejectsZFold1440PCollapse()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PatchAsJsonAsync("/admin/clients/z-fold-7/profile", new
        {
            preferredWidth = 2560,
            preferredHeight = 1440
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("2560x1440", body, StringComparison.Ordinal);
    }

    private sealed class ThrowingDisplayBackend(string message) : IDisplayBackend
    {
        public Task<DisplayHealth> GetHealthAsync(CancellationToken cancellationToken) =>
            Task.FromException<DisplayHealth>(new InvalidOperationException(message));

        public Task<DisplayEnsureResult> PrepareVirtualDisplayAsync(
            string displayId,
            int width,
            int height,
            int refreshHz,
            HdrPreference hdrPreference,
            CancellationToken cancellationToken) =>
            Task.FromException<DisplayEnsureResult>(new InvalidOperationException(message));

        public Task<DisplayEnsureResult> EnsureVirtualDisplayAsync(
            string displayId,
            int width,
            int height,
            int refreshHz,
            HdrPreference hdrPreference,
            CancellationToken cancellationToken) =>
            Task.FromException<DisplayEnsureResult>(new InvalidOperationException(message));

        public Task<DisplayRestoreResult> RestorePhysicalPrimaryAsync(CancellationToken cancellationToken) =>
            Task.FromException<DisplayRestoreResult>(new InvalidOperationException(message));

        public Task<DisplayRemoveResult> RemoveVirtualDisplayAsync(string displayId, CancellationToken cancellationToken) =>
            Task.FromException<DisplayRemoveResult>(new InvalidOperationException(message));
    }

    private sealed class ThrowingStreamingBackend(string message) : IStreamingBackend
    {
        public Task<StreamingBackendHealth> GetHealthAsync(CancellationToken cancellationToken) =>
            Task.FromException<StreamingBackendHealth>(new InvalidOperationException(message));

        public Task<StreamingPreflightResult> CheckReadinessAsync(SessionPlan plan, CancellationToken cancellationToken) =>
            Task.FromException<StreamingPreflightResult>(new InvalidOperationException(message));

        public Task<StreamingStartResult> StartAsync(SessionPlan plan, CancellationToken cancellationToken) =>
            Task.FromException<StreamingStartResult>(new InvalidOperationException(message));

        public Task<StreamingStopResult> StopAsync(string sessionId, CancellationToken cancellationToken) =>
            Task.FromException<StreamingStopResult>(new InvalidOperationException(message));

        public Task<StreamingSessionState?> GetSessionAsync(string sessionId, CancellationToken cancellationToken) =>
            Task.FromException<StreamingSessionState?>(new InvalidOperationException(message));

        public IReadOnlyList<StreamingSessionState> GetSessions() =>
            throw new InvalidOperationException(message);
    }

    private sealed class StaticStreamingBackend(StreamingBackendHealth health) : IStreamingBackend
    {
        public Task<StreamingBackendHealth> GetHealthAsync(CancellationToken cancellationToken) =>
            Task.FromResult(health);

        public Task<StreamingPreflightResult> CheckReadinessAsync(SessionPlan plan, CancellationToken cancellationToken) =>
            Task.FromResult(StreamingPreflightResult.Ok());

        public Task<StreamingStartResult> StartAsync(SessionPlan plan, CancellationToken cancellationToken) =>
            Task.FromResult(StreamingStartResult.Fail("static backend does not start streams"));

        public Task<StreamingStopResult> StopAsync(string sessionId, CancellationToken cancellationToken) =>
            Task.FromResult(StreamingStopResult.Fail("static backend does not stop streams"));

        public Task<StreamingSessionState?> GetSessionAsync(string sessionId, CancellationToken cancellationToken) =>
            Task.FromResult<StreamingSessionState?>(null);

        public IReadOnlyList<StreamingSessionState> GetSessions() => [];
    }
}

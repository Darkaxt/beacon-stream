using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Beacon.Core.Displays;
using Beacon.Core.Recovery;
using Beacon.Core.Streaming;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Beacon.Server.Tests;

public sealed class AdminApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
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
        Assert.Equal("steam-shortcut:3767414131", root.GetProperty("sessions")[0].GetProperty("appId").GetString());
        Assert.Equal("running", root.GetProperty("streams")[0].GetProperty("state").GetString());
        Assert.Equal("client-z-fold-7", root.GetProperty("streams")[0].GetProperty("displayId").GetString());
        Assert.Equal("steam-shortcut:3767414131", root.GetProperty("ownership")[0].GetProperty("appId").GetString());
        Assert.False(root.GetProperty("ownership")[0].GetProperty("launchedProcessRunning").GetBoolean());
        Assert.Equal("fake", root.GetProperty("host").GetProperty("mode").GetString());
        Assert.Equal("fake", root.GetProperty("host").GetProperty("streamingBackendMode").GetString());
        Assert.Equal("FakeDisplayBackend", root.GetProperty("host").GetProperty("displayBackend").GetString());
        Assert.Equal("FakeStreamingBackend", root.GetProperty("host").GetProperty("streamingBackend").GetString());
        Assert.Equal("memory", root.GetProperty("profiles").GetProperty("store").GetString());
        Assert.False(root.GetProperty("profiles").GetProperty("pairingEnabled").GetBoolean());
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
    public async Task AdminCanStopSelectedClientStream()
    {
        HttpClient client = factory.CreateClient();
        await client.PostAsJsonAsync("/clients/z-fold-7/launch", new { gameId = "steam-shortcut:3767414131" });

        HttpResponseMessage response = await client.PostAsJsonAsync("/admin/clients/z-fold-7/stream/stop", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;

        Assert.Equal("z-fold-7", root.GetProperty("clientId").GetString());
        Assert.Equal("stopped", root.GetProperty("stream").GetProperty("state").GetString());
    }

    [Fact]
    public async Task AdminStopSelectedClientStreamReturnsServiceUnavailableWhenBackendStopFails()
    {
        WebApplicationFactory<Program> failingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStreamingBackend>();
                services.AddSingleton<IStreamingBackend>(new FailingStopStreamingBackend("wrapper refused stop"));
            }));
        HttpClient client = failingFactory.CreateClient();

        HttpResponseMessage launch = await client.PostAsJsonAsync("/clients/z-fold-7/launch", new { gameId = "steam-shortcut:3767414131" });
        HttpResponseMessage stop = await client.PostAsJsonAsync("/admin/clients/z-fold-7/stream/stop", new { });

        Assert.Equal(HttpStatusCode.OK, launch.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, stop.StatusCode);
        string body = await stop.Content.ReadAsStringAsync();
        Assert.Contains("wrapper refused stop", body, StringComparison.OrdinalIgnoreCase);
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
}

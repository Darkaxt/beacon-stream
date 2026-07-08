using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

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

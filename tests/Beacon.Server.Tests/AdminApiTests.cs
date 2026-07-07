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
    }

    [Fact]
    public async Task AdminCanRequestPhysicalRestoreAndClientRecovery()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage restore = await client.PostAsJsonAsync("/admin/recovery/restore-physical", new { });
        HttpResponseMessage recover = await client.PostAsJsonAsync("/admin/clients/z-fold-7/display/recover", new { });

        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        Assert.Equal(HttpStatusCode.OK, recover.StatusCode);

        using JsonDocument restoreJson = await JsonDocument.ParseAsync(await restore.Content.ReadAsStreamAsync());
        using JsonDocument recoverJson = await JsonDocument.ParseAsync(await recover.Content.ReadAsStreamAsync());

        Assert.True(restoreJson.RootElement.GetProperty("restoreRequested").GetBoolean());
        Assert.True(recoverJson.RootElement.GetProperty("recovered").GetBoolean());
    }
}

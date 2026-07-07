using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Beacon.Server.Tests;

public sealed class ClientApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task HelloReturnsZFoldProfileAndEditableFields()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/clients/hello", new
        {
            clientId = "z-fold-7",
            name = "Z Fold 7"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;

        Assert.Equal("z-fold-7", root.GetProperty("clientId").GetString());
        Assert.Equal(2560, root.GetProperty("profile").GetProperty("display").GetProperty("preferredWidth").GetInt32());
        Assert.Equal(1600, root.GetProperty("profile").GetProperty("display").GetProperty("preferredHeight").GetInt32());
        Assert.Contains(root.GetProperty("editableFields").EnumerateArray(), field => field.GetString() == "preferredWidth");
        Assert.DoesNotContain(root.GetProperty("editableFields").EnumerateArray(), field => field.GetString() == "mode");
    }

    [Fact]
    public async Task PatchRejectsGlobalOrDisplayPolicyFields()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PatchAsJsonAsync("/clients/z-fold-7/profile", new
        {
            mode = "mirror"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("not editable", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlanReturnsCompletePlanBeforeLaunch()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/clients/z-fold-7/plan", new
        {
            appId = "steam-shortcut:3767414131",
            title = "Dispatch",
            source = "steam-shortcut"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;

        Assert.Equal("z-fold-7", root.GetProperty("clientId").GetString());
        Assert.Equal("steam-shortcut:3767414131", root.GetProperty("appId").GetString());
        Assert.Equal("client-z-fold-7", root.GetProperty("display").GetProperty("displayId").GetString());
        Assert.Equal(2560, root.GetProperty("display").GetProperty("width").GetInt32());
        Assert.Equal(1600, root.GetProperty("display").GetProperty("height").GetInt32());
        Assert.Equal(120, root.GetProperty("stream").GetProperty("fps").GetInt32());
        Assert.True(root.GetProperty("recovery").GetProperty("restorePhysicalDisplayOnEnd").GetBoolean());
        Assert.True(root.GetProperty("recovery").GetProperty("allowClientAbort").GetBoolean());
    }

    [Fact]
    public async Task CapabilitiesAndTelemetryInfluencePlanWithoutChangingDisplayGeometry()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage capabilities = await client.PostAsJsonAsync("/clients/z-fold-7/capabilities", new
        {
            av1 = false,
            hevc = true,
            h264 = true,
            hdr10 = false,
            virtualDisplayHdrSupported = false
        });
        HttpResponseMessage telemetry = await client.PostAsJsonAsync("/clients/z-fold-7/telemetry", new
        {
            rttMs = 95,
            packetLossPercent = 3.5,
            decoderLoadPercent = 78
        });
        HttpResponseMessage plan = await client.PostAsJsonAsync("/clients/z-fold-7/plan", new
        {
            appId = "steam-shortcut:3767414131",
            title = "Dispatch",
            source = "steam-shortcut"
        });

        Assert.Equal(HttpStatusCode.OK, capabilities.StatusCode);
        Assert.Equal(HttpStatusCode.OK, telemetry.StatusCode);
        Assert.Equal(HttpStatusCode.OK, plan.StatusCode);

        using JsonDocument document = await JsonDocument.ParseAsync(await plan.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;

        Assert.Equal("hevc", root.GetProperty("stream").GetProperty("codec").GetString());
        Assert.Equal(25, root.GetProperty("stream").GetProperty("initialBitrateMbps").GetInt32());
        Assert.Equal(2560, root.GetProperty("display").GetProperty("width").GetInt32());
        Assert.Equal(1600, root.GetProperty("display").GetProperty("height").GetInt32());
    }

    [Fact]
    public async Task LaunchCreatesVirtualDisplayLeaseBeforeReportingStarted()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/clients/z-fold-7/launch", new
        {
            appId = "steam-shortcut:3767414131",
            title = "Dispatch",
            source = "steam-shortcut"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;

        Assert.Equal("client-z-fold-7", root.GetProperty("displayId").GetString());
        Assert.Equal("started", root.GetProperty("state").GetString());
    }

    [Fact]
    public async Task DisconnectQuitAndEmergencyRestoreReturnExplicitRecoveryState()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage disconnect = await client.PostAsJsonAsync("/clients/z-fold-7/disconnect", new { });
        HttpResponseMessage reconnect = await client.PostAsJsonAsync("/clients/z-fold-7/reconnect", new { });
        HttpResponseMessage quit = await client.PostAsJsonAsync("/clients/z-fold-7/quit", new
        {
            clientActive = false,
            ownedProcessRunning = false,
            ownedWindowRemaining = false
        });
        HttpResponseMessage restore = await client.PostAsJsonAsync("/clients/z-fold-7/emergency-restore", new { });

        Assert.Equal(HttpStatusCode.OK, disconnect.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reconnect.StatusCode);
        Assert.Equal(HttpStatusCode.OK, quit.StatusCode);
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);

        using JsonDocument disconnectJson = await JsonDocument.ParseAsync(await disconnect.Content.ReadAsStreamAsync());
        using JsonDocument reconnectJson = await JsonDocument.ParseAsync(await reconnect.Content.ReadAsStreamAsync());
        using JsonDocument quitJson = await JsonDocument.ParseAsync(await quit.Content.ReadAsStreamAsync());
        using JsonDocument restoreJson = await JsonDocument.ParseAsync(await restore.Content.ReadAsStreamAsync());

        Assert.True(disconnectJson.RootElement.GetProperty("leaseRetained").GetBoolean());
        Assert.Equal("reconnected", reconnectJson.RootElement.GetProperty("state").GetString());
        Assert.Equal("client-z-fold-7", reconnectJson.RootElement.GetProperty("displayId").GetString());
        Assert.True(quitJson.RootElement.GetProperty("cleanupEvaluated").GetBoolean());
        Assert.True(quitJson.RootElement.GetProperty("displayRemoved").GetBoolean());
        Assert.True(restoreJson.RootElement.GetProperty("restoreRequested").GetBoolean());
    }
}

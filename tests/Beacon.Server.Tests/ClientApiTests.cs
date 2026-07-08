using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Beacon.Core.Displays;
using Beacon.Core.Games;
using Beacon.Core.Sessions;
using Beacon.Core.Streaming;
using Beacon.Platform.Windows.Streaming;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
    public async Task UnknownClientHelloRequiresPairingToken()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/clients/hello", new
        {
            clientId = $"unknown-{Guid.NewGuid():N}",
            name = "Unknown Client"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("pair", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HelloWithPairingTokenRegistersNewClient()
    {
        WebApplicationFactory<Program> pairedFactory = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Beacon:Pairing:Token", "pair-me"));
        HttpClient client = pairedFactory.CreateClient();
        string clientId = $"windows-handheld-{Guid.NewGuid():N}";

        HttpResponseMessage response = await client.PostAsJsonAsync("/clients/hello", new
        {
            clientId,
            name = "Windows Handheld",
            pairingToken = "pair-me"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;

        Assert.Equal(clientId, root.GetProperty("clientId").GetString());
        Assert.Equal("Windows Handheld", root.GetProperty("profile").GetProperty("name").GetString());
        Assert.Equal(2560, root.GetProperty("profile").GetProperty("display").GetProperty("preferredWidth").GetInt32());
        Assert.Equal(1600, root.GetProperty("profile").GetProperty("display").GetProperty("preferredHeight").GetInt32());
    }

    [Fact]
    public async Task RegisteredClientProfilePersistsAcrossServerInstances()
    {
        string profilePath = Path.Combine(Path.GetTempPath(), $"beacon-client-profiles-{Guid.NewGuid():N}.json");
        string clientId = $"tablet-{Guid.NewGuid():N}";

        try
        {
            WebApplicationFactory<Program> firstFactory = factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Beacon:Profiles:Path", profilePath);
                builder.UseSetting("Beacon:Pairing:Token", "pair-me");
            });
            HttpClient firstClient = firstFactory.CreateClient();

            HttpResponseMessage hello = await firstClient.PostAsJsonAsync("/clients/hello", new
            {
                clientId,
                name = "Gaming Tablet",
                pairingToken = "pair-me"
            });
            HttpResponseMessage patch = await firstClient.PatchAsJsonAsync($"/clients/{clientId}/profile", new
            {
                preferredRefreshHz = 90,
                codecPreference = "hevc",
                bitrateCapMbps = 45
            });

            Assert.Equal(HttpStatusCode.OK, hello.StatusCode);
            Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

            WebApplicationFactory<Program> secondFactory = factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Beacon:Profiles:Path", profilePath);
                builder.UseSetting("Beacon:Pairing:Token", "pair-me");
            });
            HttpClient secondClient = secondFactory.CreateClient();

            HttpResponseMessage response = await secondClient.GetAsync($"/clients/{clientId}/profile");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            JsonElement root = document.RootElement;

            Assert.Equal(clientId, root.GetProperty("clientId").GetString());
            Assert.Equal("Gaming Tablet", root.GetProperty("name").GetString());
            Assert.Equal(2560, root.GetProperty("display").GetProperty("preferredWidth").GetInt32());
            Assert.Equal(1600, root.GetProperty("display").GetProperty("preferredHeight").GetInt32());
            Assert.Equal(90, root.GetProperty("display").GetProperty("preferredRefreshHz").GetInt32());
            Assert.Equal("hevc", root.GetProperty("stream").GetProperty("codecPreference").GetString());
            Assert.Equal(45, root.GetProperty("stream").GetProperty("bitrateCapMbps").GetInt32());
        }
        finally
        {
            if (File.Exists(profilePath))
            {
                File.Delete(profilePath);
            }
        }
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
    public async Task GameLibraryEndpointReturnsNormalizedGames()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/games");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;
        JsonElement games = root.GetProperty("games");

        Assert.True(games.GetArrayLength() > 0);
        JsonElement dispatch = Assert.Single(games.EnumerateArray(), game => game.GetProperty("id").GetString() == "steam-shortcut:3767414131");
        Assert.Equal("Dispatch", dispatch.GetProperty("title").GetString());
        Assert.Equal("steam-shortcut", dispatch.GetProperty("source").GetString());
        Assert.Equal("steam-rungameid", dispatch.GetProperty("launch").GetProperty("type").GetString());
        Assert.Equal("steam://rungameid/16180920483166814208", dispatch.GetProperty("launch").GetProperty("command").GetString());
        Assert.True(dispatch.GetProperty("installed").GetBoolean());
    }

    [Fact]
    public async Task PlanCanResolveNormalizedGameIdFromLibrary()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/clients/z-fold-7/plan", new
        {
            gameId = "steam-shortcut:3767414131"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;

        Assert.Equal("steam-shortcut:3767414131", root.GetProperty("appId").GetString());
        Assert.Equal(2560, root.GetProperty("display").GetProperty("width").GetInt32());
        Assert.Equal(1600, root.GetProperty("display").GetProperty("height").GetInt32());
    }

    [Fact]
    public async Task CapabilitiesAndTelemetryInfluencePlanWithoutChangingDisplayGeometry()
    {
        WebApplicationFactory<Program> pairedFactory = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Beacon:Pairing:Token", "pair-me"));
        HttpClient client = pairedFactory.CreateClient();
        string clientId = $"telemetry-plan-{Guid.NewGuid():N}";

        await client.PostAsJsonAsync("/clients/hello", new
        {
            clientId,
            name = "Telemetry Plan Client",
            pairingToken = "pair-me"
        });

        HttpResponseMessage capabilities = await client.PostAsJsonAsync($"/clients/{clientId}/capabilities", new
        {
            av1 = false,
            hevc = true,
            h264 = true,
            hdr10 = false,
            virtualDisplayHdrSupported = false
        });
        HttpResponseMessage telemetry = await client.PostAsJsonAsync($"/clients/{clientId}/telemetry", new
        {
            rttMs = 95,
            packetLossPercent = 3.5,
            decoderLoadPercent = 78,
            estimatedBandwidthMbps = 80,
            wifiBand = "wifi-5"
        });
        HttpResponseMessage plan = await client.PostAsJsonAsync($"/clients/{clientId}/plan", new
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
        Assert.Equal("lan-conservative", root.GetProperty("stream").GetProperty("transport").GetString());
        Assert.Equal("latency-protect", root.GetProperty("stream").GetProperty("congestionPolicy").GetString());
        Assert.Contains("RTT", root.GetProperty("stream").GetProperty("reason").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2560, root.GetProperty("display").GetProperty("width").GetInt32());
        Assert.Equal(1600, root.GetProperty("display").GetProperty("height").GetInt32());
    }

    [Fact]
    public async Task PlanHonorsClientBitrateCap()
    {
        WebApplicationFactory<Program> pairedFactory = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Beacon:Pairing:Token", "pair-me"));
        HttpClient client = pairedFactory.CreateClient();
        string clientId = $"bitrate-cap-{Guid.NewGuid():N}";

        await client.PostAsJsonAsync("/clients/hello", new
        {
            clientId,
            name = "Bitrate Cap Client",
            pairingToken = "pair-me"
        });
        await client.PatchAsJsonAsync($"/clients/{clientId}/profile", new
        {
            bitrateCapMbps = 40
        });
        await client.PostAsJsonAsync($"/clients/{clientId}/capabilities", new
        {
            av1 = true,
            hevc = true,
            h264 = true,
            hdr10 = false,
            virtualDisplayHdrSupported = false,
            maxFps = 120
        });
        await client.PostAsJsonAsync($"/clients/{clientId}/telemetry", new
        {
            rttMs = 8,
            packetLossPercent = 0,
            decoderLoadPercent = 20,
            estimatedBandwidthMbps = 200,
            wifiBand = "wifi-7"
        });

        HttpResponseMessage response = await client.PostAsJsonAsync($"/clients/{clientId}/plan", new
        {
            gameId = "steam-shortcut:3767414131"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;

        Assert.Equal(40, root.GetProperty("stream").GetProperty("initialBitrateMbps").GetInt32());
        Assert.Contains("bitrate cap", root.GetProperty("stream").GetProperty("reason").GetString(), StringComparison.OrdinalIgnoreCase);
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
        Assert.Equal("streaming", root.GetProperty("state").GetString());
    }

    [Fact]
    public async Task LaunchCanResolveNormalizedGameIdFromLibrary()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/clients/z-fold-7/launch", new
        {
            gameId = "steam-shortcut:3767414131"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;

        Assert.Equal("client-z-fold-7", root.GetProperty("displayId").GetString());
        Assert.Equal("streaming", root.GetProperty("state").GetString());
    }

    [Fact]
    public async Task LaunchStartsStreamingBackendWithSessionPlan()
    {
        HttpClient client = factory.CreateClient();
        await client.PostAsJsonAsync("/clients/z-fold-7/capabilities", new
        {
            av1 = true,
            hevc = true,
            h264 = true,
            hdr10 = false,
            virtualDisplayHdrSupported = false
        });

        HttpResponseMessage response = await client.PostAsJsonAsync("/clients/z-fold-7/launch", new
        {
            gameId = "steam-shortcut:3767414131"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;

        Assert.Equal("streaming", root.GetProperty("state").GetString());
        Assert.Equal("client-z-fold-7", root.GetProperty("displayId").GetString());
        Assert.Equal("steam-rungameid", root.GetProperty("launch").GetProperty("launchType").GetString());
        Assert.Equal("running", root.GetProperty("stream").GetProperty("state").GetString());
        Assert.Equal("av1", root.GetProperty("stream").GetProperty("codec").GetString());
        Assert.Equal(120, root.GetProperty("stream").GetProperty("fps").GetInt32());
        JsonElement connection = root.GetProperty("stream").GetProperty("connection");
        Assert.Equal("beacon-fake", connection.GetProperty("protocol").GetString());
        Assert.Equal("beacon-fake://stream/z-fold-7-steam-shortcut:3767414131", connection.GetProperty("launchUri").GetString());
        Assert.Equal("control", connection.GetProperty("endpoints")[0].GetProperty("role").GetString());
    }

    [Fact]
    public async Task LaunchRecordsServerOwnedSessionState()
    {
        var launcher = new FakeGameLauncher { NextProcessId = 4321 };
        var inspector = new FakeSessionActivityInspector();
        var ownership = new SessionOwnershipTracker(inspector);
        WebApplicationFactory<Program> ownedFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IGameLauncher>();
                services.RemoveAll<FakeSessionActivityInspector>();
                services.RemoveAll<ISessionActivityInspector>();
                services.RemoveAll<ISessionOwnershipTracker>();
                services.AddSingleton<IGameLauncher>(launcher);
                services.AddSingleton<ISessionActivityInspector>(inspector);
                services.AddSingleton<ISessionOwnershipTracker>(ownership);
            }));
        HttpClient client = ownedFactory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/clients/z-fold-7/launch", new
        {
            gameId = "steam-shortcut:3767414131"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(launcher.Requests);
        Assert.Equal("client-z-fold-7", launcher.Requests[0].DisplayId);
        SessionOwnershipSnapshot? snapshot = await ownership.GetSnapshotAsync("z-fold-7-steam-shortcut:3767414131", CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal(4321, snapshot.LaunchedProcessId);
        Assert.False(snapshot.HasOwnedWork);
    }

    [Fact]
    public async Task LaunchSurfacesGameLaunchFailureAndRestoresPhysicalPrimary()
    {
        var display = new FakeDisplayBackend();
        var launcher = new FakeGameLauncher { NextError = "Steam unavailable" };
        WebApplicationFactory<Program> failingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDisplayBackend>();
                services.RemoveAll<IGameLauncher>();
                services.AddSingleton<IDisplayBackend>(display);
                services.AddSingleton<IGameLauncher>(launcher);
            }));
        HttpClient client = failingFactory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/clients/z-fold-7/launch", new
        {
            gameId = "steam-shortcut:3767414131"
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Steam unavailable", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("physical-primary", display.RestoreCalls);
    }

    [Fact]
    public async Task LaunchSurfacesStreamingStartFailureAndRestoresPhysicalPrimary()
    {
        var backend = new FakeStreamingBackend { NextStartError = "encoder unavailable" };
        WebApplicationFactory<Program> failingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStreamingBackend>();
                services.AddSingleton<IStreamingBackend>(backend);
            }));
        HttpClient client = failingFactory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/clients/z-fold-7/launch", new
        {
            gameId = "steam-shortcut:3767414131"
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("encoder unavailable", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LaunchStopsBeforeDisplayLeaseWhenStreamingPreflightFails()
    {
        var display = new FakeDisplayBackend();
        var launcher = new FakeGameLauncher();
        var backend = new FakeStreamingBackend { NextPreflightError = "stream wrapper missing" };
        WebApplicationFactory<Program> failingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDisplayBackend>();
                services.RemoveAll<IGameLauncher>();
                services.RemoveAll<IStreamingBackend>();
                services.AddSingleton<IDisplayBackend>(display);
                services.AddSingleton<IGameLauncher>(launcher);
                services.AddSingleton<IStreamingBackend>(backend);
            }));
        HttpClient client = failingFactory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/clients/z-fold-7/launch", new
        {
            gameId = "steam-shortcut:3767414131"
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("stream wrapper missing", body, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(display.EnsureCalls);
        Assert.Empty(launcher.Requests);
    }

    [Fact]
    public async Task LaunchStopsBeforeDisplayLeaseWhenExternalManifestRejectsPlan()
    {
        var display = new FakeDisplayBackend();
        var launcher = new FakeGameLauncher();
        var runner = new FakeExternalStreamingProcessRunner(["C:\\Tools\\sunshine-wrapper.exe"]);
        var reader = new FakeExternalStreamingManifestReader();
        reader.Manifests["C:\\Tools\\beacon-streaming.json"] = new ExternalStreamingManifest(
            "Sunshine bridge",
            "gamestream",
            null,
            new Dictionary<string, string>(),
            ["h264"],
            60,
            40,
            Hdr10: false,
            ["lan-direct"],
            ["software"],
            ["dxgi"],
            ["AV1 disabled"]);
        var backend = new ExternalProcessStreamingBackend(
            new ExternalProcessStreamingOptions("C:\\Tools\\sunshine-wrapper.exe", ManifestPath: "C:\\Tools\\beacon-streaming.json"),
            runner,
            reader);
        WebApplicationFactory<Program> failingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDisplayBackend>();
                services.RemoveAll<IGameLauncher>();
                services.RemoveAll<IStreamingBackend>();
                services.AddSingleton<IDisplayBackend>(display);
                services.AddSingleton<IGameLauncher>(launcher);
                services.AddSingleton<IStreamingBackend>(backend);
            }));
        HttpClient client = failingFactory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/clients/z-fold-7/launch", new
        {
            gameId = "steam-shortcut:3767414131"
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("codec av1 is not supported", body, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(display.EnsureCalls);
        Assert.Empty(launcher.Requests);
        Assert.Empty(runner.StartedCommands);
    }

    [Fact]
    public async Task DisconnectQuitAndEmergencyRestoreReturnExplicitRecoveryState()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage disconnect = await client.PostAsJsonAsync("/clients/z-fold-7/disconnect", new { });
        HttpResponseMessage reconnect = await client.PostAsJsonAsync("/clients/z-fold-7/reconnect", new { });
        HttpResponseMessage quit = await client.PostAsJsonAsync("/clients/z-fold-7/quit", new
        {
            clientActive = false
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

    [Fact]
    public async Task EmergencyRestoreRejectsClientWhenProfileDisallowsIt()
    {
        WebApplicationFactory<Program> policyFactory = factory.WithWebHostBuilder(_ => { });
        HttpClient client = policyFactory.CreateClient();
        HttpResponseMessage patch = await client.PatchAsJsonAsync("/admin/clients/z-fold-7/profile", new
        {
            allowEmergencyRestoreFromClient = false
        });

        HttpResponseMessage restore = await client.PostAsJsonAsync("/clients/z-fold-7/emergency-restore", new { });

        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, restore.StatusCode);
        string body = await restore.Content.ReadAsStringAsync();
        Assert.Contains("emergency restore", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmergencyRestoreReturnsServiceUnavailableWhenPhysicalRestoreFails()
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

        HttpResponseMessage restore = await client.PostAsJsonAsync("/clients/z-fold-7/emergency-restore", new { });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, restore.StatusCode);
        string body = await restore.Content.ReadAsStringAsync();
        Assert.Contains("physical primary was not verified", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("physical-primary", Assert.Single(display.RestoreCalls));
    }

    [Fact]
    public async Task StreamStatusAndStopAreIndependentFromDisplayCleanup()
    {
        HttpClient client = factory.CreateClient();

        await client.PostAsJsonAsync("/clients/z-fold-7/launch", new { gameId = "steam-shortcut:3767414131" });
        HttpResponseMessage statusBeforeStop = await client.GetAsync("/clients/z-fold-7/stream");
        HttpResponseMessage stop = await client.PostAsJsonAsync("/clients/z-fold-7/stream/stop", new { });
        HttpResponseMessage quit = await client.PostAsJsonAsync("/clients/z-fold-7/quit", new
        {
            clientActive = false
        });

        Assert.Equal(HttpStatusCode.OK, statusBeforeStop.StatusCode);
        Assert.Equal(HttpStatusCode.OK, stop.StatusCode);
        Assert.Equal(HttpStatusCode.OK, quit.StatusCode);

        using JsonDocument statusJson = await JsonDocument.ParseAsync(await statusBeforeStop.Content.ReadAsStreamAsync());
        using JsonDocument stopJson = await JsonDocument.ParseAsync(await stop.Content.ReadAsStreamAsync());
        using JsonDocument quitJson = await JsonDocument.ParseAsync(await quit.Content.ReadAsStreamAsync());

        Assert.Equal("running", statusJson.RootElement.GetProperty("stream").GetProperty("state").GetString());
        JsonElement connection = statusJson.RootElement.GetProperty("stream").GetProperty("connection");
        Assert.Equal("beacon-fake", connection.GetProperty("protocol").GetString());
        Assert.Equal("beacon-fake://stream/z-fold-7-steam-shortcut:3767414131", connection.GetProperty("launchUri").GetString());
        Assert.Equal("control", connection.GetProperty("endpoints")[0].GetProperty("role").GetString());
        Assert.Equal("stopped", stopJson.RootElement.GetProperty("stream").GetProperty("state").GetString());
        Assert.True(quitJson.RootElement.GetProperty("displayRemoved").GetBoolean());
    }

    [Fact]
    public async Task QuitIgnoresStaleClientOwnedWorkFlagsAndUsesServerSnapshot()
    {
        HttpClient client = factory.CreateClient();

        await client.PostAsJsonAsync("/clients/z-fold-7/launch", new { gameId = "steam-shortcut:3767414131" });
        HttpResponseMessage quit = await client.PostAsJsonAsync("/clients/z-fold-7/quit", new
        {
            clientActive = false,
            ownedProcessRunning = true,
            ownedWindowRemaining = true
        });

        Assert.Equal(HttpStatusCode.OK, quit.StatusCode);
        using JsonDocument quitJson = await JsonDocument.ParseAsync(await quit.Content.ReadAsStreamAsync());

        Assert.True(quitJson.RootElement.GetProperty("displayRemoved").GetBoolean());
    }

    [Fact]
    public async Task QuitRetainsDisplayUntilServerOwnedWorkClears()
    {
        var inspector = new FakeSessionActivityInspector();
        var ownership = new SessionOwnershipTracker(inspector);
        WebApplicationFactory<Program> ownedFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<FakeSessionActivityInspector>();
                services.RemoveAll<ISessionActivityInspector>();
                services.RemoveAll<ISessionOwnershipTracker>();
                services.AddSingleton<ISessionActivityInspector>(inspector);
                services.AddSingleton<ISessionOwnershipTracker>(ownership);
            }));
        HttpClient client = ownedFactory.CreateClient();
        const string sessionId = "z-fold-7-steam-shortcut:3767414131";

        await client.PostAsJsonAsync("/clients/z-fold-7/launch", new { gameId = "steam-shortcut:3767414131" });
        inspector.SetActivity(sessionId, new SessionActivitySnapshot(true, false, false, []));
        HttpResponseMessage retained = await client.PostAsJsonAsync("/clients/z-fold-7/quit", new { clientActive = false });
        inspector.ClearActivity(sessionId);
        HttpResponseMessage removed = await client.PostAsJsonAsync("/clients/z-fold-7/quit", new { clientActive = false });

        Assert.Equal(HttpStatusCode.OK, retained.StatusCode);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        using JsonDocument retainedJson = await JsonDocument.ParseAsync(await retained.Content.ReadAsStreamAsync());
        using JsonDocument removedJson = await JsonDocument.ParseAsync(await removed.Content.ReadAsStreamAsync());

        Assert.False(retainedJson.RootElement.GetProperty("displayRemoved").GetBoolean());
        Assert.True(retainedJson.RootElement.GetProperty("ownership").GetProperty("launchedProcessRunning").GetBoolean());
        Assert.True(removedJson.RootElement.GetProperty("displayRemoved").GetBoolean());
    }

    [Fact]
    public async Task DisconnectStopsStreamAndRetainsDisplayLease()
    {
        HttpClient client = factory.CreateClient();

        await client.PostAsJsonAsync("/clients/z-fold-7/launch", new { gameId = "steam-shortcut:3767414131" });
        HttpResponseMessage disconnect = await client.PostAsJsonAsync("/clients/z-fold-7/disconnect", new { });

        Assert.Equal(HttpStatusCode.OK, disconnect.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await disconnect.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;

        Assert.True(root.GetProperty("leaseRetained").GetBoolean());
        Assert.Equal("stopped", root.GetProperty("stream").GetProperty("state").GetString());
    }

    [Fact]
    public async Task ClientDisplayRecoverRouteIsNotAvailableBecauseDisplayRecoveryIsAdminOnly()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/clients/z-fold-7/display/recover", new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed class FakeExternalStreamingProcessRunner(IEnumerable<string>? existingFiles = null) : IExternalStreamingProcessRunner
    {
        private int nextProcessId = 1001;

        public HashSet<string> ExistingFiles { get; } = new(existingFiles ?? [], StringComparer.OrdinalIgnoreCase);

        public List<ExternalStreamingCommand> StartedCommands { get; } = [];

        public bool FileExists(string path) => ExistingFiles.Contains(path);

        public ExternalStreamingProcess Start(ExternalStreamingCommand command)
        {
            StartedCommands.Add(command);
            return new ExternalStreamingProcess(nextProcessId++);
        }

        public ExternalStreamingProcessStopResult Stop(ExternalStreamingProcess process) =>
            ExternalStreamingProcessStopResult.Ok();
    }

    private sealed class FakeExternalStreamingManifestReader : IExternalStreamingManifestReader
    {
        public Dictionary<string, ExternalStreamingManifest> Manifests { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool FileExists(string path) => Manifests.ContainsKey(path);

        public ExternalStreamingManifestReadResult Read(string path) =>
            Manifests.TryGetValue(path, out ExternalStreamingManifest? manifest)
                ? ExternalStreamingManifestReadResult.Ok(manifest)
                : ExternalStreamingManifestReadResult.Fail($"External streaming manifest '{path}' does not exist.");
    }
}

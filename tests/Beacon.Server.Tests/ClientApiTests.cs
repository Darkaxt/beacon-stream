using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Beacon.Core.Displays;
using Beacon.Core.Diagnostics;
using Beacon.Core.Games;
using Beacon.Core.Input;
using Beacon.Core.Sessions;
using Beacon.Core.Streaming;
using Beacon.Server.Hosting;
using Beacon.Server.Security;
using Beacon.Server.State;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Beacon.Server.Tests;

public sealed class ClientApiTests(BeaconServerTestFactory factory) : IClassFixture<BeaconServerTestFactory>
{
    [Fact]
    public async Task StructuredCapabilitiesPersistFullHdPolicyUsedByBeaconPreparation()
    {
        string clientId = $"full-hd-handheld-{Guid.NewGuid():N}";
        var display = new FakeDisplayBackend();
        WebApplicationFactory<Program> displayFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDisplayBackend>();
                services.AddSingleton<IDisplayBackend>(display);
            }));
        HttpClient client = displayFactory.CreateClient();
        HttpResponseMessage hello = await client.PostAsJsonAsync("/clients/hello", new
        {
            clientId,
            name = "Full HD Handheld"
        });
        Assert.Equal(HttpStatusCode.OK, hello.StatusCode);

        HttpResponseMessage capabilityResponse = await client.PostAsJsonAsync($"/clients/{clientId}/capabilities", new
        {
            av1 = false,
            hevc = true,
            h264 = true,
            hdr10 = false,
            virtualDisplayHdrSupported = false,
            maxFps = 120,
            lowLatencyDecode = true,
            currentDisplayMode = new { width = 1920, height = 1080, refreshHz = 60 },
            supportedDisplayModes = new[]
            {
                new { width = 1920, height = 1080, refreshHz = 60 },
                new { width = 1280, height = 720, refreshHz = 120 }
            }
        });

        Assert.Equal(HttpStatusCode.OK, capabilityResponse.StatusCode);
        HttpResponseMessage profileResponse = await client.GetAsync($"/clients/{clientId}/profile");
        using JsonDocument profileDocument = await JsonDocument.ParseAsync(
            await profileResponse.Content.ReadAsStreamAsync());
        JsonElement selected = profileDocument.RootElement
            .GetProperty("display")
            .GetProperty("selectedMode");
        Assert.Equal(1920, selected.GetProperty("width").GetInt32());
        Assert.Equal(1080, selected.GetProperty("height").GetInt32());
        Assert.Equal(60, selected.GetProperty("refreshHz").GetInt32());

        HttpResponseMessage beacon = await client.PostAsJsonAsync(
            $"/clients/{clientId}/beacon",
            new { active = true });

        Assert.Equal(HttpStatusCode.OK, beacon.StatusCode);
        Assert.Equal($"client-{clientId}:1920x1080@60:hdr=Prefer", Assert.Single(display.PrepareCalls));
    }

    [Fact]
    public async Task AdminSnapshotRendersWorkerEvidenceAsSanitizedMetadata()
    {
        var journal = new InMemoryDiagnosticEventJournal();
        journal.Publish(DiagnosticEvent.Create(
            DiagnosticSeverity.Information,
            "stream-worker",
            "worker.media",
            "Worker media evidence accepted.",
            sessionId: "session-a",
            metadata: new Dictionary<string, string>
            {
                ["sequence"] = "2",
                ["presentationTimeUs"] = "1000000",
                ["datagramBytes"] = "56"
            }));
        WebApplicationFactory<Program> snapshotFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<InMemoryDiagnosticEventJournal>();
                services.RemoveAll<IDiagnosticEventSink>();
                services.RemoveAll<IDiagnosticEventSource>();
                services.AddSingleton(journal);
                services.AddSingleton<IDiagnosticEventSink>(journal);
                services.AddSingleton<IDiagnosticEventSource>(journal);
            }));
        HttpClient client = snapshotFactory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/admin/snapshot");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement evidence = Assert.Single(
            document.RootElement.GetProperty("diagnostics").EnumerateArray(),
            value => value.GetProperty("operation").GetString() == "worker.media");
        Assert.Equal("2", evidence.GetProperty("metadata").GetProperty("sequence").GetString());
        Assert.Equal("56", evidence.GetProperty("metadata").GetProperty("datagramBytes").GetString());
        Assert.False(evidence.TryGetProperty("ticket", out _));
        Assert.False(evidence.TryGetProperty("input", out _));
    }

    [Fact]
    public async Task ClientInputDiagnosticsRedactPayloadAndSinkErrorCanaries()
    {
        const string canary = "HTTP-INPUT-CANARY-b7e4";
        var journal = new InMemoryDiagnosticEventJournal();
        WebApplicationFactory<Program> inputFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IClientInputSink>();
                services.RemoveAll<IDiagnosticEventSink>();
                services.AddSingleton<IClientInputSink>(new FailingInputSink(canary));
                services.AddSingleton<IDiagnosticEventSink>(journal);
            }));
        HttpClient client = inputFactory.CreateClient();
        await client.PostAsJsonAsync("/clients/z-fold-7/launch", new { gameId = "steam-shortcut:3767414131" });

        HttpResponseMessage response = await client.PostAsJsonAsync("/clients/z-fold-7/input", new
        {
            sequence = 1,
            events = new[] { new { type = "keyboard", action = "press", key = canary, code = canary } }
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        string rendered = string.Join('|', journal.GetRecent(20).Select(value =>
            $"{value.Operation}:{value.Message}:{string.Join(',', value.Metadata.Select(pair => $"{pair.Key}={pair.Value}"))}"));
        Assert.DoesNotContain(canary, rendered, StringComparison.Ordinal);
        Assert.Contains("Input forwarding failed.", rendered, StringComparison.Ordinal);
    }
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
        JsonElement display = root.GetProperty("profile").GetProperty("display");
        JsonElement preferredMode = display.GetProperty("preferredMode");
        JsonElement selectedMode = display.GetProperty("selectedMode");
        Assert.Equal(2560, preferredMode.GetProperty("width").GetInt32());
        Assert.Equal(1600, preferredMode.GetProperty("height").GetInt32());
        Assert.Equal(120, preferredMode.GetProperty("refreshHz").GetInt32());
        Assert.Equal(2560, selectedMode.GetProperty("width").GetInt32());
        Assert.Equal(1600, selectedMode.GetProperty("height").GetInt32());
        Assert.Equal(120, selectedMode.GetProperty("refreshHz").GetInt32());
        Assert.Contains(root.GetProperty("editableFields").EnumerateArray(), field => field.GetString() == "preferredWidth");
        Assert.DoesNotContain(root.GetProperty("editableFields").EnumerateArray(), field => field.GetString() == "mode");
    }

    [Fact]
    public async Task ProductionRegistrationRequiresApprovalAndScopesCredential()
    {
        string credentialPath = Path.Combine(Path.GetTempPath(), $"beacon-credentials-{Guid.NewGuid():N}.json");
        string identityPath = Path.Combine(Path.GetTempPath(), $"beacon-identity-{Guid.NewGuid():N}.pfx");
        WebApplicationFactory<Program> secureFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Beacon:Security:TestHost", "false");
            builder.UseSetting("Beacon:Security:CredentialsPath", credentialPath);
            builder.UseSetting("Beacon:Security:IdentityPath", identityPath);
        });
        HttpClient client = secureFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });
        string clientId = $"secure-{Guid.NewGuid():N}";

        HttpResponseMessage response = await client.PostAsJsonAsync("/clients/hello", new
        {
            clientId,
            name = "Secure Client"
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using JsonDocument pendingDocument = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        string registrationId = Assert.IsType<string>(
            pendingDocument.RootElement.GetProperty("registrationId").GetString());
        Task<HttpResponseMessage> completion = client.GetAsync($"/clients/registrations/{registrationId}/completion");
        Assert.False(completion.IsCompleted);

        HttpResponseMessage approval = await client.PostAsJsonAsync(
            $"/admin/registrations/{registrationId}/approve",
            new { });
        HttpResponseMessage completed = await completion;

        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        using JsonDocument credentialDocument = await JsonDocument.ParseAsync(await completed.Content.ReadAsStreamAsync());
        string credential = Assert.IsType<string>(credentialDocument.RootElement.GetProperty("credential").GetString());

        HttpResponseMessage unauthenticated = await client.GetAsync($"/clients/{clientId}/profile");
        var authenticatedRequest = new HttpRequestMessage(HttpMethod.Get, $"/clients/{clientId}/profile");
        authenticatedRequest.Headers.Authorization = new AuthenticationHeaderValue("Beacon", credential);
        HttpResponseMessage authenticated = await client.SendAsync(authenticatedRequest);
        var otherRequest = new HttpRequestMessage(HttpMethod.Get, "/clients/z-fold-7/profile");
        otherRequest.Headers.Authorization = new AuthenticationHeaderValue("Beacon", credential);
        HttpResponseMessage other = await client.SendAsync(otherRequest);

        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
        Assert.DoesNotContain(credential, File.ReadAllText(credentialPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplicitTestHostRegistersNewClientWithoutSharedToken()
    {
        HttpClient client = factory.CreateClient();
        string clientId = $"windows-handheld-{Guid.NewGuid():N}";

        HttpResponseMessage response = await client.PostAsJsonAsync("/clients/hello", new
        {
            clientId,
            name = "Windows Handheld"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;

        Assert.Equal(clientId, root.GetProperty("clientId").GetString());
        Assert.Equal("Windows Handheld", root.GetProperty("profile").GetProperty("name").GetString());
        JsonElement display = root.GetProperty("profile").GetProperty("display");
        Assert.Equal(JsonValueKind.Null, display.GetProperty("preferredMode").ValueKind);
        Assert.Equal(JsonValueKind.Null, display.GetProperty("selectedMode").ValueKind);
    }

    [Fact]
    public async Task ProductionRejectsPlainHttpAndRequiresCatalogAuthentication()
    {
        string credentialPath = Path.Combine(Path.GetTempPath(), $"beacon-credentials-{Guid.NewGuid():N}.json");
        string identityPath = Path.Combine(Path.GetTempPath(), $"beacon-identity-{Guid.NewGuid():N}.pfx");
        WebApplicationFactory<Program> secureFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Beacon:Security:TestHost", "false");
            builder.UseSetting("Beacon:Security:CredentialsPath", credentialPath);
            builder.UseSetting("Beacon:Security:IdentityPath", identityPath);
        });
        HttpClient plain = secureFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false,
        });
        HttpClient secure = secureFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

        HttpResponseMessage plainHealth = await plain.GetAsync("/health");
        HttpResponseMessage catalog = await secure.GetAsync("/games");

        Assert.Equal(HttpStatusCode.UpgradeRequired, plainHealth.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, catalog.StatusCode);
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
            });
            HttpClient firstClient = firstFactory.CreateClient();

            HttpResponseMessage hello = await firstClient.PostAsJsonAsync("/clients/hello", new
            {
                clientId,
                name = "Gaming Tablet"
            });
            HttpResponseMessage patch = await firstClient.PatchAsJsonAsync($"/clients/{clientId}/profile", new
            {
                preferredWidth = 1920,
                preferredHeight = 1080,
                preferredRefreshHz = 90,
                codecPreference = "hevc",
                bitrateCapMbps = 45
            });

            Assert.Equal(HttpStatusCode.OK, hello.StatusCode);
            Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

            WebApplicationFactory<Program> secondFactory = factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Beacon:Profiles:Path", profilePath);
            });
            HttpClient secondClient = secondFactory.CreateClient();

            HttpResponseMessage response = await secondClient.GetAsync($"/clients/{clientId}/profile");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            JsonElement root = document.RootElement;

            Assert.Equal(clientId, root.GetProperty("clientId").GetString());
            Assert.Equal("Gaming Tablet", root.GetProperty("name").GetString());
            JsonElement preferredMode = root.GetProperty("display").GetProperty("preferredMode");
            Assert.Equal(1920, preferredMode.GetProperty("width").GetInt32());
            Assert.Equal(1080, preferredMode.GetProperty("height").GetInt32());
            Assert.Equal(90, preferredMode.GetProperty("refreshHz").GetInt32());
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
    public async Task CapabilitiesAndBenchmarkEvidenceInfluencePlanWithoutChangingDisplayGeometry()
    {
        WebApplicationFactory<Program> pairedFactory = factory.WithWebHostBuilder(_ => { });
        HttpClient client = pairedFactory.CreateClient();
        string clientId = $"telemetry-plan-{Guid.NewGuid():N}";

        await client.PostAsJsonAsync("/clients/hello", new
        {
            clientId,
            name = "Telemetry Plan Client"
        });

        HttpResponseMessage capabilities = await client.PostAsJsonAsync($"/clients/{clientId}/capabilities", new
        {
            av1 = false,
            hevc = true,
            h264 = true,
            hdr10 = false,
            virtualDisplayHdrSupported = false,
            currentDisplayMode = new { width = 2560, height = 1600, refreshHz = 120 },
            supportedDisplayModes = new[]
            {
                new { width = 2560, height = 1600, refreshHz = 120 }
            }
        });
        await CompleteBenchmarkAsync(client, clientId, codec: "hevc", fps: 60, throughputMbps: 36, rttMs: 95);
        HttpResponseMessage plan = await client.PostAsJsonAsync($"/clients/{clientId}/plan", new
        {
            appId = "steam-shortcut:3767414131",
            title = "Dispatch",
            source = "steam-shortcut"
        });

        Assert.Equal(HttpStatusCode.OK, capabilities.StatusCode);
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
        WebApplicationFactory<Program> pairedFactory = factory.WithWebHostBuilder(_ => { });
        HttpClient client = pairedFactory.CreateClient();
        string clientId = $"bitrate-cap-{Guid.NewGuid():N}";

        await client.PostAsJsonAsync("/clients/hello", new
        {
            clientId,
            name = "Bitrate Cap Client"
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
            maxFps = 120,
            currentDisplayMode = new { width = 2560, height = 1600, refreshHz = 120 },
            supportedDisplayModes = new[]
            {
                new { width = 2560, height = 1600, refreshHz = 120 }
            }
        });
        await CompleteBenchmarkAsync(client, clientId, codec: "av1", fps: 120, throughputMbps: 100, rttMs: 8);

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
        FakeStreamingBackend backend = Assert.IsType<FakeStreamingBackend>(
            factory.Services.GetRequiredService<IStreamingBackend>());
        backend.ActiveListenerPort = 51234;
        BeaconServerIdentity identity = factory.Services.GetRequiredService<BeaconServerIdentity>();
        HttpClient client = factory.CreateClient();
        await client.PostAsJsonAsync("/clients/z-fold-7/capabilities", new
        {
            av1 = true,
            hevc = true,
            h264 = true,
            hdr10 = false,
            virtualDisplayHdrSupported = false,
            currentDisplayMode = new { width = 2560, height = 1600, refreshHz = 120 },
            supportedDisplayModes = new[]
            {
                new { width = 2560, height = 1600, refreshHz = 120 }
            }
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
        Assert.Equal(1, root.GetProperty("connection").GetProperty("protocolVersion").GetInt32());
        JsonElement connection = root.GetProperty("connection");
        Assert.Equal("z-fold-7-steam-shortcut:3767414131", connection.GetProperty("sessionId").GetString());
        Assert.Equal(51234, connection.GetProperty("port").GetInt32());
        Assert.Equal(identity.PublicKeyFingerprint, connection.GetProperty("publicKeyFingerprint").GetString());
        SessionPlan savedPlan = Assert.IsType<SessionPlan>(
            factory.Services.GetRequiredService<InMemorySessionStore>().Get("z-fold-7"));
        Assert.Equal(
            $"{savedPlan.Display.Reason} {savedPlan.Stream.Reason} {savedPlan.Audio.Reason}",
            connection.GetProperty("planExplanation").GetString());
        JsonElement selectedVideo = connection.GetProperty("selectedVideo");
        Assert.Equal("av1", selectedVideo.GetProperty("codec").GetString());
        Assert.Equal(2560, selectedVideo.GetProperty("width").GetInt32());
        Assert.Equal(1600, selectedVideo.GetProperty("height").GetInt32());
        Assert.Equal(120, selectedVideo.GetProperty("framesPerSecondNumerator").GetInt32());
        Assert.Equal(1, selectedVideo.GetProperty("framesPerSecondDenominator").GetInt32());
        Assert.Equal("sdr", selectedVideo.GetProperty("dynamicRange").GetString());
        JsonElement selectedAudio = connection.GetProperty("selectedAudio");
        Assert.Equal("opus", selectedAudio.GetProperty("codec").GetString());
        Assert.Equal(48_000, selectedAudio.GetProperty("sampleRateHz").GetInt32());
        Assert.Equal(2, selectedAudio.GetProperty("channelCount").GetInt32());
        Assert.Equal(20_000, selectedAudio.GetProperty("frameDurationUs").GetInt32());
        Assert.Equal(96_000, selectedAudio.GetProperty("bitrateBps").GetInt32());
        ulong planRevision = root.GetProperty("connection").GetProperty("planRevision").GetUInt64();
        Assert.NotEqual(0UL, planRevision);
        byte[] publicTicket = Convert.FromBase64String(Assert.IsType<string>(
            root.GetProperty("connection").GetProperty("ticket").GetString()));
        Assert.True(publicTicket.Length >= 32);
        FakeStreamSessionAuthorizer authorizer = Assert.IsType<FakeStreamSessionAuthorizer>(
            factory.Services.GetRequiredService<IStreamSessionAuthorizer>());
        StreamRuntimeAuthorization privateAuthorization = Assert.IsType<StreamRuntimeAuthorization>(
            authorizer.Authorizations.LastOrDefault());
        Assert.Equal(planRevision, privateAuthorization.PlanRevision);
        Assert.Equal(1, privateAuthorization.RuntimeGeneration);
        Assert.Equal(32, privateAuthorization.TicketHash.Length);
        Assert.False(CryptographicOperations.FixedTimeEquals(publicTicket, privateAuthorization.TicketHash));
        Assert.True(connection.EnumerateObject().Select(property => property.Name).ToHashSet().SetEquals(
            ["protocolVersion", "ticket", "expiresAt", "planRevision", "planExplanation", "sessionId", "port", "publicKeyFingerprint", "selectedVideo", "selectedAudio"]));
        string responseJson = root.GetRawText();
        Assert.DoesNotContain(identity.IdentityPath, responseJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("privateKey", responseJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("runtimeGeneration", responseJson, StringComparison.OrdinalIgnoreCase);
        Assert.True(root.EnumerateObject().Select(property => property.Name).ToHashSet().SetEquals(
            ["clientId", "displayId", "state", "launch", "stream", "connection"]));
    }

    [Fact]
    public async Task LaunchGrantUsesCertifiedStreamModeWithoutChangingDisplayGeometry()
    {
        WebApplicationFactory<Program> streamingFactory = factory.WithWebHostBuilder(_ => { });
        FakeStreamingBackend backend = Assert.IsType<FakeStreamingBackend>(
            streamingFactory.Services.GetRequiredService<IStreamingBackend>());
        backend.ActiveListenerPort = 51235;
        HttpClient client = streamingFactory.CreateClient();
        await client.PostAsJsonAsync("/clients/z-fold-7/capabilities", new
        {
            av1 = false,
            hevc = false,
            h264 = true,
            hdr10 = false,
            virtualDisplayHdrSupported = false,
            maxFps = 60,
            currentDisplayMode = new { width = 1280, height = 720, refreshHz = 60 },
            supportedDisplayModes = new[]
            {
                new { width = 1280, height = 720, refreshHz = 60 }
            }
        });
        await CompleteBenchmarkAsync(
            client,
            "z-fold-7",
            codec: "h264",
            fps: 60,
            throughputMbps: 100,
            rttMs: 8,
            width: 1280,
            height: 720);

        HttpResponseMessage response = await client.PostAsJsonAsync("/clients/z-fold-7/launch", new
        {
            gameId = "steam-shortcut:3767414131"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement selectedVideo = document.RootElement.GetProperty("connection").GetProperty("selectedVideo");
        Assert.Equal(1280, selectedVideo.GetProperty("width").GetInt32());
        Assert.Equal(720, selectedVideo.GetProperty("height").GetInt32());
        SessionPlan savedPlan = Assert.IsType<SessionPlan>(
            streamingFactory.Services.GetRequiredService<InMemorySessionStore>().Get("z-fold-7"));
        Assert.Equal(1280, savedPlan.Display.Width);
        Assert.Equal(720, savedPlan.Display.Height);
        Assert.Equal(1280, savedPlan.Stream.Width);
        Assert.Equal(720, savedPlan.Stream.Height);
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
    public async Task LaunchRestoresDisplayWithoutStartingWorkerWhenLauncherThrows()
    {
        var display = new FakeDisplayBackend();
        var backend = new FakeStreamingBackend();
        WebApplicationFactory<Program> failingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDisplayBackend>();
                services.RemoveAll<IGameLauncher>();
                services.RemoveAll<IStreamingBackend>();
                services.AddSingleton<IDisplayBackend>(display);
                services.AddSingleton<IGameLauncher>(new ThrowingGameLauncher());
                services.AddSingleton<IStreamingBackend>(backend);
            }));
        HttpClient client = failingFactory.CreateClient();
        const string sessionId = "z-fold-7-steam-shortcut:3767414131";

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/clients/z-fold-7/launch",
            new { gameId = "steam-shortcut:3767414131" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Null(await backend.GetSessionAsync(sessionId, CancellationToken.None));
        Assert.Empty(backend.StartCalls);
        Assert.Empty(backend.StopCalls);
        Assert.Equal("physical-primary", Assert.Single(display.RestoreCalls));
        Assert.Contains(
            "application launch failed unexpectedly",
            await response.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LaunchOrdersDisplayApplicationOwnershipBeforeWorkerCapture()
    {
        var calls = new List<string>();
        var display = new OrderedDisplayBackend(calls);
        var streaming = new OrderedStreamingBackend(calls);
        var launcher = new OrderedGameLauncher(calls);
        var ownership = new OrderedOwnershipTracker(calls);
        WebApplicationFactory<Program> orderedFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDisplayBackend>();
                services.RemoveAll<IStreamingBackend>();
                services.RemoveAll<IGameLauncher>();
                services.RemoveAll<ISessionOwnershipTracker>();
                services.AddSingleton<IDisplayBackend>(display);
                services.AddSingleton<IStreamingBackend>(streaming);
                services.AddSingleton<IGameLauncher>(launcher);
                services.AddSingleton<ISessionOwnershipTracker>(ownership);
            }));
        HttpClient client = orderedFactory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/clients/z-fold-7/launch",
            new { gameId = "steam-shortcut:3767414131" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            [
                "stream.preflight",
                "display.prepare",
                "display.activate",
                "game.launch",
                "ownership.record",
                "stream.start",
            ],
            calls);
    }

    [Fact]
    public async Task TicketProvisioningFailureCompensatesInReverseStartupOrder()
    {
        var calls = new List<string>();
        var display = new OrderedDisplayBackend(calls);
        var streaming = new OrderedStreamingBackend(calls);
        var launcher = new OrderedGameLauncher(calls);
        var ownership = new OrderedOwnershipTracker(calls);
        var authorizer = new RejectingOrderedStreamSessionAuthorizer(calls);
        WebApplicationFactory<Program> orderedFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDisplayBackend>();
                services.RemoveAll<IStreamingBackend>();
                services.RemoveAll<IGameLauncher>();
                services.RemoveAll<ISessionOwnershipTracker>();
                services.RemoveAll<IStreamSessionAuthorizer>();
                services.AddSingleton<IDisplayBackend>(display);
                services.AddSingleton<IStreamingBackend>(streaming);
                services.AddSingleton<IGameLauncher>(launcher);
                services.AddSingleton<ISessionOwnershipTracker>(ownership);
                services.AddSingleton<IStreamSessionAuthorizer>(authorizer);
            }));
        HttpClient client = orderedFactory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/clients/z-fold-7/launch",
            new { gameId = "steam-shortcut:3767414131" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(
            [
                "stream.preflight",
                "display.prepare",
                "display.activate",
                "game.launch",
                "ownership.record",
                "stream.start",
                "ticket.authorize",
                "stream.stop",
                "ownership.terminate",
                "display.restore",
            ],
            calls);
    }

    [Fact]
    public async Task LaunchWorkerFailureTerminatesOwnedApplicationAndRestoresDisplay()
    {
        var backend = new FakeStreamingBackend { NextStartError = "encoder unavailable" };
        var display = new FakeDisplayBackend();
        const string sessionId = "z-fold-7-steam-shortcut:3767414131";
        var launcher = new FakeGameLauncher { NextProcessId = 4321 };
        var inspector = new FakeSessionActivityInspector();
        inspector.SetActivity(
            sessionId,
            new SessionActivitySnapshot(true, false, false, [])
            {
                OwnedProcessIds = [4321]
            });
        var terminator = new FakeSessionOwnedWorkTerminator(inspector);
        var ownership = new SessionOwnershipTracker(inspector, terminator);
        WebApplicationFactory<Program> failingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStreamingBackend>();
                services.RemoveAll<IDisplayBackend>();
                services.RemoveAll<IGameLauncher>();
                services.RemoveAll<ISessionOwnershipTracker>();
                services.AddSingleton<IStreamingBackend>(backend);
                services.AddSingleton<IDisplayBackend>(display);
                services.AddSingleton<IGameLauncher>(launcher);
                services.AddSingleton<ISessionOwnershipTracker>(ownership);
            }));
        HttpClient client = failingFactory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/clients/z-fold-7/launch", new
        {
            gameId = "steam-shortcut:3767414131"
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("encoder unavailable", body, StringComparison.OrdinalIgnoreCase);
        FakeStreamSessionAuthorizer authorizer = Assert.IsType<FakeStreamSessionAuthorizer>(
            failingFactory.Services.GetRequiredService<IStreamSessionAuthorizer>());
        Assert.Empty(authorizer.Authorizations);
        Assert.Empty(authorizer.Revocations);
        Assert.Single(display.PrepareCalls);
        Assert.Single(display.EnsureCalls);
        Assert.Equal("physical-primary", Assert.Single(display.RestoreCalls));
        Assert.Single(launcher.Requests);
        Assert.Equal(sessionId, Assert.Single(terminator.SessionIds));
        Assert.Null(await ownership.GetSnapshotAsync(sessionId, CancellationToken.None));
    }

    [Fact]
    public async Task LaunchDisplayActivationFailureRestoresBeforeWorkerApplicationOrTicket()
    {
        var backend = new FakeStreamingBackend();
        var display = new FakeDisplayBackend();
        display.EnsureResults.Enqueue(DisplayEnsureResult.Fail("activation unavailable"));
        display.EnsureResults.Enqueue(DisplayEnsureResult.Fail("activation unavailable"));
        var launcher = new FakeGameLauncher();
        WebApplicationFactory<Program> failingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStreamingBackend>();
                services.RemoveAll<IDisplayBackend>();
                services.RemoveAll<IGameLauncher>();
                services.AddSingleton<IStreamingBackend>(backend);
                services.AddSingleton<IDisplayBackend>(display);
                services.AddSingleton<IGameLauncher>(launcher);
            }));
        HttpClient client = failingFactory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/clients/z-fold-7/launch",
            new { gameId = "steam-shortcut:3767414131" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(backend.StartCalls);
        Assert.Empty(backend.StopCalls);
        Assert.Equal(2, display.RestoreCalls.Count);
        Assert.Empty(launcher.Requests);
        FakeStreamSessionAuthorizer authorizer = Assert.IsType<FakeStreamSessionAuthorizer>(
            failingFactory.Services.GetRequiredService<IStreamSessionAuthorizer>());
        Assert.Empty(authorizer.Authorizations);
    }

    [Fact]
    public async Task LaunchThrownDisplayActivationFailureRestoresWithoutStartingWorker()
    {
        var display = new ThrowingActivationDisplayBackend();
        var backend = new FakeStreamingBackend();
        var launcher = new FakeGameLauncher();
        WebApplicationFactory<Program> failingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDisplayBackend>();
                services.RemoveAll<IStreamingBackend>();
                services.RemoveAll<IGameLauncher>();
                services.AddSingleton<IDisplayBackend>(display);
                services.AddSingleton<IStreamingBackend>(backend);
                services.AddSingleton<IGameLauncher>(launcher);
            }));
        HttpClient client = failingFactory.CreateClient();
        const string sessionId = "z-fold-7-steam-shortcut:3767414131";

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/clients/z-fold-7/launch",
            new { gameId = "steam-shortcut:3767414131" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Null(await backend.GetSessionAsync(sessionId, CancellationToken.None));
        Assert.Empty(backend.StartCalls);
        Assert.Empty(backend.StopCalls);
        Assert.Equal("physical-primary", Assert.Single(display.Inner.RestoreCalls));
        Assert.Empty(launcher.Requests);
        Assert.Contains(
            "display activation failed unexpectedly",
            await response.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LaunchTicketFailureTerminatesOnlyOwnedWorkThenRestoresAndStopsWorker()
    {
        const string sessionId = "z-fold-7-steam-shortcut:3767414131";
        var backend = new FakeStreamingBackend();
        var display = new FakeDisplayBackend();
        var launcher = new FakeGameLauncher { NextProcessId = 4321 };
        var inspector = new FakeSessionActivityInspector();
        inspector.SetActivity(
            sessionId,
            new SessionActivitySnapshot(true, false, false, [])
            {
                OwnedProcessIds = [4321]
            });
        var terminator = new FakeSessionOwnedWorkTerminator(inspector);
        var ownership = new SessionOwnershipTracker(inspector, terminator);
        var authorizer = new RejectingAuthorizationAuthorizer();
        WebApplicationFactory<Program> failingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStreamingBackend>();
                services.RemoveAll<IDisplayBackend>();
                services.RemoveAll<IGameLauncher>();
                services.RemoveAll<ISessionOwnershipTracker>();
                services.RemoveAll<IStreamSessionAuthorizer>();
                services.AddSingleton<IStreamingBackend>(backend);
                services.AddSingleton<IDisplayBackend>(display);
                services.AddSingleton<IGameLauncher>(launcher);
                services.AddSingleton<ISessionOwnershipTracker>(ownership);
                services.AddSingleton<IStreamSessionAuthorizer>(authorizer);
            }));
        HttpClient client = failingFactory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/clients/z-fold-7/launch",
            new { gameId = "steam-shortcut:3767414131" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal([sessionId], terminator.SessionIds);
        Assert.Single(display.RestoreCalls);
        Assert.Equal([sessionId], backend.StopCalls);
        Assert.Null(await ownership.GetSnapshotAsync(sessionId, CancellationToken.None));
    }

    [Fact]
    public async Task LaunchCanceledTicketAuthorizationRevokesTicketAndReversesLaunch()
    {
        const string sessionId = "z-fold-7-steam-shortcut:3767414131";
        var backend = new FakeStreamingBackend();
        var display = new FakeDisplayBackend();
        var launcher = new FakeGameLauncher { NextProcessId = 4321 };
        var inspector = new FakeSessionActivityInspector();
        inspector.SetActivity(
            sessionId,
            new SessionActivitySnapshot(true, false, false, [])
            {
                OwnedProcessIds = [4321]
            });
        var terminator = new FakeSessionOwnedWorkTerminator(inspector);
        var ownership = new SessionOwnershipTracker(inspector, terminator);
        var authorizer = new CancelingAuthorizationAuthorizer();
        WebApplicationFactory<Program> failingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStreamingBackend>();
                services.RemoveAll<IDisplayBackend>();
                services.RemoveAll<IGameLauncher>();
                services.RemoveAll<ISessionOwnershipTracker>();
                services.RemoveAll<IStreamSessionAuthorizer>();
                services.AddSingleton<IStreamingBackend>(backend);
                services.AddSingleton<IDisplayBackend>(display);
                services.AddSingleton<IGameLauncher>(launcher);
                services.AddSingleton<ISessionOwnershipTracker>(ownership);
                services.AddSingleton<IStreamSessionAuthorizer>(authorizer);
            }));
        HttpClient client = failingFactory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/clients/z-fold-7/launch",
            new { gameId = "steam-shortcut:3767414131" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Single(authorizer.Revocations);
        Assert.Equal([sessionId], terminator.SessionIds);
        Assert.Equal([sessionId], backend.StopCalls);
        Assert.Single(display.RestoreCalls);
        Assert.Null(await ownership.GetSnapshotAsync(sessionId, CancellationToken.None));
    }

    [Fact]
    public async Task LaunchOwnershipRecordingFailureRecoversOwnershipThenReversesLaunch()
    {
        var display = new FakeDisplayBackend();
        var backend = new FakeStreamingBackend();
        var ownership = new ThrowingFirstRecordOwnershipTracker();
        WebApplicationFactory<Program> failingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDisplayBackend>();
                services.RemoveAll<IStreamingBackend>();
                services.RemoveAll<ISessionOwnershipTracker>();
                services.AddSingleton<IDisplayBackend>(display);
                services.AddSingleton<IStreamingBackend>(backend);
                services.AddSingleton<ISessionOwnershipTracker>(ownership);
            }));
        HttpClient client = failingFactory.CreateClient();
        const string sessionId = "z-fold-7-steam-shortcut:3767414131";

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/clients/z-fold-7/launch",
            new { gameId = "steam-shortcut:3767414131" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(2, ownership.RecordCalls);
        Assert.Equal(1, ownership.TerminationCalls);
        Assert.Null(await backend.GetSessionAsync(sessionId, CancellationToken.None));
        Assert.Empty(backend.StartCalls);
        Assert.Empty(backend.StopCalls);
        Assert.Equal("physical-primary", Assert.Single(display.RestoreCalls));
        Assert.Contains(
            "ownership recording failed unexpectedly",
            await response.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LaunchReturnsServiceUnavailableWhenStreamingRuntimeHasNoActivePort()
    {
        var backend = new FakeStreamingBackend { ActiveListenerPort = 0 };
        WebApplicationFactory<Program> failingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStreamingBackend>();
                services.AddSingleton<IStreamingBackend>(backend);
            }));
        HttpClient client = failingFactory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/clients/z-fold-7/launch",
            new { gameId = "steam-shortcut:3767414131" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("\"port\":0", body, StringComparison.Ordinal);
        Assert.Equal(["z-fold-7-steam-shortcut:3767414131"], backend.StopCalls);
        Assert.Contains("Streaming runtime stopped after invalid metadata.", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaunchReportsFailedStopCompensationForInvalidRuntimeMetadata()
    {
        var backend = new FakeStreamingBackend
        {
            ActiveListenerPort = 0,
            NextStopError = "worker stop unavailable",
        };
        WebApplicationFactory<Program> failingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStreamingBackend>();
                services.AddSingleton<IStreamingBackend>(backend);
            }));
        HttpClient client = failingFactory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/clients/z-fold-7/launch",
            new { gameId = "steam-shortcut:3767414131" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Equal(["z-fold-7-steam-shortcut:3767414131"], backend.StopCalls);
        Assert.Contains(
            "Streaming runtime stop compensation failed after invalid metadata: worker stop unavailable.",
            body,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaunchCompensationDoesNotStopReplacementRuntime()
    {
        var backend = new ReplacingInvalidStartStreamingBackend();
        WebApplicationFactory<Program> failingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStreamingBackend>();
                services.AddSingleton<IStreamingBackend>(backend);
            }));
        HttpClient client = failingFactory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/clients/z-fold-7/launch",
            new { gameId = "steam-shortcut:3767414131" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, backend.PublicStopCalls);
        Assert.Equal(1, backend.ConditionalStopCalls);
        StreamingSessionState current = Assert.IsType<StreamingSessionState>(
            await backend.GetSessionAsync("z-fold-7-steam-shortcut:3767414131", CancellationToken.None));
        Assert.Equal(backend.ReplacementGeneration, current.RuntimeGeneration);
        Assert.Equal("running", current.State);
        Assert.Contains(
            "runtime generation changed before compensation",
            await response.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LaunchStopsBeforeDisplayLeaseWhenStreamingPreflightFails()
    {
        var display = new FakeDisplayBackend();
        var launcher = new FakeGameLauncher();
        var backend = new FakeStreamingBackend { NextPreflightError = "stream unavailable" };
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
        Assert.Contains("stream unavailable", body, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(display.EnsureCalls);
        Assert.Empty(launcher.Requests);
    }
    [Fact]
    public async Task DisconnectQuitAndEmergencyRestoreReturnExplicitRecoveryState()
    {
        WebApplicationFactory<Program> recoveryFactory = factory.WithWebHostBuilder(_ => { });
        HttpClient client = recoveryFactory.CreateClient();

        HttpResponseMessage disconnect = await client.PostAsJsonAsync("/clients/z-fold-7/disconnect", new { });
        HttpResponseMessage reconnect = await client.PostAsJsonAsync("/clients/z-fold-7/reconnect", new { });
        HttpResponseMessage quit = await client.PostAsJsonAsync("/clients/z-fold-7/quit", new
        {
            clientActive = false
        });
        HttpResponseMessage restore = await client.PostAsJsonAsync("/clients/z-fold-7/emergency-restore", new { });

        Assert.Equal(HttpStatusCode.OK, disconnect.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, reconnect.StatusCode);
        Assert.Equal(HttpStatusCode.OK, quit.StatusCode);
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);

        using JsonDocument disconnectJson = await JsonDocument.ParseAsync(await disconnect.Content.ReadAsStreamAsync());
        using JsonDocument reconnectJson = await JsonDocument.ParseAsync(await reconnect.Content.ReadAsStreamAsync());
        using JsonDocument quitJson = await JsonDocument.ParseAsync(await quit.Content.ReadAsStreamAsync());
        using JsonDocument restoreJson = await JsonDocument.ParseAsync(await restore.Content.ReadAsStreamAsync());

        Assert.True(disconnectJson.RootElement.GetProperty("leaseRetained").GetBoolean());
        Assert.Equal(503, reconnectJson.RootElement.GetProperty("status").GetInt32());
        Assert.Contains(
            "no session plan",
            reconnectJson.RootElement.GetProperty("detail").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.True(quitJson.RootElement.GetProperty("cleanupEvaluated").GetBoolean());
        Assert.True(quitJson.RootElement.GetProperty("displayRemoved").GetBoolean());
        Assert.True(restoreJson.RootElement.GetProperty("recovered").GetBoolean());
        Assert.Equal("client-z-fold-7", restoreJson.RootElement.GetProperty("displayId").GetString());
    }

    [Fact]
    public async Task DisconnectRetainsControllerButExplicitQuitReleasesExactSession()
    {
        var lifecycle = new RecordingClientInputSessionLifecycle();
        using WebApplicationFactory<Program> app = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IClientInputSessionLifecycle>();
                services.AddSingleton<IClientInputSessionLifecycle>(lifecycle);
            }));
        HttpClient client = app.CreateClient();
        const string sessionId = "z-fold-7-steam-shortcut:3767414131";

        HttpResponseMessage launch = await client.PostAsJsonAsync(
            "/clients/z-fold-7/launch",
            new { gameId = "steam-shortcut:3767414131" });
        HttpResponseMessage disconnect = await client.PostAsJsonAsync(
            "/clients/z-fold-7/disconnect",
            new { });

        Assert.Equal(HttpStatusCode.OK, launch.StatusCode);
        Assert.Equal([sessionId], lifecycle.PreparedSessionIds);
        Assert.Equal(HttpStatusCode.OK, disconnect.StatusCode);
        Assert.Empty(lifecycle.SessionIds);

        HttpResponseMessage quit = await client.PostAsJsonAsync(
            "/clients/z-fold-7/quit",
            new { clientActive = false });

        Assert.Equal(HttpStatusCode.OK, quit.StatusCode);
        Assert.Equal([sessionId], lifecycle.SessionIds);
    }

    [Fact]
    public async Task ReconnectReplacesUnusedStreamTicketWithoutStoppingSession()
    {
        FakeStreamingBackend backend = Assert.IsType<FakeStreamingBackend>(
            factory.Services.GetRequiredService<IStreamingBackend>());
        backend.ActiveListenerPort = 51235;
        BeaconServerIdentity identity = factory.Services.GetRequiredService<BeaconServerIdentity>();
        HttpClient client = factory.CreateClient();
        HttpResponseMessage launch = await client.PostAsJsonAsync(
            "/clients/z-fold-7/launch",
            new { gameId = "steam-shortcut:3767414131" });
        HttpResponseMessage reconnect = await client.PostAsJsonAsync(
            "/clients/z-fold-7/reconnect",
            new { });
        HttpResponseMessage stop = await client.PostAsJsonAsync(
            "/clients/z-fold-7/stream/stop",
            new { });

        Assert.Equal(HttpStatusCode.OK, launch.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reconnect.StatusCode);
        Assert.Equal(HttpStatusCode.OK, stop.StatusCode);
        using JsonDocument launchJson = await JsonDocument.ParseAsync(await launch.Content.ReadAsStreamAsync());
        using JsonDocument reconnectJson = await JsonDocument.ParseAsync(await reconnect.Content.ReadAsStreamAsync());
        string first = Assert.IsType<string>(
            launchJson.RootElement.GetProperty("connection").GetProperty("ticket").GetString());
        string replacement = Assert.IsType<string>(
            reconnectJson.RootElement.GetProperty("connection").GetProperty("ticket").GetString());

        Assert.NotEqual(first, replacement);
        Assert.Equal("reconnected", reconnectJson.RootElement.GetProperty("state").GetString());
        JsonElement launchConnection = launchJson.RootElement.GetProperty("connection");
        JsonElement reconnectConnection = reconnectJson.RootElement.GetProperty("connection");
        Assert.Equal(launchConnection.GetProperty("sessionId").GetString(), reconnectConnection.GetProperty("sessionId").GetString());
        Assert.Equal(51235, reconnectConnection.GetProperty("port").GetInt32());
        Assert.Equal(launchConnection.GetProperty("port").GetInt32(), reconnectConnection.GetProperty("port").GetInt32());
        Assert.Equal(identity.PublicKeyFingerprint, reconnectConnection.GetProperty("publicKeyFingerprint").GetString());
        Assert.Equal(
            launchConnection.GetProperty("planExplanation").GetString(),
            reconnectConnection.GetProperty("planExplanation").GetString());
        Assert.Equal(
            launchConnection.GetProperty("selectedVideo").GetRawText(),
            reconnectConnection.GetProperty("selectedVideo").GetRawText());
        Assert.Equal(
            launchConnection.GetProperty("selectedAudio").GetRawText(),
            reconnectConnection.GetProperty("selectedAudio").GetRawText());
        FakeStreamSessionAuthorizer authorizer = Assert.IsType<FakeStreamSessionAuthorizer>(
            factory.Services.GetRequiredService<IStreamSessionAuthorizer>());
        Assert.True(authorizer.Revocations.Count >= 2);
    }

    [Fact]
    public async Task ReconnectRejectsAndRevokesTicketForSamePortAbaRuntimeReplacement()
    {
        var backend = new FakeStreamingBackend { ActiveListenerPort = 51235 };
        var authorizer = new ReplacingOnSecondAuthorizationAuthorizer();
        WebApplicationFactory<Program> raceFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStreamingBackend>();
                services.RemoveAll<IStreamSessionAuthorizer>();
                services.AddSingleton<IStreamingBackend>(backend);
                services.AddSingleton<IStreamSessionAuthorizer>(authorizer);
            }));
        HttpClient client = raceFactory.CreateClient();
        HttpResponseMessage launch = await client.PostAsJsonAsync(
            "/clients/z-fold-7/launch",
            new { gameId = "steam-shortcut:3767414131" });
        authorizer.BeforeSecondAuthorization = async () =>
        {
            SessionPlan plan = Assert.IsType<SessionPlan>(
                raceFactory.Services.GetRequiredService<InMemorySessionStore>().Get("z-fold-7"));
            Assert.True((await backend.StopAsync(plan.SessionId, CancellationToken.None)).Success);
            backend.ActiveListenerPort = 51235;
            Assert.True((await backend.StartAsync(plan, CancellationToken.None)).Success);
        };

        HttpResponseMessage reconnect = await client.PostAsJsonAsync(
            "/clients/z-fold-7/reconnect",
            new { });

        Assert.Equal(HttpStatusCode.OK, launch.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, reconnect.StatusCode);
        Assert.DoesNotContain("\"connection\"", await reconnect.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(2, authorizer.Revocations.Count);
        StreamingSessionState runtime = Assert.IsType<StreamingSessionState>(
            await backend.GetSessionAsync("z-fold-7-steam-shortcut:3767414131", CancellationToken.None));
        Assert.Equal(51235, runtime.ActiveListenerPort);
    }

    [Fact]
    public async Task ReconnectReturnsServiceUnavailableWhenPlanHasNoActiveStreamingRuntime()
    {
        var backend = new FakeStreamingBackend();
        var display = new FakeDisplayBackend();
        WebApplicationFactory<Program> inactiveFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStreamingBackend>();
                services.RemoveAll<IDisplayBackend>();
                services.AddSingleton<IStreamingBackend>(backend);
                services.AddSingleton<IDisplayBackend>(display);
            }));
        HttpClient client = inactiveFactory.CreateClient();
        HttpResponseMessage plan = await client.PostAsJsonAsync(
            "/clients/z-fold-7/plan",
            new { gameId = "steam-shortcut:3767414131" });

        HttpResponseMessage reconnect = await client.PostAsJsonAsync(
            "/clients/z-fold-7/reconnect",
            new { });

        Assert.Equal(HttpStatusCode.OK, plan.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, reconnect.StatusCode);
        Assert.Empty(backend.GetSessions());
        Assert.Empty(display.EnsureCalls);
    }

    [Fact]
    public async Task ReconnectRestartsMissingWorkerForOwnedSessionWithoutRelaunchOrDisplayActivation()
    {
        var backend = new FakeStreamingBackend { ActiveListenerPort = 51235 };
        var display = new FakeDisplayBackend();
        var launcher = new FakeGameLauncher();
        WebApplicationFactory<Program> restartFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStreamingBackend>();
                services.RemoveAll<IDisplayBackend>();
                services.RemoveAll<IGameLauncher>();
                services.AddSingleton<IStreamingBackend>(backend);
                services.AddSingleton<IDisplayBackend>(display);
                services.AddSingleton<IGameLauncher>(launcher);
            }));
        HttpClient client = restartFactory.CreateClient();
        const string sessionId = "z-fold-7-steam-shortcut:3767414131";

        HttpResponseMessage launch = await client.PostAsJsonAsync(
            "/clients/z-fold-7/launch",
            new { gameId = "steam-shortcut:3767414131" });
        StreamingSessionState firstRuntime = Assert.IsType<StreamingSessionState>(
            await backend.GetSessionAsync(sessionId, CancellationToken.None));
        int activationCountAfterLaunch = display.EnsureCalls.Count;
        Assert.True((await backend.StopRuntimeAsync(
            sessionId,
            firstRuntime.RuntimeGeneration,
            CancellationToken.None)).Success);

        HttpResponseMessage reconnect = await client.PostAsJsonAsync(
            "/clients/z-fold-7/reconnect",
            new { });

        Assert.Equal(HttpStatusCode.OK, launch.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reconnect.StatusCode);
        StreamingSessionState restartedRuntime = Assert.IsType<StreamingSessionState>(
            await backend.GetSessionAsync(sessionId, CancellationToken.None));
        Assert.Equal("running", restartedRuntime.State);
        Assert.NotEqual(firstRuntime.RuntimeGeneration, restartedRuntime.RuntimeGeneration);
        Assert.Equal(2, backend.StartCalls.Count);
        Assert.Single(launcher.Requests);
        Assert.Equal(activationCountAfterLaunch, display.EnsureCalls.Count);
        using JsonDocument reconnectJson = await JsonDocument.ParseAsync(await reconnect.Content.ReadAsStreamAsync());
        Assert.Equal(sessionId, reconnectJson.RootElement.GetProperty("connection").GetProperty("sessionId").GetString());
        Assert.Equal(51235, reconnectJson.RootElement.GetProperty("connection").GetProperty("port").GetInt32());
    }

    [Fact]
    public async Task ReconnectStopsRestartedWorkerWhenFreshTicketAuthorizationFails()
    {
        var backend = new FakeStreamingBackend { ActiveListenerPort = 51235 };
        var display = new FakeDisplayBackend();
        var launcher = new FakeGameLauncher();
        var authorizer = new RejectingSecondAuthorizationAuthorizer();
        WebApplicationFactory<Program> restartFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStreamingBackend>();
                services.RemoveAll<IDisplayBackend>();
                services.RemoveAll<IGameLauncher>();
                services.RemoveAll<IStreamSessionAuthorizer>();
                services.AddSingleton<IStreamingBackend>(backend);
                services.AddSingleton<IDisplayBackend>(display);
                services.AddSingleton<IGameLauncher>(launcher);
                services.AddSingleton<IStreamSessionAuthorizer>(authorizer);
            }));
        HttpClient client = restartFactory.CreateClient();
        const string sessionId = "z-fold-7-steam-shortcut:3767414131";

        HttpResponseMessage launch = await client.PostAsJsonAsync(
            "/clients/z-fold-7/launch",
            new { gameId = "steam-shortcut:3767414131" });
        StreamingSessionState firstRuntime = Assert.IsType<StreamingSessionState>(
            await backend.GetSessionAsync(sessionId, CancellationToken.None));
        Assert.True((await backend.StopRuntimeAsync(
            sessionId,
            firstRuntime.RuntimeGeneration,
            CancellationToken.None)).Success);

        HttpResponseMessage reconnect = await client.PostAsJsonAsync(
            "/clients/z-fold-7/reconnect",
            new { });

        Assert.Equal(HttpStatusCode.OK, launch.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, reconnect.StatusCode);
        StreamingSessionState compensatedRuntime = Assert.IsType<StreamingSessionState>(
            await backend.GetSessionAsync(sessionId, CancellationToken.None));
        Assert.Equal("stopped", compensatedRuntime.State);
        Assert.Equal(2, backend.StartCalls.Count);
        Assert.Equal(2, backend.StopCalls.Count);
        Assert.Single(launcher.Requests);
        Assert.Empty(display.RestoreCalls);
        Assert.Empty(display.RemoveCalls);
    }

    [Fact]
    public async Task ReconnectStopsRestartedWorkerWhenFreshTicketAuthorizationIsCanceled()
    {
        var backend = new FakeStreamingBackend { ActiveListenerPort = 51235 };
        var display = new FakeDisplayBackend();
        var launcher = new FakeGameLauncher();
        var authorizer = new CancelingSecondAuthorizationAuthorizer();
        WebApplicationFactory<Program> restartFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStreamingBackend>();
                services.RemoveAll<IDisplayBackend>();
                services.RemoveAll<IGameLauncher>();
                services.RemoveAll<IStreamSessionAuthorizer>();
                services.AddSingleton<IStreamingBackend>(backend);
                services.AddSingleton<IDisplayBackend>(display);
                services.AddSingleton<IGameLauncher>(launcher);
                services.AddSingleton<IStreamSessionAuthorizer>(authorizer);
            }));
        HttpClient client = restartFactory.CreateClient();
        const string sessionId = "z-fold-7-steam-shortcut:3767414131";

        HttpResponseMessage launch = await client.PostAsJsonAsync(
            "/clients/z-fold-7/launch",
            new { gameId = "steam-shortcut:3767414131" });
        StreamingSessionState firstRuntime = Assert.IsType<StreamingSessionState>(
            await backend.GetSessionAsync(sessionId, CancellationToken.None));
        Assert.True((await backend.StopRuntimeAsync(
            sessionId,
            firstRuntime.RuntimeGeneration,
            CancellationToken.None)).Success);

        HttpResponseMessage reconnect = await client.PostAsJsonAsync(
            "/clients/z-fold-7/reconnect",
            new { });

        Assert.Equal(HttpStatusCode.OK, launch.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, reconnect.StatusCode);
        StreamingSessionState compensatedRuntime = Assert.IsType<StreamingSessionState>(
            await backend.GetSessionAsync(sessionId, CancellationToken.None));
        Assert.Equal("stopped", compensatedRuntime.State);
        Assert.Equal(2, backend.StartCalls.Count);
        Assert.Equal(2, backend.StopCalls.Count);
        Assert.Equal(2, authorizer.Revocations.Count);
        Assert.Single(launcher.Requests);
        Assert.Empty(display.RestoreCalls);
        Assert.Empty(display.RemoveCalls);
    }

    [Fact]
    public async Task ReconnectStopsRestartedWorkerWhenRuntimeMetadataIsInvalid()
    {
        var backend = new FakeStreamingBackend { ActiveListenerPort = 51235 };
        var display = new FakeDisplayBackend();
        var launcher = new FakeGameLauncher();
        WebApplicationFactory<Program> restartFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStreamingBackend>();
                services.RemoveAll<IDisplayBackend>();
                services.RemoveAll<IGameLauncher>();
                services.AddSingleton<IStreamingBackend>(backend);
                services.AddSingleton<IDisplayBackend>(display);
                services.AddSingleton<IGameLauncher>(launcher);
            }));
        HttpClient client = restartFactory.CreateClient();
        const string sessionId = "z-fold-7-steam-shortcut:3767414131";

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            "/clients/z-fold-7/launch",
            new { gameId = "steam-shortcut:3767414131" })).StatusCode);
        StreamingSessionState firstRuntime = Assert.IsType<StreamingSessionState>(
            await backend.GetSessionAsync(sessionId, CancellationToken.None));
        Assert.True((await backend.StopRuntimeAsync(
            sessionId,
            firstRuntime.RuntimeGeneration,
            CancellationToken.None)).Success);
        backend.ActiveListenerPort = 0;

        HttpResponseMessage reconnect = await client.PostAsJsonAsync(
            "/clients/z-fold-7/reconnect",
            new { });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, reconnect.StatusCode);
        StreamingSessionState compensatedRuntime = Assert.IsType<StreamingSessionState>(
            await backend.GetSessionAsync(sessionId, CancellationToken.None));
        Assert.Equal("stopped", compensatedRuntime.State);
        Assert.Equal(2, backend.StartCalls.Count);
        Assert.Equal(2, backend.StopCalls.Count);
        Assert.Single(launcher.Requests);
        Assert.Empty(display.RestoreCalls);
        Assert.Empty(display.RemoveCalls);
    }

    [Fact]
    public async Task ReconnectReturnsServiceUnavailableWhenNoSessionPlanExists()
    {
        var display = new FakeDisplayBackend();
        WebApplicationFactory<Program> inactiveFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDisplayBackend>();
                services.AddSingleton<IDisplayBackend>(display);
            }));
        HttpClient client = inactiveFactory.CreateClient();

        HttpResponseMessage reconnect = await client.PostAsJsonAsync(
            "/clients/z-fold-7/reconnect",
            new { });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, reconnect.StatusCode);
        Assert.Empty(display.EnsureCalls);
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
    public async Task EmergencyRestoreUsesClientScopedRecoveryAfterInitialRestoreFailure()
    {
        var display = new FakeDisplayBackend();
        display.RestoreResults.Enqueue(DisplayRestoreResult.Fail("stale virtual topology"));
        display.RestoreResults.Enqueue(DisplayRestoreResult.Ok());
        WebApplicationFactory<Program> recoveryFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDisplayBackend>();
                services.AddSingleton<IDisplayBackend>(display);
            }));
        HttpClient client = recoveryFactory.CreateClient();

        HttpResponseMessage restore = await client.PostAsJsonAsync("/clients/z-fold-7/emergency-restore", new { });

        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await restore.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;
        Assert.Equal("z-fold-7", root.GetProperty("clientId").GetString());
        Assert.Equal("client-z-fold-7", root.GetProperty("displayId").GetString());
        Assert.True(root.GetProperty("recovered").GetBoolean());
        Assert.Equal(["physical-primary", "physical-primary"], display.RestoreCalls);
        Assert.Equal("client-z-fold-7", Assert.Single(display.RemoveCalls));
    }

    [Fact]
    public async Task EmergencyRestoreReturnsServiceUnavailableWhenClientScopedRecoveryFails()
    {
        var display = new FakeDisplayBackend
        {
            NextRestoreResult = DisplayRestoreResult.Fail("physical primary was not verified"),
            NextRemoveResult = DisplayRemoveResult.Fail("virtual display could not be removed")
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
        Assert.Contains("virtual display could not be removed", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("physical-primary", Assert.Single(display.RestoreCalls));
        Assert.Equal("client-z-fold-7", Assert.Single(display.RemoveCalls));
    }

    [Fact]
    public async Task StreamStatusAndStopAreIndependentFromDisplayCleanup()
    {
        var backend = new FakeStreamingBackend();
        WebApplicationFactory<Program> stopFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStreamingBackend>();
                services.AddSingleton<IStreamingBackend>(backend);
            }));
        HttpClient client = stopFactory.CreateClient();

        await client.PostAsJsonAsync("/clients/z-fold-7/launch", new { gameId = "steam-shortcut:3767414131" });
        HttpResponseMessage statusBeforeStop = await client.GetAsync("/clients/z-fold-7/stream");
        HttpResponseMessage stop = await client.PostAsJsonAsync("/clients/z-fold-7/stream/stop", new { });
        backend.NextStopError = "duplicate stop must not be attempted";
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
        Assert.Equal("stopped", stopJson.RootElement.GetProperty("stream").GetProperty("state").GetString());
        Assert.True(quitJson.RootElement.GetProperty("displayRemoved").GetBoolean());
    }
    [Fact]
    public async Task ClientStreamStopReturnsServiceUnavailableWhenBackendStopFails()
    {
        WebApplicationFactory<Program> failingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStreamingBackend>();
                services.AddSingleton<IStreamingBackend>(new FailingStopStreamingBackend("stream stop failed"));
            }));
        HttpClient client = failingFactory.CreateClient();

        HttpResponseMessage launch = await client.PostAsJsonAsync("/clients/z-fold-7/launch", new { gameId = "steam-shortcut:3767414131" });
        HttpResponseMessage stop = await client.PostAsJsonAsync("/clients/z-fold-7/stream/stop", new { });

        Assert.Equal(HttpStatusCode.OK, launch.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, stop.StatusCode);
        string body = await stop.Content.ReadAsStringAsync();
        Assert.Contains("stream stop failed", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InactiveDisconnectReturnsServiceUnavailableWhenBackendStopFails()
    {
        WebApplicationFactory<Program> failingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStreamingBackend>();
                services.AddSingleton<IStreamingBackend>(new FailingStopStreamingBackend("stream stop failed"));
            }));
        HttpClient client = failingFactory.CreateClient();

        HttpResponseMessage launch = await client.PostAsJsonAsync("/clients/z-fold-7/launch", new { gameId = "steam-shortcut:3767414131" });
        HttpResponseMessage disconnect = await client.PostAsJsonAsync(
            "/clients/z-fold-7/disconnect",
            new { clientActive = false });

        Assert.Equal(HttpStatusCode.OK, launch.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, disconnect.StatusCode);
        string body = await disconnect.Content.ReadAsStringAsync();
        Assert.Contains("stream stop failed", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task QuitReturnsServiceUnavailableWhenBackendStopFailsAndSkipsDisplayCleanup()
    {
        var display = new FakeDisplayBackend();
        WebApplicationFactory<Program> failingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDisplayBackend>();
                services.RemoveAll<IStreamingBackend>();
                services.AddSingleton<IDisplayBackend>(display);
                services.AddSingleton<IStreamingBackend>(new FailingStopStreamingBackend("stream stop failed"));
            }));
        HttpClient client = failingFactory.CreateClient();

        HttpResponseMessage launch = await client.PostAsJsonAsync("/clients/z-fold-7/launch", new { gameId = "steam-shortcut:3767414131" });
        HttpResponseMessage quit = await client.PostAsJsonAsync("/clients/z-fold-7/quit", new { clientActive = false });

        Assert.Equal(HttpStatusCode.OK, launch.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, quit.StatusCode);
        string body = await quit.Content.ReadAsStringAsync();
        Assert.Contains("stream stop failed", body, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(display.RemoveCalls);
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
    public async Task QuitTerminatesVerifiedOwnedWorkBeforeRestoringAndRemovingDisplay()
    {
        var display = new FakeDisplayBackend();
        var inspector = new FakeSessionActivityInspector();
        var terminator = new FakeSessionOwnedWorkTerminator(inspector);
        var ownership = new SessionOwnershipTracker(inspector, terminator);
        WebApplicationFactory<Program> ownedFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDisplayBackend>();
                services.RemoveAll<FakeSessionActivityInspector>();
                services.RemoveAll<ISessionActivityInspector>();
                services.RemoveAll<ISessionOwnedWorkTerminator>();
                services.RemoveAll<ISessionOwnershipTracker>();
                services.AddSingleton<IDisplayBackend>(display);
                services.AddSingleton<ISessionActivityInspector>(inspector);
                services.AddSingleton<ISessionOwnedWorkTerminator>(terminator);
                services.AddSingleton<ISessionOwnershipTracker>(ownership);
            }));
        HttpClient client = ownedFactory.CreateClient();
        const string sessionId = "z-fold-7-steam-shortcut:3767414131";

        await client.PostAsJsonAsync("/clients/z-fold-7/launch", new { gameId = "steam-shortcut:3767414131" });
        inspector.SetActivity(
            sessionId,
            new SessionActivitySnapshot(true, false, false, ["Launched process is still running."])
            {
                OwnedProcessIds = [4242]
            });

        HttpResponseMessage quit = await client.PostAsJsonAsync(
            "/clients/z-fold-7/quit",
            new { clientActive = false });

        Assert.Equal(HttpStatusCode.OK, quit.StatusCode);
        using JsonDocument quitJson = await JsonDocument.ParseAsync(await quit.Content.ReadAsStreamAsync());
        Assert.Equal(sessionId, Assert.Single(terminator.SessionIds));
        Assert.True(quitJson.RootElement.GetProperty("ownedWorkTermination").GetProperty("success").GetBoolean());
        Assert.True(quitJson.RootElement.GetProperty("displayRemoved").GetBoolean());
        Assert.Equal("physical-primary", Assert.Single(display.RestoreCalls));
        Assert.Equal("client-z-fold-7", Assert.Single(display.RemoveCalls));
        Assert.Null(await ownership.GetSnapshotAsync(sessionId, CancellationToken.None));
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
    public async Task ActiveDisconnectRetainsRuntimeAndReconnectsWithoutRestartOrDisplayActivation()
    {
        var backend = new FakeStreamingBackend { ActiveListenerPort = 51235 };
        var display = new FakeDisplayBackend();
        WebApplicationFactory<Program> reconnectFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStreamingBackend>();
                services.RemoveAll<IDisplayBackend>();
                services.AddSingleton<IStreamingBackend>(backend);
                services.AddSingleton<IDisplayBackend>(display);
            }));
        HttpClient client = reconnectFactory.CreateClient();

        HttpResponseMessage launch = await client.PostAsJsonAsync(
            "/clients/z-fold-7/launch",
            new { gameId = "steam-shortcut:3767414131" });
        StreamingSessionState runtimeBeforeDisconnect = Assert.IsType<StreamingSessionState>(
            await backend.GetSessionAsync(
                "z-fold-7-steam-shortcut:3767414131",
                CancellationToken.None));
        int activationCountAfterLaunch = display.EnsureCalls.Count;
        HttpResponseMessage disconnect = await client.PostAsJsonAsync("/clients/z-fold-7/disconnect", new { });
        HttpResponseMessage reconnect = await client.PostAsJsonAsync("/clients/z-fold-7/reconnect", new { });

        Assert.Equal(HttpStatusCode.OK, launch.StatusCode);
        Assert.Equal(HttpStatusCode.OK, disconnect.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reconnect.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await disconnect.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;
        using JsonDocument reconnectDocument = await JsonDocument.ParseAsync(await reconnect.Content.ReadAsStreamAsync());
        StreamingSessionState runtimeAfterReconnect = Assert.IsType<StreamingSessionState>(
            await backend.GetSessionAsync(
                "z-fold-7-steam-shortcut:3767414131",
                CancellationToken.None));

        Assert.True(root.GetProperty("leaseRetained").GetBoolean());
        Assert.Equal("running", root.GetProperty("stream").GetProperty("state").GetString());
        Assert.Equal(51235, reconnectDocument.RootElement.GetProperty("connection").GetProperty("port").GetInt32());
        Assert.Equal(runtimeBeforeDisconnect.RuntimeGeneration, runtimeAfterReconnect.RuntimeGeneration);
        Assert.Single(backend.StartCalls);
        Assert.Empty(backend.StopCalls);
        Assert.Equal(activationCountAfterLaunch, display.EnsureCalls.Count);
    }

    [Fact]
    public async Task DisconnectWithoutBodyDefaultsToActiveClientAndRetainsDisplayLease()
    {
        HttpClient client = factory.CreateClient();

        await client.PostAsJsonAsync("/clients/z-fold-7/launch", new { gameId = "steam-shortcut:3767414131" });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/clients/z-fold-7/disconnect");
        HttpResponseMessage disconnect = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, disconnect.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await disconnect.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;

        Assert.True(root.GetProperty("leaseRetained").GetBoolean());
        Assert.False(root.GetProperty("displayRemoved").GetBoolean());
        Assert.Equal("running", root.GetProperty("stream").GetProperty("state").GetString());
    }

    [Fact]
    public async Task DisconnectWithInactiveClientRemovesDisplayWhenNoOwnedWorkRemains()
    {
        var display = new FakeDisplayBackend();
        WebApplicationFactory<Program> displayFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDisplayBackend>();
                services.AddSingleton<IDisplayBackend>(display);
            }));
        HttpClient client = displayFactory.CreateClient();

        await client.PostAsJsonAsync("/clients/z-fold-7/launch", new { gameId = "steam-shortcut:3767414131" });
        HttpResponseMessage disconnect = await client.PostAsJsonAsync("/clients/z-fold-7/disconnect", new { clientActive = false });

        Assert.Equal(HttpStatusCode.OK, disconnect.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await disconnect.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;

        Assert.False(root.GetProperty("leaseRetained").GetBoolean());
        Assert.True(root.GetProperty("displayRemoved").GetBoolean());
        Assert.Equal("stopped", root.GetProperty("stream").GetProperty("state").GetString());
        Assert.Equal("physical-primary", Assert.Single(display.RestoreCalls));
        Assert.Equal("client-z-fold-7", Assert.Single(display.RemoveCalls));
    }

    [Fact]
    public async Task InactiveDisconnectRevokesUnusedStreamTicketBeforeDisplayCleanup()
    {
        WebApplicationFactory<Program> disconnectFactory =
            factory.WithWebHostBuilder(_ => { });
        HttpClient client = disconnectFactory.CreateClient();

        HttpResponseMessage launch = await client.PostAsJsonAsync(
            "/clients/z-fold-7/launch",
            new { gameId = "steam-shortcut:3767414131" });
        Assert.Equal(HttpStatusCode.OK, launch.StatusCode);
        using JsonDocument launchDocument = await JsonDocument.ParseAsync(
            await launch.Content.ReadAsStreamAsync());
        JsonElement connection = launchDocument.RootElement.GetProperty("connection");
        string ticket = connection.GetProperty("ticket").GetString()!;
        string sessionId = connection.GetProperty("sessionId").GetString()!;
        ulong planRevision = connection.GetProperty("planRevision").GetUInt64();

        HttpResponseMessage disconnect = await client.PostAsJsonAsync(
            "/clients/z-fold-7/disconnect",
            new { clientActive = false });

        Assert.Equal(HttpStatusCode.OK, disconnect.StatusCode);
        StreamTicketService tickets = disconnectFactory.Services
            .GetRequiredService<StreamTicketService>();
        StreamTicketValidation validation = tickets.Consume(
            ticket,
            "z-fold-7",
            sessionId,
            planRevision,
            [0x42, 0x45, 0x41, 0x43, 0x4f, 0x4e],
            DateTimeOffset.UtcNow);
        Assert.False(validation.Success);
        Assert.Equal(StreamTicketFailure.Revoked, validation.Failure);
    }

    [Fact]
    public async Task InactiveDisconnectSkipsDisplayCleanupWhenTicketRevocationFails()
    {
        var display = new FakeDisplayBackend();
        WebApplicationFactory<Program> failingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDisplayBackend>();
                services.RemoveAll<IStreamSessionAuthorizer>();
                services.AddSingleton<IDisplayBackend>(display);
                services.AddSingleton<IStreamSessionAuthorizer>(
                    new RejectingRevocationAuthorizer());
            }));
        HttpClient client = failingFactory.CreateClient();

        HttpResponseMessage launch = await client.PostAsJsonAsync(
            "/clients/z-fold-7/launch",
            new { gameId = "steam-shortcut:3767414131" });
        HttpResponseMessage disconnect = await client.PostAsJsonAsync(
            "/clients/z-fold-7/disconnect",
            new { clientActive = false });

        Assert.Equal(HttpStatusCode.OK, launch.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, disconnect.StatusCode);
        Assert.Contains(
            "ticket revocation rejected",
            await disconnect.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Empty(display.RestoreCalls);
        Assert.Empty(display.RemoveCalls);
    }

    [Fact]
    public async Task DisconnectWithUnknownLengthJsonBodyParsesInactiveClient()
    {
        var display = new FakeDisplayBackend();
        WebApplicationFactory<Program> displayFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDisplayBackend>();
                services.AddSingleton<IDisplayBackend>(display);
            }));
        HttpClient client = displayFactory.CreateClient();

        await client.PostAsJsonAsync("/clients/z-fold-7/launch", new { gameId = "steam-shortcut:3767414131" });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/clients/z-fold-7/disconnect")
        {
            Content = new UnknownLengthJsonContent("""{"clientActive":false}""", "application/json")
        };
        HttpResponseMessage disconnect = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, disconnect.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await disconnect.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;

        Assert.False(root.GetProperty("leaseRetained").GetBoolean());
        Assert.True(root.GetProperty("displayRemoved").GetBoolean());
        Assert.Equal("physical-primary", Assert.Single(display.RestoreCalls));
        Assert.Equal("client-z-fold-7", Assert.Single(display.RemoveCalls));
    }

    [Fact]
    public async Task BeaconActiveClientPreparesDisplayLeaseWithoutActivatingSession()
    {
        var display = new FakeDisplayBackend();
        WebApplicationFactory<Program> displayFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDisplayBackend>();
                services.AddSingleton<IDisplayBackend>(display);
            }));
        HttpClient client = displayFactory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/clients/z-fold-7/beacon", new { active = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;

        Assert.Equal("active", root.GetProperty("state").GetString());
        Assert.Equal("client-z-fold-7", root.GetProperty("displayId").GetString());
        Assert.True(root.GetProperty("leasePrepared").GetBoolean());
        Assert.False(root.GetProperty("displayRemoved").GetBoolean());
        Assert.Equal("client-z-fold-7:2560x1600@120:hdr=Prefer", Assert.Single(display.PrepareCalls));
        Assert.Empty(display.EnsureCalls);
        Assert.Empty(display.RemoveCalls);
    }

    [Fact]
    public async Task BeaconRejectsUnknownClientWithoutDisplaySideEffects()
    {
        var display = new FakeDisplayBackend();
        WebApplicationFactory<Program> displayFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDisplayBackend>();
                services.AddSingleton<IDisplayBackend>(display);
            }));
        HttpClient client = displayFactory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/clients/unknown-client/beacon", new { active = true });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(display.EnsureCalls);
        Assert.Empty(display.RemoveCalls);
    }

    [Fact]
    public async Task BeaconInactiveClientRemovesDisplayWhenNoOwnedWorkRemains()
    {
        var display = new FakeDisplayBackend();
        WebApplicationFactory<Program> displayFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDisplayBackend>();
                services.AddSingleton<IDisplayBackend>(display);
            }));
        HttpClient client = displayFactory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/clients/z-fold-7/beacon", new { active = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;

        Assert.Equal("inactive", root.GetProperty("state").GetString());
        Assert.False(root.GetProperty("leasePrepared").GetBoolean());
        Assert.True(root.GetProperty("displayRemoved").GetBoolean());
        Assert.Equal("physical-primary", Assert.Single(display.RestoreCalls));
        Assert.Equal("client-z-fold-7", Assert.Single(display.RemoveCalls));
    }

    [Fact]
    public async Task BeaconInactiveStopsRuntimeBeforeRemovingEmptyLaunchedSessionDisplay()
    {
        var display = new FakeDisplayBackend();
        var backend = new FakeStreamingBackend();
        WebApplicationFactory<Program> displayFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDisplayBackend>();
                services.RemoveAll<IStreamingBackend>();
                services.AddSingleton<IDisplayBackend>(display);
                services.AddSingleton<IStreamingBackend>(backend);
            }));
        HttpClient client = displayFactory.CreateClient();
        const string sessionId = "z-fold-7-steam-shortcut:3767414131";

        await client.PostAsJsonAsync(
            "/clients/z-fold-7/launch",
            new { gameId = "steam-shortcut:3767414131" });
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/clients/z-fold-7/beacon",
            new { active = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        StreamingSessionState stopped = Assert.IsType<StreamingSessionState>(
            await backend.GetSessionAsync(sessionId, CancellationToken.None));
        Assert.Equal("stopped", stopped.State);
        Assert.Equal("physical-primary", Assert.Single(display.RestoreCalls));
        Assert.Equal("client-z-fold-7", Assert.Single(display.RemoveCalls));
    }

    [Fact]
    public async Task BeaconInactiveClientRetainsDisplayWhenServerOwnedWorkRemains()
    {
        var display = new FakeDisplayBackend();
        var inspector = new FakeSessionActivityInspector();
        var ownership = new SessionOwnershipTracker(inspector);
        WebApplicationFactory<Program> ownedFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDisplayBackend>();
                services.RemoveAll<FakeSessionActivityInspector>();
                services.RemoveAll<ISessionActivityInspector>();
                services.RemoveAll<ISessionOwnershipTracker>();
                services.AddSingleton<IDisplayBackend>(display);
                services.AddSingleton<ISessionActivityInspector>(inspector);
                services.AddSingleton<ISessionOwnershipTracker>(ownership);
            }));
        HttpClient client = ownedFactory.CreateClient();
        const string sessionId = "z-fold-7-steam-shortcut:3767414131";

        await client.PostAsJsonAsync("/clients/z-fold-7/launch", new { gameId = "steam-shortcut:3767414131" });
        inspector.SetActivity(sessionId, new SessionActivitySnapshot(false, true, false, []));
        HttpResponseMessage response = await client.PostAsJsonAsync("/clients/z-fold-7/beacon", new { active = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;

        Assert.Equal("inactive", root.GetProperty("state").GetString());
        Assert.False(root.GetProperty("leasePrepared").GetBoolean());
        Assert.False(root.GetProperty("displayRemoved").GetBoolean());
        Assert.True(root.GetProperty("ownership").GetProperty("childProcessRunning").GetBoolean());
        Assert.Empty(display.RestoreCalls);
        Assert.Empty(display.RemoveCalls);
    }

    [Fact]
    public async Task DisconnectWithInactiveClientRetainsDisplayWhenServerOwnedWorkRemains()
    {
        var display = new FakeDisplayBackend();
        var inspector = new FakeSessionActivityInspector();
        var ownership = new SessionOwnershipTracker(inspector);
        WebApplicationFactory<Program> ownedFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDisplayBackend>();
                services.RemoveAll<FakeSessionActivityInspector>();
                services.RemoveAll<ISessionActivityInspector>();
                services.RemoveAll<ISessionOwnershipTracker>();
                services.AddSingleton<IDisplayBackend>(display);
                services.AddSingleton<ISessionActivityInspector>(inspector);
                services.AddSingleton<ISessionOwnershipTracker>(ownership);
            }));
        HttpClient client = ownedFactory.CreateClient();
        const string sessionId = "z-fold-7-steam-shortcut:3767414131";

        await client.PostAsJsonAsync("/clients/z-fold-7/launch", new { gameId = "steam-shortcut:3767414131" });
        inspector.SetActivity(sessionId, new SessionActivitySnapshot(true, false, false, []));
        HttpResponseMessage disconnect = await client.PostAsJsonAsync("/clients/z-fold-7/disconnect", new { clientActive = false });

        Assert.Equal(HttpStatusCode.OK, disconnect.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await disconnect.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;

        Assert.True(root.GetProperty("leaseRetained").GetBoolean());
        Assert.False(root.GetProperty("displayRemoved").GetBoolean());
        Assert.True(root.GetProperty("ownership").GetProperty("launchedProcessRunning").GetBoolean());
        Assert.Empty(display.RestoreCalls);
        Assert.Empty(display.RemoveCalls);
    }

    private sealed class UnknownLengthJsonContent : HttpContent
    {
        private readonly byte[] content;

        public UnknownLengthJsonContent(string json, string contentType)
        {
            content = Encoding.UTF8.GetBytes(json);
            Headers.TryAddWithoutValidation("Content-Type", contentType);
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return stream.WriteAsync(content, 0, content.Length);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }

    [Fact]
    public async Task ClientInputForwardsToActiveStreamSession()
    {
        var input = new RecordingClientInputSink();
        WebApplicationFactory<Program> inputFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IClientInputSink>();
                services.AddSingleton<IClientInputSink>(input);
            }));
        HttpClient client = inputFactory.CreateClient();
        await client.PostAsJsonAsync("/clients/z-fold-7/launch", new { gameId = "steam-shortcut:3767414131" });

        HttpResponseMessage response = await client.PostAsJsonAsync("/clients/z-fold-7/input", new
        {
            sequence = 42,
            events = new[]
            {
                new
                {
                    type = "pointer",
                    action = "move",
                    pointerId = 1,
                    x = 0.5,
                    y = 0.25,
                    buttons = 1
                }
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ClientInputBatch batch = Assert.Single(input.Batches);
        Assert.Equal("z-fold-7", batch.ClientId);
        Assert.Equal("z-fold-7-steam-shortcut:3767414131", batch.SessionId);
        Assert.Equal("client-z-fold-7", batch.DisplayId);
        Assert.Equal(42, batch.Sequence);
        ClientInputEvent inputEvent = Assert.Single(batch.Events);
        Assert.Equal("pointer", inputEvent.Type);
        Assert.Equal("move", inputEvent.Action);
        Assert.Equal(1, inputEvent.PointerId);
        Assert.Equal(0.5, inputEvent.X);
        Assert.Equal(0.25, inputEvent.Y);
        Assert.Equal(1, inputEvent.Buttons);

        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.True(document.RootElement.GetProperty("accepted").GetBoolean());
        Assert.Equal(1, document.RootElement.GetProperty("eventCount").GetInt32());
    }

    [Fact]
    public async Task ClientInputHttpBindingIgnoresWorkerOnlyStructuredPayloads()
    {
        var input = new RecordingClientInputSink();
        WebApplicationFactory<Program> inputFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IClientInputSink>();
                services.AddSingleton<IClientInputSink>(input);
            }));
        HttpClient client = inputFactory.CreateClient();
        await client.PostAsJsonAsync("/clients/z-fold-7/launch", new { gameId = "steam-shortcut:3767414131" });

        HttpResponseMessage response = await client.PostAsJsonAsync("/clients/z-fold-7/input", new
        {
            sequence = 44,
            events = new[]
            {
                new
                {
                    type = "pointer",
                    action = "move",
                    pointerId = 1,
                    x = 0.5,
                    y = 0.25,
                    pointer = new { action = 0, x = 32768, y = 32768, wheelDelta = 0, button = 0 },
                    keyboard = new { scanCode = 30, pressed = true },
                    controller = new { controllerIndex = 1, controlId = 2, value = 3 },
                    touch = new
                    {
                        contactId = 1,
                        action = 0,
                        xNumerator = 1,
                        yNumerator = 2,
                        coordinateDenominator = 3,
                        pressureNumerator = 4,
                        pressureDenominator = 5
                    }
                }
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ClientInputEvent inputEvent = Assert.Single(Assert.Single(input.Batches).Events);
        Assert.Null(inputEvent.Pointer);
        Assert.Null(inputEvent.Keyboard);
        Assert.Null(inputEvent.Controller);
        Assert.Null(inputEvent.Touch);
        Assert.Equal(0.5, inputEvent.X);
        Assert.Equal(0.25, inputEvent.Y);
    }

    [Fact]
    public async Task ClientInputForwardsKeyboardEventToActiveStreamSession()
    {
        var input = new RecordingClientInputSink();
        WebApplicationFactory<Program> inputFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IClientInputSink>();
                services.AddSingleton<IClientInputSink>(input);
            }));
        HttpClient client = inputFactory.CreateClient();
        await client.PostAsJsonAsync("/clients/z-fold-7/launch", new { gameId = "steam-shortcut:3767414131" });

        HttpResponseMessage response = await client.PostAsJsonAsync("/clients/z-fold-7/input", new
        {
            sequence = 43,
            events = new[]
            {
                new
                {
                    type = "keyboard",
                    action = "press",
                    key = "Escape",
                    code = "Escape"
                }
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ClientInputBatch batch = Assert.Single(input.Batches);
        Assert.Equal("z-fold-7", batch.ClientId);
        Assert.Equal("z-fold-7-steam-shortcut:3767414131", batch.SessionId);
        Assert.Equal("client-z-fold-7", batch.DisplayId);
        Assert.Equal(43, batch.Sequence);
        ClientInputEvent inputEvent = Assert.Single(batch.Events);
        Assert.Equal("keyboard", inputEvent.Type);
        Assert.Equal("press", inputEvent.Action);
        Assert.Equal("Escape", inputEvent.Key);
        Assert.Equal("Escape", inputEvent.Code);

        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.True(document.RootElement.GetProperty("accepted").GetBoolean());
        Assert.Equal(1, document.RootElement.GetProperty("eventCount").GetInt32());
    }

    [Fact]
    public async Task ClientInputRequiresActiveStreamSession()
    {
        var input = new RecordingClientInputSink();
        WebApplicationFactory<Program> inputFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IClientInputSink>();
                services.AddSingleton<IClientInputSink>(input);
            }));
        HttpClient client = inputFactory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/clients/z-fold-7/input", new
        {
            sequence = 1,
            events = new[]
            {
                new { type = "pointer", action = "tap", pointerId = 1, x = 0.5, y = 0.5 }
            }
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(input.Batches);
    }

    [Fact]
    public async Task ClientDisplayRecoverRouteIsNotAvailableBecauseDisplayRecoveryIsAdminOnly()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/clients/z-fold-7/display/recover", new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task CompleteBenchmarkAsync(
        HttpClient client,
        string clientId,
        string codec,
        int fps,
        double throughputMbps,
        double rttMs,
        int width = 2560,
        int height = 1600)
    {
        object fingerprints = new
        {
            network = new
            {
                schemaVersion = 3,
                serverRoute = "192.168.1.10",
                transport = "wifi",
                localNetworkPrefix = "192.168.1.0/24",
                wifiBand = "6-ghz",
                wifiChannel = 37,
                linkSpeedBucket = "500-999-mbps",
                saltedNetworkIdHash = new string('a', 64)
            },
            hardware = new
            {
                schemaVersion = 3,
                deviceCapabilityRevision = "test-capabilities",
                androidVersion = "16",
                apkVersion = "test",
                displayModeInventoryRevision = $"{width}x{height}-{fps}",
                codecInventoryRevision = codec
            }
        };
        HttpResponseMessage prepare = await client.PostAsJsonAsync(
            $"/clients/{clientId}/benchmarks/prepare",
            new { trigger = "automatic", fingerprints });
        Assert.Equal(HttpStatusCode.OK, prepare.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await prepare.Content.ReadAsStreamAsync());
        Guid runId = document.RootElement.GetProperty("runId").GetGuid();
        int expectedPackets = document.RootElement.GetProperty("networkCoverage").GetProperty("expectedPacketCount").GetInt32();
        int payloadBytes = document.RootElement.GetProperty("transportPlan").GetProperty("datagramPayloadBytes").GetInt32();

        HttpResponseMessage complete = await client.PostAsJsonAsync(
            $"/clients/{clientId}/benchmarks/{runId:D}/complete",
            new
            {
                networkSamples = Enumerable.Range(0, expectedPackets)
                    .Select(sequence => new { sequence, payloadBytes, rttMs, jitterMs = 1.0, received = true, throughputMbps, reorderDistance = 0 }),
                decoderSamples = new[]
                {
                    new { codec, profile = "main", bitDepth = 8, width, height, targetFps = fps, configured = true, sustainedFps = fps, p95DecodeLatencyMs = 5, p95PresentationLatencyMs = 9, droppedFrames = 0, outputErrors = 0 }
                },
                powerSamples = new[]
                {
                    new { batteryPercent = 80, isCharging = false, thermalState = "nominal" }
                }
            });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
    }

    private sealed class RecordingClientInputSink : IClientInputSink
    {
        public List<ClientInputBatch> Batches { get; } = [];

        public Task<ClientInputResult> ForwardAsync(ClientInputBatch batch, CancellationToken cancellationToken)
        {
            Batches.Add(batch);
            return Task.FromResult(ClientInputResult.Ok(batch.Events.Count));
        }
    }

    private sealed class FailingInputSink(string error) : IClientInputSink
    {
        public Task<ClientInputResult> ForwardAsync(
            ClientInputBatch batch,
            CancellationToken cancellationToken) =>
            Task.FromResult(ClientInputResult.Fail(error));
    }

    private sealed class ReplacingOnSecondAuthorizationAuthorizer : IStreamSessionAuthorizer
    {
        private static readonly byte[] RuntimeInstanceId = [0x52, 0x41, 0x43, 0x45];
        private int authorizationCount;

        public Func<Task>? BeforeSecondAuthorization { get; set; }

        public List<StreamRuntimeRevocation> Revocations { get; } = [];

        public Task<StreamRuntimeAuthorizationContext> GetContextAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new StreamRuntimeAuthorizationContext((byte[])RuntimeInstanceId.Clone(), 7));

        public async Task<StreamRuntimeAuthorizationResult> AuthorizeAsync(
            StreamRuntimeAuthorization authorization,
            CancellationToken cancellationToken)
        {
            authorizationCount++;
            if (authorizationCount == 2 && BeforeSecondAuthorization is not null)
            {
                await BeforeSecondAuthorization();
            }
            return StreamRuntimeAuthorizationResult.Accepted;
        }

        public Task<StreamRuntimeAuthorizationResult> RevokeAsync(
            StreamRuntimeRevocation revocation,
            CancellationToken cancellationToken)
        {
            Revocations.Add(revocation);
            return Task.FromResult(StreamRuntimeAuthorizationResult.Accepted);
        }
    }

    private sealed class RejectingAuthorizationAuthorizer : IStreamSessionAuthorizer
    {
        public Task<StreamRuntimeAuthorizationContext> GetContextAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new StreamRuntimeAuthorizationContext([1, 2, 3, 4], 7));

        public Task<StreamRuntimeAuthorizationResult> AuthorizeAsync(
            StreamRuntimeAuthorization authorization,
            CancellationToken cancellationToken) =>
            Task.FromResult(StreamRuntimeAuthorizationResult.Reject("ticket rejected"));

        public Task<StreamRuntimeAuthorizationResult> RevokeAsync(
            StreamRuntimeRevocation revocation,
            CancellationToken cancellationToken) =>
            Task.FromResult(StreamRuntimeAuthorizationResult.Accepted);
    }

    private sealed class RejectingSecondAuthorizationAuthorizer : IStreamSessionAuthorizer
    {
        private int authorizationCount;

        public Task<StreamRuntimeAuthorizationContext> GetContextAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new StreamRuntimeAuthorizationContext([1, 2, 3, 4], 7));

        public Task<StreamRuntimeAuthorizationResult> AuthorizeAsync(
            StreamRuntimeAuthorization authorization,
            CancellationToken cancellationToken)
        {
            authorizationCount++;
            return Task.FromResult(authorizationCount == 2
                ? StreamRuntimeAuthorizationResult.Reject("replacement ticket rejected")
                : StreamRuntimeAuthorizationResult.Accepted);
        }

        public Task<StreamRuntimeAuthorizationResult> RevokeAsync(
            StreamRuntimeRevocation revocation,
            CancellationToken cancellationToken) =>
            Task.FromResult(StreamRuntimeAuthorizationResult.Accepted);
    }

    private sealed class CancelingSecondAuthorizationAuthorizer : IStreamSessionAuthorizer
    {
        private int authorizationCount;

        public List<StreamRuntimeRevocation> Revocations { get; } = [];

        public Task<StreamRuntimeAuthorizationContext> GetContextAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new StreamRuntimeAuthorizationContext([1, 2, 3, 4], 7));

        public Task<StreamRuntimeAuthorizationResult> AuthorizeAsync(
            StreamRuntimeAuthorization authorization,
            CancellationToken cancellationToken)
        {
            authorizationCount++;
            return authorizationCount == 2
                ? throw new OperationCanceledException(
                    "simulated replacement ticket authorization cancellation")
                : Task.FromResult(StreamRuntimeAuthorizationResult.Accepted);
        }

        public Task<StreamRuntimeAuthorizationResult> RevokeAsync(
            StreamRuntimeRevocation revocation,
            CancellationToken cancellationToken)
        {
            Revocations.Add(revocation);
            return Task.FromResult(StreamRuntimeAuthorizationResult.Accepted);
        }
    }

    private sealed class RejectingRevocationAuthorizer : IStreamSessionAuthorizer
    {
        public Task<StreamRuntimeAuthorizationContext> GetContextAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new StreamRuntimeAuthorizationContext([1, 2, 3, 4], 7));

        public Task<StreamRuntimeAuthorizationResult> AuthorizeAsync(
            StreamRuntimeAuthorization authorization,
            CancellationToken cancellationToken) =>
            Task.FromResult(StreamRuntimeAuthorizationResult.Accepted);

        public Task<StreamRuntimeAuthorizationResult> RevokeAsync(
            StreamRuntimeRevocation revocation,
            CancellationToken cancellationToken) =>
            Task.FromResult(StreamRuntimeAuthorizationResult.Reject(
                "ticket revocation rejected"));
    }

    private sealed class CancelingAuthorizationAuthorizer : IStreamSessionAuthorizer
    {
        public List<StreamRuntimeRevocation> Revocations { get; } = [];

        public Task<StreamRuntimeAuthorizationContext> GetContextAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new StreamRuntimeAuthorizationContext([1, 2, 3, 4], 7));

        public Task<StreamRuntimeAuthorizationResult> AuthorizeAsync(
            StreamRuntimeAuthorization authorization,
            CancellationToken cancellationToken) =>
            throw new OperationCanceledException("simulated ticket authorization cancellation");

        public Task<StreamRuntimeAuthorizationResult> RevokeAsync(
            StreamRuntimeRevocation revocation,
            CancellationToken cancellationToken)
        {
            Revocations.Add(revocation);
            return Task.FromResult(StreamRuntimeAuthorizationResult.Accepted);
        }
    }

    private sealed class OrderedDisplayBackend(List<string> calls) : IDisplayBackend
    {
        private readonly FakeDisplayBackend inner = new();

        public Task<DisplayHealth> GetHealthAsync(CancellationToken cancellationToken) =>
            inner.GetHealthAsync(cancellationToken);

        public Task<DisplayEnsureResult> PrepareVirtualDisplayAsync(
            string displayId,
            int width,
            int height,
            int refreshHz,
            HdrPreference hdrPreference,
            CancellationToken cancellationToken)
        {
            calls.Add("display.prepare");
            return inner.PrepareVirtualDisplayAsync(
                displayId, width, height, refreshHz, hdrPreference, cancellationToken);
        }

        public Task<DisplayEnsureResult> EnsureVirtualDisplayAsync(
            string displayId,
            int width,
            int height,
            int refreshHz,
            HdrPreference hdrPreference,
            CancellationToken cancellationToken)
        {
            calls.Add("display.activate");
            return inner.EnsureVirtualDisplayAsync(
                displayId, width, height, refreshHz, hdrPreference, cancellationToken);
        }

        public Task<DisplayRestoreResult> RestorePhysicalPrimaryAsync(
            CancellationToken cancellationToken)
        {
            calls.Add("display.restore");
            return inner.RestorePhysicalPrimaryAsync(cancellationToken);
        }

        public Task<DisplayRemoveResult> RemoveVirtualDisplayAsync(
            string displayId,
            CancellationToken cancellationToken) =>
            inner.RemoveVirtualDisplayAsync(displayId, cancellationToken);
    }

    private sealed class ThrowingActivationDisplayBackend : IDisplayBackend
    {
        public FakeDisplayBackend Inner { get; } = new();

        public Task<DisplayHealth> GetHealthAsync(CancellationToken cancellationToken) =>
            Inner.GetHealthAsync(cancellationToken);

        public Task<DisplayEnsureResult> PrepareVirtualDisplayAsync(
            string displayId,
            int width,
            int height,
            int refreshHz,
            HdrPreference hdrPreference,
            CancellationToken cancellationToken) =>
            Inner.PrepareVirtualDisplayAsync(
                displayId,
                width,
                height,
                refreshHz,
                hdrPreference,
                cancellationToken);

        public Task<DisplayEnsureResult> EnsureVirtualDisplayAsync(
            string displayId,
            int width,
            int height,
            int refreshHz,
            HdrPreference hdrPreference,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("simulated activation failure");

        public Task<DisplayRestoreResult> RestorePhysicalPrimaryAsync(
            CancellationToken cancellationToken) =>
            Inner.RestorePhysicalPrimaryAsync(cancellationToken);

        public Task<DisplayRemoveResult> RemoveVirtualDisplayAsync(
            string displayId,
            CancellationToken cancellationToken) =>
            Inner.RemoveVirtualDisplayAsync(displayId, cancellationToken);
    }

    private sealed class OrderedStreamingBackend(List<string> calls) : IStreamingBackend
    {
        private readonly FakeStreamingBackend inner = new();

        public Task<StreamingBackendHealth> GetHealthAsync(CancellationToken cancellationToken) =>
            inner.GetHealthAsync(cancellationToken);

        public Task<StreamingPreflightResult> CheckReadinessAsync(
            SessionPlan plan,
            CancellationToken cancellationToken)
        {
            calls.Add("stream.preflight");
            return inner.CheckReadinessAsync(plan, cancellationToken);
        }

        public Task<StreamingStartResult> StartAsync(
            SessionPlan plan,
            CancellationToken cancellationToken)
        {
            calls.Add("stream.start");
            return inner.StartAsync(plan, cancellationToken);
        }

        public Task<StreamingStopResult> StopAsync(
            string sessionId,
            CancellationToken cancellationToken) =>
            inner.StopAsync(sessionId, cancellationToken);

        public Task<StreamingStopResult> StopRuntimeAsync(
            string sessionId,
            Guid expectedGeneration,
            CancellationToken cancellationToken)
        {
            calls.Add("stream.stop");
            return inner.StopRuntimeAsync(sessionId, expectedGeneration, cancellationToken);
        }

        public Task<StreamingSessionState?> GetSessionAsync(
            string sessionId,
            CancellationToken cancellationToken) =>
            inner.GetSessionAsync(sessionId, cancellationToken);

        public IReadOnlyList<StreamingSessionState> GetSessions() => inner.GetSessions();
    }

    private sealed class OrderedGameLauncher(List<string> calls) : IGameLauncher
    {
        private readonly FakeGameLauncher inner = new();

        public Task<GameLaunchResult> LaunchAsync(
            GameLaunchRequest request,
            CancellationToken cancellationToken)
        {
            calls.Add("game.launch");
            return inner.LaunchAsync(request, cancellationToken);
        }
    }

    private sealed class OrderedOwnershipTracker(List<string> calls) : ISessionOwnershipTracker
    {
        public Task RecordLaunchAsync(
            SessionPlan plan,
            GameLaunchState launchState,
            CancellationToken cancellationToken)
        {
            calls.Add("ownership.record");
            return Task.CompletedTask;
        }

        public Task<SessionOwnershipSnapshot?> GetSnapshotAsync(
            string sessionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<SessionOwnershipSnapshot?>(null);

        public Task<IReadOnlyList<SessionOwnershipSnapshot>> GetSnapshotsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SessionOwnershipSnapshot>>([]);

        public Task<SessionOwnedWorkTerminationResult> TerminateOwnedWorkAsync(
            string sessionId,
            CancellationToken cancellationToken)
        {
            calls.Add("ownership.terminate");
            return Task.FromResult(SessionOwnedWorkTerminationResult.Ok([]));
        }

        public Task ClearAsync(string sessionId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class RejectingOrderedStreamSessionAuthorizer(List<string> calls)
        : IStreamSessionAuthorizer
    {
        public Task<StreamRuntimeAuthorizationContext> GetContextAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new StreamRuntimeAuthorizationContext([1, 2, 3, 4], 7));

        public Task<StreamRuntimeAuthorizationResult> AuthorizeAsync(
            StreamRuntimeAuthorization authorization,
            CancellationToken cancellationToken)
        {
            calls.Add("ticket.authorize");
            return Task.FromResult(StreamRuntimeAuthorizationResult.Reject(
                "simulated ticket authorization failure"));
        }

        public Task<StreamRuntimeAuthorizationResult> RevokeAsync(
            StreamRuntimeRevocation revocation,
            CancellationToken cancellationToken) =>
            Task.FromResult(StreamRuntimeAuthorizationResult.Accepted);
    }

    private sealed class ThrowingGameLauncher : IGameLauncher
    {
        public Task<GameLaunchResult> LaunchAsync(
            GameLaunchRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("simulated launcher failure");
    }

    private sealed class ThrowingFirstRecordOwnershipTracker : ISessionOwnershipTracker
    {
        public int RecordCalls { get; private set; }

        public int TerminationCalls { get; private set; }

        public Task RecordLaunchAsync(
            SessionPlan plan,
            GameLaunchState launchState,
            CancellationToken cancellationToken)
        {
            RecordCalls++;
            if (RecordCalls == 1)
            {
                throw new InvalidOperationException("simulated ownership persistence failure");
            }
            return Task.CompletedTask;
        }

        public Task<SessionOwnershipSnapshot?> GetSnapshotAsync(
            string sessionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<SessionOwnershipSnapshot?>(null);

        public Task<IReadOnlyList<SessionOwnershipSnapshot>> GetSnapshotsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SessionOwnershipSnapshot>>([]);

        public Task<SessionOwnedWorkTerminationResult> TerminateOwnedWorkAsync(
            string sessionId,
            CancellationToken cancellationToken)
        {
            TerminationCalls++;
            return Task.FromResult(SessionOwnedWorkTerminationResult.Ok([]));
        }

        public Task ClearAsync(string sessionId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class ReplacingInvalidStartStreamingBackend : IStreamingBackend
    {
        private readonly FakeStreamingBackend inner = new();

        public int PublicStopCalls { get; private set; }

        public int ConditionalStopCalls { get; private set; }

        public Guid ReplacementGeneration { get; private set; }

        public Task<StreamingBackendHealth> GetHealthAsync(CancellationToken cancellationToken) =>
            inner.GetHealthAsync(cancellationToken);

        public Task<StreamingPreflightResult> CheckReadinessAsync(
            SessionPlan plan,
            CancellationToken cancellationToken) =>
            inner.CheckReadinessAsync(plan, cancellationToken);

        public async Task<StreamingStartResult> StartAsync(
            SessionPlan plan,
            CancellationToken cancellationToken)
        {
            inner.ActiveListenerPort = 0;
            StreamingStartResult invalid = await inner.StartAsync(plan, cancellationToken);
            inner.ActiveListenerPort = 47998;
            StreamingStartResult replacement = await inner.StartAsync(plan, cancellationToken);
            ReplacementGeneration = replacement.Session!.RuntimeGeneration;
            return invalid;
        }

        public Task<StreamingStopResult> StopAsync(
            string sessionId,
            CancellationToken cancellationToken)
        {
            PublicStopCalls++;
            return inner.StopAsync(sessionId, cancellationToken);
        }

        public async Task<StreamingStopResult> StopRuntimeAsync(
            string sessionId,
            Guid expectedGeneration,
            CancellationToken cancellationToken)
        {
            ConditionalStopCalls++;
            StreamingSessionState? current = await inner.GetSessionAsync(sessionId, cancellationToken);
            if (current?.RuntimeGeneration != expectedGeneration)
            {
                return StreamingStopResult.Fail(
                    $"Stream session '{sessionId}' runtime generation changed before compensation.");
            }
            return await inner.StopAsync(sessionId, cancellationToken);
        }

        public Task<StreamingSessionState?> GetSessionAsync(
            string sessionId,
            CancellationToken cancellationToken) =>
            inner.GetSessionAsync(sessionId, cancellationToken);

        public IReadOnlyList<StreamingSessionState> GetSessions() => inner.GetSessions();
    }

    private sealed class RecordingClientInputSessionLifecycle : IClientInputSessionLifecycle
    {
        public List<string> PreparedSessionIds { get; } = [];

        public List<string> SessionIds { get; } = [];

        public Task PrepareSessionAsync(string sessionId, CancellationToken cancellationToken)
        {
            PreparedSessionIds.Add(sessionId);
            return Task.CompletedTask;
        }

        public Task ReleaseSessionAsync(string sessionId, CancellationToken cancellationToken)
        {
            SessionIds.Add(sessionId);
            return Task.CompletedTask;
        }
    }
}

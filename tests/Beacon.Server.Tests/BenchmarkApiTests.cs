using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Beacon.Core.Benchmarks;
using Beacon.Server.State;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Beacon.Server.Tests;

public sealed class BenchmarkApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task AutomaticAndManualRunsStoreServerSelectionAndDrivePlanning()
    {
        HttpClient client = factory.CreateClient();
        string clientId = $"benchmark-{Guid.NewGuid():N}";
        await RegisterAsync(client, clientId);
        object fingerprints = CreateFingerprints();

        HttpResponseMessage prepare = await client.PostAsJsonAsync(
            $"/clients/{clientId}/benchmarks/prepare",
            new { trigger = "automatic", fingerprints });

        Assert.Equal(HttpStatusCode.OK, prepare.StatusCode);
        using JsonDocument prepareDocument = await JsonDocument.ParseAsync(await prepare.Content.ReadAsStreamAsync());
        Assert.Equal("start-new", prepareDocument.RootElement.GetProperty("disposition").GetString());
        Guid runId = prepareDocument.RootElement.GetProperty("runId").GetGuid();
        Assert.Equal(1, prepareDocument.RootElement.GetProperty("networkCoverage").GetProperty("firstSequence").GetInt64());
        Assert.Equal(1, prepareDocument.RootElement.GetProperty("networkCoverage").GetProperty("expectedPacketCount").GetInt32());

        HttpResponseMessage complete = await client.PostAsJsonAsync(
            $"/clients/{clientId}/benchmarks/{runId:D}/complete",
            new
            {
                networkSamples = new[]
                {
                    new { sequence = 1, payloadBytes = 1200, rttMs = 8, jitterMs = 1.0, received = true, throughputMbps = 100, reorderDistance = 0 }
                },
                decoderSamples = new[]
                {
                    new { codec = "h264", profile = "high", bitDepth = 8, width = 2560, height = 1600, targetFps = 120, configured = true, sustainedFps = 120, p95DecodeLatencyMs = 5, p95PresentationLatencyMs = 9, droppedFrames = 0, outputErrors = 0 }
                },
                powerSamples = new[]
                {
                    new { batteryPercent = 80, isCharging = false, thermalState = "nominal" }
                }
            });

        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        using JsonDocument completeDocument = await JsonDocument.ParseAsync(await complete.Content.ReadAsStreamAsync());
        Assert.Equal("h264", completeDocument.RootElement.GetProperty("selectedResult").GetProperty("codec").GetString());
        Assert.Equal(120, completeDocument.RootElement.GetProperty("selectedResult").GetProperty("maxSustainableFps").GetInt32());

        HttpResponseMessage history = await client.GetAsync($"/clients/{clientId}/benchmarks");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        using JsonDocument historyDocument = await JsonDocument.ParseAsync(await history.Content.ReadAsStreamAsync());
        JsonElement stored = Assert.Single(historyDocument.RootElement.GetProperty("runs").EnumerateArray());
        Assert.Equal(runId, stored.GetProperty("runId").GetGuid());
        Assert.Equal(1, stored.GetProperty("networkSamples").GetArrayLength());
        Assert.Equal(1, stored.GetProperty("decoderSamples").GetArrayLength());
        Assert.Equal("h264", stored.GetProperty("selectedResult").GetProperty("codec").GetString());

        HttpResponseMessage plan = await client.PostAsJsonAsync(
            $"/clients/{clientId}/plan",
            new { gameId = "steam-shortcut:3767414131" });

        Assert.Equal(HttpStatusCode.OK, plan.StatusCode);
        using JsonDocument planDocument = await JsonDocument.ParseAsync(await plan.Content.ReadAsStreamAsync());
        JsonElement stream = planDocument.RootElement.GetProperty("stream");
        Assert.Equal(runId, stream.GetProperty("benchmarkRunId").GetGuid());
        Assert.Equal("h264", stream.GetProperty("codec").GetString());

        HttpResponseMessage reuse = await client.PostAsJsonAsync(
            $"/clients/{clientId}/benchmarks/prepare",
            new { trigger = "automatic", fingerprints });
        using JsonDocument reuseDocument = await JsonDocument.ParseAsync(await reuse.Content.ReadAsStreamAsync());
        Assert.Equal("reuse", reuseDocument.RootElement.GetProperty("disposition").GetString());
        Assert.Equal(runId, reuseDocument.RootElement.GetProperty("runId").GetGuid());

        HttpResponseMessage manual = await client.PostAsJsonAsync(
            $"/clients/{clientId}/benchmarks/prepare",
            new { trigger = "manual", fingerprints });
        using JsonDocument manualDocument = await JsonDocument.ParseAsync(await manual.Content.ReadAsStreamAsync());
        Assert.Equal("start-new", manualDocument.RootElement.GetProperty("disposition").GetString());
        Assert.NotEqual(runId, manualDocument.RootElement.GetProperty("runId").GetGuid());
    }

    [Theory]
    [InlineData("ssid")]
    [InlineData("bssid")]
    public async Task PrepareRejectsRawNetworkIdentityFields(string fieldName)
    {
        HttpClient client = factory.CreateClient();
        string clientId = $"raw-network-{Guid.NewGuid():N}";
        await RegisterAsync(client, clientId);
        string canary = $"RAW-NETWORK-{Guid.NewGuid():N}";
        string json = $$"""
        {
          "trigger": "automatic",
          "fingerprints": {
            "network": {
              "schemaVersion": 3,
              "serverRoute": "192.168.1.10",
              "transport": "wifi",
              "localNetworkPrefix": "192.168.1.0/24",
              "wifiBand": "6-ghz",
              "wifiChannel": 37,
              "linkSpeedBucket": "500-999-mbps",
              "saltedNetworkIdHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
              "{{fieldName}}": "{{canary}}"
            },
            "hardware": {
              "schemaVersion": 3,
              "deviceCapabilityRevision": "caps-a",
              "androidVersion": "16",
              "apkVersion": "1.0.0",
              "displayModeInventoryRevision": "display-a",
              "codecInventoryRevision": "codec-a"
            }
          }
        }
        """;

        HttpResponseMessage response = await client.PostAsync(
            $"/clients/{clientId}/benchmarks/prepare",
            new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(canary, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlanningRejectsEvidenceOutsideTheValidityWindow()
    {
        BenchmarkEvidence stale = BenchmarkEvidenceRepositoryTests.CreateEvidence(
            Guid.Parse("f17d408d-61a0-489c-9509-6351a35e65b7"),
            DateTimeOffset.UtcNow.AddDays(-8));
        WebApplicationFactory<Program> staleFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBenchmarkEvidenceRepository>();
                services.AddSingleton<IBenchmarkEvidenceRepository>(
                    new InMemoryBenchmarkEvidenceRepository([stale]));
            }));
        HttpClient client = staleFactory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/clients/z-fold-7/plan",
            new { gameId = "steam-shortcut:3767414131" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("benchmark", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompletionRejectsNullSampleCollections()
    {
        HttpClient client = factory.CreateClient();
        string clientId = $"null-samples-{Guid.NewGuid():N}";
        await RegisterAsync(client, clientId);
        HttpResponseMessage prepare = await client.PostAsJsonAsync(
            $"/clients/{clientId}/benchmarks/prepare",
            new { trigger = "automatic", fingerprints = CreateFingerprints() });
        using JsonDocument document = await JsonDocument.ParseAsync(await prepare.Content.ReadAsStreamAsync());
        Guid runId = document.RootElement.GetProperty("runId").GetGuid();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/clients/{clientId}/benchmarks/{runId:D}/complete",
            new { networkSamples = (object?)null, decoderSamples = (object?)null, powerSamples = (object?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FingerprintChangesBlockPlanningAndReturningNetworkReusesMatchingHistory()
    {
        HttpClient client = factory.CreateClient();
        string clientId = $"fingerprint-change-{Guid.NewGuid():N}";
        await RegisterAsync(client, clientId);
        object networkA = CreateFingerprints(wifiChannel: 37);

        (string firstDisposition, Guid runA) = await PrepareAsync(client, clientId, networkA);
        (string repeatedDisposition, Guid repeatedRun) = await PrepareAsync(client, clientId, networkA);
        await CompleteRunAsync(client, clientId, runA);

        (string changedDisposition, _) = await PrepareAsync(
            client,
            clientId,
            CreateFingerprints(wifiChannel: 44));
        HttpResponseMessage blockedPlan = await client.PostAsJsonAsync(
            $"/clients/{clientId}/plan",
            new { gameId = "steam-shortcut:3767414131" });
        (string returnedDisposition, Guid returnedRun) = await PrepareAsync(client, clientId, networkA);
        HttpResponseMessage restoredPlan = await client.PostAsJsonAsync(
            $"/clients/{clientId}/plan",
            new { gameId = "steam-shortcut:3767414131" });

        Assert.Equal("start-new", firstDisposition);
        Assert.Equal("continue", repeatedDisposition);
        Assert.Equal(runA, repeatedRun);
        Assert.Equal("start-new", changedDisposition);
        Assert.Equal(HttpStatusCode.Conflict, blockedPlan.StatusCode);
        Assert.Equal("reuse", returnedDisposition);
        Assert.Equal(runA, returnedRun);
        Assert.Equal(HttpStatusCode.OK, restoredPlan.StatusCode);
    }

    [Fact]
    public async Task ServerCodecPolicyRescoresStoredRawEvidenceWithoutNewNetworkRun()
    {
        HttpClient client = factory.CreateClient();
        string clientId = $"policy-rescore-{Guid.NewGuid():N}";
        await RegisterAsync(client, clientId);
        (_, Guid runId) = await PrepareAsync(client, clientId, CreateFingerprints());
        HttpResponseMessage complete = await client.PostAsJsonAsync(
            $"/clients/{clientId}/benchmarks/{runId:D}/complete",
            new
            {
                networkSamples = new[]
                {
                    new { sequence = 1, payloadBytes = 1200, rttMs = 8, jitterMs = 1.0, received = true, throughputMbps = 100, reorderDistance = 0 }
                },
                decoderSamples = new[]
                {
                    new { codec = "av1", profile = "main", bitDepth = 10, width = 2560, height = 1600, targetFps = 120, configured = true, sustainedFps = 120, p95DecodeLatencyMs = 5, p95PresentationLatencyMs = 9, droppedFrames = 0, outputErrors = 0 },
                    new { codec = "hevc", profile = "main10", bitDepth = 10, width = 2560, height = 1600, targetFps = 120, configured = true, sustainedFps = 120, p95DecodeLatencyMs = 5, p95PresentationLatencyMs = 9, droppedFrames = 0, outputErrors = 0 }
                },
                powerSamples = new[]
                {
                    new { batteryPercent = 80, isCharging = false, thermalState = "nominal" }
                }
            });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        HttpResponseMessage initialPlan = await client.PostAsJsonAsync(
            $"/clients/{clientId}/plan",
            new { gameId = "steam-shortcut:3767414131" });
        HttpResponseMessage patch = await client.PatchAsJsonAsync(
            $"/clients/{clientId}/profile",
            new { codecPreference = "hevc" });
        HttpResponseMessage rescoredPlan = await client.PostAsJsonAsync(
            $"/clients/{clientId}/plan",
            new { gameId = "steam-shortcut:3767414131" });

        Assert.Equal(HttpStatusCode.OK, initialPlan.StatusCode);
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rescoredPlan.StatusCode);
        using JsonDocument initialDocument = await JsonDocument.ParseAsync(await initialPlan.Content.ReadAsStreamAsync());
        using JsonDocument rescoredDocument = await JsonDocument.ParseAsync(await rescoredPlan.Content.ReadAsStreamAsync());
        Assert.Equal("av1", initialDocument.RootElement.GetProperty("stream").GetProperty("codec").GetString());
        Assert.Equal("hevc", rescoredDocument.RootElement.GetProperty("stream").GetProperty("codec").GetString());
        Assert.Equal(runId, rescoredDocument.RootElement.GetProperty("stream").GetProperty("benchmarkRunId").GetGuid());
        Assert.NotEqual(initialDocument.RootElement.GetProperty("stream").GetProperty("benchmarkEvidenceRevision").GetString(),
            rescoredDocument.RootElement.GetProperty("stream").GetProperty("benchmarkEvidenceRevision").GetString());
    }

    [Fact]
    public async Task UnsupportedCodecPolicyReturnsConflictForPlanAndLaunch()
    {
        HttpClient client = factory.CreateClient();
        string clientId = $"policy-mismatch-{Guid.NewGuid():N}";
        await RegisterAsync(client, clientId);
        (_, Guid runId) = await PrepareAsync(client, clientId, CreateFingerprints());
        HttpResponseMessage complete = await client.PostAsJsonAsync(
            $"/clients/{clientId}/benchmarks/{runId:D}/complete",
            new
            {
                networkSamples = new[]
                {
                    new { sequence = 1, payloadBytes = 1200, rttMs = 8, jitterMs = 1.0, received = true, throughputMbps = 100, reorderDistance = 0 }
                },
                decoderSamples = new[]
                {
                    new { codec = "av1", profile = "main", bitDepth = 10, width = 2560, height = 1600, targetFps = 120, configured = true, sustainedFps = 120, p95DecodeLatencyMs = 5, p95PresentationLatencyMs = 9, droppedFrames = 0, outputErrors = 0 }
                },
                powerSamples = new[]
                {
                    new { batteryPercent = 80, isCharging = false, thermalState = "nominal" }
                }
            });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        HttpResponseMessage patch = await client.PatchAsJsonAsync(
            $"/clients/{clientId}/profile",
            new { codecPreference = "hevc" });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        HttpResponseMessage plan = await client.PostAsJsonAsync(
            $"/clients/{clientId}/plan",
            new { gameId = "steam-shortcut:3767414131" });
        HttpResponseMessage launch = await client.PostAsJsonAsync(
            $"/clients/{clientId}/launch",
            new { gameId = "steam-shortcut:3767414131" });

        Assert.Equal(HttpStatusCode.Conflict, plan.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, launch.StatusCode);
        string planError = await plan.Content.ReadAsStringAsync();
        string launchError = await launch.Content.ReadAsStringAsync();
        Assert.Contains("benchmark", planError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hevc", planError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("benchmark", launchError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hevc", launchError, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task RegisterAsync(HttpClient client, string clientId)
    {
        HttpResponseMessage hello = await client.PostAsJsonAsync("/clients/hello", new { clientId, name = clientId });
        Assert.Equal(HttpStatusCode.OK, hello.StatusCode);
    }

    private static object CreateFingerprints(int wifiChannel = 37) => new
    {
        network = new
        {
            schemaVersion = 3,
            serverRoute = "192.168.1.10",
            transport = "wifi",
            localNetworkPrefix = "192.168.1.0/24",
            wifiBand = "6-ghz",
            wifiChannel,
            linkSpeedBucket = "500-999-mbps",
            saltedNetworkIdHash = new string('a', 64)
        },
        hardware = new
        {
            schemaVersion = 3,
            deviceCapabilityRevision = "caps-a",
            androidVersion = "16",
            apkVersion = "1.0.0",
            displayModeInventoryRevision = "display-a",
            codecInventoryRevision = "codec-a"
        }
    };

    private static async Task<(string Disposition, Guid RunId)> PrepareAsync(
        HttpClient client,
        string clientId,
        object fingerprints)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/clients/{clientId}/benchmarks/prepare",
            new { trigger = "automatic", fingerprints });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return (
            Assert.IsType<string>(document.RootElement.GetProperty("disposition").GetString()),
            document.RootElement.GetProperty("runId").GetGuid());
    }

    private static async Task CompleteRunAsync(HttpClient client, string clientId, Guid runId)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/clients/{clientId}/benchmarks/{runId:D}/complete",
            new
            {
                networkSamples = new[]
                {
                    new { sequence = 1, payloadBytes = 1200, rttMs = 8, jitterMs = 1.0, received = true, throughputMbps = 100, reorderDistance = 0 }
                },
                decoderSamples = new[]
                {
                    new { codec = "h264", profile = "high", bitDepth = 8, width = 2560, height = 1600, targetFps = 120, configured = true, sustainedFps = 120, p95DecodeLatencyMs = 5, p95PresentationLatencyMs = 9, droppedFrames = 0, outputErrors = 0 }
                },
                powerSamples = new[]
                {
                    new { batteryPercent = 80, isCharging = false, thermalState = "nominal" }
                }
            });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

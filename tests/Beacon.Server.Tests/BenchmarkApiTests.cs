using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Beacon.Core.Benchmarks;
using Beacon.Core.Clients;
using Beacon.Core.Streaming;
using Beacon.Server.State;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Beacon.Server.Tests;

public sealed class BenchmarkApiTests(BeaconServerTestFactory factory) : IClassFixture<BeaconServerTestFactory>
{
    [Fact]
    public async Task NewRunReturnsBenchmarkConnectionGrantWithoutVideoMode()
    {
        HttpClient client = factory.CreateClient();
        string clientId = $"benchmark-grant-{Guid.NewGuid():N}";
        await RegisterAsync(client, clientId);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/clients/{clientId}/benchmarks/prepare",
            new { trigger = "manual", fingerprints = CreateFingerprints() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;
        Guid runId = root.GetProperty("runId").GetGuid();
        JsonElement plan = root.GetProperty("transportPlan");
        JsonElement hardwarePlan = root.GetProperty("hardwarePlan");
        JsonElement connection = root.GetProperty("connection");
        JsonElement benchmark = connection.GetProperty("benchmark");

        Assert.Equal("start-new", root.GetProperty("disposition").GetString());
        Assert.Equal($"benchmark:{runId:D}", connection.GetProperty("sessionId").GetString());
        Assert.InRange(connection.GetProperty("port").GetInt32(), 1, 65_535);
        Assert.NotEqual(0UL, connection.GetProperty("planRevision").GetUInt64());
        Assert.Equal(runId, benchmark.GetProperty("runId").GetGuid());
        Assert.Equal(1, benchmark.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(
            plan.GetProperty("reliablePacketCount").GetInt32(),
            benchmark.GetProperty("reliableRound").GetProperty("packetCount").GetInt32());
        Assert.Equal(
            plan.GetProperty("datagramPacketCount").GetInt32(),
            benchmark.GetProperty("datagramRound").GetProperty("packetCount").GetInt32());
        Assert.Equal(16, Convert.FromBase64String(benchmark.GetProperty("runToken").GetString()!).Length);
        Assert.Equal(1, hardwarePlan.GetProperty("schemaVersion").GetInt32());
        Assert.NotEmpty(hardwarePlan.GetProperty("decoderRounds").EnumerateArray());
        Assert.False(connection.TryGetProperty("selectedVideo", out _));
    }

    [Fact]
    public async Task CompletionStopsBenchmarkRuntimeAndRevokesItsTicket()
    {
        HttpClient client = factory.CreateClient();
        string clientId = $"benchmark-complete-{Guid.NewGuid():N}";
        await RegisterAsync(client, clientId);
        HttpResponseMessage prepareResponse = await client.PostAsJsonAsync(
            $"/clients/{clientId}/benchmarks/prepare",
            new { trigger = "manual", fingerprints = CreateFingerprints() });
        using JsonDocument prepare = await JsonDocument.ParseAsync(
            await prepareResponse.Content.ReadAsStreamAsync());
        Guid runId = prepare.RootElement.GetProperty("runId").GetGuid();
        string sessionId = prepare.RootElement
            .GetProperty("connection")
            .GetProperty("sessionId")
            .GetString()!;
        int packetCount = prepare.RootElement
            .GetProperty("transportPlan")
            .GetProperty("datagramPacketCount")
            .GetInt32();
        int payloadBytes = prepare.RootElement
            .GetProperty("transportPlan")
            .GetProperty("datagramPayloadBytes")
            .GetInt32();

        HttpResponseMessage complete = await client.PostAsJsonAsync(
            $"/clients/{clientId}/benchmarks/{runId:D}/complete",
            new
            {
                networkSamples = Enumerable.Range(0, packetCount)
                    .Select(sequence => new { sequence, payloadBytes, rttMs = 8, jitterMs = 1.0, received = true, throughputMbps = 100, reorderDistance = 0 }),
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
        FakeBenchmarkRuntime runtime = factory.Services.GetRequiredService<FakeBenchmarkRuntime>();
        BenchmarkRuntimeState stopped = Assert.IsType<BenchmarkRuntimeState>(
            await runtime.GetAsync(sessionId, CancellationToken.None));
        Assert.Equal("stopped", stopped.State);
        Assert.Null(stopped.ActiveListenerPort);
        Assert.Empty(stopped.RunToken);
        FakeStreamSessionAuthorizer authorizer = Assert.IsType<FakeStreamSessionAuthorizer>(
            factory.Services.GetRequiredService<IStreamSessionAuthorizer>());
        Assert.Contains(authorizer.Revocations, value => value.SessionId == sessionId);
    }

    [Fact]
    public async Task CancelStopsPendingBenchmarkRuntimeAndRevokesItsTicket()
    {
        HttpClient client = factory.CreateClient();
        string clientId = $"benchmark-cancel-{Guid.NewGuid():N}";
        await RegisterAsync(client, clientId);
        HttpResponseMessage prepareResponse = await client.PostAsJsonAsync(
            $"/clients/{clientId}/benchmarks/prepare",
            new { trigger = "manual", fingerprints = CreateFingerprints() });
        using JsonDocument prepare = await JsonDocument.ParseAsync(
            await prepareResponse.Content.ReadAsStreamAsync());
        Guid runId = prepare.RootElement.GetProperty("runId").GetGuid();
        string sessionId = prepare.RootElement
            .GetProperty("connection")
            .GetProperty("sessionId")
            .GetString()!;

        HttpResponseMessage cancel = await client.PostAsync(
            $"/clients/{clientId}/benchmarks/{runId:D}/cancel",
            content: null);

        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        using JsonDocument cancelled = await JsonDocument.ParseAsync(
            await cancel.Content.ReadAsStreamAsync());
        Assert.Equal("cancelled", cancelled.RootElement.GetProperty("state").GetString());
        FakeBenchmarkRuntime runtime = factory.Services.GetRequiredService<FakeBenchmarkRuntime>();
        Assert.Equal(
            "stopped",
            (await runtime.GetAsync(sessionId, CancellationToken.None))?.State);
        FakeStreamSessionAuthorizer authorizer = Assert.IsType<FakeStreamSessionAuthorizer>(
            factory.Services.GetRequiredService<IStreamSessionAuthorizer>());
        Assert.Contains(authorizer.Revocations, value => value.SessionId == sessionId);
        InMemoryClientStore store = factory.Services.GetRequiredService<InMemoryClientStore>();
        Assert.Null(store.GetBenchmarkEvidence(runId));
    }

    [Fact]
    public async Task FingerprintChangeStopsThePreviousPendingRuntimeBeforeStartingTheReplacement()
    {
        HttpClient client = factory.CreateClient();
        string clientId = $"benchmark-restart-{Guid.NewGuid():N}";
        await RegisterAsync(client, clientId);
        HttpResponseMessage firstResponse = await client.PostAsJsonAsync(
            $"/clients/{clientId}/benchmarks/prepare",
            new { trigger = "automatic", fingerprints = CreateFingerprints(wifiChannel: 37) });
        using JsonDocument first = await JsonDocument.ParseAsync(
            await firstResponse.Content.ReadAsStreamAsync());
        string firstSessionId = first.RootElement
            .GetProperty("connection")
            .GetProperty("sessionId")
            .GetString()!;

        HttpResponseMessage replacementResponse = await client.PostAsJsonAsync(
            $"/clients/{clientId}/benchmarks/prepare",
            new { trigger = "automatic", fingerprints = CreateFingerprints(wifiChannel: 44) });

        Assert.Equal(HttpStatusCode.OK, replacementResponse.StatusCode);
        using JsonDocument replacement = await JsonDocument.ParseAsync(
            await replacementResponse.Content.ReadAsStreamAsync());
        string replacementSessionId = replacement.RootElement
            .GetProperty("connection")
            .GetProperty("sessionId")
            .GetString()!;
        Assert.NotEqual(firstSessionId, replacementSessionId);
        FakeBenchmarkRuntime runtime = factory.Services.GetRequiredService<FakeBenchmarkRuntime>();
        Assert.Equal(
            "stopped",
            (await runtime.GetAsync(firstSessionId, CancellationToken.None))?.State);
        Assert.Equal(
            "running",
            (await runtime.GetAsync(replacementSessionId, CancellationToken.None))?.State);
        Assert.True(
            runtime.Operations.IndexOf($"stop:{firstSessionId}")
            < runtime.Operations.IndexOf($"start:{replacementSessionId}"));
    }

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
        Assert.Equal(0, prepareDocument.RootElement.GetProperty("networkCoverage").GetProperty("firstSequence").GetInt64());
        int expectedPackets = prepareDocument.RootElement.GetProperty("networkCoverage").GetProperty("expectedPacketCount").GetInt32();
        int payloadBytes = prepareDocument.RootElement.GetProperty("transportPlan").GetProperty("datagramPayloadBytes").GetInt32();
        Assert.Equal(256, expectedPackets);

        HttpResponseMessage complete = await client.PostAsJsonAsync(
            $"/clients/{clientId}/benchmarks/{runId:D}/complete",
            new
            {
                networkSamples = Enumerable.Range(0, expectedPackets)
                    .Select(sequence => new { sequence, payloadBytes, rttMs = 8, jitterMs = 1.0, received = true, throughputMbps = 100, reorderDistance = 0 }),
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
        Assert.Equal(expectedPackets, stored.GetProperty("networkSamples").GetArrayLength());
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

    [Fact]
    public async Task SessionPreflightMergesFreshNetworkWithStoredHardwareEvidence()
    {
        HttpClient client = factory.CreateClient();
        string clientId = $"benchmark-preflight-{Guid.NewGuid():N}";
        await RegisterAsync(client, clientId);
        object fingerprints = CreateFingerprints();
        (_, Guid fullRunId) = await PrepareAsync(client, clientId, fingerprints);
        await CompleteRunAsync(client, clientId, fullRunId);

        HttpResponseMessage prepare = await client.PostAsJsonAsync(
            $"/clients/{clientId}/benchmarks/prepare",
            new { trigger = "sessionPreflight", fingerprints });
        Assert.Equal(HttpStatusCode.OK, prepare.StatusCode);
        using JsonDocument prepared = await JsonDocument.ParseAsync(
            await prepare.Content.ReadAsStreamAsync());
        Guid preflightRunId = prepared.RootElement.GetProperty("runId").GetGuid();
        Assert.Empty(prepared.RootElement
            .GetProperty("hardwarePlan")
            .GetProperty("decoderRounds")
            .EnumerateArray());

        HttpResponseMessage complete = await client.PostAsJsonAsync(
            $"/clients/{clientId}/benchmarks/{preflightRunId:D}/complete",
            new
            {
                networkSamples = new[]
                {
                    new { sequence = 0, payloadBytes = 1000, rttMs = 18, jitterMs = 2.0, received = true, throughputMbps = 80, reorderDistance = 0 }
                },
                decoderSamples = Array.Empty<object>(),
                powerSamples = new[]
                {
                    new { batteryPercent = 70, isCharging = false, thermalState = "nominal" }
                }
            });

        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        using JsonDocument completed = await JsonDocument.ParseAsync(
            await complete.Content.ReadAsStreamAsync());
        Assert.Equal(
            "h264",
            completed.RootElement.GetProperty("selectedResult").GetProperty("codec").GetString());

        HttpResponseMessage history = await client.GetAsync($"/clients/{clientId}/benchmarks");
        using JsonDocument stored = await JsonDocument.ParseAsync(
            await history.Content.ReadAsStreamAsync());
        JsonElement preflight = stored.RootElement.GetProperty("runs").EnumerateArray()
            .Single(run => run.GetProperty("runId").GetGuid() == preflightRunId);
        Assert.Equal("SessionPreflight", preflight.GetProperty("trigger").GetString());
        Assert.Single(preflight.GetProperty("decoderSamples").EnumerateArray());
        Assert.Equal(70, preflight.GetProperty("powerSamples")[0].GetProperty("batteryPercent").GetInt32());
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
    public async Task CompletingPersistedBenchmarkWithoutClientProfileReturnsNotFound()
    {
        string clientId = $"missing-profile-{Guid.NewGuid():N}";
        Guid runId = Guid.NewGuid();
        BenchmarkEvidence pending = BenchmarkEvidenceRepositoryTests.CreateEvidence(
            runId,
            DateTimeOffset.UtcNow) with
        {
            ClientId = new ClientId(clientId),
            CompletedAt = null,
            NetworkSamples = [],
            DecoderSamples = [],
            PowerSamples = [],
            SelectedResult = null,
            NetworkCoverage = null
        };
        WebApplicationFactory<Program> persistedFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBenchmarkEvidenceRepository>();
                services.AddSingleton<IBenchmarkEvidenceRepository>(
                    new InMemoryBenchmarkEvidenceRepository([pending]));
            }));
        HttpClient client = persistedFactory.CreateClient();

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

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("not registered", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
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
        await client.PostAsJsonAsync($"/clients/{clientId}/capabilities", new
        {
            av1 = true,
            hevc = true,
            h264 = true,
            hdr10 = true,
            virtualDisplayHdrSupported = true,
            maxFps = 120,
            currentDisplayMode = new { width = 2560, height = 1600, refreshHz = 120 },
            supportedDisplayModes = new[]
            {
                new { width = 2560, height = 1600, refreshHz = 120 }
            }
        });
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
                    new { codec = "av1", profile = "main", bitDepth = 10, width = 2560, height = 1600, targetFps = 120, configured = true, sustainedFps = 120, p95DecodeLatencyMs = 5, p95PresentationLatencyMs = 9, droppedFrames = 0, outputErrors = 0, tenBitPresentationVerified = false, hdrPresentationVerified = false },
                    new { codec = "hevc", profile = "main10", bitDepth = 10, width = 2560, height = 1600, targetFps = 120, configured = true, sustainedFps = 120, p95DecodeLatencyMs = 5, p95PresentationLatencyMs = 9, droppedFrames = 0, outputErrors = 0, tenBitPresentationVerified = true, hdrPresentationVerified = true },
                    new { codec = "h264", profile = "high", bitDepth = 8, width = 2560, height = 1600, targetFps = 120, configured = true, sustainedFps = 120, p95DecodeLatencyMs = 5, p95PresentationLatencyMs = 9, droppedFrames = 0, outputErrors = 0, tenBitPresentationVerified = false, hdrPresentationVerified = false }
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
            $"/admin/clients/{clientId}/profile",
            new { codecPreference = "hevc" });
        HttpResponseMessage rescoredPlan = await client.PostAsJsonAsync(
            $"/clients/{clientId}/plan",
            new { gameId = "steam-shortcut:3767414131" });

        Assert.Equal(HttpStatusCode.OK, initialPlan.StatusCode);
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rescoredPlan.StatusCode);
        using JsonDocument initialDocument = await JsonDocument.ParseAsync(await initialPlan.Content.ReadAsStreamAsync());
        using JsonDocument rescoredDocument = await JsonDocument.ParseAsync(await rescoredPlan.Content.ReadAsStreamAsync());
        Assert.Equal("h264", initialDocument.RootElement.GetProperty("stream").GetProperty("codec").GetString());
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
            $"/admin/clients/{clientId}/profile",
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

        HttpResponseMessage capabilities = await client.PostAsJsonAsync($"/clients/{clientId}/capabilities", new
        {
            av1 = true,
            hevc = true,
            h264 = true,
            hdr10 = true,
            virtualDisplayHdrSupported = false,
            maxFps = 120,
            lowLatencyDecode = true,
            currentDisplayMode = new { width = 2560, height = 1600, refreshHz = 120 },
            supportedDisplayModes = new[]
            {
                new { width = 2560, height = 1600, refreshHz = 120 }
            }
        });
        Assert.Equal(HttpStatusCode.OK, capabilities.StatusCode);
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

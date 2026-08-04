using System.Net;
using System.Text.Json;
using Beacon.FakeEndpoint;

namespace Beacon.FakeEndpoint.Tests;

public sealed class FakeEndpointRunnerTests
{
    [Fact]
    public void ParsesServerAndScriptOverridesFromCommandLine()
    {
        FakeEndpointCommandLineOptions options = FakeEndpointCommandLine.Parse(
            [
                "--server",
                "http://127.0.0.1:5111",
                "--width",
                "1920",
                "--height",
                "1200",
                "--refresh",
                "60",
                "--app-id",
                "manual:game",
                "--title",
                "Manual Game",
                "--source",
                "manual",
                "--client-id",
                "handheld-1",
                "--name",
                "Handheld 1",
                "--benchmark-trigger",
                "manual"
            ]);

        Assert.Equal(new Uri("http://127.0.0.1:5111"), options.ServerUri);
        Assert.Equal("handheld-1", options.Script.ClientId);
        Assert.Equal("Handheld 1", options.Script.Name);
        Assert.Equal(1920, options.Script.Width);
        Assert.Equal(1200, options.Script.Height);
        Assert.Equal(60, options.Script.RefreshHz);
        Assert.Equal("manual:game", options.Script.AppId);
        Assert.Equal("Manual Game", options.Script.Title);
        Assert.Equal("manual", options.Script.Source);
        Assert.Equal("manual", options.Script.BenchmarkTrigger);
    }

    [Fact]
    public void ParsesTelemetryProfileAndTelemetryOverridesFromCommandLine()
    {
        FakeEndpointCommandLineOptions options = FakeEndpointCommandLine.Parse(
            [
                "--telemetry-profile",
                "thermal-battery",
                "--rtt-ms",
                "44",
                "--packet-loss",
                "1.25",
                "--estimated-bandwidth",
                "70",
                "--wifi-band",
                "5-ghz"
            ]);

        Assert.Equal("thermal-battery", options.Script.TelemetryProfile);
        Assert.Equal(44, options.Script.RttMs);
        Assert.Equal(1.25, options.Script.PacketLossPercent);
        Assert.Equal(70, options.Script.EstimatedBandwidthMbps);
        Assert.Equal("5-ghz", options.Script.WifiBand);
    }

    [Fact]
    public void ScriptContainsFactsButNoServerOwnedBitratePolicy()
    {
        Assert.DoesNotContain(
            typeof(FakeEndpointScript).GetProperties(),
            property => property.Name == "BitrateCapMbps");
    }

    [Fact]
    public async Task RunsZFoldControlPlaneScriptInOrder()
    {
        var handler = new RecordingHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var runner = new FakeEndpointRunner(client);

        FakeEndpointResult result = await runner.RunAsync(FakeEndpointScript.CreateZFold7Default(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(
            [
                "POST /clients/hello",
                "POST /clients/z-fold-7/capabilities",
                "POST /clients/z-fold-7/beacon",
                "POST /clients/z-fold-7/telemetry",
                "POST /clients/z-fold-7/benchmarks/prepare",
                "POST /clients/z-fold-7/benchmarks/00000000-0000-0000-0000-000000000001/complete",
                "POST /clients/z-fold-7/benchmarks/prepare",
                "POST /clients/z-fold-7/benchmarks/00000000-0000-0000-0000-000000000002/complete",
                "POST /clients/z-fold-7/plan",
                "POST /clients/z-fold-7/launch",
                "POST /clients/z-fold-7/input",
                "POST /clients/z-fold-7/disconnect",
                "POST /clients/z-fold-7/reconnect",
                "POST /clients/z-fold-7/stream/stop",
                "POST /clients/z-fold-7/beacon",
                "POST /clients/z-fold-7/quit",
                "POST /clients/z-fold-7/emergency-restore"
            ],
            handler.Requests);
        Assert.Equal(handler.Requests, result.Operations);
        Assert.DoesNotContain(handler.Requests, request => request.StartsWith("PATCH ", StringComparison.Ordinal));
        Assert.Equal(2, handler.Requests.Count(request => request.EndsWith("/beacon", StringComparison.Ordinal)));
        int[] benchmarkPrepareIndexes = handler.Requests
            .Select((request, index) => (request, index))
            .Where(value => value.request.EndsWith("/benchmarks/prepare", StringComparison.Ordinal))
            .Select(value => value.index)
            .ToArray();
        Assert.Equal("automatic", JsonDocument.Parse(handler.Bodies[benchmarkPrepareIndexes[0]]).RootElement.GetProperty("trigger").GetString());
        Assert.Equal("sessionPreflight", JsonDocument.Parse(handler.Bodies[benchmarkPrepareIndexes[1]]).RootElement.GetProperty("trigger").GetString());
    }

    [Fact]
    public async Task FailureAfterActivationStillRunsInactiveQuitAndRecoveryCleanup()
    {
        var handler = new RecordingHandler(request =>
            request.RequestUri?.AbsolutePath.EndsWith("/telemetry", StringComparison.Ordinal) == true
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : null);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var runner = new FakeEndpointRunner(client);

        FakeEndpointResult result = await runner.RunAsync(
            FakeEndpointScript.CreateZFold7Default(),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(
            [
                "POST /clients/hello",
                "POST /clients/z-fold-7/capabilities",
                "POST /clients/z-fold-7/beacon",
                "POST /clients/z-fold-7/telemetry",
                "POST /clients/z-fold-7/beacon",
                "POST /clients/z-fold-7/quit",
                "POST /clients/z-fold-7/emergency-restore"
            ],
            result.Operations);
        Assert.Contains("\"active\":false", handler.Bodies[4], StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailureAfterLaunchStopsStreamBeforeInactiveQuitAndRecoveryCleanup()
    {
        var handler = new RecordingHandler(request =>
            request.RequestUri?.AbsolutePath.EndsWith("/input", StringComparison.Ordinal) == true
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : null);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var runner = new FakeEndpointRunner(client);

        FakeEndpointResult result = await runner.RunAsync(
            FakeEndpointScript.CreateZFold7Default(),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(
            [
                "POST /clients/z-fold-7/input",
                "POST /clients/z-fold-7/stream/stop",
                "POST /clients/z-fold-7/beacon",
                "POST /clients/z-fold-7/quit",
                "POST /clients/z-fold-7/emergency-restore"
            ],
            result.Operations.Skip(result.Operations.Count - 5));
        Assert.Contains("\"active\":false", handler.Bodies[^3], StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellationAfterActivationUsesIndependentCleanupToken()
    {
        using var cancellation = new CancellationTokenSource();
        int beaconRequests = 0;
        var handler = new RecordingHandler(request =>
        {
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.EndsWith("/beacon", StringComparison.Ordinal) && ++beaconRequests == 2)
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }
            if (path.EndsWith("/telemetry", StringComparison.Ordinal))
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }
            return null;
        });
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var runner = new FakeEndpointRunner(client);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(
            FakeEndpointScript.CreateZFold7Default(),
            cancellation.Token));

        Assert.Equal(
            [
                "POST /clients/z-fold-7/telemetry",
                "POST /clients/z-fold-7/beacon",
                "POST /clients/z-fold-7/quit",
                "POST /clients/z-fold-7/emergency-restore"
            ],
            handler.Requests.Skip(handler.Requests.Count - 4));
        Assert.Contains("\"active\":false", handler.Bodies[^3], StringComparison.Ordinal);
    }

    [Fact]
    public async Task LostActivationResponseStillAttemptsEveryCleanupStep()
    {
        int beaconRequests = 0;
        bool serverActive = false;
        var handler = new RecordingHandler(request =>
        {
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (!path.EndsWith("/beacon", StringComparison.Ordinal))
            {
                return null;
            }

            beaconRequests++;
            if (beaconRequests == 1)
            {
                serverActive = true;
                throw new HttpRequestException("Active response was lost.");
            }

            serverActive = false;
            throw new HttpRequestException("Inactive response was lost.");
        });
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var runner = new FakeEndpointRunner(client);

        FakeEndpointResult result = await runner.RunAsync(
            FakeEndpointScript.CreateZFold7Default(),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(serverActive);
        Assert.Equal(
            [
                "POST /clients/hello",
                "POST /clients/z-fold-7/capabilities",
                "POST /clients/z-fold-7/beacon",
                "POST /clients/z-fold-7/beacon",
                "POST /clients/z-fold-7/quit",
                "POST /clients/z-fold-7/emergency-restore"
            ],
            handler.Requests);
        Assert.Contains("\"active\":false", handler.Bodies[3], StringComparison.Ordinal);
    }

    [Fact]
    public async Task HelloContainsIdentityWithoutSharedToken()
    {
        var handler = new RecordingHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var runner = new FakeEndpointRunner(client);
        FakeEndpointScript script = FakeEndpointScript.CreateZFold7Default() with
        {
            ClientId = "handheld-1",
            Name = "Handheld 1"
        };

        FakeEndpointResult result = await runner.RunAsync(script, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("\"clientId\":\"handheld-1\"", handler.Bodies[0], StringComparison.Ordinal);
        Assert.Contains("\"name\":\"Handheld 1\"", handler.Bodies[0], StringComparison.Ordinal);
        Assert.DoesNotContain("pairingToken", handler.Bodies[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ManualFullBenchmarkRemainsDistinctFromSessionPreflight()
    {
        var handler = new RecordingHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var runner = new FakeEndpointRunner(client);
        FakeEndpointScript script = FakeEndpointScript.CreateZFold7Default() with
        {
            BenchmarkTrigger = "manual"
        };

        FakeEndpointResult result = await runner.RunAsync(script, CancellationToken.None);

        Assert.True(result.Success);
        string[] triggers = handler.Requests
            .Select((request, index) => (request, index))
            .Where(value => value.request.EndsWith("/benchmarks/prepare", StringComparison.Ordinal))
            .Select(value => JsonDocument.Parse(handler.Bodies[value.index]).RootElement.GetProperty("trigger").GetString()!)
            .ToArray();
        Assert.Equal(["manual", "sessionPreflight"], triggers);
    }

    [Fact]
    public async Task SendsExpandedTelemetryFacts()
    {
        var handler = new RecordingHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var runner = new FakeEndpointRunner(client);
        FakeEndpointScript script = FakeEndpointScript.CreateZFold7Default().ApplyTelemetryProfile("packet-loss");

        FakeEndpointResult result = await runner.RunAsync(script, CancellationToken.None);

        Assert.True(result.Success);
        int telemetryIndex = handler.Requests.IndexOf("POST /clients/z-fold-7/telemetry");
        string telemetryBody = handler.Bodies[telemetryIndex];
        Assert.Contains("\"packetLossPercent\":3.2", telemetryBody, StringComparison.Ordinal);
        Assert.Contains("\"wifiBand\":\"5-ghz\"", telemetryBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendsDeterministicPointerGestureAndKeyboardPressAfterLaunch()
    {
        var handler = new RecordingHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var runner = new FakeEndpointRunner(client);

        FakeEndpointResult result = await runner.RunAsync(FakeEndpointScript.CreateZFold7Default(), CancellationToken.None);

        Assert.True(result.Success);
        int inputIndex = handler.Requests.IndexOf("POST /clients/z-fold-7/input");
        Assert.True(inputIndex > 0);
        Assert.Contains("\"sequence\":1", handler.Bodies[inputIndex], StringComparison.Ordinal);
        Assert.Contains("\"type\":\"pointer\"", handler.Bodies[inputIndex], StringComparison.Ordinal);
        Assert.Contains("\"action\":\"down\"", handler.Bodies[inputIndex], StringComparison.Ordinal);
        Assert.Contains("\"action\":\"move\"", handler.Bodies[inputIndex], StringComparison.Ordinal);
        Assert.Contains("\"action\":\"up\"", handler.Bodies[inputIndex], StringComparison.Ordinal);
        Assert.Contains("\"type\":\"keyboard\"", handler.Bodies[inputIndex], StringComparison.Ordinal);
        Assert.Contains("\"action\":\"press\"", handler.Bodies[inputIndex], StringComparison.Ordinal);
        Assert.Contains("\"key\":\"Escape\"", handler.Bodies[inputIndex], StringComparison.Ordinal);
        Assert.Contains("\"code\":\"Escape\"", handler.Bodies[inputIndex], StringComparison.Ordinal);
        Assert.Contains("\"x\":0.5", handler.Bodies[inputIndex], StringComparison.Ordinal);
        Assert.Contains("\"y\":0.5", handler.Bodies[inputIndex], StringComparison.Ordinal);
        Assert.Contains("\"x\":0.75", handler.Bodies[inputIndex], StringComparison.Ordinal);
        Assert.Contains("\"y\":0.25", handler.Bodies[inputIndex], StringComparison.Ordinal);
        Assert.DoesNotContain("\"buttons\":null", handler.Bodies[inputIndex], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportsObservedZFold1440pAsCapabilityFact()
    {
        var handler = new RecordingHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var runner = new FakeEndpointRunner(client);
        FakeEndpointScript script = FakeEndpointScript.CreateZFold7Default() with { Height = 1440 };

        FakeEndpointResult result = await runner.RunAsync(script, CancellationToken.None);

        Assert.True(result.Success);
        int capabilitiesIndex = handler.Requests.IndexOf("POST /clients/z-fold-7/capabilities");
        Assert.Contains("\"height\":1440", handler.Bodies[capabilitiesIndex], StringComparison.Ordinal);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage?>? responseFactory = null) : HttpMessageHandler
    {
        private bool runtimeActive;
        private int benchmarkRun;

        public List<string> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add($"{request.Method.Method} {request.RequestUri?.PathAndQuery}");
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            HttpResponseMessage? response = responseFactory?.Invoke(request);
            if (response is not null)
            {
                return response;
            }

            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.EndsWith("/benchmarks/prepare", StringComparison.Ordinal))
            {
                benchmarkRun++;
                string body = Bodies[^1];
                string trigger = JsonDocument.Parse(body).RootElement.GetProperty("trigger").GetString()!;
                object[] rounds = trigger == "sessionPreflight"
                    ? []
                    :
                    [
                        new
                        {
                            codec = "h264",
                            profile = "high",
                            bitDepth = 8,
                            width = 640,
                            height = 360,
                            targetFps = 30
                        }
                    ];
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        disposition = "start-new",
                        runId = $"00000000-0000-0000-0000-{benchmarkRun:000000000000}",
                        networkCoverage = new { firstSequence = 0, expectedPacketCount = 3 },
                        transportPlan = new { datagramPayloadBytes = 1000 },
                        hardwarePlan = new { decoderRounds = rounds }
                    }))
                };
            }
            if (path.EndsWith("/plan", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"stream\":{\"congestionPolicy\":\"adaptive\"}}")
                };
            }
            if (path.EndsWith("/reconnect", StringComparison.Ordinal) && !runtimeActive)
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("{\"status\":503}")
                };
            }
            if (path.EndsWith("/launch", StringComparison.Ordinal))
            {
                runtimeActive = true;
            }
            else if (path.EndsWith("/stream/stop", StringComparison.Ordinal)
                || path.EndsWith("/quit", StringComparison.Ordinal))
            {
                runtimeActive = false;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            };
        }
    }
}

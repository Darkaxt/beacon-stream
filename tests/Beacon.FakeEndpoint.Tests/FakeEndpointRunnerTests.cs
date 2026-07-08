using System.Net;
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
                "--pairing-token",
                "pair-me"
            ]);

        Assert.Equal(new Uri("http://127.0.0.1:5111"), options.ServerUri);
        Assert.Equal("handheld-1", options.Script.ClientId);
        Assert.Equal("Handheld 1", options.Script.Name);
        Assert.Equal("pair-me", options.Script.PairingToken);
        Assert.Equal(1920, options.Script.Width);
        Assert.Equal(1200, options.Script.Height);
        Assert.Equal(60, options.Script.RefreshHz);
        Assert.Equal("manual:game", options.Script.AppId);
        Assert.Equal("Manual Game", options.Script.Title);
        Assert.Equal("manual", options.Script.Source);
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
                "wifi-6"
            ]);

        Assert.Equal("thermal-battery", options.Script.TelemetryProfile);
        Assert.Equal(44, options.Script.RttMs);
        Assert.Equal(1.25, options.Script.PacketLossPercent);
        Assert.Equal(70, options.Script.EstimatedBandwidthMbps);
        Assert.Equal("wifi-6", options.Script.WifiBand);
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
                "GET /clients/z-fold-7/profile",
                "PATCH /clients/z-fold-7/profile",
                "POST /clients/z-fold-7/capabilities",
                "POST /clients/z-fold-7/telemetry",
                "POST /clients/z-fold-7/plan",
                "POST /clients/z-fold-7/launch",
                "POST /clients/z-fold-7/input",
                "POST /clients/z-fold-7/disconnect",
                "POST /clients/z-fold-7/reconnect",
                "POST /clients/z-fold-7/plan",
                "POST /clients/z-fold-7/quit",
                "POST /clients/z-fold-7/emergency-restore"
            ],
            handler.Requests);
        Assert.Equal(handler.Requests, result.Operations);
    }

    [Fact]
    public async Task SendsPairingTokenWhenConfigured()
    {
        var handler = new RecordingHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var runner = new FakeEndpointRunner(client);
        FakeEndpointScript script = FakeEndpointScript.CreateZFold7Default() with
        {
            ClientId = "handheld-1",
            Name = "Handheld 1",
            PairingToken = "pair-me"
        };

        FakeEndpointResult result = await runner.RunAsync(script, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("\"clientId\":\"handheld-1\"", handler.Bodies[0], StringComparison.Ordinal);
        Assert.Contains("\"name\":\"Handheld 1\"", handler.Bodies[0], StringComparison.Ordinal);
        Assert.Contains("\"pairingToken\":\"pair-me\"", handler.Bodies[0], StringComparison.Ordinal);
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
        string telemetryBody = handler.Bodies[4];
        Assert.Contains("\"packetLossPercent\":3.2", telemetryBody, StringComparison.Ordinal);
        Assert.Contains("\"wifiBand\":\"wifi-6\"", telemetryBody, StringComparison.Ordinal);
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
    public async Task RejectsZFold1440pBeforeCallingServer()
    {
        var handler = new RecordingHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var runner = new FakeEndpointRunner(client);
        FakeEndpointScript script = FakeEndpointScript.CreateZFold7Default() with { Height = 1440 };

        FakeEndpointResult result = await runner.RunAsync(script, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("2560x1440", result.Error, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add($"{request.Method.Method} {request.RequestUri?.PathAndQuery}");
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            };
        }
    }
}

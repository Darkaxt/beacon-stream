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
                "manual"
            ]);

        Assert.Equal(new Uri("http://127.0.0.1:5111"), options.ServerUri);
        Assert.Equal(1920, options.Script.Width);
        Assert.Equal(1200, options.Script.Height);
        Assert.Equal(60, options.Script.RefreshHz);
        Assert.Equal("manual:game", options.Script.AppId);
        Assert.Equal("Manual Game", options.Script.Title);
        Assert.Equal("manual", options.Script.Source);
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
                "POST /clients/z-fold-7/disconnect",
                "POST /clients/z-fold-7/plan",
                "POST /clients/z-fold-7/quit",
                "POST /clients/z-fold-7/emergency-restore"
            ],
            handler.Requests);
        Assert.Equal(handler.Requests, result.Operations);
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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add($"{request.Method.Method} {request.RequestUri?.PathAndQuery}");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            });
        }
    }
}

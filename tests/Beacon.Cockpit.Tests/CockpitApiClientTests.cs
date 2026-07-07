using System.Net;
using Beacon.Cockpit.Cockpit;

namespace Beacon.Cockpit.Tests;

public sealed class CockpitApiClientTests
{
    [Fact]
    public async Task LoadsSnapshotFromServer()
    {
        var handler = new FakeHttpHandler(
            """
            {
              "clients": [{ "clientId": "z-fold-7", "profile": {}, "capabilities": {}, "telemetry": {} }],
              "sessions": [{ "clientId": { "value": "z-fold-7" }, "appId": "steam-shortcut:3767414131" }],
              "games": { "total": 36, "diagnostics": ["Steam library 'G:\\SteamLibrary\\steamapps' does not exist."] }
            }
            """);
        var client = new CockpitApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") });

        CockpitSnapshot snapshot = await client.GetSnapshotAsync(CancellationToken.None);

        Assert.Single(snapshot.Clients);
        Assert.Equal("z-fold-7", snapshot.Clients[0].ClientId);
        Assert.Equal(36, snapshot.Games.Total);
        Assert.Single(snapshot.Games.Diagnostics);
    }

    [Fact]
    public async Task SendsRecoveryRequestsToAdminEndpoints()
    {
        var handler = new FakeHttpHandler("{}");
        var client = new CockpitApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") });

        await client.RestorePhysicalAsync(CancellationToken.None);
        await client.RecoverClientDisplayAsync("z fold/7", CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal("/admin/recovery/restore-physical", handler.Requests[0].RequestUri?.PathAndQuery);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        Assert.Equal("/admin/clients/z%20fold%2F7/display/recover", handler.Requests[1].RequestUri?.PathAndQuery);
    }

    private sealed class FakeHttpHandler(string responseBody) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody)
            });
        }
    }
}

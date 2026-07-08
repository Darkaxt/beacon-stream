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
              "clients": [{
                "clientId": "z-fold-7",
                "profile": {
                  "name": "Z Fold 7",
                  "display": { "preferredWidth": 2560, "preferredHeight": 1600, "preferredRefreshHz": 120, "hdrPreference": "Prefer", "mode": "virtual-primary", "restorePhysicalDisplayOnEnd": true, "forbidMirrorMode": true },
                  "stream": { "qualityMode": "auto", "codecPreference": "auto", "bitrateCapMbps": null },
                  "audio": { "mode": "stereo" },
                  "session": { "keepAppRunningOnDisconnect": false, "allowEmergencyRestoreFromClient": true }
                },
                "capabilities": {},
                "telemetry": {}
              }],
              "sessions": [{ "clientId": { "value": "z-fold-7" }, "appId": "steam-shortcut:3767414131" }],
              "streams": [{ "sessionId": "z-fold-7-steam-shortcut:3767414131", "clientId": "z-fold-7", "appId": "steam-shortcut:3767414131", "displayId": "client-z-fold-7", "codec": "av1", "fps": 120, "initialBitrateMbps": 65, "transport": "lan-direct", "state": "running", "error": null }],
              "ownership": [{ "sessionId": "z-fold-7-steam-shortcut:3767414131", "appId": "steam-shortcut:3767414131", "launchedProcessId": 4321, "launchedProcessRunning": false, "childProcessRunning": false, "ownedWindowRemaining": false, "reasons": [] }],
              "games": { "total": 36, "diagnostics": ["Steam library 'G:\\SteamLibrary\\steamapps' does not exist."] }
            }
            """);
        var client = new CockpitApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") });

        CockpitSnapshot snapshot = await client.GetSnapshotAsync(CancellationToken.None);

        Assert.Single(snapshot.Clients);
        Assert.Equal("z-fold-7", snapshot.Clients[0].ClientId);
        Assert.Equal("Z Fold 7", snapshot.Clients[0].Profile.Name);
        Assert.Equal(2560, snapshot.Clients[0].Profile.Display.PreferredWidth);
        Assert.Equal(1600, snapshot.Clients[0].Profile.Display.PreferredHeight);
        Assert.Equal("virtual-primary", snapshot.Clients[0].Profile.Display.Mode);
        Assert.Single(snapshot.Streams);
        Assert.Equal("running", snapshot.Streams[0].State);
        Assert.Equal("client-z-fold-7", snapshot.Streams[0].DisplayId);
        Assert.Single(snapshot.Ownership);
        Assert.Equal(4321, snapshot.Ownership[0].LaunchedProcessId);
        Assert.Equal(36, snapshot.Games.Total);
        Assert.Single(snapshot.Games.Diagnostics);
    }

    [Fact]
    public async Task SendsProfilePatchToAdminEndpoint()
    {
        var handler = new FakeHttpHandler("{}");
        var client = new CockpitApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") });

        await client.PatchClientProfileAsync("z fold/7", new CockpitClientProfilePatch
        {
            PreferredWidth = 2560,
            PreferredHeight = 1600,
            PreferredRefreshHz = 90,
            Mode = "physical-blackout",
            RestorePhysicalDisplayOnEnd = false,
            ForbidMirrorMode = false,
            KeepAppRunningOnDisconnect = true,
            AllowEmergencyRestoreFromClient = false
        }, CancellationToken.None);

        Assert.Equal(HttpMethod.Patch, handler.Requests[0].Method);
        Assert.Equal("/admin/clients/z%20fold%2F7/profile", handler.Requests[0].RequestUri?.PathAndQuery);
        Assert.Contains("\"preferredRefreshHz\":90", handler.Bodies[0], StringComparison.Ordinal);
        Assert.Contains("\"mode\":\"physical-blackout\"", handler.Bodies[0], StringComparison.Ordinal);
        Assert.Contains("\"keepAppRunningOnDisconnect\":true", handler.Bodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendsRecoveryRequestsToAdminEndpoints()
    {
        var handler = new FakeHttpHandler("{}");
        var client = new CockpitApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") });

        await client.RestorePhysicalAsync(CancellationToken.None);
        await client.MoveWindowsBackAsync(minimize: true, CancellationToken.None);
        await client.CloseVirtualWindowsAsync(CancellationToken.None);
        await client.TerminateVirtualProcessesAsync(CancellationToken.None);
        await client.RecoverClientDisplayAsync("z fold/7", CancellationToken.None);
        await client.RemoveClientDisplayLeaseAsync("z fold/7", CancellationToken.None);
        await client.StopClientStreamAsync("z fold/7", CancellationToken.None);

        Assert.Equal(7, handler.Requests.Count);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal("/admin/recovery/restore-physical", handler.Requests[0].RequestUri?.PathAndQuery);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        Assert.Equal("/admin/recovery/move-windows-back", handler.Requests[1].RequestUri?.PathAndQuery);
        Assert.Equal(HttpMethod.Post, handler.Requests[2].Method);
        Assert.Equal("/admin/recovery/close-virtual-windows", handler.Requests[2].RequestUri?.PathAndQuery);
        Assert.Equal(HttpMethod.Post, handler.Requests[3].Method);
        Assert.Equal("/admin/recovery/terminate-virtual-processes", handler.Requests[3].RequestUri?.PathAndQuery);
        Assert.Equal(HttpMethod.Post, handler.Requests[4].Method);
        Assert.Equal("/admin/clients/z%20fold%2F7/display/recover", handler.Requests[4].RequestUri?.PathAndQuery);
        Assert.Equal(HttpMethod.Post, handler.Requests[5].Method);
        Assert.Equal("/admin/clients/z%20fold%2F7/display/remove", handler.Requests[5].RequestUri?.PathAndQuery);
        Assert.Equal(HttpMethod.Post, handler.Requests[6].Method);
        Assert.Equal("/admin/clients/z%20fold%2F7/stream/stop", handler.Requests[6].RequestUri?.PathAndQuery);
    }

    private sealed class FakeHttpHandler(string responseBody) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody)
            };
        }
    }
}

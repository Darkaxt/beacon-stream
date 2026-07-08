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
              "streams": [{
                "sessionId": "z-fold-7-steam-shortcut:3767414131",
                "clientId": "z-fold-7",
                "appId": "steam-shortcut:3767414131",
                "displayId": "client-z-fold-7",
                "codec": "av1",
                "fps": 120,
                "initialBitrateMbps": 65,
                "transport": "lan-direct",
                "state": "running",
                "error": null,
                "connection": {
                  "protocol": "beacon-fake",
                  "launchUri": "beacon-fake://stream/z-fold-7-steam-shortcut:3767414131",
                  "endpoints": [{ "role": "control", "uri": "beacon-fake://stream/z-fold-7-steam-shortcut:3767414131" }],
                  "metadata": { "displayId": "client-z-fold-7" }
                }
              }],
              "ownership": [{ "sessionId": "z-fold-7-steam-shortcut:3767414131", "appId": "steam-shortcut:3767414131", "launchedProcessId": 4321, "launchedProcessRunning": false, "childProcessRunning": false, "ownedWindowRemaining": false, "reasons": [] }],
              "display": {
                "driverReady": true,
                "diagnostic": "SudoVDA driver is ready. Protocol 0.2.1.",
                "topologyAvailable": true,
                "mirrorMode": false,
                "physicalPrimaryVerified": true,
                "paths": [
                  { "displayId": "\\\\.\\DISPLAY5", "kind": "Physical", "width": 2560, "height": 1600, "refreshHz": 240, "isPrimary": true, "x": 0, "y": 0 }
                ]
              },
              "streamingHealth": {
                "ready": true,
                "backend": "external-process",
                "diagnostic": "External streaming backend ready.",
                "executableConfigured": true,
                "executableAvailable": true,
                "executablePath": "C:\\Tools\\sunshine-wrapper.exe",
                "wrapperChildExecutableConfigured": true,
                "wrapperChildExecutableAvailable": true,
                "wrapperChildExecutablePath": "C:\\Tools\\sunshine.exe",
                "wrapperChildArgumentsConfigured": true,
                "manifestConfigured": true,
                "manifestAvailable": true,
                "manifestPath": "C:\\Tools\\beacon-streaming.json",
                "manifestName": "Sunshine bridge",
                "protocol": "gamestream",
                "launchUri": "moonlight://beacon/z-fold-7",
                "endpoints": [
                  { "role": "rtsp", "uri": "rtsp://127.0.0.1:48010" },
                  { "role": "audio", "uri": "udp://127.0.0.1:48000" }
                ],
                "codecs": ["av1", "hevc"],
                "transports": ["lan-direct"],
                "encoders": ["nvenc"],
                "capture": ["dxgi"],
                "maxFps": 120,
                "maxBitrateMbps": 150,
                "hdr10": true,
                "activeSessions": 1,
                "diagnostics": ["ready"]
              },
              "inputHealth": {
                "ready": true,
                "backend": "windows-sendinput",
                "diagnostic": "Windows SendInput pointer and keyboard sink ready.",
                "supportedEventTypes": ["pointer", "keyboard"],
                "supportedPointerActions": ["move", "down", "up", "tap"],
                "supportedKeyboardActions": ["down", "up", "press"]
              },
              "games": { "total": 36, "diagnostics": ["Steam library 'G:\\SteamLibrary\\steamapps' does not exist."] },
              "diagnostics": [{
                "id": "evt-1",
                "timestampUtc": "1970-01-01T00:00:00+00:00",
                "severity": "error",
                "category": "streaming",
                "operation": "preflight",
                "message": "External streaming manifest codec av1 is not supported.",
                "clientId": "z-fold-7",
                "sessionId": "session-1",
                "displayId": "client-z-fold-7",
                "metadata": { "codec": "av1" }
              }]
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
        Assert.Equal("beacon-fake", snapshot.Streams[0].Connection?.Protocol);
        Assert.Equal("beacon-fake://stream/z-fold-7-steam-shortcut:3767414131", snapshot.Streams[0].Connection?.LaunchUri);
        Assert.Single(snapshot.Ownership);
        Assert.Equal(4321, snapshot.Ownership[0].LaunchedProcessId);
        Assert.True(snapshot.Display.DriverReady);
        Assert.True(snapshot.Display.PhysicalPrimaryVerified);
        Assert.Single(snapshot.Display.Paths);
        Assert.Equal(@"\\.\DISPLAY5", snapshot.Display.Paths[0].DisplayId);
        Assert.True(snapshot.StreamingHealth.Ready);
        Assert.Equal("external-process", snapshot.StreamingHealth.Backend);
        Assert.True(snapshot.StreamingHealth.WrapperChildExecutableConfigured);
        Assert.True(snapshot.StreamingHealth.WrapperChildExecutableAvailable);
        Assert.Equal("C:\\Tools\\sunshine.exe", snapshot.StreamingHealth.WrapperChildExecutablePath);
        Assert.True(snapshot.StreamingHealth.WrapperChildArgumentsConfigured);
        Assert.Equal(["av1", "hevc"], snapshot.StreamingHealth.Codecs);
        Assert.Contains(snapshot.StreamingHealth.Endpoints, endpoint => endpoint.Role == "rtsp" && endpoint.Uri == "rtsp://127.0.0.1:48010");
        Assert.True(snapshot.StreamingHealth.Hdr10);
        Assert.True(snapshot.InputHealth.Ready);
        Assert.Equal("windows-sendinput", snapshot.InputHealth.Backend);
        Assert.Equal(["pointer", "keyboard"], snapshot.InputHealth.SupportedEventTypes);
        Assert.Equal(["move", "down", "up", "tap"], snapshot.InputHealth.SupportedPointerActions);
        Assert.Equal(["down", "up", "press"], snapshot.InputHealth.SupportedKeyboardActions);
        Assert.Equal(36, snapshot.Games.Total);
        Assert.Single(snapshot.Games.Diagnostics);
        Assert.Single(snapshot.Diagnostics);
        Assert.Equal("streaming", snapshot.Diagnostics[0].Category);
        Assert.Equal("av1", snapshot.Diagnostics[0].Metadata["codec"]);
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
        await client.ResetTopologyAsync(CancellationToken.None);
        await client.MoveWindowsBackAsync(minimize: true, CancellationToken.None);
        await client.CloseVirtualWindowsAsync(CancellationToken.None);
        await client.TerminateVirtualProcessesAsync(CancellationToken.None);
        await client.RecoverClientDisplayAsync("z fold/7", CancellationToken.None);
        await client.RemoveClientDisplayLeaseAsync("z fold/7", CancellationToken.None);
        await client.StopClientStreamAsync("z fold/7", CancellationToken.None);

        Assert.Equal(8, handler.Requests.Count);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal("/admin/recovery/restore-physical", handler.Requests[0].RequestUri?.PathAndQuery);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        Assert.Equal("/admin/recovery/reset-topology", handler.Requests[1].RequestUri?.PathAndQuery);
        Assert.Equal(HttpMethod.Post, handler.Requests[2].Method);
        Assert.Equal("/admin/recovery/move-windows-back", handler.Requests[2].RequestUri?.PathAndQuery);
        Assert.Equal(HttpMethod.Post, handler.Requests[3].Method);
        Assert.Equal("/admin/recovery/close-virtual-windows", handler.Requests[3].RequestUri?.PathAndQuery);
        Assert.Equal(HttpMethod.Post, handler.Requests[4].Method);
        Assert.Equal("/admin/recovery/terminate-virtual-processes", handler.Requests[4].RequestUri?.PathAndQuery);
        Assert.Equal(HttpMethod.Post, handler.Requests[5].Method);
        Assert.Equal("/admin/clients/z%20fold%2F7/display/recover", handler.Requests[5].RequestUri?.PathAndQuery);
        Assert.Equal(HttpMethod.Post, handler.Requests[6].Method);
        Assert.Equal("/admin/clients/z%20fold%2F7/display/remove", handler.Requests[6].RequestUri?.PathAndQuery);
        Assert.Equal(HttpMethod.Post, handler.Requests[7].Method);
        Assert.Equal("/admin/clients/z%20fold%2F7/stream/stop", handler.Requests[7].RequestUri?.PathAndQuery);
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

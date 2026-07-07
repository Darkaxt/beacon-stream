using System.Net.Http.Json;

namespace Beacon.FakeEndpoint;

public sealed record FakeEndpointScript(
    string ClientId,
    string Name,
    int Width,
    int Height,
    int RefreshHz,
    bool Av1,
    bool Hevc,
    bool H264,
    bool Hdr10,
    bool VirtualDisplayHdrSupported,
    int RttMs,
    double PacketLossPercent,
    int? DecoderLoadPercent,
    string AppId,
    string Title,
    string Source)
{
    public static FakeEndpointScript CreateZFold7Default() =>
        new(
            ClientId: "z-fold-7",
            Name: "Z Fold 7",
            Width: 2560,
            Height: 1600,
            RefreshHz: 120,
            Av1: true,
            Hevc: true,
            H264: true,
            Hdr10: true,
            VirtualDisplayHdrSupported: false,
            RttMs: 8,
            PacketLossPercent: 0,
            DecoderLoadPercent: null,
            AppId: "steam-shortcut:3767414131",
            Title: "Dispatch",
            Source: "steam-shortcut");
}

public sealed record FakeEndpointResult(bool Success, IReadOnlyList<string> Operations, string? Error);

public sealed class FakeEndpointRunner(HttpClient httpClient)
{
    public async Task<FakeEndpointResult> RunAsync(FakeEndpointScript script, CancellationToken cancellationToken)
    {
        if (script.ClientId == "z-fold-7" && script.Width == 2560 && script.Height == 1440)
        {
            return new FakeEndpointResult(false, [], "Z Fold 7 script must not request 2560x1440.");
        }

        var operations = new List<string>();

        bool ok =
            await SendAsync(HttpMethod.Post, "/clients/hello", new { clientId = script.ClientId, name = script.Name }, operations, cancellationToken) &&
            await SendAsync(HttpMethod.Get, $"/clients/{script.ClientId}/profile", null, operations, cancellationToken) &&
            await SendAsync(HttpMethod.Patch, $"/clients/{script.ClientId}/profile", CreateProfilePatch(script), operations, cancellationToken) &&
            await SendAsync(HttpMethod.Post, $"/clients/{script.ClientId}/capabilities", CreateCapabilities(script), operations, cancellationToken) &&
            await SendAsync(HttpMethod.Post, $"/clients/{script.ClientId}/telemetry", CreateTelemetry(script), operations, cancellationToken) &&
            await SendAsync(HttpMethod.Post, $"/clients/{script.ClientId}/plan", CreatePlanRequest(script), operations, cancellationToken) &&
            await SendAsync(HttpMethod.Post, $"/clients/{script.ClientId}/disconnect", new { }, operations, cancellationToken) &&
            await SendAsync(HttpMethod.Post, $"/clients/{script.ClientId}/plan", CreatePlanRequest(script), operations, cancellationToken) &&
            await SendAsync(HttpMethod.Post, $"/clients/{script.ClientId}/quit", CreateQuitRequest(), operations, cancellationToken) &&
            await SendAsync(HttpMethod.Post, $"/clients/{script.ClientId}/emergency-restore", new { }, operations, cancellationToken);

        return ok
            ? new FakeEndpointResult(true, operations, null)
            : new FakeEndpointResult(false, operations, "Fake endpoint operation failed.");
    }

    private async Task<bool> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        List<string> operations,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = body is null ? null : JsonContent.Create(body)
        };

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        operations.Add($"{method.Method} {path}");
        return response.IsSuccessStatusCode;
    }

    private static object CreateProfilePatch(FakeEndpointScript script) =>
        new
        {
            preferredWidth = script.Width,
            preferredHeight = script.Height,
            preferredRefreshHz = script.RefreshHz
        };

    private static object CreateCapabilities(FakeEndpointScript script) =>
        new
        {
            av1 = script.Av1,
            hevc = script.Hevc,
            h264 = script.H264,
            hdr10 = script.Hdr10,
            virtualDisplayHdrSupported = script.VirtualDisplayHdrSupported
        };

    private static object CreateTelemetry(FakeEndpointScript script) =>
        new
        {
            rttMs = script.RttMs,
            packetLossPercent = script.PacketLossPercent,
            decoderLoadPercent = script.DecoderLoadPercent
        };

    private static object CreatePlanRequest(FakeEndpointScript script) =>
        new
        {
            appId = script.AppId,
            title = script.Title,
            source = script.Source
        };

    private static object CreateQuitRequest() =>
        new
        {
            clientActive = false,
            ownedProcessRunning = false,
            ownedWindowRemaining = false
        };
}

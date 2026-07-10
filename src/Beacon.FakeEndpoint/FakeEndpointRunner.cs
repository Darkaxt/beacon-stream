using System.Net.Http.Json;

namespace Beacon.FakeEndpoint;

public sealed record FakeEndpointScript(
    string ClientId,
    string Name,
    string? PairingToken,
    int Width,
    int Height,
    int RefreshHz,
    int? BitrateCapMbps,
    bool Av1,
    bool Hevc,
    bool H264,
    bool Hdr10,
    bool VirtualDisplayHdrSupported,
    int MaxFps,
    bool LowLatencyDecode,
    string? CurrentScreenMode,
    string TelemetryProfile,
    int RttMs,
    double PacketLossPercent,
    int? DecoderLoadPercent,
    int? EstimatedBandwidthMbps,
    string? WifiBand,
    int? BatteryPercent,
    string? ThermalState,
    string AppId,
    string Title,
    string Source)
{
    public static FakeEndpointScript CreateZFold7Default() =>
        new(
            ClientId: "z-fold-7",
            Name: "Z Fold 7",
            PairingToken: null,
            Width: 2560,
            Height: 1600,
            RefreshHz: 120,
            BitrateCapMbps: null,
            Av1: true,
            Hevc: true,
            H264: true,
            Hdr10: true,
            VirtualDisplayHdrSupported: false,
            MaxFps: 120,
            LowLatencyDecode: true,
            CurrentScreenMode: "2560x1600@120",
            TelemetryProfile: "excellent-lan",
            RttMs: 8,
            PacketLossPercent: 0,
            DecoderLoadPercent: 20,
            EstimatedBandwidthMbps: 120,
            WifiBand: "wifi-7",
            BatteryPercent: 80,
            ThermalState: "nominal",
            AppId: "steam-shortcut:3767414131",
            Title: "Dispatch",
            Source: "steam-shortcut");

    public FakeEndpointScript ApplyTelemetryProfile(string profile)
    {
        string normalized = profile.Trim().ToLowerInvariant();
        return normalized switch
        {
            "congested-lan" => this with
            {
                TelemetryProfile = "congested-lan",
                RttMs = 55,
                PacketLossPercent = 1.5,
                DecoderLoadPercent = 55,
                EstimatedBandwidthMbps = 45,
                WifiBand = "wifi-6",
                BatteryPercent = 60,
                ThermalState = "nominal"
            },
            "high-rtt" => this with
            {
                TelemetryProfile = "high-rtt",
                RttMs = 115,
                PacketLossPercent = 0.5,
                DecoderLoadPercent = 35,
                EstimatedBandwidthMbps = 80,
                WifiBand = "wifi-5",
                BatteryPercent = 70,
                ThermalState = "nominal"
            },
            "packet-loss" => this with
            {
                TelemetryProfile = "packet-loss",
                RttMs = 22,
                PacketLossPercent = 3.2,
                DecoderLoadPercent = 40,
                EstimatedBandwidthMbps = 90,
                WifiBand = "wifi-6",
                BatteryPercent = 70,
                ThermalState = "nominal"
            },
            "low-bitrate-cap" => this with
            {
                TelemetryProfile = "low-bitrate-cap",
                RttMs = 8,
                PacketLossPercent = 0,
                DecoderLoadPercent = 30,
                EstimatedBandwidthMbps = 35,
                WifiBand = "wifi-6",
                BatteryPercent = 75,
                ThermalState = "nominal",
                BitrateCapMbps = 35
            },
            "thermal-battery" => this with
            {
                TelemetryProfile = "thermal-battery",
                RttMs = 12,
                PacketLossPercent = 0,
                DecoderLoadPercent = 88,
                EstimatedBandwidthMbps = 100,
                WifiBand = "wifi-6",
                BatteryPercent = 9,
                ThermalState = "hot"
            },
            _ => this with
            {
                TelemetryProfile = "excellent-lan",
                RttMs = 8,
                PacketLossPercent = 0,
                DecoderLoadPercent = 20,
                EstimatedBandwidthMbps = 120,
                WifiBand = "wifi-7",
                BatteryPercent = 80,
                ThermalState = "nominal"
            }
        };
    }
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
            await SendAsync(HttpMethod.Post, "/clients/hello", new { clientId = script.ClientId, name = script.Name, pairingToken = script.PairingToken }, operations, cancellationToken) &&
            await SendAsync(HttpMethod.Get, $"/clients/{script.ClientId}/profile", null, operations, cancellationToken) &&
            await SendAsync(HttpMethod.Patch, $"/clients/{script.ClientId}/profile", CreateProfilePatch(script), operations, cancellationToken) &&
            await SendAsync(HttpMethod.Post, $"/clients/{script.ClientId}/capabilities", CreateCapabilities(script), operations, cancellationToken) &&
            await SendAsync(HttpMethod.Post, $"/clients/{script.ClientId}/telemetry", CreateTelemetry(script), operations, cancellationToken) &&
            await SendAsync(HttpMethod.Post, $"/clients/{script.ClientId}/beacon", new { active = true }, operations, cancellationToken) &&
            await SendAsync(HttpMethod.Post, $"/clients/{script.ClientId}/plan", CreatePlanRequest(script), operations, cancellationToken) &&
            await SendAsync(HttpMethod.Post, $"/clients/{script.ClientId}/launch", CreatePlanRequest(script), operations, cancellationToken);

        ok = ok &&
            await SendAsync(HttpMethod.Post, $"/clients/{script.ClientId}/input", CreateInputSample(), operations, cancellationToken) &&
            await SendAsync(HttpMethod.Post, $"/clients/{script.ClientId}/disconnect", new { }, operations, cancellationToken) &&
            await SendAsync(HttpMethod.Post, $"/clients/{script.ClientId}/reconnect", new { }, operations, cancellationToken) &&
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
            preferredRefreshHz = script.RefreshHz,
            bitrateCapMbps = script.BitrateCapMbps
        };

    private static object CreateCapabilities(FakeEndpointScript script) =>
        new
        {
            av1 = script.Av1,
            hevc = script.Hevc,
            h264 = script.H264,
            hdr10 = script.Hdr10,
            virtualDisplayHdrSupported = script.VirtualDisplayHdrSupported,
            maxFps = script.MaxFps,
            lowLatencyDecode = script.LowLatencyDecode,
            currentScreenMode = script.CurrentScreenMode
        };

    private static object CreateTelemetry(FakeEndpointScript script) =>
        new
        {
            rttMs = script.RttMs,
            packetLossPercent = script.PacketLossPercent,
            decoderLoadPercent = script.DecoderLoadPercent,
            estimatedBandwidthMbps = script.EstimatedBandwidthMbps,
            wifiBand = script.WifiBand,
            batteryPercent = script.BatteryPercent,
            thermalState = script.ThermalState
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
            clientActive = false
        };

    private static object CreateInputSample() =>
        new
        {
            sequence = 1,
            events = new[]
            {
                CreatePointerEvent("down", x: 0.5, y: 0.5, buttons: 1),
                CreatePointerEvent("move", x: 0.75, y: 0.25),
                CreatePointerEvent("up", x: 0.75, y: 0.25, buttons: 1),
                CreateKeyboardEvent("press", key: "Escape", code: "Escape")
            }
        };

    private static Dictionary<string, object> CreatePointerEvent(string action, double x, double y, int? buttons = null)
    {
        var inputEvent = new Dictionary<string, object>
        {
            ["type"] = "pointer",
            ["action"] = action,
            ["pointerId"] = 1,
            ["x"] = x,
            ["y"] = y
        };

        if (buttons is not null)
        {
            inputEvent["buttons"] = buttons.Value;
        }

        return inputEvent;
    }

    private static Dictionary<string, object> CreateKeyboardEvent(string action, string key, string code) =>
        new()
        {
            ["type"] = "keyboard",
            ["action"] = action,
            ["key"] = key,
            ["code"] = code
        };
}

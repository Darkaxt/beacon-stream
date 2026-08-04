using System.Net.Http.Json;
using System.Text.Json;

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
    int MaxFps,
    bool LowLatencyDecode,
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
    string Source,
    string BenchmarkTrigger)
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
            MaxFps: 120,
            LowLatencyDecode: true,
            TelemetryProfile: "excellent-lan",
            RttMs: 8,
            PacketLossPercent: 0,
            DecoderLoadPercent: 20,
            EstimatedBandwidthMbps: 120,
            WifiBand: "6-ghz",
            BatteryPercent: 80,
            ThermalState: "nominal",
            AppId: "steam-shortcut:3767414131",
            Title: "Dispatch",
            Source: "steam-shortcut",
            BenchmarkTrigger: "automatic");

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
                WifiBand = "5-ghz",
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
                WifiBand = "5-ghz",
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
                WifiBand = "5-ghz",
                BatteryPercent = 70,
                ThermalState = "nominal"
            },
            "thermal-battery" => this with
            {
                TelemetryProfile = "thermal-battery",
                RttMs = 12,
                PacketLossPercent = 0,
                DecoderLoadPercent = 88,
                EstimatedBandwidthMbps = 100,
                WifiBand = "5-ghz",
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
                WifiBand = "6-ghz",
                BatteryPercent = 80,
                ThermalState = "nominal"
            }
        };
    }
}

public sealed record FakeEndpointResult(
    bool Success,
    IReadOnlyList<string> Operations,
    string? Error,
    string? PlanCongestionPolicy = null);

public sealed class FakeEndpointRunner(HttpClient httpClient)
{
    public async Task<FakeEndpointResult> RunAsync(FakeEndpointScript script, CancellationToken cancellationToken)
    {
        var operations = new List<string>();
        var state = new TransactionState();
        bool transactionSucceeded = false;
        bool cleanupSucceeded = true;
        Exception? transactionFailure = null;

        try
        {
            transactionSucceeded = await RunTransactionAsync(script, state, operations, cancellationToken);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            transactionFailure = failure;
        }
        finally
        {
            if (state.ActivationAttempted)
            {
                cleanupSucceeded = await CleanupAsync(script, state, operations);
            }
        }

        bool succeeded = transactionSucceeded && cleanupSucceeded;
        return new FakeEndpointResult(
            succeeded,
            operations,
            succeeded ? null : transactionFailure?.Message ?? "Fake endpoint operation failed.",
            state.PlanCongestionPolicy);
    }

    private async Task<bool> RunTransactionAsync(
        FakeEndpointScript script,
        TransactionState state,
        List<string> operations,
        CancellationToken cancellationToken)
    {
        if (!await SendAsync(HttpMethod.Post, "/clients/hello", new { clientId = script.ClientId, name = script.Name }, operations, cancellationToken) ||
            !await SendAsync(HttpMethod.Post, $"/clients/{script.ClientId}/capabilities", CreateCapabilities(script), operations, cancellationToken))
        {
            return false;
        }

        state.ActivationAttempted = true;
        if (!await SendAsync(
            HttpMethod.Post,
            $"/clients/{script.ClientId}/beacon",
            new { active = true },
            operations,
            cancellationToken))
        {
            return false;
        }

        if (!await SendAsync(HttpMethod.Post, $"/clients/{script.ClientId}/telemetry", CreateTelemetry(script), operations, cancellationToken) ||
            !await RunBenchmarkAsync(script, script.BenchmarkTrigger, operations, cancellationToken) ||
            !await RunBenchmarkAsync(script, "sessionPreflight", operations, cancellationToken))
        {
            return false;
        }

        using JsonDocument? plan = await SendForJsonAsync(
            HttpMethod.Post,
            $"/clients/{script.ClientId}/plan",
            CreatePlanRequest(script),
            operations,
            cancellationToken);
        if (plan is null)
        {
            return false;
        }
        state.PlanCongestionPolicy = plan.RootElement
            .GetProperty("stream")
            .GetProperty("congestionPolicy")
            .GetString();

        if (!await SendAsync(HttpMethod.Post, $"/clients/{script.ClientId}/launch", CreatePlanRequest(script), operations, cancellationToken))
        {
            return false;
        }
        state.StreamStarted = true;

        if (!await SendAsync(HttpMethod.Post, $"/clients/{script.ClientId}/input", CreateInputSample(), operations, cancellationToken) ||
            !await SendAsync(HttpMethod.Post, $"/clients/{script.ClientId}/disconnect", new { }, operations, cancellationToken) ||
            !await SendAsync(HttpMethod.Post, $"/clients/{script.ClientId}/reconnect", new { }, operations, cancellationToken) ||
            !await SendAsync(HttpMethod.Post, $"/clients/{script.ClientId}/stream/stop", new { }, operations, cancellationToken))
        {
            return false;
        }

        state.StreamStarted = false;
        return true;
    }

    private async Task<bool> CleanupAsync(
        FakeEndpointScript script,
        TransactionState state,
        List<string> operations)
    {
        bool succeeded = true;
        if (state.StreamStarted)
        {
            bool stopped = await TryCleanupRequestAsync(
                $"/clients/{script.ClientId}/stream/stop", new { }, operations);
            succeeded &= stopped;
        }

        bool inactive = await TryCleanupRequestAsync(
            $"/clients/{script.ClientId}/beacon", new { active = false }, operations);
        bool quit = await TryCleanupRequestAsync(
            $"/clients/{script.ClientId}/quit", CreateQuitRequest(), operations);
        bool recovered = await TryCleanupRequestAsync(
            $"/clients/{script.ClientId}/emergency-restore", new { }, operations);
        return succeeded && inactive && quit && recovered;
    }

    private async Task<bool> TryCleanupRequestAsync(
        string path,
        object body,
        List<string> operations)
    {
        try
        {
            return await SendAsync(HttpMethod.Post, path, body, operations, CancellationToken.None);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task<bool> RunBenchmarkAsync(
        FakeEndpointScript script,
        string trigger,
        List<string> operations,
        CancellationToken cancellationToken)
    {
        using JsonDocument? prepared = await SendForJsonAsync(
            HttpMethod.Post,
            $"/clients/{script.ClientId}/benchmarks/prepare",
            CreateBenchmarkPrepare(script, trigger),
            operations,
            cancellationToken);
        if (prepared is null)
        {
            return false;
        }

        JsonElement root = prepared.RootElement;
        if (root.GetProperty("disposition").GetString() == "reuse")
        {
            return true;
        }

        Guid runId = root.GetProperty("runId").GetGuid();
        JsonElement coverage = root.GetProperty("networkCoverage");
        JsonElement transport = root.GetProperty("transportPlan");
        JsonElement rounds = root.GetProperty("hardwarePlan").GetProperty("decoderRounds");
        int firstSequence = coverage.GetProperty("firstSequence").GetInt32();
        int packetCount = coverage.GetProperty("expectedPacketCount").GetInt32();
        int payloadBytes = transport.GetProperty("datagramPayloadBytes").GetInt32();

        BenchmarkDecoderSample[] decoderSamples = rounds.EnumerateArray()
            .Select(round => new BenchmarkDecoderSample(
                round.GetProperty("codec").GetString()!,
                round.GetProperty("profile").GetString()!,
                round.GetProperty("bitDepth").GetInt32(),
                round.GetProperty("width").GetInt32(),
                round.GetProperty("height").GetInt32(),
                round.GetProperty("targetFps").GetInt32(),
                Configured: true,
                SustainedFps: round.GetProperty("targetFps").GetInt32(),
                P95DecodeLatencyMs: 1,
                P95PresentationLatencyMs: 2,
                DroppedFrames: 0,
                OutputErrors: 0,
                TenBitPresentationVerified: round.GetProperty("bitDepth").GetInt32() == 10,
                HdrPresentationVerified: false))
            .ToArray();
        int lostPacketCount = script.PacketLossPercent <= 0
            ? 0
            : Math.Max(
                1,
                (int)Math.Round(
                    packetCount * script.PacketLossPercent / 100,
                    MidpointRounding.AwayFromZero));
        lostPacketCount = Math.Min(packetCount, lostPacketCount);
        BenchmarkPowerSample observedPower = new(
            script.BatteryPercent,
            IsCharging: true,
            script.ThermalState ?? "nominal");
        BenchmarkPowerSample[] powerSamples = decoderSamples.Length == 0
            ? [observedPower]
            : decoderSamples
                .SelectMany(_ => new[]
                {
                    observedPower,
                    observedPower
                })
                .ToArray();
        var completion = new
        {
            networkSamples = Enumerable.Range(0, packetCount).Select(index =>
            {
                bool received = index >= lostPacketCount;
                return new
                {
                    sequence = firstSequence + index,
                    payloadBytes,
                    rttMs = script.RttMs,
                    jitterMs = 1,
                    received,
                    throughputMbps = received ? script.EstimatedBandwidthMbps ?? 100 : 0,
                    reorderDistance = 0
                };
            }),
            decoderSamples,
            powerSamples
        };

        return await SendAsync(
            HttpMethod.Post,
            $"/clients/{script.ClientId}/benchmarks/{runId:D}/complete",
            completion,
            operations,
            cancellationToken);
    }

    private async Task<JsonDocument?> SendForJsonAsync(
        HttpMethod method,
        string path,
        object body,
        List<string> operations,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body)
        };
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        operations.Add($"{method.Method} {path}");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        return await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
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
            currentDisplayMode = new
            {
                width = script.Width,
                height = script.Height,
                refreshHz = script.RefreshHz
            },
            supportedDisplayModes = new[]
            {
                new
                {
                    width = script.Width,
                    height = script.Height,
                    refreshHz = script.RefreshHz
                }
            }
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

    private static object CreateBenchmarkPrepare(FakeEndpointScript script, string trigger) =>
        new
        {
            trigger,
            fingerprints = new
            {
                network = new
                {
                    schemaVersion = 3,
                    serverRoute = "fake-endpoint-server",
                    transport = "wifi",
                    localNetworkPrefix = "192.168.1.0/24",
                    wifiBand = script.WifiBand,
                    wifiChannel = script.WifiBand == "6-ghz" ? 37 : 149,
                    linkSpeedBucket = "500-999-mbps",
                    saltedNetworkIdHash = new string('a', 64)
                },
                hardware = new
                {
                    schemaVersion = 3,
                    deviceCapabilityRevision = $"{script.ClientId}-capabilities-v1",
                    androidVersion = "simulated",
                    apkVersion = "fake-endpoint-1",
                    displayModeInventoryRevision = $"{script.Width}x{script.Height}-{script.RefreshHz}",
                    codecInventoryRevision = $"{script.Av1}-{script.Hevc}-{script.H264}"
                }
            }
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

    private sealed record BenchmarkDecoderSample(
        string Codec,
        string Profile,
        int BitDepth,
        int Width,
        int Height,
        int TargetFps,
        bool Configured,
        int SustainedFps,
        double P95DecodeLatencyMs,
        double P95PresentationLatencyMs,
        int DroppedFrames,
        int OutputErrors,
        bool TenBitPresentationVerified,
        bool HdrPresentationVerified);

    private sealed record BenchmarkPowerSample(
        int? BatteryPercent,
        bool IsCharging,
        string ThermalState);

    private sealed class TransactionState
    {
        public bool ActivationAttempted { get; set; }
        public bool StreamStarted { get; set; }
        public string? PlanCongestionPolicy { get; set; }
    }
}

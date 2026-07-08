using Beacon.Core.Clients;
using Beacon.Core.Displays;
using Beacon.Core.Games;

namespace Beacon.Core.Sessions;

public static class SessionPlanner
{
    public static SessionPlanResult CreatePlan(
        ClientProfile profile,
        EndpointCapabilities capabilities,
        TelemetrySnapshot telemetry,
        GameDescriptor game)
    {
        string? hdrBlocker = GetHdrBlocker(capabilities);

        if (profile.Display.HdrPreference == HdrPreference.Require && hdrBlocker is not null)
        {
            return new SessionPlanResult(false, null, $"HDR required but {hdrBlocker}.");
        }

        bool hdrEnabled = profile.Display.HdrPreference == HdrPreference.Prefer && hdrBlocker is null;
        string hdrReason = CreateHdrReason(profile.Display.HdrPreference, hdrEnabled, hdrBlocker);

        var display = new PlannedDisplay(
            DisplayId: DisplayLease.CreateDisplayId(profile.ClientId),
            Width: profile.Display.PreferredWidth,
            Height: profile.Display.PreferredHeight,
            RefreshHz: profile.Display.PreferredRefreshHz,
            Mode: profile.Display.Mode,
            HdrPreference: profile.Display.HdrPreference,
            HdrEnabled: hdrEnabled,
            HdrMode: hdrEnabled ? "hdr10" : "sdr",
            Reason: hdrReason);

        var stream = new PlannedStream(
            Codec: SelectCodec(profile.Stream.CodecPreference, capabilities, out string codecReason),
            Fps: SelectFps(profile, capabilities, telemetry),
            InitialBitrateMbps: SelectInitialBitrate(profile, telemetry, out bool bitrateCapApplied),
            Transport: SelectTransport(telemetry),
            CongestionPolicy: SelectCongestionPolicy(telemetry),
            Reason: CreateStreamReason(codecReason, profile, capabilities, telemetry, bitrateCapApplied));

        var plan = new SessionPlan(
            SessionId: $"{profile.ClientId.Value}-{game.Id}",
            ClientId: profile.ClientId,
            AppId: game.Id,
            Display: display,
            Stream: stream);

        return new SessionPlanResult(true, plan, null);
    }

    private static string? GetHdrBlocker(EndpointCapabilities capabilities)
    {
        if (!capabilities.Hdr10)
        {
            return "client does not report HDR10 decoder/display capability";
        }

        if (!capabilities.VirtualDisplayHdrSupported)
        {
            return "virtual display does not report HDR capability";
        }

        return null;
    }

    private static string CreateHdrReason(HdrPreference preference, bool hdrEnabled, string? hdrBlocker) =>
        preference switch
        {
            HdrPreference.Off => "HDR disabled by client profile.",
            HdrPreference.Prefer when hdrEnabled => "HDR enabled because the full advertised chain reports support.",
            HdrPreference.Prefer => $"HDR disabled because {hdrBlocker}.",
            HdrPreference.Require => "HDR required and available.",
            _ => "HDR mode resolved."
        };

    private static string SelectCodec(string preference, EndpointCapabilities capabilities, out string reason)
    {
        string normalized = preference.Trim().ToLowerInvariant();
        if (normalized == "av1" && capabilities.Av1)
        {
            reason = "Codec selected from profile preference av1.";
            return "av1";
        }

        if (normalized == "hevc" && capabilities.Hevc)
        {
            reason = "Codec selected from profile preference hevc.";
            return "hevc";
        }

        if (normalized == "h264" && capabilities.H264)
        {
            reason = "Codec selected from profile preference h264.";
            return "h264";
        }

        if (capabilities.Av1)
        {
            reason = normalized is "auto" or "" ? "Auto codec selected av1." : $"Profile codec preference {preference} unavailable; selected av1.";
            return "av1";
        }

        if (capabilities.Hevc)
        {
            reason = normalized is "auto" or "" ? "Auto codec selected hevc." : $"Profile codec preference {preference} unavailable; selected hevc.";
            return "hevc";
        }

        reason = normalized is "auto" or "" ? "Auto codec selected h264." : $"Profile codec preference {preference} unavailable; selected h264.";
        return "h264";
    }

    private static int SelectFps(ClientProfile profile, EndpointCapabilities capabilities, TelemetrySnapshot telemetry)
    {
        int fps = Math.Min(profile.Display.PreferredRefreshHz, Math.Max(1, capabilities.MaxFps));

        if (telemetry.RttMs >= 80 ||
            telemetry.DecoderLoadPercent >= 85 ||
            IsPowerConstrained(telemetry))
        {
            fps = Math.Min(fps, 60);
        }

        return fps;
    }

    private static int SelectInitialBitrate(ClientProfile profile, TelemetrySnapshot telemetry, out bool bitrateCapApplied)
    {
        int bitrate = 65;

        if (telemetry.EstimatedBandwidthMbps is > 0)
        {
            bitrate = Math.Min(bitrate, Math.Max(10, (int)Math.Floor(telemetry.EstimatedBandwidthMbps.Value * 0.75)));
        }

        if (telemetry.RttMs >= 80)
        {
            bitrate = Math.Min(bitrate, 25);
        }
        else if (telemetry.PacketLossPercent >= 2.0)
        {
            bitrate = Math.Min(bitrate, 35);
        }

        if (telemetry.DecoderLoadPercent >= 85 || IsPowerConstrained(telemetry))
        {
            bitrate = Math.Min(bitrate, 30);
        }

        bitrateCapApplied = false;
        if (profile.Stream.BitrateCapMbps is > 0)
        {
            int capped = Math.Min(bitrate, profile.Stream.BitrateCapMbps.Value);
            bitrateCapApplied = capped != bitrate;
            bitrate = capped;
        }

        return bitrate;
    }

    private static string SelectTransport(TelemetrySnapshot telemetry) =>
        telemetry.RttMs >= 80 || telemetry.PacketLossPercent >= 2.0
            ? "lan-conservative"
            : "lan-direct";

    private static string SelectCongestionPolicy(TelemetrySnapshot telemetry)
    {
        if (IsPowerConstrained(telemetry) || telemetry.DecoderLoadPercent >= 85)
        {
            return "power-save";
        }

        if (telemetry.RttMs >= 80)
        {
            return "latency-protect";
        }

        if (telemetry.PacketLossPercent >= 2.0)
        {
            return "loss-protect";
        }

        return "adaptive";
    }

    private static string CreateStreamReason(
        string codecReason,
        ClientProfile profile,
        EndpointCapabilities capabilities,
        TelemetrySnapshot telemetry,
        bool bitrateCapApplied)
    {
        var reasons = new List<string> { codecReason };

        if (profile.Display.PreferredRefreshHz <= capabilities.MaxFps &&
            profile.Display.PreferredRefreshHz >= 120 &&
            telemetry.RttMs < 40 &&
            telemetry.PacketLossPercent < 1.0 &&
            telemetry.DecoderLoadPercent is null or < 70 &&
            !IsPowerConstrained(telemetry))
        {
            reasons.Add("Excellent LAN telemetry kept 120 FPS.");
        }

        if (telemetry.RttMs >= 80)
        {
            reasons.Add($"RTT {telemetry.RttMs}ms selected latency protection.");
        }

        if (telemetry.PacketLossPercent >= 2.0)
        {
            reasons.Add($"Packet loss {telemetry.PacketLossPercent:0.#}% selected loss protection.");
        }

        if (telemetry.DecoderLoadPercent >= 85)
        {
            reasons.Add($"Decoder load {telemetry.DecoderLoadPercent}% selected power-save planning.");
        }

        if (IsPowerConstrained(telemetry))
        {
            reasons.Add("Thermal or battery telemetry selected power-save planning.");
        }

        if (telemetry.EstimatedBandwidthMbps is > 0 and < 90)
        {
            reasons.Add($"Estimated bandwidth {telemetry.EstimatedBandwidthMbps}Mbps limited initial bitrate.");
        }

        if (bitrateCapApplied)
        {
            reasons.Add("Client bitrate cap limited initial bitrate.");
        }

        return string.Join(" ", reasons);
    }

    private static bool IsPowerConstrained(TelemetrySnapshot telemetry) =>
        telemetry.BatteryPercent is <= 15 ||
        telemetry.ThermalState?.Equals("hot", StringComparison.OrdinalIgnoreCase) == true ||
        telemetry.ThermalState?.Equals("critical", StringComparison.OrdinalIgnoreCase) == true;
}

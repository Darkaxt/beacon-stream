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
            Codec: SelectCodec(capabilities),
            Fps: Math.Min(profile.Display.PreferredRefreshHz, 120),
            InitialBitrateMbps: SelectInitialBitrate(telemetry),
            Transport: "lan-direct",
            CongestionPolicy: "adaptive");

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

    private static string SelectCodec(EndpointCapabilities capabilities)
    {
        if (capabilities.Av1)
        {
            return "av1";
        }

        if (capabilities.Hevc)
        {
            return "hevc";
        }

        return "h264";
    }

    private static int SelectInitialBitrate(TelemetrySnapshot telemetry)
    {
        if (telemetry.RttMs >= 80)
        {
            return 25;
        }

        if (telemetry.PacketLossPercent >= 2.0)
        {
            return 35;
        }

        return 65;
    }
}

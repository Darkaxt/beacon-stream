using Beacon.Core.Clients;
using Beacon.Core.Displays;
using Beacon.Core.Games;
using Beacon.Core.Sessions;

namespace Beacon.Core.Tests.Sessions;

public sealed class SessionPlannerTests
{
    private static readonly GameDescriptor Dispatch = new(
        "steam-shortcut:3767414131",
        "Dispatch",
        "steam-shortcut",
        new GameLaunchIntent("steam-rungameid", "steam://rungameid/16180920483166814208"),
        new GameArtwork(null, "none"),
        Installed: true,
        new GameProcessHints(null, null));

    [Fact]
    public void PreservesZFoldResolutionRefreshAndVirtualPrimaryMode()
    {
        SessionPlanResult result = SessionPlanner.CreatePlan(
            ClientProfile.CreateZFold7Default(),
            new EndpointCapabilities(Av1: true, Hevc: true, H264: true, Hdr10: true, VirtualDisplayHdrSupported: true),
            new TelemetrySnapshot(RttMs: 8, PacketLossPercent: 0, DecoderLoadPercent: 12),
            Dispatch);

        Assert.True(result.Success);
        SessionPlan plan = Assert.IsType<SessionPlan>(result.Plan);
        Assert.Equal("client-z-fold-7", plan.Display.DisplayId);
        Assert.Equal(2560, plan.Display.Width);
        Assert.Equal(1600, plan.Display.Height);
        Assert.Equal(120, plan.Display.RefreshHz);
        Assert.Equal("virtual-primary", plan.Display.Mode);
        Assert.Equal(120, plan.Stream.Fps);
        Assert.Equal("av1", plan.Stream.Codec);
    }

    [Fact]
    public void HdrPreferFallsBackToSdrWithReasonWhenDisplayCannotExposeHdr()
    {
        SessionPlanResult result = SessionPlanner.CreatePlan(
            ClientProfile.CreateZFold7Default(),
            new EndpointCapabilities(Av1: true, Hevc: true, H264: true, Hdr10: true, VirtualDisplayHdrSupported: false),
            new TelemetrySnapshot(RttMs: 8, PacketLossPercent: 0, DecoderLoadPercent: 12),
            Dispatch);

        Assert.True(result.Success);
        SessionPlan plan = Assert.IsType<SessionPlan>(result.Plan);
        Assert.False(plan.Display.HdrEnabled);
        Assert.Equal("sdr", plan.Display.HdrMode);
        Assert.Contains("virtual display", plan.Display.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DisplayReasonExplainsPhysicalBlackoutModeBeforeLaunch()
    {
        ClientProfile profile = ClientProfile.CreateZFold7Default() with
        {
            Display = ClientProfile.CreateZFold7Default().Display with { Mode = "physical-blackout" }
        };

        SessionPlanResult result = SessionPlanner.CreatePlan(
            profile,
            new EndpointCapabilities(Av1: true, Hevc: true, H264: true, Hdr10: true, VirtualDisplayHdrSupported: false, MaxFps: 120),
            new TelemetrySnapshot(RttMs: 8, PacketLossPercent: 0, DecoderLoadPercent: 20, EstimatedBandwidthMbps: 120, WifiBand: "wifi-7"),
            Dispatch);

        SessionPlan plan = Assert.IsType<SessionPlan>(result.Plan);
        Assert.Equal("physical-blackout", plan.Display.Mode);
        Assert.Contains("blackout", plan.Display.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("HDR disabled", plan.Display.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HdrRequireFailsBeforeLaunchWhenHdrChainIsIncomplete()
    {
        ClientProfile profile = ClientProfile.CreateZFold7Default() with
        {
            Display = ClientProfile.CreateZFold7Default().Display with { HdrPreference = HdrPreference.Require }
        };

        SessionPlanResult result = SessionPlanner.CreatePlan(
            profile,
            new EndpointCapabilities(Av1: true, Hevc: true, H264: true, Hdr10: true, VirtualDisplayHdrSupported: false),
            new TelemetrySnapshot(RttMs: 8, PacketLossPercent: 0, DecoderLoadPercent: 12),
            Dispatch);

        Assert.False(result.Success);
        Assert.Null(result.Plan);
        Assert.Contains("HDR", result.Error, StringComparison.Ordinal);
        Assert.Contains("virtual display", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CongestedTelemetryLowersInitialBitrateButDoesNotChangeDisplayResolution()
    {
        SessionPlanResult result = SessionPlanner.CreatePlan(
            ClientProfile.CreateZFold7Default(),
            new EndpointCapabilities(Av1: false, Hevc: true, H264: true, Hdr10: false, VirtualDisplayHdrSupported: false),
            new TelemetrySnapshot(RttMs: 95, PacketLossPercent: 3.5, DecoderLoadPercent: 78),
            Dispatch);

        Assert.True(result.Success);
        SessionPlan plan = Assert.IsType<SessionPlan>(result.Plan);
        Assert.Equal(2560, plan.Display.Width);
        Assert.Equal(1600, plan.Display.Height);
        Assert.Equal(25, plan.Stream.InitialBitrateMbps);
        Assert.Equal("hevc", plan.Stream.Codec);
        Assert.Equal("latency-protect", plan.Stream.CongestionPolicy);
        Assert.Equal("lan-conservative", plan.Stream.Transport);
    }

    [Fact]
    public void ExcellentLanKeepsRequested120FpsAndExplainsPlan()
    {
        SessionPlanResult result = SessionPlanner.CreatePlan(
            ClientProfile.CreateZFold7Default(),
            new EndpointCapabilities(Av1: true, Hevc: true, H264: true, Hdr10: true, VirtualDisplayHdrSupported: false, MaxFps: 120),
            new TelemetrySnapshot(RttMs: 8, PacketLossPercent: 0, DecoderLoadPercent: 20, EstimatedBandwidthMbps: 120, WifiBand: "wifi-7"),
            Dispatch);

        SessionPlan plan = Assert.IsType<SessionPlan>(result.Plan);
        Assert.Equal(120, plan.Stream.Fps);
        Assert.Equal(65, plan.Stream.InitialBitrateMbps);
        Assert.Equal("av1", plan.Stream.Codec);
        Assert.Contains("excellent LAN", plan.Stream.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HighRttUsesConservativeTransportAndLowerFps()
    {
        SessionPlanResult result = SessionPlanner.CreatePlan(
            ClientProfile.CreateZFold7Default(),
            new EndpointCapabilities(Av1: true, Hevc: true, H264: true, Hdr10: false, VirtualDisplayHdrSupported: false, MaxFps: 120),
            new TelemetrySnapshot(RttMs: 115, PacketLossPercent: 0.5, DecoderLoadPercent: 35, EstimatedBandwidthMbps: 80, WifiBand: "wifi-5"),
            Dispatch);

        SessionPlan plan = Assert.IsType<SessionPlan>(result.Plan);
        Assert.Equal(60, plan.Stream.Fps);
        Assert.Equal(25, plan.Stream.InitialBitrateMbps);
        Assert.Equal("latency-protect", plan.Stream.CongestionPolicy);
        Assert.Equal("lan-conservative", plan.Stream.Transport);
        Assert.Contains("RTT", plan.Stream.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PacketLossKeepsResolutionButProtectsBitrate()
    {
        SessionPlanResult result = SessionPlanner.CreatePlan(
            ClientProfile.CreateZFold7Default(),
            new EndpointCapabilities(Av1: false, Hevc: true, H264: true, Hdr10: false, VirtualDisplayHdrSupported: false, MaxFps: 120),
            new TelemetrySnapshot(RttMs: 22, PacketLossPercent: 3.2, DecoderLoadPercent: 40, EstimatedBandwidthMbps: 90, WifiBand: "wifi-6"),
            Dispatch);

        SessionPlan plan = Assert.IsType<SessionPlan>(result.Plan);
        Assert.Equal(2560, plan.Display.Width);
        Assert.Equal(1600, plan.Display.Height);
        Assert.Equal("hevc", plan.Stream.Codec);
        Assert.Equal(35, plan.Stream.InitialBitrateMbps);
        Assert.Equal("loss-protect", plan.Stream.CongestionPolicy);
    }

    [Fact]
    public void BitrateCapWinsOverExcellentNetwork()
    {
        ClientProfile profile = ClientProfile.CreateZFold7Default() with
        {
            Stream = ClientProfile.CreateZFold7Default().Stream with { BitrateCapMbps = 40 }
        };

        SessionPlanResult result = SessionPlanner.CreatePlan(
            profile,
            new EndpointCapabilities(Av1: true, Hevc: true, H264: true, Hdr10: false, VirtualDisplayHdrSupported: false, MaxFps: 120),
            new TelemetrySnapshot(RttMs: 8, PacketLossPercent: 0, DecoderLoadPercent: 20, EstimatedBandwidthMbps: 200, WifiBand: "wifi-7"),
            Dispatch);

        SessionPlan plan = Assert.IsType<SessionPlan>(result.Plan);
        Assert.Equal(40, plan.Stream.InitialBitrateMbps);
        Assert.Contains("bitrate cap", plan.Stream.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ThermalAndBatteryConstrainedEndpointUsesPowerSave()
    {
        SessionPlanResult result = SessionPlanner.CreatePlan(
            ClientProfile.CreateZFold7Default(),
            new EndpointCapabilities(Av1: true, Hevc: true, H264: true, Hdr10: false, VirtualDisplayHdrSupported: false, MaxFps: 120),
            new TelemetrySnapshot(RttMs: 12, PacketLossPercent: 0, DecoderLoadPercent: 88, EstimatedBandwidthMbps: 100, WifiBand: "wifi-6", BatteryPercent: 9, ThermalState: "hot"),
            Dispatch);

        SessionPlan plan = Assert.IsType<SessionPlan>(result.Plan);
        Assert.Equal(60, plan.Stream.Fps);
        Assert.Equal(30, plan.Stream.InitialBitrateMbps);
        Assert.Equal("power-save", plan.Stream.CongestionPolicy);
        Assert.Contains("thermal", plan.Stream.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExplicitHevcPreferenceOverridesAutoAv1WhenAvailable()
    {
        ClientProfile profile = ClientProfile.CreateZFold7Default() with
        {
            Stream = ClientProfile.CreateZFold7Default().Stream with { CodecPreference = "hevc" }
        };

        SessionPlanResult result = SessionPlanner.CreatePlan(
            profile,
            new EndpointCapabilities(Av1: true, Hevc: true, H264: true, Hdr10: false, VirtualDisplayHdrSupported: false, MaxFps: 120),
            new TelemetrySnapshot(RttMs: 8, PacketLossPercent: 0, DecoderLoadPercent: 20, EstimatedBandwidthMbps: 120, WifiBand: "wifi-7"),
            Dispatch);

        SessionPlan plan = Assert.IsType<SessionPlan>(result.Plan);
        Assert.Equal("hevc", plan.Stream.Codec);
        Assert.Contains("profile", plan.Stream.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RevisionIsStableForTheSamePlanAndChangesWithPlanFacts()
    {
        EndpointCapabilities capabilities = new(
            Av1: true,
            Hevc: true,
            H264: true,
            Hdr10: false,
            VirtualDisplayHdrSupported: false,
            MaxFps: 120);
        TelemetrySnapshot excellent = new(
            RttMs: 8,
            PacketLossPercent: 0,
            DecoderLoadPercent: 20,
            EstimatedBandwidthMbps: 120,
            WifiBand: "wifi-7");
        TelemetrySnapshot congested = excellent with { RttMs = 115 };

        SessionPlan first = Assert.IsType<SessionPlan>(SessionPlanner.CreatePlan(
            ClientProfile.CreateZFold7Default(), capabilities, excellent, Dispatch).Plan);
        SessionPlan repeated = Assert.IsType<SessionPlan>(SessionPlanner.CreatePlan(
            ClientProfile.CreateZFold7Default(), capabilities, excellent, Dispatch).Plan);
        SessionPlan changed = Assert.IsType<SessionPlan>(SessionPlanner.CreatePlan(
            ClientProfile.CreateZFold7Default(), capabilities, congested, Dispatch).Plan);

        Assert.NotEqual(0UL, first.Revision);
        Assert.Equal(first.Revision, repeated.Revision);
        Assert.NotEqual(first.Revision, changed.Revision);
    }
}

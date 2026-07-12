using Beacon.Core.Benchmarks;
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
            CreateEvidence(),
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
            CreateEvidence(),
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
            CreateEvidence(),
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
            CreateEvidence(),
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
            CreateEvidence(codec: "hevc", fps: 60, bitrateMbps: 25, rttMs: 95, packetLossPercent: 3.5),
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
            CreateEvidence(reason: "Excellent LAN benchmark evidence kept 120 FPS."),
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
            CreateEvidence(fps: 60, bitrateMbps: 25, rttMs: 115, packetLossPercent: 0.5, reason: "RTT 115ms selected latency protection."),
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
            CreateEvidence(codec: "hevc", bitrateMbps: 35, rttMs: 22, packetLossPercent: 3.2),
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
            CreateEvidence(),
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
            CreateEvidence(fps: 60, bitrateMbps: 30, rttMs: 12, powerConstrained: true, reason: "Thermal benchmark evidence selected power-save planning."),
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
            CreateEvidence(codec: "hevc", reason: "Codec selected from profile preference hevc."),
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
        BenchmarkPlanEvidence excellent = CreateEvidence(revision: "benchmark-a");
        BenchmarkPlanEvidence congested = CreateEvidence(
            fps: 60,
            bitrateMbps: 25,
            rttMs: 115,
            revision: "benchmark-b",
            runId: Guid.Parse("66ea5505-9c2e-40b2-8c54-1e31be4d8012"));

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

    private static BenchmarkPlanEvidence CreateEvidence(
        string codec = "av1",
        int fps = 120,
        int bitrateMbps = 65,
        double rttMs = 8,
        double packetLossPercent = 0,
        bool powerConstrained = false,
        string reason = "Active benchmark selected the stream limits.",
        string revision = "benchmark-a",
        Guid? runId = null) =>
        new(
            RunId: runId ?? Guid.Parse("39b5f009-f495-4f84-b5e6-6d3911bfaa16"),
            Revision: revision,
            SelectedResult: new SelectedBenchmarkResult(
                Codec: codec,
                MaxSustainableFps: fps,
                InitialBitrateMbps: bitrateMbps,
                SustainableThroughputMbps: 100,
                RttMs: rttMs,
                JitterMs: 1.5,
                PacketLossPercent: packetLossPercent,
                PowerConstrained: powerConstrained,
                Reasons: [reason]));
}

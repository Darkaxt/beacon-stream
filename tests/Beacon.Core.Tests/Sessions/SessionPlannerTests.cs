using Beacon.Core.Clients;
using Beacon.Core.Displays;
using Beacon.Core.Games;
using Beacon.Core.Sessions;

namespace Beacon.Core.Tests.Sessions;

public sealed class SessionPlannerTests
{
    private static readonly GameDescriptor Dispatch = new("steam-shortcut:3767414131", "Dispatch", "steam-shortcut");

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
        Assert.Equal("adaptive", plan.Stream.CongestionPolicy);
    }
}

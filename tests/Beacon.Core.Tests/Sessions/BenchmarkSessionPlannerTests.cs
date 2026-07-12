using Beacon.Core.Benchmarks;
using Beacon.Core.Clients;
using Beacon.Core.Games;
using Beacon.Core.Sessions;

namespace Beacon.Core.Tests.Sessions;

public sealed class BenchmarkSessionPlannerTests
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
    public void PlannerHasNoTelemetryPlanningOverload()
    {
        bool exposesTelemetry = typeof(SessionPlanner)
            .GetMethods()
            .Where(method => method.Name == nameof(SessionPlanner.CreatePlan))
            .SelectMany(method => method.GetParameters())
            .Any(parameter => parameter.ParameterType == typeof(TelemetrySnapshot));

        Assert.False(exposesTelemetry);
    }

    [Fact]
    public void ValidatedBenchmarkConstrainsStreamWithoutChangingDisplayIntent()
    {
        BenchmarkPlanEvidence evidence = CreatePlanEvidence(
            codec: "h264",
            fps: 60,
            bitrateMbps: 35);

        SessionPlanResult result = SessionPlanner.CreatePlan(
            ClientProfile.CreateZFold7Default(),
            new EndpointCapabilities(Av1: true, Hevc: true, H264: true, Hdr10: false, VirtualDisplayHdrSupported: false),
            evidence,
            Dispatch);

        SessionPlan plan = Assert.IsType<SessionPlan>(result.Plan);
        Assert.Equal(2560, plan.Display.Width);
        Assert.Equal(1600, plan.Display.Height);
        Assert.Equal(120, plan.Display.RefreshHz);
        Assert.Equal("h264", plan.Stream.Codec);
        Assert.Equal(60, plan.Stream.Fps);
        Assert.Equal(35, plan.Stream.InitialBitrateMbps);
        Assert.Equal(evidence.RunId, plan.Stream.BenchmarkRunId);
        Assert.Equal(evidence.Revision, plan.Stream.BenchmarkEvidenceRevision);
        Assert.Contains(evidence.RunId.ToString("D"), plan.Stream.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlanRevisionChangesWhenBenchmarkEvidenceChanges()
    {
        BenchmarkPlanEvidence firstEvidence = CreatePlanEvidence("h264", 120, 70);
        BenchmarkPlanEvidence changedEvidence = CreatePlanEvidence("h264", 60, 35) with
        {
            RunId = Guid.Parse("a739f293-1ff9-4e06-a354-c76cb16ee590")
        };

        SessionPlan first = Assert.IsType<SessionPlan>(SessionPlanner.CreatePlan(
            ClientProfile.CreateZFold7Default(),
            new EndpointCapabilities(true, true, true, false, false),
            firstEvidence,
            Dispatch).Plan);
        SessionPlan changed = Assert.IsType<SessionPlan>(SessionPlanner.CreatePlan(
            ClientProfile.CreateZFold7Default(),
            new EndpointCapabilities(true, true, true, false, false),
            changedEvidence,
            Dispatch).Plan);

        Assert.NotEqual(first.Revision, changed.Revision);
    }

    [Fact]
    public void RejectsSelectionThatCurrentCapabilitiesNoLongerAdvertise()
    {
        BenchmarkPlanEvidence evidence = CreatePlanEvidence("av1", 120, 70);

        SessionPlanResult result = SessionPlanner.CreatePlan(
            ClientProfile.CreateZFold7Default(),
            new EndpointCapabilities(Av1: false, Hevc: true, H264: true, Hdr10: false, VirtualDisplayHdrSupported: false),
            evidence,
            Dispatch);

        Assert.False(result.Success);
        Assert.Null(result.Plan);
        Assert.Contains("benchmark", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("capabil", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static BenchmarkPlanEvidence CreatePlanEvidence(string codec, int fps, int bitrateMbps) =>
        new(
            RunId: Guid.Parse("39b5f009-f495-4f84-b5e6-6d3911bfaa16"),
            Revision: "benchmark-revision-a",
            SelectedResult: new SelectedBenchmarkResult(
                Codec: codec,
                MaxSustainableFps: fps,
                InitialBitrateMbps: bitrateMbps,
                SustainableThroughputMbps: 100,
                RttMs: 12,
                JitterMs: 1.5,
                PacketLossPercent: 0,
                PowerConstrained: false,
                Reasons: ["Selected from active network and decoder measurements."]));
}

using Beacon.Core.Clients;
using Beacon.Core.Displays;
using Beacon.Core.Sessions;
using Beacon.Core.Streaming;
using Beacon.Platform.Windows.Streaming;

namespace Beacon.Platform.Windows.Tests.Streaming;

public sealed class ExternalProcessStreamingBackendTests
{
    [Fact]
    public void CreateStartInfoPassesSessionPlanAsArgumentsAndEnvironment()
    {
        SessionPlan plan = CreatePlan();

        ExternalStreamingCommand command = ExternalProcessStreamingBackend.CreateStartCommand(
            "C:\\Tools\\sunshine-wrapper.exe",
            plan);

        Assert.Equal("C:\\Tools\\sunshine-wrapper.exe", command.FileName);
        Assert.Contains("--session", command.Arguments);
        Assert.Contains("z-fold-7-steam-shortcut:3767414131", command.Arguments);
        Assert.Equal("client-z-fold-7", command.Environment["BEACON_DISPLAY_ID"]);
        Assert.Equal("av1", command.Environment["BEACON_STREAM_CODEC"]);
        Assert.Equal("120", command.Environment["BEACON_STREAM_FPS"]);
        Assert.Equal("65", command.Environment["BEACON_STREAM_BITRATE_MBPS"]);
    }

    [Fact]
    public async Task PreflightFailsWhenExecutablePathIsMissing()
    {
        var backend = new ExternalProcessStreamingBackend(
            new ExternalProcessStreamingOptions(null),
            new FakeExternalStreamingProcessRunner());

        StreamingPreflightResult result = await backend.CheckReadinessAsync(CreatePlan(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("External streaming executable path is not configured", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreflightFailsWhenExecutableDoesNotExist()
    {
        var backend = new ExternalProcessStreamingBackend(
            new ExternalProcessStreamingOptions("C:\\Tools\\missing-wrapper.exe"),
            new FakeExternalStreamingProcessRunner());

        StreamingPreflightResult result = await backend.CheckReadinessAsync(CreatePlan(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("does not exist", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartUsesRunnerAndRecordsRunningSession()
    {
        var runner = new FakeExternalStreamingProcessRunner();
        runner.ExistingFiles.Add("C:\\Tools\\sunshine-wrapper.exe");
        var backend = new ExternalProcessStreamingBackend(
            new ExternalProcessStreamingOptions("C:\\Tools\\sunshine-wrapper.exe"),
            runner);

        StreamingStartResult result = await backend.StartAsync(CreatePlan(), CancellationToken.None);

        Assert.True(result.Success);
        StreamingSessionState session = Assert.IsType<StreamingSessionState>(result.Session);
        Assert.Equal("running", session.State);
        Assert.Equal("client-z-fold-7", session.DisplayId);
        ExternalStreamingCommand command = Assert.Single(runner.StartedCommands);
        Assert.Equal("C:\\Tools\\sunshine-wrapper.exe", command.FileName);
        Assert.Equal("120", command.Environment["BEACON_STREAM_FPS"]);
        Assert.Single(backend.GetSessions());
    }

    [Fact]
    public async Task StopTerminatesOwnedProcessAndMarksSessionStopped()
    {
        var runner = new FakeExternalStreamingProcessRunner();
        runner.ExistingFiles.Add("C:\\Tools\\sunshine-wrapper.exe");
        var backend = new ExternalProcessStreamingBackend(
            new ExternalProcessStreamingOptions("C:\\Tools\\sunshine-wrapper.exe"),
            runner);
        SessionPlan plan = CreatePlan();
        await backend.StartAsync(plan, CancellationToken.None);

        StreamingStopResult result = await backend.StopAsync(plan.SessionId, CancellationToken.None);

        Assert.True(result.Success);
        StreamingSessionState session = Assert.IsType<StreamingSessionState>(result.Session);
        Assert.Equal("stopped", session.State);
        Assert.Equal([1001], runner.StoppedProcessIds);
    }

    [Fact]
    public async Task StopFailureIsRecordedAndReturned()
    {
        var runner = new FakeExternalStreamingProcessRunner { NextStopError = "wrapper refused stop" };
        runner.ExistingFiles.Add("C:\\Tools\\sunshine-wrapper.exe");
        var backend = new ExternalProcessStreamingBackend(
            new ExternalProcessStreamingOptions("C:\\Tools\\sunshine-wrapper.exe"),
            runner);
        SessionPlan plan = CreatePlan();
        await backend.StartAsync(plan, CancellationToken.None);

        StreamingStopResult result = await backend.StopAsync(plan.SessionId, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("wrapper refused stop", result.Error, StringComparison.Ordinal);
        StreamingSessionState? session = await backend.GetSessionAsync(plan.SessionId, CancellationToken.None);
        Assert.Equal("stop-failed", session?.State);
        Assert.Equal("wrapper refused stop", session?.Error);
    }

    private static SessionPlan CreatePlan() =>
        new(
            "z-fold-7-steam-shortcut:3767414131",
            new ClientId("z-fold-7"),
            "steam-shortcut:3767414131",
            new PlannedDisplay("client-z-fold-7", 2560, 1600, 120, "virtual-primary", HdrPreference.Prefer, false, "sdr", "HDR unavailable."),
            new PlannedStream("av1", 120, 65, "lan-direct", "adaptive"));

    private sealed class FakeExternalStreamingProcessRunner : IExternalStreamingProcessRunner
    {
        private int nextProcessId = 1001;

        public HashSet<string> ExistingFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<ExternalStreamingCommand> StartedCommands { get; } = [];

        public List<int> StoppedProcessIds { get; } = [];

        public string? NextStopError { get; set; }

        public bool FileExists(string path) => ExistingFiles.Contains(path);

        public ExternalStreamingProcess Start(ExternalStreamingCommand command)
        {
            StartedCommands.Add(command);
            return new ExternalStreamingProcess(nextProcessId++);
        }

        public ExternalStreamingProcessStopResult Stop(ExternalStreamingProcess process)
        {
            if (!string.IsNullOrWhiteSpace(NextStopError))
            {
                return ExternalStreamingProcessStopResult.Fail(NextStopError);
            }

            StoppedProcessIds.Add(process.ProcessId);
            return ExternalStreamingProcessStopResult.Ok();
        }
    }
}

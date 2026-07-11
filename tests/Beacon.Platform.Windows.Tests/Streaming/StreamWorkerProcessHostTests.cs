using System.Security.AccessControl;
using System.Security.Principal;
using System.IO.Pipes;
using Beacon.Platform.Windows.Streaming;
using Beacon.StreamWorker.Contracts.Worker.V1;
using Beacon.Core.Streaming;

namespace Beacon.Platform.Windows.Tests.Streaming;

public sealed class StreamWorkerProcessHostTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "\"\"")]
    [InlineData("C:\\Program Files\\Beacon\\", "\"C:\\Program Files\\Beacon\\\\\"")]
    [InlineData("quoted\"value", "\"quoted\\\"value\"")]
    public void ActiveSessionArgumentsUseWindowsCommandLineQuoting(string value, string expected)
    {
        Assert.Equal(expected, InteractiveStreamWorkerLauncher.QuoteArgument(value));
    }

    [Fact]
    public void PipeSecurityAllowsOnlyOwningUserAndLocalSystem()
    {
        var owner = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

        PipeSecurity security = StreamWorkerProcessHost.CreatePipeSecurity(owner);

        Assert.True(security.AreAccessRulesProtected);
        AuthorizationRuleCollection rules = security.GetAccessRules(
            includeExplicit: true,
            includeInherited: false,
            typeof(SecurityIdentifier));
        PipeAccessRule[] access = rules.Cast<PipeAccessRule>().ToArray();
        Assert.Equal(2, access.Length);
        Assert.All(access, rule => Assert.Equal(AccessControlType.Allow, rule.AccessControlType));
        Assert.Contains(access, rule => owner.Equals(rule.IdentityReference));
        Assert.Contains(access, rule =>
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Equals(rule.IdentityReference));
    }

    [Fact]
    public async Task RealWorkerCompletesExplicitLifecycleWhenBinaryIsAvailable()
    {
        string? executable = Environment.GetEnvironmentVariable("BEACON_STREAM_WORKER_PATH");
        string? identity = Environment.GetEnvironmentVariable("BEACON_SERVER_IDENTITY_PATH");
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable)
            || string.IsNullOrWhiteSpace(identity) || !File.Exists(identity))
        {
            return;
        }

        await using var host = new StreamWorkerProcessHost(
            new StreamWorkerProcessHostOptions(executable, identity));

        await host.EnsureReadyAsync(CancellationToken.None);
        int processId = host.ProcessId;
        var authorizer = new StreamWorkerSessionAuthorizer(host);
        StreamWorkerAuthorizationContext authorizationContext =
            await authorizer.GetContextAsync(CancellationToken.None);
        StreamWorkerAuthorizationResult authorization = await authorizer.AuthorizeAsync(
            new StreamWorkerAuthorization(
                "integration-session",
                "z-fold-7",
                1,
                Enumerable.Repeat((byte)0x5a, 32).ToArray(),
                authorizationContext.WorkerInstanceId,
                DateTimeOffset.UtcNow.AddMinutes(2)),
            CancellationToken.None);
        StreamWorkerAuthorizationResult revocation = await authorizer.RevokeAsync(
            new StreamWorkerRevocation(
                "integration-session",
                Enumerable.Repeat((byte)0x5a, 32).ToArray()),
            CancellationToken.None);
        StreamWorkerCommandResponse prepare = await host.SendAsync(
            Prepare("integration-session"),
            CancellationToken.None);
        StreamWorkerCommandResponse start = await host.SendAsync(
            new WorkerIpcEnvelope
            {
                SessionId = "integration-session",
                StartMedia = new StartMedia(),
            },
            CancellationToken.None);
        StreamWorkerCommandResponse stop = await host.SendAsync(
            new WorkerIpcEnvelope
            {
                SessionId = "integration-session",
                StopMedia = new StopMedia { Reason = StopMediaReason.Explicit },
            },
            CancellationToken.None);

        Assert.True(host.IsReady);
        Assert.True(authorization.Success);
        Assert.True(revocation.Success);
        Assert.True(prepare.Completion.WorkerCompletion.Succeeded);
        Assert.True(start.Completion.WorkerCompletion.Succeeded);
        Assert.True(stop.Completion.WorkerCompletion.Succeeded);
        Assert.Equal(0ul, start.Events.Single(e => e.BodyCase == WorkerIpcEnvelope.BodyOneofCase.MediaMetrics)
            .MediaMetrics.EncodedFrames);

        using (System.Diagnostics.Process firstWorker = System.Diagnostics.Process.GetProcessById(processId))
        {
            firstWorker.Kill();
            await firstWorker.WaitForExitAsync();
        }

        await host.EnsureReadyAsync(CancellationToken.None);

        Assert.True(host.IsReady);
        Assert.NotEqual(processId, host.ProcessId);

        await host.ShutdownAsync(CancellationToken.None);

        Assert.False(host.IsReady);
        Assert.True(host.HasExited);
        Assert.DoesNotContain(System.Diagnostics.Process.GetProcesses(), process => process.Id == processId);
    }

    private static WorkerIpcEnvelope Prepare(string sessionId) => new()
    {
        SessionId = sessionId,
        PrepareSession = new PrepareSession
        {
            DisplayTarget = "virtual-test",
            VideoCodec = WorkerVideoCodec.H264,
            Width = 2560,
            Height = 1600,
            FramesPerSecondNumerator = 120,
            FramesPerSecondDenominator = 1,
            DynamicRange = WorkerDynamicRange.Sdr,
            MinimumBitrateKbps = 1000,
            InitialBitrateKbps = 45000,
            MaximumBitrateKbps = 90000,
        },
    };
}

using System.Diagnostics;
using Beacon.ProductionAcceptance;

namespace Beacon.Server.Tests;

public sealed class ProductionDisplayGuardTests
{
    [Fact]
    public void AcceptanceOptionsPreserveCallerOwnedRunIdentity()
    {
        AcceptanceOptions options = AcceptanceOptions.Parse(
            [
                "--repository-root", @"C:\repo",
                "--serial", "emulator-5554",
                "--run-id", "0123456789abcdef0123456789abcdef"
            ]);

        Assert.Equal("0123456789abcdef0123456789abcdef", options.RunId);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF")]
    public void AcceptanceOptionsRejectUnsafeRunIdentity(string runId)
    {
        Assert.Throws<ArgumentException>(() => AcceptanceOptions.Parse(
            ["--repository-root", @"C:\repo", "--run-id", runId]));
    }

    [Fact]
    public void GuardOptionsRequireExactAcceptanceAndRecoveryIdentity()
    {
        ProductionDisplayGuardOptions options = ProductionDisplayGuardOptions.Parse(
            [
                "--acceptance-process-id", "4321",
                "--client-id", "gate5-emulator-0123456789abcdef0123456789abcdef",
                "--repository-root", @"C:\repo",
                "--ready-event", @"Local\Beacon.Gate5.Ready.test",
                "--completion-event", @"Local\Beacon.Gate5.Complete.test",
                "--log-path", @"C:\evidence\display-guard.log"
            ]);

        Assert.Equal(4321, options.AcceptanceProcessId);
        Assert.Equal("gate5-emulator-0123456789abcdef0123456789abcdef", options.ClientId);
        Assert.Equal(@"C:\repo", options.RepositoryRoot);
        Assert.Equal(@"Local\Beacon.Gate5.Ready.test", options.ReadyEventName);
        Assert.Equal(@"Local\Beacon.Gate5.Complete.test", options.CompletionEventName);
        Assert.Equal(@"C:\evidence\display-guard.log", options.LogPath);
    }

    [Fact]
    public void RecoveryVerificationRequiresPhysicalOnlyTopologyAndZeroDriverLeases()
    {
        ProductionDisplayRecoveryResult result = ProductionDisplayRecoveryVerifier.Verify(
            [
                Command("restore-before-remove", "restore-physical: success verified=True"),
                Command("remove", "remove: success"),
                Command("restore-after-remove", "restore-physical: success verified=True"),
                Command(
                    "display-status",
                    "mirrorMode=False physicalPrimaryVerified=True\n" +
                    "display=\\\\.\\DISPLAY5 kind=Physical 2560x1600@240 primary=True x=0 y=0"),
                Command(
                    "host-agent-status",
                    "{\"success\":true,\"payload\":{\"lease\":{\"leaseCount\":0,\"heartbeatActive\":false}}}")
            ]);

        Assert.True(result.Success, result.Diagnostic);
    }

    [Fact]
    public void RecoveryVerificationAcceptsAnAlreadyAbsentExactLease()
    {
        ProductionDisplayRecoveryResult result = ProductionDisplayRecoveryVerifier.Verify(
            [
                Command("restore-before-remove", "restore-physical: success verified=True"),
                Command(
                    "remove",
                    "remove: failed: No active SudoVDA driver lease owns client-gate5-emulator-test.",
                    exitCode: 2),
                Command("restore-after-remove", "restore-physical: success verified=True"),
                Command(
                    "display-status",
                    "mirrorMode=False physicalPrimaryVerified=True\n" +
                    "display=\\\\.\\DISPLAY5 kind=Physical 2560x1600@240 primary=True x=0 y=0"),
                Command(
                    "host-agent-status",
                    "{\"success\":true,\"payload\":{\"lease\":{\"leaseCount\":0,\"heartbeatActive\":false}}}")
            ]);

        Assert.True(result.Success, result.Diagnostic);
    }

    [Theory]
    [InlineData(
        "mirrorMode=False physicalPrimaryVerified=True\ndisplay=client-test kind=Virtual 1280x720@60 primary=False x=2560 y=0",
        0)]
    [InlineData(
        "mirrorMode=False physicalPrimaryVerified=True\ndisplay=\\\\.\\DISPLAY5 kind=Physical 2560x1600@240 primary=True x=0 y=0",
        1)]
    public void RecoveryVerificationRejectsResidualVirtualTopologyOrLease(
        string displayStatus,
        int leaseCount)
    {
        ProductionDisplayRecoveryResult result = ProductionDisplayRecoveryVerifier.Verify(
            [
                Command("restore-before-remove", "restore-physical: success verified=True"),
                Command("remove", "remove: success"),
                Command("restore-after-remove", "restore-physical: success verified=True"),
                Command("display-status", displayStatus),
                Command(
                    "host-agent-status",
                    $"{{\"success\":true,\"payload\":{{\"lease\":{{\"leaseCount\":{leaseCount},\"heartbeatActive\":false}}}}}}")
            ]);

        Assert.False(result.Success);
    }

    [Theory]
    [InlineData(0x0218u, 0x12u, 2)]
    [InlineData(0x0218u, 0x07u, 2)]
    [InlineData(0x02B1u, 0x08u, 3)]
    [InlineData(0x0312u, 0xB501u, 4)]
    public void WindowsGuardEventsMapOnlyExplicitRecoverySignals(
        uint message,
        nuint parameter,
        int expected)
    {
        Assert.Equal(
            (ProductionDisplayGuardTrigger)expected,
            ProductionDisplayGuardWindowsEvents.Classify(message, parameter));
        Assert.Null(ProductionDisplayGuardWindowsEvents.Classify(0x9999, parameter));
    }

    [Fact]
    public async Task SystemRuntimeForcesInternalOutputThenPerformsExactVerifiedCleanup()
    {
        var options = new ProductionDisplayGuardOptions(
            4321,
            "gate5-emulator-0123456789abcdef0123456789abcdef",
            @"C:\repo",
            @"Local\Beacon.Gate5.Ready.test",
            @"Local\Beacon.Gate5.Complete.test",
            @"C:\evidence\display-guard.log");
        var events = new FakeProductionDisplayGuardEventSource();
        var commands = new FakeProductionDisplayGuardCommandRunner();
        var runtime = new SystemProductionDisplayGuardRuntime(
            options,
            events,
            commands,
            TextWriter.Null);

        await runtime.ForcePhysicalOutputAsync(CancellationToken.None);
        ProductionDisplayRecoveryResult result = await runtime.RecoverAsync(CancellationToken.None);

        Assert.True(result.Success, result.Diagnostic);
        Assert.Equal(
            [
                ("force-physical", new[] { "/internal" }),
                ("restore-before-remove", new[] { "restore-physical" }),
                ("remove", new[] { "remove", "--client", options.ClientId }),
                ("restore-after-remove", new[] { "restore-physical" }),
                ("display-status", new[] { "status" }),
                ("host-agent-status", new[] { "status" })
            ],
            commands.Invocations.Select(invocation => (invocation.Name, invocation.Arguments)));
    }

    [Fact]
    public async Task WindowsEventSourceArmsBeforeAcceptingCompletionSignal()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string token = Guid.NewGuid().ToString("N");
        string childExitEventName = $"Local\\Beacon.Gate5.Test.ChildExit.{token}";
        string completionEventName = $"Local\\Beacon.Gate5.Test.Complete.{token}";
        using var childExit = new EventWaitHandle(false, EventResetMode.ManualReset, childExitEventName);
        using var completion = new EventWaitHandle(false, EventResetMode.ManualReset, completionEventName);
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(
            $"$event=[Threading.EventWaitHandle]::OpenExisting('{childExitEventName}'); " +
            "$null=$event.WaitOne(); $event.Dispose()");
        using Process child = Process.Start(startInfo)!;

        try
        {
            using var events = new WindowsProductionDisplayGuardEventSource(
                child.Id,
                completionEventName);

            completion.Set();
            ProductionDisplayGuardTrigger trigger = await events.WaitForTriggerAsync(CancellationToken.None);

            Assert.Equal(ProductionDisplayGuardTrigger.CompletionSignaled, trigger);
            Assert.True(events.AcceptanceRunning);
        }
        finally
        {
            childExit.Set();
            await child.WaitForExitAsync();
        }
    }

    [Fact]
    public async Task CompletionRestoresPhysicalOutputAndVerifiesRecovery()
    {
        var runtime = new FakeProductionDisplayGuardRuntime(
            ProductionDisplayGuardTrigger.CompletionSignaled,
            acceptanceRunning: false,
            [ProductionDisplayRecoveryResult.Verified("physical-only")]);
        using var output = new StringWriter();
        var guard = new ProductionDisplayGuard(runtime, output);

        int exitCode = await guard.RunAsync(CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(["wait", "force-physical", "recover"], runtime.Operations);
        Assert.Contains("physical-only", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResumeTerminatesAcceptanceBeforeRestoringPhysicalOutput()
    {
        var runtime = new FakeProductionDisplayGuardRuntime(
            ProductionDisplayGuardTrigger.PowerResumed,
            acceptanceRunning: true,
            [ProductionDisplayRecoveryResult.Verified("resumed")]);
        var guard = new ProductionDisplayGuard(runtime, TextWriter.Null);

        int exitCode = await guard.RunAsync(CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(["wait", "terminate", "force-physical", "recover"], runtime.Operations);
    }

    [Fact]
    public async Task FailedRecoveryRetriesOnlyOnTheRecoveryHeartbeat()
    {
        var runtime = new FakeProductionDisplayGuardRuntime(
            ProductionDisplayGuardTrigger.AcceptanceExited,
            acceptanceRunning: false,
            [
                ProductionDisplayRecoveryResult.Failed("Host Agent unavailable"),
                ProductionDisplayRecoveryResult.Verified("lease-count=0")
            ]);
        using var output = new StringWriter();
        var guard = new ProductionDisplayGuard(runtime, output);

        int exitCode = await guard.RunAsync(CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(
            ["wait", "force-physical", "recover", "recovery-heartbeat", "recover"],
            runtime.Operations);
        Assert.Contains("Host Agent unavailable", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("lease-count=0", output.ToString(), StringComparison.Ordinal);
    }

    private sealed class FakeProductionDisplayGuardRuntime(
        ProductionDisplayGuardTrigger trigger,
        bool acceptanceRunning,
        IEnumerable<ProductionDisplayRecoveryResult> recoveryResults)
        : IProductionDisplayGuardRuntime
    {
        private readonly Queue<ProductionDisplayRecoveryResult> recoveryResults = new(recoveryResults);

        public List<string> Operations { get; } = [];

        public bool AcceptanceRunning { get; private set; } = acceptanceRunning;

        public Task<ProductionDisplayGuardTrigger> WaitForTriggerAsync(CancellationToken cancellationToken)
        {
            Operations.Add("wait");
            return Task.FromResult(trigger);
        }

        public Task TerminateAcceptanceAsync(CancellationToken cancellationToken)
        {
            Operations.Add("terminate");
            AcceptanceRunning = false;
            return Task.CompletedTask;
        }

        public Task ForcePhysicalOutputAsync(CancellationToken cancellationToken)
        {
            Operations.Add("force-physical");
            return Task.CompletedTask;
        }

        public Task<ProductionDisplayRecoveryResult> RecoverAsync(CancellationToken cancellationToken)
        {
            Operations.Add("recover");
            return Task.FromResult(recoveryResults.Dequeue());
        }

        public Task WaitForRecoveryHeartbeatAsync(CancellationToken cancellationToken)
        {
            Operations.Add("recovery-heartbeat");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProductionDisplayGuardEventSource : IProductionDisplayGuardEventSource
    {
        public bool AcceptanceRunning => false;

        public Task<ProductionDisplayGuardTrigger> WaitForTriggerAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ProductionDisplayGuardTrigger.AcceptanceExited);

        public Task TerminateAcceptanceAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class FakeProductionDisplayGuardCommandRunner : IProductionDisplayGuardCommandRunner
    {
        public List<(string Name, string FileName, string[] Arguments)> Invocations { get; } = [];

        public Task<ProductionDisplayGuardCommandResult> RunAsync(
            string name,
            string fileName,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            Invocations.Add((name, fileName, arguments.ToArray()));
            string output = name switch
            {
                "display-status" =>
                    "mirrorMode=False physicalPrimaryVerified=True\n" +
                    "display=\\\\.\\DISPLAY5 kind=Physical 2560x1600@240 primary=True x=0 y=0",
                "host-agent-status" =>
                    "{\"success\":true,\"payload\":{\"lease\":{\"leaseCount\":0,\"heartbeatActive\":false}}}",
                _ => "success",
            };
            return Task.FromResult(new ProductionDisplayGuardCommandResult(name, 0, output, string.Empty));
        }
    }

    private static ProductionDisplayGuardCommandResult Command(
        string name,
        string output,
        int exitCode = 0) =>
        new(name, exitCode, output, Error: string.Empty);
}

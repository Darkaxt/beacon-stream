using System.Diagnostics;
using System.Text.Json;
using Beacon.ProductionAcceptance;

namespace Beacon.Server.Tests;

public sealed class ProductionDisplayGuardTests
{
    [Fact]
    public void ProductionProcessTreeAllowsOnlyTheWindowsSystemConsoleHost()
    {
        const string worker = @"C:\repo\Beacon.StreamWorker.exe";
        const string probe = @"C:\repo\Beacon.SessionProbe.exe";
        string systemConsoleHost = Path.Combine(Environment.SystemDirectory, "conhost.exe");

        Assert.True(Beacon.ProductionAcceptance.Program.IsExpectedProductionChildPath(
            systemConsoleHost,
            worker,
            probe));
        Assert.False(Beacon.ProductionAcceptance.Program.IsExpectedProductionChildPath(
            @"C:\repo\conhost.exe",
            worker,
            probe));
    }

    [Fact]
    public void RestoredSnapshotTreatsStoppedStreamHistoryAsTerminal()
    {
        using JsonDocument stopped = JsonDocument.Parse(
            """{"streams":[{"clientId":"client-a","state":"stopped","activeListenerPort":null}]}""");
        using JsonDocument running = JsonDocument.Parse(
            """{"streams":[{"clientId":"client-a","state":"running","activeListenerPort":51234}]}""");

        Assert.False(Beacon.ProductionAcceptance.Program.HasActiveStreamingRuntime(
            stopped.RootElement,
            "client-a"));
        Assert.True(Beacon.ProductionAcceptance.Program.HasActiveStreamingRuntime(
            running.RootElement,
            "client-a"));
    }

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

    [Fact]
    public void AcceptanceOptionsPreserveEmulatorDefaults()
    {
        AcceptanceOptions options = AcceptanceOptions.Parse(
            [
                "--repository-root", @"C:\repo",
                "--run-id", "0123456789abcdef0123456789abcdef"
            ]);

        Assert.Equal(AndroidClientKind.Emulator, options.ClientKind);
        Assert.Equal("emulator-5554", options.Serial);
        Assert.Equal("10.0.2.2", options.ServerHost);
        Assert.Equal("gate5-emulator-0123456789abcdef0123456789abcdef", options.ClientId);
        Assert.True(options.ClearAndroidPackageData);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("*")]
    public void PhysicalAcceptanceRequiresExplicitReachableServerHost(string? serverHost)
    {
        var arguments = new List<string>
        {
            "--repository-root", @"C:\repo",
            "--run-id", "0123456789abcdef0123456789abcdef",
            "--android-client-kind", "physical",
        };
        if (serverHost is not null)
        {
            arguments.Add("--server-host");
            arguments.Add(serverHost);
        }

        Assert.Throws<ArgumentException>(() => AcceptanceOptions.Parse(arguments));
    }

    [Fact]
    public void PhysicalAcceptanceUsesPhysicalRunIdentityWithoutPackageClear()
    {
        AcceptanceOptions options = AcceptanceOptions.Parse(
            [
                "--repository-root", @"C:\repo",
                "--run-id", "0123456789abcdef0123456789abcdef",
                "--android-client-kind", "physical",
                "--server-host", "192.168.8.3",
                "--serial", "R5CX123456A"
            ]);

        Assert.Equal(AndroidClientKind.Physical, options.ClientKind);
        Assert.Equal("192.168.8.3", options.ServerHost);
        Assert.Equal("https://192.168.8.3:47990", options.CreateAndroidServerUrl(47990));
        Assert.Equal("gate5-physical-0123456789abcdef0123456789abcdef", options.ClientId);
        Assert.False(options.ClearAndroidPackageData);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("-1")]
    [InlineData("2")]
    [InlineData(" emulator ")]
    [InlineData("tablet")]
    public void AcceptanceOptionsRejectNonTextualClientKind(string clientKind)
    {
        Assert.Throws<ArgumentException>(() => AcceptanceOptions.Parse(
            [
                "--repository-root", @"C:\repo",
                "--android-client-kind", clientKind,
                "--server-host", "192.168.8.3"
            ]));
    }

    [Theory]
    [InlineData("EMULATOR", "Emulator")]
    [InlineData("PhYsIcAl", "Physical")]
    public void AcceptanceOptionsMatchSupportedClientKindTextIgnoringCase(
        string clientKind,
        string expected)
    {
        var arguments = new List<string>
        {
            "--repository-root", @"C:\repo",
            "--android-client-kind", clientKind,
        };
        if (expected == "Physical")
        {
            arguments.Add("--server-host");
            arguments.Add("192.168.8.3");
        }

        Assert.Equal(expected, AcceptanceOptions.Parse(arguments).ClientKind.ToString());
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF")]
    public void AcceptanceOptionsRejectUnsafeRunIdentity(string runId)
    {
        Assert.Throws<ArgumentException>(() => AcceptanceOptions.Parse(
            ["--repository-root", @"C:\repo", "--run-id", runId]));
    }

    [Theory]
    [InlineData("gate5-emulator-0123456789abcdef0123456789abcdef")]
    [InlineData("gate5-physical-fedcba9876543210fedcba9876543210")]
    public void GuardOptionsRequireExactAcceptanceAndRecoveryIdentity(string clientId)
    {
        ProductionDisplayGuardOptions options = ProductionDisplayGuardOptions.Parse(
            [
                "--acceptance-process-id", "4321",
                "--client-id", clientId,
                "--repository-root", @"C:\repo",
                "--ready-event", @"Local\Beacon.Gate5.Ready.test",
                "--completion-event", @"Local\Beacon.Gate5.Complete.test",
                "--log-path", @"C:\evidence\display-guard.log"
            ]);

        Assert.Equal(4321, options.AcceptanceProcessId);
        Assert.Equal(clientId, options.ClientId);
        Assert.Equal(@"C:\repo", options.RepositoryRoot);
        Assert.Equal(@"Local\Beacon.Gate5.Ready.test", options.ReadyEventName);
        Assert.Equal(@"Local\Beacon.Gate5.Complete.test", options.CompletionEventName);
        Assert.Equal(@"C:\evidence\display-guard.log", options.LogPath);
    }

    [Theory]
    [InlineData("z-fold-7")]
    [InlineData("gate5-emulator-not-a-run-id")]
    [InlineData("gate5-emulator-0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("gate5-tablet-0123456789abcdef0123456789abcdef")]
    [InlineData("gate5-physical-0123456789abcdef0123456789abcde")]
    [InlineData("gate5-physical-0123456789abcdef0123456789abcdef-extra")]
    public void GuardOptionsRejectNonRunScopedClientIdentity(string clientId)
    {
        Assert.Throws<ArgumentException>(() => ProductionDisplayGuardOptions.Parse(
            [
                "--acceptance-process-id", "4321",
                "--client-id", clientId,
                "--repository-root", @"C:\repo",
                "--ready-event", @"Local\Beacon.Gate5.Ready.test",
                "--completion-event", @"Local\Beacon.Gate5.Complete.test",
                "--log-path", @"C:\evidence\display-guard.log"
            ]));
    }

    [Fact]
    public void GuardHostPassesTheOptionsClientIdentityToRecovery()
    {
        AcceptanceOptions options = AcceptanceOptions.Parse(
            [
                "--repository-root", @"C:\repo",
                "--run-id", "0123456789abcdef0123456789abcdef",
                "--android-client-kind", "physical",
                "--server-host", "beacon-host.lan"
            ]);

        ProcessStartInfo startInfo = ProductionDisplayGuardHost.CreateStartInfo(
            options,
            @"C:\repo\Beacon.ProductionAcceptance.exe",
            @"Local\Beacon.Gate5.Ready.test",
            @"Local\Beacon.Gate5.Complete.test",
            @"C:\evidence\display-guard.log");

        string[] arguments = startInfo.ArgumentList.ToArray();
        int clientIdOption = Array.IndexOf(arguments, "--client-id");
        Assert.True(clientIdOption >= 0);
        Assert.Equal(options.ClientId, arguments[clientIdOption + 1]);
    }

    [Fact]
    public void PhysicalAcceptanceHostsServerThroughTheExecutableApphost()
    {
        AcceptanceOptions physical = AcceptanceOptions.Parse(
            [
                "--repository-root", @"C:\repo",
                "--run-id", "0123456789abcdef0123456789abcdef",
                "--android-client-kind", "physical",
                "--server-host", "192.168.8.3"
            ]);
        AcceptanceOptions emulator = AcceptanceOptions.Parse(
            [
                "--repository-root", @"C:\repo",
                "--run-id", "0123456789abcdef0123456789abcdef"
            ]);
        const string serverExecutable = @"C:\repo\Beacon.Server.exe";
        const string serverAssembly = @"C:\repo\Beacon.Server.dll";

        ProcessStartInfo physicalStart = Beacon.ProductionAcceptance.Program
            .CreateServerHostStartInfo(physical, serverExecutable, serverAssembly);
        ProcessStartInfo emulatorStart = Beacon.ProductionAcceptance.Program
            .CreateServerHostStartInfo(emulator, serverExecutable, serverAssembly);

        Assert.Equal(serverExecutable, physicalStart.FileName);
        Assert.DoesNotContain(serverAssembly, physicalStart.ArgumentList);
        Assert.DoesNotContain("dotnet", physicalStart.ArgumentList);
        Assert.False(physicalStart.UseShellExecute);
        Assert.Equal("dotnet", emulatorStart.FileName);
        Assert.Equal(serverAssembly, emulatorStart.ArgumentList[0]);
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
            ],
            "gate5-emulator-0123456789abcdef0123456789abcdef");

        Assert.True(result.Success, result.Diagnostic);
    }

    [Fact]
    public void RecoveryVerificationAcceptsAnAlreadyAbsentExactLease()
    {
        const string clientId = "gate5-emulator-0123456789abcdef0123456789abcdef";
        ProductionDisplayRecoveryResult result = ProductionDisplayRecoveryVerifier.Verify(
            [
                Command("restore-before-remove", "restore-physical: success verified=True"),
                Command(
                    "remove",
                    $"remove: failed: No active SudoVDA driver lease owns client-{clientId}.",
                    exitCode: 2),
                Command("restore-after-remove", "restore-physical: success verified=True"),
                Command(
                    "display-status",
                    "mirrorMode=False physicalPrimaryVerified=True\n" +
                    "display=\\\\.\\DISPLAY5 kind=Physical 2560x1600@240 primary=True x=0 y=0"),
                Command(
                    "host-agent-status",
                    "{\"success\":true,\"payload\":{\"lease\":{\"leaseCount\":0,\"heartbeatActive\":false}}}")
            ],
            clientId);

        Assert.True(result.Success, result.Diagnostic);
    }

    [Fact]
    public void RecoveryVerificationRejectsAlreadyAbsentDiagnosticForAnotherClient()
    {
        ProductionDisplayRecoveryResult result = ProductionDisplayRecoveryVerifier.Verify(
            [
                Command("restore-before-remove", "restore-physical: success verified=True"),
                Command(
                    "remove",
                    "remove: failed: No active SudoVDA driver lease owns client-" +
                    "gate5-physical-fedcba9876543210fedcba9876543210.",
                    exitCode: 2),
                Command("restore-after-remove", "restore-physical: success verified=True"),
                Command(
                    "display-status",
                    "mirrorMode=False physicalPrimaryVerified=True\n" +
                    "display=\\\\.\\DISPLAY5 kind=Physical 2560x1600@240 primary=True x=0 y=0"),
                Command(
                    "host-agent-status",
                    "{\"success\":true,\"payload\":{\"lease\":{\"leaseCount\":0,\"heartbeatActive\":false}}}")
            ],
            "gate5-emulator-0123456789abcdef0123456789abcdef");

        Assert.False(result.Success);
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
            ],
            "gate5-emulator-0123456789abcdef0123456789abcdef");

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

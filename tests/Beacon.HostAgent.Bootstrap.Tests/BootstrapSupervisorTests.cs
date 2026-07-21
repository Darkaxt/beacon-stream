using System.Security.Principal;
using Beacon.HostAgent.Contracts;
using Beacon.HostAgent.Update;

namespace Beacon.HostAgent.Bootstrap.Tests;

public sealed class BootstrapSupervisorTests
{
    [Fact]
    public async Task CurrentVersionReadinessThenNormalExitReturnsChildCode()
    {
        using var fixture = new SupervisorFixture();
        fixture.Platform.Enqueue(FakeLaunch.ReadyThenExit(0));

        int result = await fixture.Supervisor.RunAsync();

        Assert.Equal(0, result);
        Assert.Equal(new[] { "agent-current" }, fixture.Platform.LaunchedVersions);
        Assert.Equal("agent-current", fixture.State.ReadCurrent()?.VersionId);
    }

    [Fact]
    public async Task UpdateExitActivatesCandidateAndCommitsOnlyAfterReadiness()
    {
        using var fixture = new SupervisorFixture();
        fixture.PreparePendingUpdate();
        fixture.Platform.Enqueue(FakeLaunch.ReadyThenExit(BootstrapExitCodes.ApplyUpdate));
        fixture.Platform.Enqueue(FakeLaunch.ReadyThenExit(0));

        int result = await fixture.Supervisor.RunAsync();

        Assert.Equal(0, result);
        Assert.Equal(new[] { "agent-current", fixture.PackageId }, fixture.Platform.LaunchedVersions);
        Assert.Equal(fixture.PackageId, fixture.State.ReadCurrent()?.VersionId);
        Assert.Null(fixture.State.ReadPending());
        Assert.True(fixture.Journal.TryRead(fixture.TransactionId, out HostAgentUpdatePayload? update));
        Assert.Equal(HostAgentUpdateState.Succeeded, update?.State);
        Assert.Equal(fixture.PackageId, update?.ActiveVersionId);
    }

    [Fact]
    public async Task CandidateExitBeforeReadinessRollsBackExactlyOnce()
    {
        using var fixture = new SupervisorFixture();
        fixture.PreparePendingUpdate();
        fixture.Platform.Enqueue(FakeLaunch.ReadyThenExit(BootstrapExitCodes.ApplyUpdate));
        fixture.Platform.Enqueue(FakeLaunch.ExitBeforeReadiness(55));
        fixture.Platform.Enqueue(FakeLaunch.ReadyThenExit(0));

        int result = await fixture.Supervisor.RunAsync();

        Assert.Equal(0, result);
        Assert.Equal(
            new[] { "agent-current", fixture.PackageId, "agent-current" },
            fixture.Platform.LaunchedVersions);
        Assert.Equal("agent-current", fixture.State.ReadCurrent()?.VersionId);
        Assert.Null(fixture.State.ReadPending());
        Assert.True(fixture.Journal.TryRead(fixture.TransactionId, out HostAgentUpdatePayload? update));
        Assert.Equal(HostAgentUpdateState.RolledBack, update?.State);
    }

    [Fact]
    public async Task CandidateReadinessAuthenticationFailureRollsBackExactlyOnce()
    {
        using var fixture = new SupervisorFixture();
        fixture.PreparePendingUpdate();
        fixture.Platform.Enqueue(FakeLaunch.ReadyThenExit(BootstrapExitCodes.ApplyUpdate));
        fixture.Platform.Enqueue(FakeLaunch.ReadinessFails());
        fixture.Platform.Enqueue(FakeLaunch.ReadyThenExit(0));

        int result = await fixture.Supervisor.RunAsync();

        Assert.Equal(0, result);
        Assert.Equal(
            new[] { "agent-current", fixture.PackageId, "agent-current" },
            fixture.Platform.LaunchedVersions);
        Assert.True(fixture.Journal.TryRead(fixture.TransactionId, out HostAgentUpdatePayload? update));
        Assert.Equal(HostAgentUpdateState.RolledBack, update?.State);
    }

    [Fact]
    public async Task PreviousVersionExitBeforeReadinessIsDurablyDegraded()
    {
        using var fixture = new SupervisorFixture();
        fixture.PreparePendingUpdate();
        fixture.Platform.Enqueue(FakeLaunch.ReadyThenExit(BootstrapExitCodes.ApplyUpdate));
        fixture.Platform.Enqueue(FakeLaunch.ExitBeforeReadiness(55));
        fixture.Platform.Enqueue(FakeLaunch.ExitBeforeReadiness(56));

        int result = await fixture.Supervisor.RunAsync();

        Assert.Equal(56, result);
        Assert.Equal(3, fixture.Platform.LaunchedVersions.Count);
        Assert.True(fixture.Journal.TryRead(fixture.TransactionId, out HostAgentUpdatePayload? update));
        Assert.Equal(HostAgentUpdateState.Degraded, update?.State);
        Assert.Equal("agent-current", fixture.State.ReadCurrent()?.VersionId);
    }

    [Fact]
    public async Task RestartWhileAwaitingCandidateReadinessStillRollsBack()
    {
        using var fixture = new SupervisorFixture();
        fixture.PreparePendingUpdate();
        fixture.State.WriteCurrent(new HostAgentSelectedVersion(
            fixture.PackageId,
            new string('A', 40)));
        fixture.Journal.Write(new HostAgentUpdatePayload(
            fixture.TransactionId,
            fixture.PackageId,
            HostAgentUpdateState.AwaitingReadiness,
            "host-agent-update-awaiting-readiness",
            new string('A', 40),
            "agent-current",
            fixture.PackageId));
        fixture.Platform.Enqueue(FakeLaunch.ExitBeforeReadiness(55));
        fixture.Platform.Enqueue(FakeLaunch.ReadyThenExit(0));

        int result = await fixture.Supervisor.RunAsync();

        Assert.Equal(0, result);
        Assert.Equal(new[] { fixture.PackageId, "agent-current" }, fixture.Platform.LaunchedVersions);
        Assert.Equal("agent-current", fixture.State.ReadCurrent()?.VersionId);
        Assert.True(fixture.Journal.TryRead(fixture.TransactionId, out HostAgentUpdatePayload? update));
        Assert.Equal(HostAgentUpdateState.RolledBack, update?.State);
    }

    private sealed class SupervisorFixture : IDisposable
    {
        private readonly string root = Path.Combine(
            Path.GetTempPath(),
            $"beacon-bootstrap-supervisor-{Guid.NewGuid():N}");

        public SupervisorFixture()
        {
            Storage = new BootstrapStorage(
                Path.Combine(root, "install"),
                Path.Combine(root, "data"));
            State = new HostAgentUpdateStateStore(Storage.StateRoot);
            State.WriteCurrent(new HostAgentSelectedVersion("agent-current", new string('B', 40)));
            Journal = new HostAgentUpdateJournal(Storage.TransactionsRoot);
            Activator = new FakeActivator(
                new HostAgentSelectedVersion(PackageId, new string('A', 40)));
            Platform = new FakeBootstrapPlatform();
            Supervisor = new BootstrapSupervisor(
                CurrentOwner(),
                Storage,
                State,
                Journal,
                Activator,
                Platform);
        }

        public Guid TransactionId { get; } = Guid.NewGuid();

        public string PackageId { get; } = "agent-0123456789abcdef";

        public BootstrapStorage Storage { get; }

        public HostAgentUpdateStateStore State { get; }

        public HostAgentUpdateJournal Journal { get; }

        public FakeActivator Activator { get; }

        public FakeBootstrapPlatform Platform { get; }

        public BootstrapSupervisor Supervisor { get; }

        public void PreparePendingUpdate()
        {
            string staged = Path.Combine(Storage.StagedPackagesRoot, TransactionId.ToString("D"));
            Directory.CreateDirectory(staged);
            State.WritePending(new HostAgentPendingUpdate(
                TransactionId,
                PackageId,
                new string('A', 40),
                "agent-current",
                new string('B', 40),
                staged));
            Journal.Write(new HostAgentUpdatePayload(
                TransactionId,
                PackageId,
                HostAgentUpdateState.Staged,
                "host-agent-update-staged",
                new string('A', 40),
                "agent-current",
                ActiveVersionId: null));
        }

        public void Dispose()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private static SecurityIdentifier CurrentOwner() =>
            WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current Windows identity has no SID.");
    }

    private sealed class FakeActivator(HostAgentSelectedVersion candidate) : IHostAgentUpdateActivator
    {
        public List<Guid> Transactions { get; } = [];

        public Task<HostAgentSelectedVersion> ActivateAsync(
            HostAgentPendingUpdate pending,
            HostAgentSelectedVersion current)
        {
            Transactions.Add(pending.TransactionId);
            return Task.FromResult(candidate);
        }
    }

    private sealed class FakeBootstrapPlatform : IBootstrapPlatform
    {
        private readonly Queue<IBootstrapChildLaunch> launches = [];

        public List<string> LaunchedVersions { get; } = [];

        public void Enqueue(IBootstrapChildLaunch launch) => launches.Enqueue(launch);

        public IBootstrapChildLaunch Launch(
            SecurityIdentifier owner,
            HostAgentSelectedVersion version,
            string executablePath)
        {
            LaunchedVersions.Add(version.VersionId);
            return launches.Dequeue();
        }
    }

    private sealed class FakeLaunch(Task readiness, Task<int> exitCode) : IBootstrapChildLaunch
    {
        public Task Readiness { get; } = readiness;

        public Task<int> ExitCode { get; } = exitCode;

        public static FakeLaunch ReadyThenExit(int exitCode) =>
            new(Task.CompletedTask, Task.FromResult(exitCode));

        public static FakeLaunch ExitBeforeReadiness(int exitCode) =>
            new(new TaskCompletionSource().Task, Task.FromResult(exitCode));

        public static FakeLaunch ReadinessFails() =>
            new(
                Task.FromException(new InvalidDataException("readiness rejected")),
                new TaskCompletionSource<int>().Task);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

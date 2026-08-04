using Beacon.Core.Benchmarks;
using Beacon.Core.Clients;
using Beacon.Core.Streaming;
using Beacon.Server.Benchmarks;
using Beacon.Server.Security;

namespace Beacon.Server.Tests;

public sealed class BenchmarkRuntimeOrchestratorTests
{
    [Fact]
    public async Task ConcurrentPrepareForSameRunSharesRuntimeAndTicketGrant()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "beacon-benchmark-orchestrator-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var identity = new BeaconServerIdentity(Path.Combine(directory, "identity.pfx"));
            var runtime = new CoordinatedBenchmarkRuntime();
            var tickets = new StreamTicketService();
            var orchestrator = new BenchmarkRuntimeOrchestrator(
                runtime,
                new StreamTicketProvisioningService(tickets, new FakeStreamSessionAuthorizer()),
                identity);
            var plan = new BenchmarkRuntimePlan(
                Guid.NewGuid(),
                new ClientId("z-fold-7"),
                BenchmarkTrigger.Automatic,
                new BenchmarkTransportPlan(16, 32 * 1024, 64, 1000, 250_000));

            Task<BenchmarkRuntimeGrantResult> first = orchestrator.StartAsync(
                plan,
                [],
                CancellationToken.None);
            Task<BenchmarkRuntimeGrantResult> second = orchestrator.StartAsync(
                plan,
                [],
                CancellationToken.None);

            Assert.Equal(1, runtime.StartCallCount);
            runtime.CompleteStart(plan);
            BenchmarkRuntimeGrantResult[] results = await Task.WhenAll(first, second);

            Assert.All(results, result => Assert.True(result.Success, result.Error));
            Assert.Equal(results[0].Connection?.Ticket, results[1].Connection?.Ticket);
            Assert.Equal(1, runtime.StartCallCount);
            Assert.Equal(
                StreamTicketValidation.Accepted,
                tickets.Consume(
                    results[0].Connection!.Ticket,
                    plan.ClientId.Value,
                    plan.SessionId,
                    plan.Revision,
                    [0x42, 0x45, 0x41, 0x43, 0x4f, 0x4e],
                    DateTimeOffset.UtcNow));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CancelledDuplicateWaiterDoesNotCancelSharedPrepare()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "beacon-benchmark-orchestrator-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var identity = new BeaconServerIdentity(Path.Combine(directory, "identity.pfx"));
            var runtime = new CoordinatedBenchmarkRuntime();
            var orchestrator = new BenchmarkRuntimeOrchestrator(
                runtime,
                new StreamTicketProvisioningService(
                    new StreamTicketService(),
                    new FakeStreamSessionAuthorizer()),
                identity);
            var plan = new BenchmarkRuntimePlan(
                Guid.NewGuid(),
                new ClientId("z-fold-7"),
                BenchmarkTrigger.Automatic,
                new BenchmarkTransportPlan(16, 32 * 1024, 64, 1000, 250_000));
            using var cancelledWaiter = new CancellationTokenSource();

            Task<BenchmarkRuntimeGrantResult> first = orchestrator.StartAsync(
                plan,
                [],
                cancelledWaiter.Token);
            Task<BenchmarkRuntimeGrantResult> second = orchestrator.StartAsync(
                plan,
                [],
                CancellationToken.None);
            cancelledWaiter.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

            runtime.CompleteStart(plan);
            BenchmarkRuntimeGrantResult result = await second;

            Assert.True(result.Success, result.Error);
            Assert.Equal(1, runtime.StartCallCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class CoordinatedBenchmarkRuntime : IBenchmarkRuntime
    {
        private readonly TaskCompletionSource<BenchmarkRuntimeStartResult> start =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private BenchmarkRuntimeState? state;
        private int startCallCount;

        public int StartCallCount => Volatile.Read(ref startCallCount);

        public Task<BenchmarkRuntimeStartResult> StartAsync(
            BenchmarkRuntimePlan plan,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref startCallCount);
            return start.Task;
        }

        public Task<BenchmarkRuntimeStopResult> StopAsync(
            string sessionId,
            CancellationToken cancellationToken) =>
            Task.FromResult(BenchmarkRuntimeStopResult.Fail("Stop was not expected."));

        public Task<BenchmarkRuntimeState?> GetAsync(
            string sessionId,
            CancellationToken cancellationToken) =>
            Task.FromResult(state);

        public void CompleteStart(BenchmarkRuntimePlan plan)
        {
            state = new BenchmarkRuntimeState(
                plan.RunId,
                plan.SessionId,
                plan.ClientId.Value,
                plan.Revision,
                plan.SchemaVersion,
                plan.TransportPlan,
                new byte[16],
                "running",
                Error: null,
                ActiveListenerPort: 48_000,
                RuntimeGeneration: Guid.NewGuid());
            start.SetResult(BenchmarkRuntimeStartResult.Started(state));
        }
    }
}

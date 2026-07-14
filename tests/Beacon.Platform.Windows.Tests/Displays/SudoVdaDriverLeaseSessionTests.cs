using Beacon.Platform.Windows.Displays;

namespace Beacon.Platform.Windows.Tests.Displays;

public sealed class SudoVdaDriverLeaseSessionTests
{
    [Fact]
    public async Task IdleSessionDoesNotOpenOrPingDriver()
    {
        var factory = new FakeSudoVdaDriverConnectionFactory();
        var scheduler = new ManualSudoVdaHeartbeatScheduler();
        await using var session = new SudoVdaDriverLeaseSession(factory, scheduler);

        SudoVdaDriverLeaseSessionSnapshot snapshot = session.Snapshot;

        Assert.Equal(0, snapshot.LeaseCount);
        Assert.False(snapshot.HeartbeatActive);
        Assert.Equal(0, factory.OpenCount);
        Assert.Equal(0, scheduler.ScheduleCount);
    }

    [Fact]
    public async Task FirstLeaseOpensDriverPingsImmediatelyAndReservesOneDriverTickOfMargin()
    {
        var connection = new FakeSudoVdaDriverConnection(timeoutSeconds: 3);
        var factory = new FakeSudoVdaDriverConnectionFactory(connection);
        var scheduler = new ManualSudoVdaHeartbeatScheduler();
        await using var session = new SudoVdaDriverLeaseSession(factory, scheduler);

        SudoVdaDriverLeaseHoldResult result = await session.HoldAsync(
            "client-z-fold-7",
            CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.True(result.Acquired);
        Assert.Equal(1, factory.OpenCount);
        Assert.Equal(1, connection.WatchdogQueryCount);
        Assert.Equal(1, connection.PingCount);
        Assert.Equal(TimeSpan.FromSeconds(1), scheduler.Interval);
        Assert.Equal(1, scheduler.ScheduleCount);
        Assert.Equal(1, session.Snapshot.LeaseCount);
        Assert.True(session.Snapshot.HeartbeatActive);
    }

    [Fact]
    public async Task MultipleLeasesShareOneConnectionAndOneHeartbeat()
    {
        var connection = new FakeSudoVdaDriverConnection(timeoutSeconds: 3);
        var factory = new FakeSudoVdaDriverConnectionFactory(connection);
        var scheduler = new ManualSudoVdaHeartbeatScheduler();
        await using var session = new SudoVdaDriverLeaseSession(factory, scheduler);

        SudoVdaDriverLeaseHoldResult first = await session.HoldAsync("client-one", CancellationToken.None);
        SudoVdaDriverLeaseHoldResult duplicate = await session.HoldAsync("client-one", CancellationToken.None);
        SudoVdaDriverLeaseHoldResult second = await session.HoldAsync("client-two", CancellationToken.None);

        Assert.True(first.Acquired);
        Assert.False(duplicate.Acquired);
        Assert.True(second.Acquired);
        Assert.Equal(2, session.Snapshot.LeaseCount);
        Assert.Equal(1, factory.OpenCount);
        Assert.Equal(1, scheduler.ScheduleCount);
    }

    [Fact]
    public async Task ScheduledTickPingsAndFinalReleaseStopsHeartbeatAndClosesDriver()
    {
        var connection = new FakeSudoVdaDriverConnection(timeoutSeconds: 3);
        var factory = new FakeSudoVdaDriverConnectionFactory(connection);
        var scheduler = new ManualSudoVdaHeartbeatScheduler();
        await using var session = new SudoVdaDriverLeaseSession(factory, scheduler);
        await session.HoldAsync("client-one", CancellationToken.None);
        await session.HoldAsync("client-two", CancellationToken.None);

        await scheduler.TickAsync();
        await session.ReleaseAsync("client-one", CancellationToken.None);

        Assert.Equal(2, connection.PingCount);
        Assert.False(scheduler.Disposed);
        Assert.False(connection.Disposed);
        Assert.Equal(1, session.Snapshot.LeaseCount);

        await session.ReleaseAsync("client-two", CancellationToken.None);

        Assert.True(scheduler.Disposed);
        Assert.True(connection.Disposed);
        Assert.Equal(0, session.Snapshot.LeaseCount);
        Assert.False(session.Snapshot.HeartbeatActive);
    }

    [Fact]
    public async Task PingFailureReportsFaultAndNeverInvokesDisplayCleanup()
    {
        var connection = new FakeSudoVdaDriverConnection(timeoutSeconds: 3);
        connection.PingResults.Enqueue(SudoVdaDriverOperationResult.Ok());
        connection.PingResults.Enqueue(SudoVdaDriverOperationResult.Fail("device disconnected"));
        var factory = new FakeSudoVdaDriverConnectionFactory(connection);
        factory.OpenResults.Enqueue(SudoVdaDriverConnectionOpenResult.Fail("driver unavailable"));
        var scheduler = new ManualSudoVdaHeartbeatScheduler();
        await using var session = new SudoVdaDriverLeaseSession(factory, scheduler);
        await session.HoldAsync("client-one", CancellationToken.None);

        await scheduler.TickAsync();

        Assert.Equal(1, session.Snapshot.LeaseCount);
        Assert.False(session.Snapshot.Healthy);
        Assert.Contains("device disconnected", session.Snapshot.Diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("driver unavailable", session.Snapshot.Diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, connection.RemoveCount);
    }

    [Fact]
    public async Task NextHeartbeatRecoversConnectionWithoutChangingLeaseOwnership()
    {
        var firstConnection = new FakeSudoVdaDriverConnection(timeoutSeconds: 3);
        firstConnection.PingResults.Enqueue(SudoVdaDriverOperationResult.Ok());
        firstConnection.PingResults.Enqueue(SudoVdaDriverOperationResult.Fail("stale handle"));
        var recoveredConnection = new FakeSudoVdaDriverConnection(timeoutSeconds: 3);
        var factory = new FakeSudoVdaDriverConnectionFactory(firstConnection);
        factory.OpenResults.Enqueue(SudoVdaDriverConnectionOpenResult.Fail("driver restarting"));
        factory.OpenResults.Enqueue(SudoVdaDriverConnectionOpenResult.Ok(recoveredConnection));
        var scheduler = new ManualSudoVdaHeartbeatScheduler();
        await using var session = new SudoVdaDriverLeaseSession(factory, scheduler);
        await session.HoldAsync("client-one", CancellationToken.None);

        await scheduler.TickAsync();
        await scheduler.TickAsync();

        Assert.True(session.Snapshot.Healthy);
        Assert.Equal(1, session.Snapshot.LeaseCount);
        Assert.Contains("recovered", session.Snapshot.Diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, recoveredConnection.PingCount);
    }

    [Fact]
    public async Task DisabledDriverWatchdogKeepsLeaseWithoutSchedulingHeartbeat()
    {
        var connection = new FakeSudoVdaDriverConnection(timeoutSeconds: 0);
        var factory = new FakeSudoVdaDriverConnectionFactory(connection);
        var scheduler = new ManualSudoVdaHeartbeatScheduler();
        await using var session = new SudoVdaDriverLeaseSession(factory, scheduler);

        SudoVdaDriverLeaseHoldResult result = await session.HoldAsync(
            "client-one",
            CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal(1, connection.PingCount);
        Assert.Equal(0, scheduler.ScheduleCount);
        Assert.False(session.Snapshot.HeartbeatActive);
        Assert.Equal(1, session.Snapshot.LeaseCount);
    }

    [Fact]
    public async Task OneSecondWatchdogFailsHoldBecauseNoHeartbeatCanBeatImmediateDriverTick()
    {
        var connection = new FakeSudoVdaDriverConnection(timeoutSeconds: 1);
        var factory = new FakeSudoVdaDriverConnectionFactory(connection);
        var scheduler = new ManualSudoVdaHeartbeatScheduler();
        await using var session = new SudoVdaDriverLeaseSession(factory, scheduler);

        SudoVdaDriverLeaseHoldResult result = await session.HoldAsync(
            "client-one",
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("one second", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.True(connection.Disposed);
        Assert.Equal(0, scheduler.ScheduleCount);
        Assert.False(session.Snapshot.Healthy);
    }

    [Fact]
    public async Task DriverOpenExceptionBecomesFailedHoldDiagnostic()
    {
        var factory = new FakeSudoVdaDriverConnectionFactory
        {
            ExceptionToThrow = new InvalidOperationException("native open exploded")
        };
        var scheduler = new ManualSudoVdaHeartbeatScheduler();
        await using var session = new SudoVdaDriverLeaseSession(factory, scheduler);

        SudoVdaDriverLeaseHoldResult result = await session.HoldAsync(
            "client-one",
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("native open exploded", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, session.Snapshot.LeaseCount);
        Assert.False(session.Snapshot.Healthy);
        Assert.Equal(0, scheduler.ScheduleCount);
    }

    [Fact]
    public async Task NativePingExceptionFaultsHealthWithoutEscapingOrRemovingLease()
    {
        var connection = new FakeSudoVdaDriverConnection(timeoutSeconds: 3);
        var factory = new FakeSudoVdaDriverConnectionFactory(connection);
        factory.OpenResults.Enqueue(SudoVdaDriverConnectionOpenResult.Fail("driver unavailable"));
        var scheduler = new ManualSudoVdaHeartbeatScheduler();
        await using var session = new SudoVdaDriverLeaseSession(factory, scheduler);
        await session.HoldAsync("client-one", CancellationToken.None);
        connection.PingException = new InvalidOperationException("native ping exploded");

        await scheduler.TickAsync();

        Assert.Equal(1, session.Snapshot.LeaseCount);
        Assert.False(session.Snapshot.Healthy);
        Assert.Contains("native ping exploded", session.Snapshot.Diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, connection.RemoveCount);
    }

    private sealed class FakeSudoVdaDriverConnectionFactory(params FakeSudoVdaDriverConnection[] connections)
        : ISudoVdaDriverConnectionFactory
    {
        private readonly Queue<FakeSudoVdaDriverConnection> connections = new(connections);

        public Queue<SudoVdaDriverConnectionOpenResult> OpenResults { get; } = new();

        public Exception? ExceptionToThrow { get; set; }

        public int OpenCount { get; private set; }

        public SudoVdaDriverConnectionOpenResult Open()
        {
            OpenCount++;
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            if (connections.TryDequeue(out FakeSudoVdaDriverConnection? connection))
            {
                return SudoVdaDriverConnectionOpenResult.Ok(connection);
            }

            return OpenResults.TryDequeue(out SudoVdaDriverConnectionOpenResult? result)
                ? result
                : SudoVdaDriverConnectionOpenResult.Fail("No fake driver connection configured.");
        }
    }

    private sealed class FakeSudoVdaDriverConnection(uint timeoutSeconds) : ISudoVdaDriverConnection
    {
        public Queue<SudoVdaDriverOperationResult> PingResults { get; } = new();

        public int WatchdogQueryCount { get; private set; }

        public int PingCount { get; private set; }

        public int RemoveCount { get; private set; }

        public Exception? PingException { get; set; }

        public bool Disposed { get; private set; }

        public SudoVdaWatchdogQueryResult QueryWatchdog()
        {
            WatchdogQueryCount++;
            return SudoVdaWatchdogQueryResult.Ok(new SudoVdaWatchdogState(timeoutSeconds, timeoutSeconds));
        }

        public SudoVdaDriverOperationResult Ping()
        {
            PingCount++;
            if (PingException is not null)
            {
                throw PingException;
            }

            return PingResults.TryDequeue(out SudoVdaDriverOperationResult? result)
                ? result
                : SudoVdaDriverOperationResult.Ok();
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    private sealed class ManualSudoVdaHeartbeatScheduler : ISudoVdaHeartbeatScheduler
    {
        private Func<CancellationToken, ValueTask>? heartbeat;
        private Func<TimeSpan>? intervalProvider;

        public int ScheduleCount { get; private set; }

        public TimeSpan? Interval { get; private set; }

        public bool Disposed { get; private set; }

        public ISudoVdaHeartbeatRegistration Schedule(
            Func<TimeSpan> intervalProvider,
            Func<CancellationToken, ValueTask> callback)
        {
            ScheduleCount++;
            this.intervalProvider = intervalProvider;
            Interval = intervalProvider();
            heartbeat = callback;
            return new Registration(this);
        }

        public async ValueTask TickAsync()
        {
            if (!Disposed && heartbeat is not null)
            {
                await heartbeat(CancellationToken.None);
            }
        }

        private sealed class Registration(ManualSudoVdaHeartbeatScheduler owner)
            : ISudoVdaHeartbeatRegistration
        {
            public ValueTask DisposeAsync()
            {
                owner.Disposed = true;
                owner.heartbeat = null;
                owner.intervalProvider = null;
                return ValueTask.CompletedTask;
            }
        }
    }
}

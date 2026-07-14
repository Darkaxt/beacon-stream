namespace Beacon.Platform.Windows.Displays;

internal sealed class SudoVdaDriverLeaseSession(
    ISudoVdaDriverConnectionFactory connectionFactory,
    ISudoVdaHeartbeatScheduler heartbeatScheduler)
    : IWindowsDisplayLeaseSession, IDisposable, IAsyncDisposable
{
    private readonly SemaphoreSlim transitionGate = new(1, 1);
    private readonly SemaphoreSlim stateGate = new(1, 1);
    private readonly HashSet<string> displayIds = new(StringComparer.OrdinalIgnoreCase);
    private ISudoVdaDriverConnection? connection;
    private ISudoVdaHeartbeatRegistration? heartbeatRegistration;
    private long heartbeatIntervalTicks;
    private bool disposed;
    private SudoVdaDriverLeaseSessionSnapshot snapshot = IdleSnapshot("No Beacon display leases are active.");

    public SudoVdaDriverLeaseSessionSnapshot Snapshot => Volatile.Read(ref snapshot);

    public async Task<SudoVdaDriverLeaseHoldResult> HoldAsync(
        string displayId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayId);
        await transitionGate.WaitAsync(cancellationToken);
        try
        {
            await stateGate.WaitAsync(cancellationToken);
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (displayIds.Contains(displayId))
                {
                    return SudoVdaDriverLeaseHoldResult.AlreadyHeld();
                }

                if (displayIds.Count == 0)
                {
                    SudoVdaDriverConnectionOpenResult openResult = OpenHealthyConnection();
                    if (!openResult.Success || openResult.Connection is null)
                    {
                        SetSnapshot(IdleSnapshot(openResult.Error ?? "Unable to open SudoVDA driver session."));
                        return SudoVdaDriverLeaseHoldResult.Fail(Snapshot.Diagnostic);
                    }

                    connection = openResult.Connection;
                    uint watchdogTimeoutSeconds = openResult.WatchdogTimeoutSeconds;
                    displayIds.Add(displayId);
                    if (watchdogTimeoutSeconds > 0)
                    {
                        Volatile.Write(
                            ref heartbeatIntervalTicks,
                            CalculateHeartbeatInterval(watchdogTimeoutSeconds).Ticks);
                        heartbeatRegistration = heartbeatScheduler.Schedule(
                            () => TimeSpan.FromTicks(Volatile.Read(ref heartbeatIntervalTicks)),
                            HeartbeatAsync);
                    }

                    SetHealthySnapshot(
                        watchdogTimeoutSeconds,
                        watchdogTimeoutSeconds > 0
                            ? $"Beacon SudoVDA driver session started for {displayId}; watchdog {watchdogTimeoutSeconds}s."
                            : $"Beacon SudoVDA driver session started for {displayId}; driver watchdog disabled.");
                    return SudoVdaDriverLeaseHoldResult.Held();
                }

                if (connection is null)
                {
                    SudoVdaDriverConnectionOpenResult recoveryResult = OpenHealthyConnection();
                    if (!recoveryResult.Success || recoveryResult.Connection is null)
                    {
                        SetUnhealthySnapshot(recoveryResult.Error ?? "Unable to recover SudoVDA driver session.");
                        return SudoVdaDriverLeaseHoldResult.Fail(Snapshot.Diagnostic);
                    }

                    connection = recoveryResult.Connection;
                    UpdateHeartbeatInterval(recoveryResult.WatchdogTimeoutSeconds);
                }

                displayIds.Add(displayId);
                SetHealthySnapshot(
                    Snapshot.WatchdogTimeoutSeconds ?? 0,
                    $"Beacon SudoVDA driver session now owns {displayIds.Count} display leases.");
                return SudoVdaDriverLeaseHoldResult.Held();
            }
            finally
            {
                stateGate.Release();
            }
        }
        finally
        {
            transitionGate.Release();
        }
    }

    public async Task ReleaseAsync(string displayId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayId);
        await transitionGate.WaitAsync(cancellationToken);
        try
        {
            ISudoVdaHeartbeatRegistration? registrationToDispose = null;
            ISudoVdaDriverConnection? connectionToDispose = null;

            await stateGate.WaitAsync(CancellationToken.None);
            try
            {
                if (!displayIds.Remove(displayId))
                {
                    return;
                }

                if (displayIds.Count > 0)
                {
                    SetHealthySnapshot(
                        Snapshot.WatchdogTimeoutSeconds ?? 0,
                        $"Beacon SudoVDA driver session retains {displayIds.Count} display leases.");
                    return;
                }

                registrationToDispose = heartbeatRegistration;
                heartbeatRegistration = null;
                connectionToDispose = connection;
                connection = null;
                Volatile.Write(ref heartbeatIntervalTicks, 0);
                SetSnapshot(IdleSnapshot("Final Beacon display lease released; driver session closed."));
            }
            finally
            {
                stateGate.Release();
            }

            if (registrationToDispose is not null)
            {
                await registrationToDispose.DisposeAsync();
            }

            connectionToDispose?.Dispose();
        }
        finally
        {
            transitionGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await transitionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            ISudoVdaHeartbeatRegistration? registrationToDispose;
            ISudoVdaDriverConnection? connectionToDispose;

            await stateGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                displayIds.Clear();
                registrationToDispose = heartbeatRegistration;
                heartbeatRegistration = null;
                connectionToDispose = connection;
                connection = null;
                SetSnapshot(IdleSnapshot("Beacon SudoVDA driver session disposed."));
            }
            finally
            {
                stateGate.Release();
            }

            if (registrationToDispose is not null)
            {
                await registrationToDispose.DisposeAsync().ConfigureAwait(false);
            }

            connectionToDispose?.Dispose();
        }
        finally
        {
            transitionGate.Release();
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    internal static TimeSpan CalculateHeartbeatInterval(uint watchdogTimeoutSeconds)
    {
        if (watchdogTimeoutSeconds == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(watchdogTimeoutSeconds),
                "A disabled watchdog does not require a heartbeat interval.");
        }

        return TimeSpan.FromTicks(TimeSpan.FromSeconds(watchdogTimeoutSeconds).Ticks / 2);
    }

    private async ValueTask HeartbeatAsync(CancellationToken cancellationToken)
    {
        await stateGate.WaitAsync(cancellationToken);
        try
        {
            if (displayIds.Count == 0)
            {
                return;
            }

            if (connection is not null)
            {
                SudoVdaDriverOperationResult pingResult = connection.Ping();
                if (pingResult.Success)
                {
                    SetHealthySnapshot(
                        Snapshot.WatchdogTimeoutSeconds ?? 0,
                        $"Beacon SudoVDA heartbeat acknowledged for {displayIds.Count} display leases.");
                    return;
                }

                connection.Dispose();
                connection = null;
                SudoVdaDriverConnectionOpenResult recoveryResult = OpenHealthyConnection();
                if (recoveryResult.Success && recoveryResult.Connection is not null)
                {
                    connection = recoveryResult.Connection;
                    UpdateHeartbeatInterval(recoveryResult.WatchdogTimeoutSeconds);
                    SetHealthySnapshot(
                        recoveryResult.WatchdogTimeoutSeconds,
                        $"Beacon SudoVDA heartbeat recovered after: {pingResult.Error}");
                    return;
                }

                SetUnhealthySnapshot(
                    $"Beacon SudoVDA heartbeat failed: {pingResult.Error}; recovery failed: {recoveryResult.Error}");
                return;
            }

            SudoVdaDriverConnectionOpenResult reconnectResult = OpenHealthyConnection();
            if (reconnectResult.Success && reconnectResult.Connection is not null)
            {
                connection = reconnectResult.Connection;
                UpdateHeartbeatInterval(reconnectResult.WatchdogTimeoutSeconds);
                SetHealthySnapshot(
                    reconnectResult.WatchdogTimeoutSeconds,
                    "Beacon SudoVDA heartbeat recovered its driver connection.");
                return;
            }

            SetUnhealthySnapshot($"Beacon SudoVDA heartbeat recovery failed: {reconnectResult.Error}");
        }
        finally
        {
            stateGate.Release();
        }
    }

    private SudoVdaDriverConnectionOpenResult OpenHealthyConnection()
    {
        ISudoVdaDriverConnection? openedConnection = null;
        try
        {
            SudoVdaDriverConnectionOpenResult openResult = connectionFactory.Open();
            if (!openResult.Success || openResult.Connection is null)
            {
                return openResult;
            }

            openedConnection = openResult.Connection;
            SudoVdaWatchdogQueryResult watchdogResult = openedConnection.QueryWatchdog();
            if (!watchdogResult.Success || watchdogResult.State is null)
            {
                openedConnection.Dispose();
                return SudoVdaDriverConnectionOpenResult.Fail(
                    watchdogResult.Error ?? "Unable to query SudoVDA watchdog state.");
            }

            SudoVdaDriverOperationResult pingResult = openedConnection.Ping();
            if (!pingResult.Success)
            {
                openedConnection.Dispose();
                return SudoVdaDriverConnectionOpenResult.Fail(
                    $"Unable to initialize SudoVDA heartbeat: {pingResult.Error}");
            }

            return SudoVdaDriverConnectionOpenResult.Ok(
                openedConnection,
                watchdogResult.State.TimeoutSeconds);
        }
        catch (Exception ex)
        {
            openedConnection?.Dispose();
            return SudoVdaDriverConnectionOpenResult.Fail(
                $"SudoVDA driver session initialization failed: {ex.Message}");
        }
    }

    private void UpdateHeartbeatInterval(uint watchdogTimeoutSeconds)
    {
        if (watchdogTimeoutSeconds > 0)
        {
            Volatile.Write(
                ref heartbeatIntervalTicks,
                CalculateHeartbeatInterval(watchdogTimeoutSeconds).Ticks);
        }
    }

    private void SetHealthySnapshot(uint watchdogTimeoutSeconds, string diagnostic) =>
        SetSnapshot(new SudoVdaDriverLeaseSessionSnapshot(
            displayIds.Count,
            watchdogTimeoutSeconds,
            heartbeatRegistration is not null,
            Healthy: true,
            diagnostic));

    private void SetUnhealthySnapshot(string diagnostic) =>
        SetSnapshot(new SudoVdaDriverLeaseSessionSnapshot(
            displayIds.Count,
            Snapshot.WatchdogTimeoutSeconds,
            heartbeatRegistration is not null,
            Healthy: false,
            diagnostic));

    private void SetSnapshot(SudoVdaDriverLeaseSessionSnapshot value) =>
        Volatile.Write(ref snapshot, value);

    private static SudoVdaDriverLeaseSessionSnapshot IdleSnapshot(string diagnostic) => new(
        LeaseCount: 0,
        WatchdogTimeoutSeconds: null,
        HeartbeatActive: false,
        Healthy: true,
        diagnostic);
}

internal interface ISudoVdaDriverConnectionFactory
{
    SudoVdaDriverConnectionOpenResult Open();
}

internal interface ISudoVdaDriverConnection : IDisposable
{
    SudoVdaWatchdogQueryResult QueryWatchdog();

    SudoVdaDriverOperationResult Ping();
}

internal interface ISudoVdaHeartbeatScheduler
{
    ISudoVdaHeartbeatRegistration Schedule(
        Func<TimeSpan> intervalProvider,
        Func<CancellationToken, ValueTask> heartbeat);
}

internal interface ISudoVdaHeartbeatRegistration : IAsyncDisposable
{
}

internal sealed record SudoVdaWatchdogState(uint TimeoutSeconds, uint CountdownSeconds);

internal sealed record SudoVdaWatchdogQueryResult(
    bool Success,
    SudoVdaWatchdogState? State,
    string? Error)
{
    public static SudoVdaWatchdogQueryResult Ok(SudoVdaWatchdogState state) => new(true, state, null);

    public static SudoVdaWatchdogQueryResult Fail(string error) => new(false, null, error);
}

internal sealed record SudoVdaDriverOperationResult(bool Success, string? Error)
{
    public static SudoVdaDriverOperationResult Ok() => new(true, null);

    public static SudoVdaDriverOperationResult Fail(string error) => new(false, error);
}

internal sealed record SudoVdaDriverConnectionOpenResult(
    bool Success,
    ISudoVdaDriverConnection? Connection,
    uint WatchdogTimeoutSeconds,
    string? Error)
{
    public static SudoVdaDriverConnectionOpenResult Ok(
        ISudoVdaDriverConnection connection,
        uint watchdogTimeoutSeconds = 0) =>
        new(true, connection, watchdogTimeoutSeconds, null);

    public static SudoVdaDriverConnectionOpenResult Fail(string error) =>
        new(false, null, 0, error);
}

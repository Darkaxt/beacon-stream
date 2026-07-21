namespace Beacon.Platform.Windows.Displays;

internal sealed class SudoVdaDriverLeaseSession(
    ISudoVdaDriverConnectionFactory connectionFactory,
    ISudoVdaHeartbeatScheduler heartbeatScheduler,
    Func<CancellationToken, ValueTask>? reconcileLeasedDisplayTopology = null)
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
                        SetUnhealthySnapshot(openResult.Error ?? "Unable to open SudoVDA driver session.");
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

    internal async Task<SudoVdaVirtualDisplayCreateResult> CreateVirtualDisplayAsync(
        string displayId,
        SudoVdaVirtualDisplayCreateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayId);
        ArgumentNullException.ThrowIfNull(request);
        await stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!displayIds.Contains(displayId) || connection is null)
            {
                return SudoVdaVirtualDisplayCreateResult.Fail(
                    $"No active SudoVDA driver lease owns {displayId}.");
            }

            return connection.CreateVirtualDisplay(request);
        }
        finally
        {
            stateGate.Release();
        }
    }

    internal async Task<SudoVdaDriverOperationResult> RemoveVirtualDisplayAsync(
        string displayId,
        Guid monitorGuid,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayId);
        await stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!displayIds.Contains(displayId) || connection is null)
            {
                return SudoVdaDriverOperationResult.Fail(
                    $"No active SudoVDA driver lease owns {displayId}.");
            }

            return connection.RemoveVirtualDisplay(monitorGuid);
        }
        finally
        {
            stateGate.Release();
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

        if (watchdogTimeoutSeconds == 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(watchdogTimeoutSeconds),
                "A one second watchdog can expire on the driver's first timer tick.");
        }

        return TimeSpan.FromSeconds((watchdogTimeoutSeconds - 1) / 2d);
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
                SudoVdaDriverOperationResult pingResult;
                try
                {
                    pingResult = connection.Ping();
                }
                catch (Exception ex)
                {
                    pingResult = SudoVdaDriverOperationResult.Fail(
                        $"Native SudoVDA heartbeat failed: {ex.Message}");
                }

                if (pingResult.Success)
                {
                    SetHealthySnapshot(
                        Snapshot.WatchdogTimeoutSeconds ?? 0,
                        $"Beacon SudoVDA heartbeat acknowledged for {displayIds.Count} display leases.");
                    await ReconcileLeasedDisplayTopologyAsync(cancellationToken);
                    return;
                }

                try
                {
                    connection.Dispose();
                }
                catch (Exception ex)
                {
                    pingResult = SudoVdaDriverOperationResult.Fail(
                        $"{pingResult.Error}; stale driver connection disposal failed: {ex.Message}");
                }

                connection = null;
                SudoVdaDriverConnectionOpenResult recoveryResult = OpenHealthyConnection();
                if (recoveryResult.Success && recoveryResult.Connection is not null)
                {
                    connection = recoveryResult.Connection;
                    UpdateHeartbeatInterval(recoveryResult.WatchdogTimeoutSeconds);
                    SetHealthySnapshot(
                        recoveryResult.WatchdogTimeoutSeconds,
                        $"Beacon SudoVDA heartbeat recovered after: {pingResult.Error}");
                    await ReconcileLeasedDisplayTopologyAsync(cancellationToken);
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
                await ReconcileLeasedDisplayTopologyAsync(cancellationToken);
                return;
            }

            SetUnhealthySnapshot($"Beacon SudoVDA heartbeat recovery failed: {reconnectResult.Error}");
        }
        finally
        {
            stateGate.Release();
        }
    }

    private async ValueTask ReconcileLeasedDisplayTopologyAsync(CancellationToken cancellationToken)
    {
        if (reconcileLeasedDisplayTopology is null)
        {
            return;
        }

        try
        {
            await reconcileLeasedDisplayTopology(cancellationToken);
        }
        catch (Exception ex)
        {
            SetUnhealthySnapshot(
                $"Beacon SudoVDA heartbeat was acknowledged, but leased display topology reconciliation failed: {ex.Message}");
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

            if (watchdogResult.State.TimeoutSeconds == 1)
            {
                openedConnection.Dispose();
                return SudoVdaDriverConnectionOpenResult.Fail(
                    "SudoVDA reports a one second watchdog, which can expire on the driver's first timer tick and cannot safely preserve a Beacon display lease.");
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

    SudoVdaVirtualDisplayCreateResult CreateVirtualDisplay(
        SudoVdaVirtualDisplayCreateRequest request);

    SudoVdaDriverOperationResult RemoveVirtualDisplay(Guid monitorGuid);
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

internal sealed record SudoVdaVirtualDisplayCreateRequest(
    uint Width,
    uint Height,
    uint RefreshRate,
    Guid MonitorGuid,
    string DeviceName,
    string SerialNumber);

internal sealed record SudoVdaVirtualDisplayCreateResult(
    bool Success,
    uint AdapterLowPart,
    int AdapterHighPart,
    uint TargetId,
    string? Error)
{
    public static SudoVdaVirtualDisplayCreateResult Ok(
        uint adapterLowPart,
        int adapterHighPart,
        uint targetId) =>
        new(true, adapterLowPart, adapterHighPart, targetId, null);

    public static SudoVdaVirtualDisplayCreateResult Fail(string error) =>
        new(false, 0, 0, 0, error);
}

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

using Beacon.Core.Games;

namespace Beacon.Core.Sessions;

public sealed class SessionOwnershipTracker : ISessionOwnershipTracker
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, SessionOwnershipRecord> records = new(StringComparer.OrdinalIgnoreCase);
    private readonly ISessionActivityInspector inspector;
    private readonly ISessionOwnedWorkTerminator terminator;

    public SessionOwnershipTracker(ISessionActivityInspector inspector)
        : this(inspector, new UnavailableSessionOwnedWorkTerminator())
    {
    }

    public SessionOwnershipTracker(
        ISessionActivityInspector inspector,
        ISessionOwnedWorkTerminator terminator)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
        this.terminator = terminator ?? throw new ArgumentNullException(nameof(terminator));
    }

    public Task RecordLaunchAsync(SessionPlan plan, GameLaunchState launchState, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            records[plan.SessionId] = new SessionOwnershipRecord(plan, launchState, DateTimeOffset.UtcNow);
        }
        return Task.CompletedTask;
    }

    public async Task<SessionOwnershipSnapshot?> GetSnapshotAsync(string sessionId, CancellationToken cancellationToken)
    {
        SessionOwnershipRecord? record;
        lock (gate)
        {
            records.TryGetValue(sessionId, out record);
        }
        if (record is null)
        {
            return null;
        }

        return await CreateSnapshotAsync(record, cancellationToken);
    }

    public async Task<IReadOnlyList<SessionOwnershipSnapshot>> GetSnapshotsAsync(CancellationToken cancellationToken)
    {
        SessionOwnershipRecord[] current;
        lock (gate)
        {
            current = records.Values
                .OrderBy(record => record.Plan.ClientId.Value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        var snapshots = new List<SessionOwnershipSnapshot>();
        foreach (SessionOwnershipRecord record in current)
        {
            snapshots.Add(await CreateSnapshotAsync(record, cancellationToken));
        }

        return snapshots;
    }

    public async Task<SessionOwnedWorkTerminationResult> TerminateOwnedWorkAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        SessionOwnershipRecord? record;
        lock (gate)
        {
            records.TryGetValue(sessionId, out record);
        }
        if (record is null)
        {
            return SessionOwnedWorkTerminationResult.Ok([]);
        }

        SessionActivitySnapshot activity = await inspector.InspectAsync(record, cancellationToken);
        if (!activity.HasOwnedWork)
        {
            if (record.LaunchState.Started && record.LaunchState.ProcessId is null)
            {
                return SessionOwnedWorkTerminationResult.Fail(
                    $"Owned work for session '{sessionId}' is not observable yet; ownership was retained.");
            }
            ClearIfCurrent(sessionId, record);
            return SessionOwnedWorkTerminationResult.Ok([]);
        }

        SessionOwnedWorkTerminationResult terminated = await terminator.TerminateAsync(
            record,
            activity,
            cancellationToken);
        if (!terminated.Success)
        {
            return terminated;
        }

        SessionActivitySnapshot remaining = await inspector.InspectAsync(record, cancellationToken);
        if (remaining.HasOwnedWork)
        {
            return SessionOwnedWorkTerminationResult.Fail(
                $"Owned work remains for session '{sessionId}' after termination.");
        }

        ClearIfCurrent(sessionId, record);
        return terminated;
    }

    public Task ClearAsync(string sessionId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            records.Remove(sessionId);
        }
        return Task.CompletedTask;
    }

    private void ClearIfCurrent(string sessionId, SessionOwnershipRecord expected)
    {
        lock (gate)
        {
            if (records.TryGetValue(sessionId, out SessionOwnershipRecord? current)
                && ReferenceEquals(current, expected))
            {
                records.Remove(sessionId);
            }
        }
    }

    private async Task<SessionOwnershipSnapshot> CreateSnapshotAsync(
        SessionOwnershipRecord record,
        CancellationToken cancellationToken)
    {
        SessionActivitySnapshot activity = await inspector.InspectAsync(record, cancellationToken);
        IReadOnlyList<string> reasons = activity.Reasons.Count > 0
            ? activity.Reasons
            : CreateReasons(activity);

        return new SessionOwnershipSnapshot(
            record.Plan.SessionId,
            record.Plan.ClientId,
            record.Plan.AppId,
            record.LaunchState.ProcessId,
            activity.LaunchedProcessRunning,
            activity.ChildProcessRunning,
            activity.OwnedWindowRemaining,
            reasons)
        {
            DisplayId = record.Plan.Display.DisplayId,
            OwnedProcessIds = activity.OwnedProcessIds,
        };
    }

    private static IReadOnlyList<string> CreateReasons(SessionActivitySnapshot activity)
    {
        var reasons = new List<string>();
        if (activity.LaunchedProcessRunning)
        {
            reasons.Add("Launched process is still running.");
        }

        if (activity.ChildProcessRunning)
        {
            reasons.Add("Child process is still running.");
        }

        if (activity.OwnedWindowRemaining)
        {
            reasons.Add("Owned window remains on the client display.");
        }

        return reasons;
    }

    private sealed class UnavailableSessionOwnedWorkTerminator : ISessionOwnedWorkTerminator
    {
        public Task<SessionOwnedWorkTerminationResult> TerminateAsync(
            SessionOwnershipRecord record,
            SessionActivitySnapshot activity,
            CancellationToken cancellationToken) =>
            Task.FromResult(SessionOwnedWorkTerminationResult.Fail(
                $"Owned work termination is unavailable for session '{record.Plan.SessionId}'."));
    }
}

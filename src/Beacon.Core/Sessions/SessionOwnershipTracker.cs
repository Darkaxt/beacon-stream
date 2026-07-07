using Beacon.Core.Games;

namespace Beacon.Core.Sessions;

public sealed class SessionOwnershipTracker(ISessionActivityInspector inspector) : ISessionOwnershipTracker
{
    private readonly Dictionary<string, SessionOwnershipRecord> records = new(StringComparer.OrdinalIgnoreCase);

    public Task RecordLaunchAsync(SessionPlan plan, GameLaunchState launchState, CancellationToken cancellationToken)
    {
        records[plan.SessionId] = new SessionOwnershipRecord(plan, launchState, DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }

    public async Task<SessionOwnershipSnapshot?> GetSnapshotAsync(string sessionId, CancellationToken cancellationToken)
    {
        if (!records.TryGetValue(sessionId, out SessionOwnershipRecord? record))
        {
            return null;
        }

        return await CreateSnapshotAsync(record, cancellationToken);
    }

    public async Task<IReadOnlyList<SessionOwnershipSnapshot>> GetSnapshotsAsync(CancellationToken cancellationToken)
    {
        var snapshots = new List<SessionOwnershipSnapshot>();
        foreach (SessionOwnershipRecord record in records.Values.OrderBy(record => record.Plan.ClientId.Value, StringComparer.OrdinalIgnoreCase))
        {
            snapshots.Add(await CreateSnapshotAsync(record, cancellationToken));
        }

        return snapshots;
    }

    public Task ClearAsync(string sessionId, CancellationToken cancellationToken)
    {
        records.Remove(sessionId);
        return Task.CompletedTask;
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
            reasons);
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
}

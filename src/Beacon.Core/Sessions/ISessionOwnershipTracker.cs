using Beacon.Core.Games;

namespace Beacon.Core.Sessions;

public sealed record SessionOwnershipRecord(SessionPlan Plan, GameLaunchState LaunchState, DateTimeOffset StartedAt);

public sealed record SessionActivitySnapshot(
    bool LaunchedProcessRunning,
    bool ChildProcessRunning,
    bool OwnedWindowRemaining,
    IReadOnlyList<string> Reasons)
{
    public static SessionActivitySnapshot None { get; } = new(false, false, false, []);
}

public interface ISessionActivityInspector
{
    Task<SessionActivitySnapshot> InspectAsync(SessionOwnershipRecord record, CancellationToken cancellationToken);
}

public interface ISessionOwnershipTracker
{
    Task RecordLaunchAsync(SessionPlan plan, GameLaunchState launchState, CancellationToken cancellationToken);

    Task<SessionOwnershipSnapshot?> GetSnapshotAsync(string sessionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SessionOwnershipSnapshot>> GetSnapshotsAsync(CancellationToken cancellationToken);

    Task ClearAsync(string sessionId, CancellationToken cancellationToken);
}

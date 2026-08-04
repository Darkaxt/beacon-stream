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

    public IReadOnlyList<int> OwnedProcessIds { get; init; } = [];

    public bool HasOwnedWork =>
        LaunchedProcessRunning || ChildProcessRunning || OwnedWindowRemaining;
}

public sealed record SessionOwnedWorkTerminationResult(
    bool Success,
    IReadOnlyList<int> ProcessIds,
    string? Error)
{
    public static SessionOwnedWorkTerminationResult Ok(IEnumerable<int> processIds) =>
        new(true, processIds.Distinct().Order().ToArray(), null);

    public static SessionOwnedWorkTerminationResult Fail(
        string error,
        IEnumerable<int>? processIds = null) =>
        new(false, processIds?.Distinct().Order().ToArray() ?? [], error);
}

public interface ISessionActivityInspector
{
    Task<SessionActivitySnapshot> InspectAsync(SessionOwnershipRecord record, CancellationToken cancellationToken);
}

public interface ISessionOwnedWorkTerminator
{
    Task<SessionOwnedWorkTerminationResult> TerminateAsync(
        SessionOwnershipRecord record,
        SessionActivitySnapshot activity,
        CancellationToken cancellationToken);
}

public interface ISessionOwnershipTracker
{
    Task RecordLaunchAsync(SessionPlan plan, GameLaunchState launchState, CancellationToken cancellationToken);

    Task<SessionOwnershipSnapshot?> GetSnapshotAsync(string sessionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SessionOwnershipSnapshot>> GetSnapshotsAsync(CancellationToken cancellationToken);

    Task<SessionOwnedWorkTerminationResult> TerminateOwnedWorkAsync(
        string sessionId,
        CancellationToken cancellationToken);

    Task ClearAsync(string sessionId, CancellationToken cancellationToken);
}

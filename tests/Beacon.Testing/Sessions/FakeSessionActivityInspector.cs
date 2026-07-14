namespace Beacon.Core.Sessions;

public sealed class FakeSessionActivityInspector : ISessionActivityInspector
{
    private readonly Dictionary<string, SessionActivitySnapshot> snapshots =
        new(StringComparer.OrdinalIgnoreCase);

    public void SetActivity(string sessionId, SessionActivitySnapshot snapshot) =>
        snapshots[sessionId] = snapshot;

    public void ClearActivity(string sessionId) =>
        snapshots.Remove(sessionId);

    public Task<SessionActivitySnapshot> InspectAsync(
        SessionOwnershipRecord record,
        CancellationToken cancellationToken) =>
        Task.FromResult(snapshots.GetValueOrDefault(
            record.Plan.SessionId,
            SessionActivitySnapshot.None));
}

public sealed class FakeSessionOwnedWorkTerminator(FakeSessionActivityInspector inspector)
    : ISessionOwnedWorkTerminator
{
    public List<string> SessionIds { get; } = [];

    public SessionOwnedWorkTerminationResult? NextResult { get; set; }

    public Task<SessionOwnedWorkTerminationResult> TerminateAsync(
        SessionOwnershipRecord record,
        SessionActivitySnapshot activity,
        CancellationToken cancellationToken)
    {
        SessionIds.Add(record.Plan.SessionId);
        SessionOwnedWorkTerminationResult result = NextResult
            ?? SessionOwnedWorkTerminationResult.Ok(activity.OwnedProcessIds);
        NextResult = null;
        if (result.Success)
        {
            inspector.ClearActivity(record.Plan.SessionId);
        }
        return Task.FromResult(result);
    }
}

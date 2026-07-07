namespace Beacon.Core.Sessions;

public sealed class FakeSessionActivityInspector : ISessionActivityInspector
{
    private readonly Dictionary<string, SessionActivitySnapshot> snapshots = new(StringComparer.OrdinalIgnoreCase);

    public void SetActivity(string sessionId, SessionActivitySnapshot snapshot) =>
        snapshots[sessionId] = snapshot;

    public void ClearActivity(string sessionId) =>
        snapshots.Remove(sessionId);

    public Task<SessionActivitySnapshot> InspectAsync(SessionOwnershipRecord record, CancellationToken cancellationToken) =>
        Task.FromResult(snapshots.GetValueOrDefault(record.Plan.SessionId, SessionActivitySnapshot.None));
}

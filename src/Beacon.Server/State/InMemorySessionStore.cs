using Beacon.Core.Sessions;

namespace Beacon.Server.State;

public sealed class InMemorySessionStore
{
    private readonly Dictionary<string, SessionPlan> plans = new(StringComparer.OrdinalIgnoreCase);

    public void Save(SessionPlan plan) =>
        plans[plan.ClientId.Value] = plan;

    public SessionPlan? Get(string clientId) =>
        plans.GetValueOrDefault(clientId);

    public IReadOnlyList<SessionPlan> GetAll() =>
        plans.Values.OrderBy(plan => plan.ClientId.Value, StringComparer.OrdinalIgnoreCase).ToArray();
}

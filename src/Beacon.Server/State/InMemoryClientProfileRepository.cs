using Beacon.Core.Clients;

namespace Beacon.Server.State;

public sealed class InMemoryClientProfileRepository : IClientProfileRepository
{
    private readonly object gate = new();
    private ClientProfile[] profiles = [];

    public string Kind => "memory";

    public string? Location => null;

    public IReadOnlyList<ClientProfile> LoadProfiles()
    {
        lock (gate)
        {
            return profiles;
        }
    }

    public void SaveProfiles(IReadOnlyList<ClientProfile> profiles)
    {
        lock (gate)
        {
            this.profiles = profiles.ToArray();
        }
    }
}

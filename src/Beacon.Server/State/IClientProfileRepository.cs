using Beacon.Core.Clients;

namespace Beacon.Server.State;

public interface IClientProfileRepository
{
    string Kind { get; }

    string? Location { get; }

    IReadOnlyList<ClientProfile> LoadProfiles();

    void SaveProfiles(IReadOnlyList<ClientProfile> profiles);
}

using Beacon.Core.Clients;

namespace Beacon.Server.State;

public sealed class InMemoryClientStore
{
    private readonly object gate = new();
    private readonly IClientProfileRepository profileRepository;
    private readonly Dictionary<string, ClientProfile> profiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EndpointCapabilities> capabilities = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TelemetrySnapshot> telemetry = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryClientStore(IClientProfileRepository profileRepository)
    {
        this.profileRepository = profileRepository;
        foreach (ClientProfile profile in profileRepository.LoadProfiles())
        {
            profiles[profile.ClientId.Value] = profile;
        }

        if (profiles.Count == 0)
        {
            ClientProfile zFold = ClientProfile.CreateZFold7Default();
            profiles[zFold.ClientId.Value] = zFold;
        }
    }

    public string ProfileStoreKind => profileRepository.Kind;

    public string? ProfileStoreLocation => profileRepository.Location;

    public ClientProfile? GetProfile(string clientId) =>
        WithLock(() => profiles.GetValueOrDefault(clientId));

    public IReadOnlyList<ClientProfile> GetProfiles() =>
        WithLock(() => profiles.Values.OrderBy(profile => profile.ClientId.Value, StringComparer.OrdinalIgnoreCase).ToArray());

    public void SaveProfile(ClientProfile profile)
    {
        lock (gate)
        {
            profiles[profile.ClientId.Value] = profile;
            PersistProfiles();
        }
    }

    public ClientProfile RegisterProfile(string clientId, string? name)
    {
        lock (gate)
        {
            if (profiles.TryGetValue(clientId, out ClientProfile? existing))
            {
                return existing;
            }

            string resolvedName = string.IsNullOrWhiteSpace(name) ? clientId : name.Trim();
            ClientProfile profile = ClientProfile.CreateDefault(new ClientId(clientId), resolvedName);
            profiles[profile.ClientId.Value] = profile;
            PersistProfiles();
            return profile;
        }
    }

    private void PersistProfiles() =>
        profileRepository.SaveProfiles(profiles.Values.OrderBy(profile => profile.ClientId.Value, StringComparer.OrdinalIgnoreCase).ToArray());

    public EndpointCapabilities GetCapabilities(string clientId) =>
        WithLock(() => capabilities.GetValueOrDefault(clientId)
        ?? new EndpointCapabilities(Av1: true, Hevc: true, H264: true, Hdr10: true, VirtualDisplayHdrSupported: false));

    public void SaveCapabilities(string clientId, EndpointCapabilities value)
    {
        lock (gate)
        {
            capabilities[clientId] = value;
        }
    }

    public TelemetrySnapshot GetTelemetry(string clientId) =>
        WithLock(() => telemetry.GetValueOrDefault(clientId)
        ?? new TelemetrySnapshot(RttMs: 8, PacketLossPercent: 0, DecoderLoadPercent: null));

    public void SaveTelemetry(string clientId, TelemetrySnapshot value)
    {
        lock (gate)
        {
            telemetry[clientId] = value;
        }
    }

    private T WithLock<T>(Func<T> read)
    {
        lock (gate)
        {
            return read();
        }
    }
}

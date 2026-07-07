using Beacon.Core.Clients;

namespace Beacon.Server.State;

public sealed class InMemoryClientStore
{
    private readonly Dictionary<string, ClientProfile> profiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EndpointCapabilities> capabilities = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TelemetrySnapshot> telemetry = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryClientStore()
    {
        ClientProfile zFold = ClientProfile.CreateZFold7Default();
        profiles[zFold.ClientId.Value] = zFold;
    }

    public ClientProfile? GetProfile(string clientId) =>
        profiles.GetValueOrDefault(clientId);

    public void SaveProfile(ClientProfile profile) =>
        profiles[profile.ClientId.Value] = profile;

    public EndpointCapabilities GetCapabilities(string clientId) =>
        capabilities.GetValueOrDefault(clientId)
        ?? new EndpointCapabilities(Av1: true, Hevc: true, H264: true, Hdr10: true, VirtualDisplayHdrSupported: false);

    public void SaveCapabilities(string clientId, EndpointCapabilities value) =>
        capabilities[clientId] = value;

    public TelemetrySnapshot GetTelemetry(string clientId) =>
        telemetry.GetValueOrDefault(clientId)
        ?? new TelemetrySnapshot(RttMs: 8, PacketLossPercent: 0, DecoderLoadPercent: null);

    public void SaveTelemetry(string clientId, TelemetrySnapshot value) =>
        telemetry[clientId] = value;
}

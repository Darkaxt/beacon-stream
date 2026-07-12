using Beacon.Core.Benchmarks;
using Beacon.Core.Clients;

namespace Beacon.Server.State;

public sealed class InMemoryClientStore
{
    private readonly object gate = new();
    private readonly IClientProfileRepository profileRepository;
    private readonly IBenchmarkEvidenceRepository benchmarkRepository;
    private readonly Dictionary<string, ClientProfile> profiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EndpointCapabilities> capabilities = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TelemetrySnapshot> telemetry = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, BenchmarkEvidence> benchmarkEvidence = [];

    public InMemoryClientStore(
        IClientProfileRepository profileRepository,
        IBenchmarkEvidenceRepository? benchmarkRepository = null)
    {
        this.profileRepository = profileRepository;
        this.benchmarkRepository = benchmarkRepository ?? new InMemoryBenchmarkEvidenceRepository();
        foreach (ClientProfile profile in profileRepository.LoadProfiles())
        {
            profiles[profile.ClientId.Value] = profile;
        }

        if (profiles.Count == 0)
        {
            ClientProfile zFold = ClientProfile.CreateZFold7Default();
            profiles[zFold.ClientId.Value] = zFold;
        }

        foreach (BenchmarkEvidence evidence in this.benchmarkRepository.LoadEvidence())
        {
            benchmarkEvidence[evidence.RunId] = evidence;
        }
    }

    public string ProfileStoreKind => profileRepository.Kind;

    public string? ProfileStoreLocation => profileRepository.Location;

    public string BenchmarkStoreKind => benchmarkRepository.Kind;

    public string? BenchmarkStoreLocation => benchmarkRepository.Location;

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

    public void SaveBenchmarkEvidence(BenchmarkEvidence evidence)
    {
        lock (gate)
        {
            benchmarkEvidence[evidence.RunId] = evidence;
            benchmarkRepository.SaveEvidence(benchmarkEvidence.Values
                .OrderBy(value => value.StartedAt)
                .ThenBy(value => value.RunId)
                .ToArray());
        }
    }

    public BenchmarkEvidence? GetBenchmarkEvidence(Guid runId) =>
        WithLock(() => benchmarkEvidence.GetValueOrDefault(runId));

    public IReadOnlyList<BenchmarkEvidence> GetBenchmarkEvidence(string clientId) =>
        WithLock(() => benchmarkEvidence.Values
            .Where(value => value.ClientId.Value.Equals(clientId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(value => value.StartedAt)
            .ThenByDescending(value => value.RunId)
            .ToArray());

    public BenchmarkEvidence? GetLatestCompletedBenchmarkEvidence(string clientId) =>
        WithLock(() => benchmarkEvidence.Values
            .Where(value => value.ClientId.Value.Equals(clientId, StringComparison.OrdinalIgnoreCase))
            .Where(value => value.CompletedAt is not null && value.SelectedResult is not null)
            .OrderByDescending(value => value.CompletedAt)
            .ThenByDescending(value => value.RunId)
            .FirstOrDefault());

    public BenchmarkPlanEvidence? GetLatestBenchmarkPlanEvidence(
        string clientId,
        DateTimeOffset evaluatedAt,
        TimeSpan maximumEvidenceAge)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximumEvidenceAge, TimeSpan.Zero);
        return WithLock(() => benchmarkEvidence.Values
            .Where(value => value.ClientId.Value.Equals(clientId, StringComparison.OrdinalIgnoreCase))
            .Where(value => value.CompletedAt is not null && value.SelectedResult is not null)
            .Where(value => value.CompletedAt <= evaluatedAt)
            .Where(value => evaluatedAt - value.CompletedAt!.Value <= maximumEvidenceAge)
            .OrderByDescending(value => value.CompletedAt)
            .ThenByDescending(value => value.RunId)
            .FirstOrDefault()
            ?.ToPlanEvidence());
    }

    private T WithLock<T>(Func<T> read)
    {
        lock (gate)
        {
            return read();
        }
    }
}

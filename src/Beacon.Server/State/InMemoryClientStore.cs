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
    private readonly Dictionary<string, BenchmarkFingerprintSet> currentFingerprints = new(StringComparer.OrdinalIgnoreCase);

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
            BenchmarkEvidenceValidator.Validate(evidence);
            benchmarkEvidence[evidence.RunId] = evidence;
        }

        if (this.benchmarkRepository is InMemoryBenchmarkEvidenceRepository)
        {
            foreach (IGrouping<string, BenchmarkEvidence> clientEvidence in benchmarkEvidence.Values
                .GroupBy(value => value.ClientId.Value, StringComparer.OrdinalIgnoreCase))
            {
                currentFingerprints[clientEvidence.Key] = clientEvidence
                    .OrderByDescending(value => value.StartedAt)
                    .ThenByDescending(value => value.RunId)
                    .First()
                    .Fingerprints;
            }
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

    public BenchmarkPreparationResult PrepareBenchmarkRun(
        ClientId clientId,
        BenchmarkTrigger trigger,
        BenchmarkFingerprintSet fingerprints,
        DateTimeOffset evaluatedAt,
        TimeSpan maximumEvidenceAge)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximumEvidenceAge, TimeSpan.Zero);
        lock (gate)
        {
            if (trigger == BenchmarkTrigger.Automatic)
            {
                BenchmarkEvidence? pending = benchmarkEvidence.Values
                    .Where(value => value.ClientId == clientId)
                    .Where(value => value.Fingerprints == fingerprints)
                    .Where(value => value.CompletedAt is null && value.SelectedResult is null)
                    .OrderByDescending(value => value.StartedAt)
                    .ThenByDescending(value => value.RunId)
                    .FirstOrDefault();
                if (pending is not null)
                {
                    currentFingerprints[clientId.Value] = fingerprints;
                    return new BenchmarkPreparationResult(
                        BenchmarkPreparationDisposition.Continue,
                        pending,
                        "An automatic benchmark run for the current fingerprints is already active.");
                }

                BenchmarkEvidence? completed = benchmarkEvidence.Values
                    .Where(value => value.ClientId == clientId)
                    .Where(value => value.Fingerprints == fingerprints)
                    .Where(value => value.CompletedAt is not null && value.SelectedResult is not null)
                    .OrderByDescending(value => value.CompletedAt)
                    .ThenByDescending(value => value.RunId)
                    .FirstOrDefault();
                BenchmarkReuseDecision reuse = BenchmarkReuseEvaluator.Decide(
                    trigger,
                    fingerprints,
                    completed,
                    evaluatedAt,
                    maximumEvidenceAge);
                if (reuse.Disposition == BenchmarkRunDisposition.Reuse && completed is not null)
                {
                    currentFingerprints[clientId.Value] = fingerprints;
                    return new BenchmarkPreparationResult(
                        BenchmarkPreparationDisposition.Reuse,
                        completed,
                        reuse.Reason);
                }
            }

            var created = new BenchmarkEvidence(
                RunId: Guid.NewGuid(),
                ClientId: clientId,
                Trigger: trigger,
                Fingerprints: fingerprints,
                StartedAt: evaluatedAt,
                CompletedAt: null,
                NetworkSamples: [],
                DecoderSamples: [],
                PowerSamples: [],
                SelectedResult: null);
            BenchmarkEvidenceValidator.Validate(created);
            PersistAndPublishBenchmarkEvidence(created);
            currentFingerprints[clientId.Value] = fingerprints;
            return new BenchmarkPreparationResult(
                BenchmarkPreparationDisposition.StartNew,
                created,
                trigger == BenchmarkTrigger.Manual
                    ? "Manual benchmark requests always create a new run."
                    : trigger == BenchmarkTrigger.SessionPreflight
                        ? "Session preflight always creates a fresh measurement run."
                        : "No reusable benchmark evidence matches the current fingerprints.");
        }
    }

    public bool TryCompleteBenchmarkEvidence(
        Guid runId,
        string clientId,
        BenchmarkEvidence completed,
        out BenchmarkEvidence? committed)
    {
        BenchmarkEvidenceValidator.Validate(completed);
        lock (gate)
        {
            committed = null;
            if (!benchmarkEvidence.TryGetValue(runId, out BenchmarkEvidence? pending) ||
                !pending.ClientId.Value.Equals(clientId, StringComparison.OrdinalIgnoreCase) ||
                pending.CompletedAt is not null ||
                pending.SelectedResult is not null ||
                completed.RunId != pending.RunId ||
                completed.ClientId != pending.ClientId ||
                completed.Fingerprints != pending.Fingerprints ||
                completed.StartedAt != pending.StartedAt ||
                completed.CompletedAt is null ||
                completed.SelectedResult is null)
            {
                return false;
            }

            PersistAndPublishBenchmarkEvidence(completed);
            committed = completed;
            return true;
        }
    }

    public bool TryCancelBenchmarkEvidence(Guid runId, string clientId)
    {
        lock (gate)
        {
            if (!benchmarkEvidence.TryGetValue(runId, out BenchmarkEvidence? pending) ||
                !pending.ClientId.Value.Equals(clientId, StringComparison.OrdinalIgnoreCase) ||
                pending.CompletedAt is not null ||
                pending.SelectedResult is not null)
            {
                return false;
            }

            BenchmarkEvidence[] persisted = benchmarkEvidence.Values
                .Where(value => value.RunId != runId)
                .OrderBy(value => value.StartedAt)
                .ThenBy(value => value.RunId)
                .ToArray();
            benchmarkRepository.SaveEvidence(persisted);
            return benchmarkEvidence.Remove(runId);
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

    public BenchmarkPlanEvidence? GetLatestBenchmarkPlanEvidence(
        string clientId,
        DateTimeOffset evaluatedAt,
        TimeSpan maximumEvidenceAge,
        string codecPreference)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximumEvidenceAge, TimeSpan.Zero);
        return WithLock(() => currentFingerprints.TryGetValue(clientId, out BenchmarkFingerprintSet? current)
            ? benchmarkEvidence.Values
            .Where(value => value.ClientId.Value.Equals(clientId, StringComparison.OrdinalIgnoreCase))
            .Where(value => value.Fingerprints == current)
            .Where(value => value.CompletedAt is not null && value.SelectedResult is not null)
            .Where(value => value.CompletedAt <= evaluatedAt)
            .Where(value => evaluatedAt - value.CompletedAt!.Value <= maximumEvidenceAge)
            .OrderByDescending(value => value.CompletedAt)
            .ThenByDescending(value => value.RunId)
            .FirstOrDefault()
            ?.ToPlanEvidence(codecPreference)
            : null);
    }

    private void PersistAndPublishBenchmarkEvidence(BenchmarkEvidence evidence)
    {
        BenchmarkEvidence[] persisted = benchmarkEvidence.Values
            .Where(value => value.RunId != evidence.RunId)
            .Append(evidence)
            .OrderBy(value => value.StartedAt)
            .ThenBy(value => value.RunId)
            .ToArray();
        benchmarkRepository.SaveEvidence(persisted);
        benchmarkEvidence[evidence.RunId] = evidence;
    }

    private T WithLock<T>(Func<T> read)
    {
        lock (gate)
        {
            return read();
        }
    }
}

using Beacon.Core.Benchmarks;
using Beacon.Core.Diagnostics;
using Beacon.Core.Displays;
using Beacon.Core.Games;
using Beacon.Core.Games.Artwork;
using Beacon.Core.Input;
using Beacon.Core.Recovery;
using Beacon.Core.Sessions;
using Beacon.Core.Streaming;
using Beacon.Platform.Windows.Displays;
using Beacon.Platform.Windows.Games;
using Beacon.Platform.Windows.Input;
using Beacon.Platform.Windows.Recovery;
using Beacon.Platform.Windows.Sessions;
using Beacon.Platform.Windows.Streaming;
using Beacon.Server.Benchmarks;
using Beacon.Server.State;
using Beacon.Server.Security;
using Beacon.Server.Streaming;

namespace Beacon.Server.Hosting;

public static class BeaconServiceRegistration
{
    public const string HostModeConfigurationKey = "Beacon:HostMode";
    public const string HostModeEnvironmentVariable = "BEACON_HOST_MODE";
    public const string StreamingModeConfigurationKey = "Beacon:StreamingMode";
    public const string StreamingModeEnvironmentVariable = "BEACON_STREAMING_MODE";
    public const string StreamWorkerPathConfigurationKey = "Beacon:Streaming:WorkerPath";
    public const string StreamWorkerPathEnvironmentVariable = "BEACON_STREAM_WORKER_PATH";
    public const string ClientProfilesPathConfigurationKey = "Beacon:Profiles:Path";
    public const string ClientProfilesPathEnvironmentVariable = "BEACON_CLIENT_PROFILES_PATH";
    public const string BenchmarkEvidencePathConfigurationKey = "Beacon:Benchmarks:Path";
    public const string BenchmarkEvidencePathEnvironmentVariable = "BEACON_BENCHMARK_EVIDENCE_PATH";
    public const string SecurityTestHostConfigurationKey = "Beacon:Security:TestHost";
    public const string SecurityIdentityPathConfigurationKey = "Beacon:Security:IdentityPath";
    public const string SecurityCredentialsPathConfigurationKey = "Beacon:Security:CredentialsPath";

    public static IServiceCollection AddBeaconServices(
        this IServiceCollection services,
        IConfiguration configuration) =>
        services.AddBeaconServices(
            configuration,
            environmentHostMode: Environment.GetEnvironmentVariable(HostModeEnvironmentVariable),
            environmentClientProfilesPath: Environment.GetEnvironmentVariable(ClientProfilesPathEnvironmentVariable),
            environmentStreamingMode: Environment.GetEnvironmentVariable(StreamingModeEnvironmentVariable),
            environmentStreamWorkerPath: Environment.GetEnvironmentVariable(StreamWorkerPathEnvironmentVariable),
            environmentBenchmarkEvidencePath: Environment.GetEnvironmentVariable(
                BenchmarkEvidencePathEnvironmentVariable));

    public static IServiceCollection AddBeaconServices(
        this IServiceCollection services,
        IConfiguration configuration,
        string? environmentHostMode,
        string? environmentClientProfilesPath = null) =>
        services.AddBeaconServices(
            configuration,
            environmentHostMode,
            environmentClientProfilesPath,
            environmentStreamingMode: null,
            environmentStreamWorkerPath: null,
            environmentBenchmarkEvidencePath: null);

    public static IServiceCollection AddBeaconServices(
        this IServiceCollection services,
        IConfiguration configuration,
        string? environmentHostMode,
        string? environmentClientProfilesPath,
        string? environmentStreamingMode,
        string? environmentStreamWorkerPath,
        string? environmentBenchmarkEvidencePath)
    {
        BeaconHostMode hostMode = ResolveHostMode(configuration, environmentHostMode);
        BeaconStreamingMode streamingMode = ResolveStreamingMode(
            configuration,
            environmentStreamingMode,
            hostMode);
        BeaconHostOptions hostOptions = BeaconHostOptions.Create(hostMode) with
        {
            StreamingBackendName = streamingMode switch
            {
                BeaconStreamingMode.Fake => nameof(FakeStreamingBackend),
                BeaconStreamingMode.Worker => nameof(StreamWorkerStreamingBackend),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(streamingMode),
                    streamingMode,
                    "Unsupported Beacon streaming mode.")
            }
        };
        services.AddSingleton(hostOptions);
        services.AddSingleton<IClientProfileRepository>(_ =>
            CreateClientProfileRepository(configuration, environmentClientProfilesPath));
        services.AddSingleton<IBenchmarkEvidenceRepository>(_ =>
            CreateBenchmarkEvidenceRepository(
                configuration,
                environmentBenchmarkEvidencePath,
                hostMode));
        BeaconSecurityOptions securityOptions = CreateSecurityOptions(configuration);
        services.AddSingleton(securityOptions);
        services.AddSingleton<BeaconSecurityPolicy>();
        services.AddSingleton(_ => new BeaconServerIdentity(securityOptions.IdentityPath));
        services.AddSingleton(_ => new ClientCredentialService(securityOptions.CredentialsPath));
        services.AddSingleton<StreamTicketService>();
        services.AddSingleton<StreamTicketProvisioningService>();
        services.AddSingleton<BenchmarkRuntimeOrchestrator>();
        services.AddSingleton<StreamSessionLaunchService>();
        services.AddSingleton<StreamSessionReconnectService>();
        services.AddSingleton<InMemoryDiagnosticEventJournal>();
        services.AddSingleton<IDiagnosticEventSink>(sp =>
            sp.GetRequiredService<InMemoryDiagnosticEventJournal>());
        services.AddSingleton<IDiagnosticEventSource>(sp =>
            sp.GetRequiredService<InMemoryDiagnosticEventJournal>());
        services.AddSingleton<InMemoryClientStore>();
        services.AddSingleton<InMemorySessionStore>();
        services.AddSingleton<DisplayLeaseManager>();
        services.AddSingleton<ISessionOwnershipTracker, SessionOwnershipTracker>();
        services.AddSingleton<NoOpClientInputSink>();
        services.AddSingleton<IClientInputSink>(sp => sp.GetRequiredService<NoOpClientInputSink>());
        services.AddSingleton<IClientInputHealthProvider>(sp =>
            sp.GetRequiredService<NoOpClientInputSink>());
        services.AddSingleton<IGameLibraryProvider>(_ => new StaticGameLibraryProvider(
            "seed",
            [
                new GameDescriptor(
                    "steam-shortcut:3767414131",
                    "Dispatch",
                    "steam-shortcut",
                    new GameLaunchIntent(
                        "steam-rungameid",
                        "steam://rungameid/16180920483166814208"),
                    new GameArtwork(null, "none"),
                    Installed: true,
                    new GameProcessHints(null, null))
            ]));
        services.AddSingleton<IArtworkProvider, NoArtworkProvider>();
        services.AddSingleton(sp => new GameLibraryService(
            sp.GetServices<IGameLibraryProvider>().ToArray(),
            sp.GetRequiredService<IArtworkProvider>()));

        AddHostBoundaries(services, hostMode);
        AddStreamingBoundary(
            services,
            streamingMode,
            ResolveStreamWorkerPath(configuration, environmentStreamWorkerPath));
        return services;
    }

    public static BeaconStreamingMode ResolveStreamingMode(
        IConfiguration configuration,
        string? environmentStreamingMode,
        BeaconHostMode hostMode)
    {
        string? configuredMode = string.IsNullOrWhiteSpace(environmentStreamingMode)
            ? configuration[StreamingModeConfigurationKey]
            : environmentStreamingMode;

        if (string.IsNullOrWhiteSpace(configuredMode))
        {
            return hostMode == BeaconHostMode.Windows
                ? BeaconStreamingMode.Worker
                : BeaconStreamingMode.Fake;
        }

        return configuredMode.Trim().ToLowerInvariant() switch
        {
            "fake" => BeaconStreamingMode.Fake,
            "worker" => BeaconStreamingMode.Worker,
            _ => throw new InvalidOperationException(
                $"Unsupported Beacon streaming mode '{configuredMode}'. Set {StreamingModeConfigurationKey} or " +
                $"{StreamingModeEnvironmentVariable} to one of: fake, worker.")
        };
    }

    public static BeaconHostMode ResolveHostMode(
        IConfiguration configuration,
        string? environmentHostMode)
    {
        string? configuredMode = string.IsNullOrWhiteSpace(environmentHostMode)
            ? configuration[HostModeConfigurationKey]
            : environmentHostMode;

        if (string.IsNullOrWhiteSpace(configuredMode))
        {
            return BeaconHostMode.Fake;
        }

        return configuredMode.Trim().ToLowerInvariant() switch
        {
            "fake" => BeaconHostMode.Fake,
            "windows" => BeaconHostMode.Windows,
            _ => throw new InvalidOperationException(
                $"Unsupported Beacon host mode '{configuredMode}'. Set {HostModeConfigurationKey} or " +
                $"{HostModeEnvironmentVariable} to one of: fake, windows.")
        };
    }

    private static void AddHostBoundaries(IServiceCollection services, BeaconHostMode mode)
    {
        switch (mode)
        {
            case BeaconHostMode.Fake:
                services.AddFakeHostBoundaries();
                break;
            case BeaconHostMode.Windows:
                services.AddWindowsHostBoundaries();
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(mode),
                    mode,
                    "Unsupported Beacon host mode.");
        }
    }

    private static void AddStreamingBoundary(
        IServiceCollection services,
        BeaconStreamingMode mode,
        string? workerExecutablePath)
    {
        switch (mode)
        {
            case BeaconStreamingMode.Fake:
                services.AddSingleton<IStreamSessionAuthorizer, FakeStreamSessionAuthorizer>();
                services.AddSingleton<IStreamingBackend, FakeStreamingBackend>();
                services.AddSingleton<FakeBenchmarkRuntime>();
                services.AddSingleton<IBenchmarkRuntime>(sp =>
                    sp.GetRequiredService<FakeBenchmarkRuntime>());
                break;
            case BeaconStreamingMode.Worker:
                services.AddSingleton(sp => StreamWorkerProcessHostOptions.Create(
                    workerExecutablePath,
                    sp.GetRequiredService<BeaconServerIdentity>().IdentityPath));
                services.AddSingleton<StreamWorkerProcessHost>();
                services.AddSingleton<IStreamWorkerHost>(sp =>
                    sp.GetRequiredService<StreamWorkerProcessHost>());
                services.AddSingleton<IGenerationBoundStreamWorkerHost>(sp =>
                    sp.GetRequiredService<StreamWorkerProcessHost>());
                services.AddSingleton<IStreamSessionAuthorizer, StreamWorkerSessionAuthorizer>();
                services.AddSingleton(sp =>
                {
                    IStreamWorkerHost host = sp.GetRequiredService<IStreamWorkerHost>();
                    IWindowsDisplayNameResolver? displayNames =
                        sp.GetService<IWindowsDisplayNameResolver>();
                    return displayNames is null
                        ? new StreamWorkerStreamingBackend(host)
                        : new StreamWorkerStreamingBackend(host, displayNames);
                });
                services.AddSingleton<IStreamingBackend>(sp =>
                    sp.GetRequiredService<StreamWorkerStreamingBackend>());
                services.AddSingleton<IBenchmarkRuntime>(sp =>
                    sp.GetRequiredService<StreamWorkerStreamingBackend>());
                services.AddSingleton<IStreamWorkerRuntimeEvents>(sp =>
                    sp.GetRequiredService<StreamWorkerStreamingBackend>());
                services.AddHostedService<StreamWorkerEventRelay>();
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(mode),
                    mode,
                    "Unsupported Beacon streaming mode.");
        }
    }

    private static IServiceCollection AddFakeHostBoundaries(this IServiceCollection services)
    {
        services.AddSingleton<IDisplayBackend, FakeDisplayBackend>();
        services.AddSingleton<IRecoveryBackend, FakeRecoveryBackend>();
        services.AddSingleton<IGameLauncher, FakeGameLauncher>();
        services.AddSingleton<FakeSessionActivityInspector>();
        services.AddSingleton<ISessionActivityInspector>(sp =>
            sp.GetRequiredService<FakeSessionActivityInspector>());
        services.AddSingleton<ISessionOwnedWorkTerminator>(sp =>
            new FakeSessionOwnedWorkTerminator(
                sp.GetRequiredService<FakeSessionActivityInspector>()));
        return services;
    }

    private static IServiceCollection AddWindowsHostBoundaries(this IServiceCollection services)
    {
        services.AddSingleton(WindowsDisplayNameMapStore.Default);
        services.AddSingleton<WindowsDisplayNameMap>();
        services.AddSingleton<IWindowsDisplayNameResolver>(sp =>
            sp.GetRequiredService<WindowsDisplayNameMap>());
        services.AddSingleton<IWindowsDisplayApi>(sp =>
            new WindowsDisplayApi(sp.GetRequiredService<WindowsDisplayNameMap>()));
        services.AddSingleton<IDisplayBackend, WindowsDisplayBackend>();
        services.AddSingleton<IWindowsRecoveryApi, WindowsRecoveryApi>();
        services.AddSingleton<IRecoveryBackend, WindowsRecoveryBackend>();
        services.AddSingleton<IGameLauncher, WindowsGameLauncher>();
        services.AddSingleton<IWindowsSessionActivityApi, WindowsSessionActivityApi>();
        services.AddSingleton<ISessionActivityInspector, WindowsSessionActivityInspector>();
        services.AddSingleton<ISessionOwnedWorkTerminator, WindowsSessionOwnedWorkTerminator>();
        services.AddSingleton<IWindowsInputApi, WindowsInputApi>();
        services.AddSingleton<WindowsClientInputSink>();
        services.AddSingleton<IClientInputSink>(sp =>
            sp.GetRequiredService<WindowsClientInputSink>());
        services.AddSingleton<IClientInputHealthProvider>(sp =>
            sp.GetRequiredService<WindowsClientInputSink>());
        return services;
    }

    private static IClientProfileRepository CreateClientProfileRepository(
        IConfiguration configuration,
        string? environmentClientProfilesPath)
    {
        string? path = ResolveClientProfilesPath(configuration, environmentClientProfilesPath);
        return string.IsNullOrWhiteSpace(path)
            ? new InMemoryClientProfileRepository()
            : new FileClientProfileRepository(path);
    }

    public static string? ResolveClientProfilesPath(
        IConfiguration configuration,
        string? environmentClientProfilesPath) =>
        string.IsNullOrWhiteSpace(environmentClientProfilesPath)
            ? configuration[ClientProfilesPathConfigurationKey]
            : environmentClientProfilesPath;

    private static IBenchmarkEvidenceRepository CreateBenchmarkEvidenceRepository(
        IConfiguration configuration,
        string? environmentBenchmarkEvidencePath,
        BeaconHostMode hostMode)
    {
        string? path = ResolveBenchmarkEvidencePath(configuration, environmentBenchmarkEvidencePath);
        if (!string.IsNullOrWhiteSpace(path))
        {
            return new FileBenchmarkEvidenceRepository(path);
        }

        return hostMode == BeaconHostMode.Fake
            ? new InMemoryBenchmarkEvidenceRepository([FakeBenchmarkEvidence.CreateZFold7(DateTimeOffset.UtcNow)])
            : new InMemoryBenchmarkEvidenceRepository();
    }

    public static string? ResolveBenchmarkEvidencePath(
        IConfiguration configuration,
        string? environmentBenchmarkEvidencePath) =>
        string.IsNullOrWhiteSpace(environmentBenchmarkEvidencePath)
            ? configuration[BenchmarkEvidencePathConfigurationKey]
            : environmentBenchmarkEvidencePath;

    public static string? ResolveStreamWorkerPath(
        IConfiguration configuration,
        string? environmentStreamWorkerPath) =>
        string.IsNullOrWhiteSpace(environmentStreamWorkerPath)
            ? configuration[StreamWorkerPathConfigurationKey]
            : environmentStreamWorkerPath;

    private static BeaconSecurityOptions CreateSecurityOptions(IConfiguration configuration)
    {
        bool? testHost = bool.TryParse(configuration[SecurityTestHostConfigurationKey], out bool configured)
            ? configured
            : null;
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Beacon Stream");
        string identityPath = configuration[SecurityIdentityPathConfigurationKey]
            ?? Path.Combine(root, "server-identity.pfx");
        string credentialsPath = configuration[SecurityCredentialsPathConfigurationKey]
            ?? Path.Combine(root, "client-credentials.json");
        return new BeaconSecurityOptions(testHost, identityPath, credentialsPath);
    }
}

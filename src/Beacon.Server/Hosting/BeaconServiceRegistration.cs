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
using Beacon.Platform.Windows.HostAgent;
using Beacon.Platform.Windows.Recovery;
using Beacon.Platform.Windows.Sessions;
using Beacon.Platform.Windows.Streaming;
using Beacon.Server.Benchmarks;
using Beacon.Server.State;
using Beacon.Server.Security;
using Beacon.Server.Streaming;
using System.Security.Principal;

namespace Beacon.Server.Hosting;

public static class BeaconServiceRegistration
{
    public const string StreamWorkerPathConfigurationKey = "Beacon:Streaming:WorkerPath";
    public const string StreamWorkerPathEnvironmentVariable = "BEACON_STREAM_WORKER_PATH";
    public const string ClientProfilesPathConfigurationKey = "Beacon:Profiles:Path";
    public const string ClientProfilesPathEnvironmentVariable = "BEACON_CLIENT_PROFILES_PATH";
    public const string BenchmarkEvidencePathConfigurationKey = "Beacon:Benchmarks:Path";
    public const string BenchmarkEvidencePathEnvironmentVariable = "BEACON_BENCHMARK_EVIDENCE_PATH";
    public const string SteamRootConfigurationKey = "Beacon:Games:SteamRoot";
    public const string HeroicRootConfigurationKey = "Beacon:Games:HeroicRoot";
    public const string HydraDatabasePathConfigurationKey = "Beacon:Games:HydraDatabasePath";
    public const string ManualGamesPathConfigurationKey = "Beacon:Games:ManualPath";
    public const string SecurityTestHostConfigurationKey = "Beacon:Security:TestHost";
    public const string SecurityIdentityPathConfigurationKey = "Beacon:Security:IdentityPath";
    public const string SecurityCredentialsPathConfigurationKey = "Beacon:Security:CredentialsPath";

    public static IServiceCollection AddBeaconServices(
        this IServiceCollection services,
        IConfiguration configuration) =>
        services.AddBeaconServices(
            configuration,
            environmentClientProfilesPath: Environment.GetEnvironmentVariable(ClientProfilesPathEnvironmentVariable),
            environmentStreamWorkerPath: Environment.GetEnvironmentVariable(StreamWorkerPathEnvironmentVariable),
            environmentBenchmarkEvidencePath: Environment.GetEnvironmentVariable(
                BenchmarkEvidencePathEnvironmentVariable));

    public static IServiceCollection AddBeaconServices(
        this IServiceCollection services,
        IConfiguration configuration,
        string? environmentClientProfilesPath,
        string? environmentStreamWorkerPath,
        string? environmentBenchmarkEvidencePath)
    {
        services.AddSingleton(BeaconHostOptions.Production);
        services.AddSingleton<IClientProfileRepository>(_ =>
            CreateClientProfileRepository(configuration, environmentClientProfilesPath));
        services.AddSingleton<IBenchmarkEvidenceRepository>(_ =>
            CreateBenchmarkEvidenceRepository(
                configuration,
                environmentBenchmarkEvidencePath));
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
        WindowsGameLibraryProviderOptions gameLocations = new(
            configuration[SteamRootConfigurationKey],
            configuration[HeroicRootConfigurationKey],
            configuration[HydraDatabasePathConfigurationKey],
            configuration[ManualGamesPathConfigurationKey]);
        foreach (IGameLibraryProvider provider in WindowsGameLibraryProviderFactory.CreateProviders(gameLocations))
        {
            services.AddSingleton(typeof(IGameLibraryProvider), provider);
        }
        services.AddSingleton<IArtworkProvider, NoArtworkProvider>();
        services.AddSingleton(sp => new GameLibraryService(
            sp.GetServices<IGameLibraryProvider>().ToArray(),
            sp.GetRequiredService<IArtworkProvider>()));

        services.AddWindowsHostBoundaries();
        AddStreamWorkerBoundary(
            services,
            ResolveStreamWorkerPath(configuration, environmentStreamWorkerPath));
        return services;
    }

    private static void AddStreamWorkerBoundary(
        IServiceCollection services,
        string? workerExecutablePath)
    {
        services.AddSingleton(sp => StreamWorkerProcessHostOptions.Create(
            workerExecutablePath,
            sp.GetRequiredService<BeaconServerIdentity>().IdentityPath));
        services.AddSingleton<StreamWorkerProcessHost>();
        services.AddSingleton<IStreamWorkerHost>(sp =>
            sp.GetRequiredService<StreamWorkerProcessHost>());
        services.AddSingleton<IStreamSessionAuthorizer, StreamWorkerSessionAuthorizer>();
        services.AddSingleton(sp => new StreamWorkerStreamingBackend(
            sp.GetRequiredService<IStreamWorkerHost>(),
            sp.GetRequiredService<IWindowsDisplayNameResolver>()));
        services.AddSingleton<IStreamingBackend>(sp =>
            sp.GetRequiredService<StreamWorkerStreamingBackend>());
        services.AddSingleton<IBenchmarkRuntime>(sp =>
            sp.GetRequiredService<StreamWorkerStreamingBackend>());
        services.AddSingleton<IStreamWorkerRuntimeEvents>(sp =>
            sp.GetRequiredService<StreamWorkerStreamingBackend>());
        services.AddHostedService<StreamWorkerEventRelay>();
    }

    private static IServiceCollection AddWindowsHostBoundaries(
        this IServiceCollection services)
    {
        services.AddSingleton(_ =>
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            SecurityIdentifier owner = identity.User
                ?? throw new InvalidOperationException("The Beacon server user has no Windows SID.");
            return new HostAgentConnection(owner);
        });
        services.AddSingleton<IHostAgentConnection>(sp =>
            sp.GetRequiredService<HostAgentConnection>());
        services.AddHostedService<HostAgentConnectionHostedService>();
        services.AddSingleton<HostAgentWindowsDisplayApi>();
        services.AddSingleton<IWindowsDisplayApi>(sp =>
            sp.GetRequiredService<HostAgentWindowsDisplayApi>());
        services.AddSingleton<IWindowsDisplayLeaseSession>(sp =>
            sp.GetRequiredService<HostAgentWindowsDisplayApi>());
        services.AddSingleton<IWindowsDisplayNameResolver>(sp =>
            sp.GetRequiredService<HostAgentWindowsDisplayApi>());
        services.AddSingleton<HostAgentDriverUpdateClient>();
        services.AddSingleton<IHostAgentDriverUpdateClient>(sp =>
            sp.GetRequiredService<HostAgentDriverUpdateClient>());
        services.AddSingleton<IDisplayBackend, WindowsDisplayBackend>();
        services.AddSingleton<IWindowsRecoveryApi, WindowsRecoveryApi>();
        services.AddSingleton<IRecoveryBackend, WindowsRecoveryBackend>();
        services.AddSingleton<IGameLauncher, WindowsGameLauncher>();
        services.AddSingleton<IWindowsSessionActivityApi, WindowsSessionActivityApi>();
        services.AddSingleton<ISessionActivityInspector, WindowsSessionActivityInspector>();
        services.AddSingleton<ISessionOwnedWorkTerminator, WindowsSessionOwnedWorkTerminator>();
        services.AddSingleton<IWindowsInputApi, WindowsInputApi>();
        services.AddSingleton<IWindowsSessionInputTargetActivator, WindowsSessionInputTargetActivator>();
        services.AddSingleton<IWindowsVirtualControllerApi, WindowsVirtualControllerApi>();
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
        string? environmentBenchmarkEvidencePath)
    {
        string? path = ResolveBenchmarkEvidencePath(configuration, environmentBenchmarkEvidencePath);
        if (!string.IsNullOrWhiteSpace(path))
        {
            return new FileBenchmarkEvidenceRepository(path);
        }

        return new InMemoryBenchmarkEvidenceRepository();
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

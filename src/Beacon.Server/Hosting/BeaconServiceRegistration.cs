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
using Beacon.Server.State;

namespace Beacon.Server.Hosting;

public static class BeaconServiceRegistration
{
    public const string HostModeConfigurationKey = "Beacon:HostMode";
    public const string HostModeEnvironmentVariable = "BEACON_HOST_MODE";
    public const string ClientProfilesPathConfigurationKey = "Beacon:Profiles:Path";
    public const string ClientProfilesPathEnvironmentVariable = "BEACON_CLIENT_PROFILES_PATH";
    public const string PairingTokenConfigurationKey = "Beacon:Pairing:Token";
    public const string PairingTokenEnvironmentVariable = "BEACON_PAIRING_TOKEN";

    public static IServiceCollection AddBeaconServices(
        this IServiceCollection services,
        IConfiguration configuration) =>
        services.AddBeaconServices(
            configuration,
            Environment.GetEnvironmentVariable(HostModeEnvironmentVariable),
            Environment.GetEnvironmentVariable(ClientProfilesPathEnvironmentVariable),
            Environment.GetEnvironmentVariable(PairingTokenEnvironmentVariable));

    public static IServiceCollection AddBeaconServices(
        this IServiceCollection services,
        IConfiguration configuration,
        string? environmentHostMode,
        string? environmentClientProfilesPath = null,
        string? environmentPairingToken = null)
    {
        BeaconHostMode mode = ResolveHostMode(configuration, environmentHostMode);
        services.AddSingleton(BeaconHostOptions.Create(mode));
        services.AddSingleton<IClientProfileRepository>(_ =>
            CreateClientProfileRepository(configuration, environmentClientProfilesPath));
        services.AddSingleton(new ClientPairingOptions(
            ResolvePairingToken(configuration, environmentPairingToken)));
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

        AddHostBoundaries(services, mode);
        AddStreamingBoundary(services, mode);
        return services;
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

    private static void AddStreamingBoundary(IServiceCollection services, BeaconHostMode mode)
    {
        if (mode == BeaconHostMode.Windows)
        {
            services.AddSingleton(StreamWorkerProcessHostOptions.CreateDefault());
            services.AddSingleton<StreamWorkerProcessHost>();
            services.AddSingleton<IStreamWorkerHost>(sp =>
                sp.GetRequiredService<StreamWorkerProcessHost>());
            services.AddSingleton<IStreamingBackend, StreamWorkerStreamingBackend>();
            return;
        }

        services.AddSingleton<IStreamingBackend, FakeStreamingBackend>();
    }

    private static IServiceCollection AddFakeHostBoundaries(this IServiceCollection services)
    {
        services.AddSingleton<IDisplayBackend, FakeDisplayBackend>();
        services.AddSingleton<IRecoveryBackend, FakeRecoveryBackend>();
        services.AddSingleton<IGameLauncher, FakeGameLauncher>();
        services.AddSingleton<FakeSessionActivityInspector>();
        services.AddSingleton<ISessionActivityInspector>(sp =>
            sp.GetRequiredService<FakeSessionActivityInspector>());
        return services;
    }

    private static IServiceCollection AddWindowsHostBoundaries(this IServiceCollection services)
    {
        services.AddSingleton<IWindowsDisplayApi, WindowsDisplayApi>();
        services.AddSingleton<IDisplayBackend, WindowsDisplayBackend>();
        services.AddSingleton<IWindowsRecoveryApi, WindowsRecoveryApi>();
        services.AddSingleton<IRecoveryBackend, WindowsRecoveryBackend>();
        services.AddSingleton<IGameLauncher, WindowsGameLauncher>();
        services.AddSingleton<IWindowsSessionActivityApi, WindowsSessionActivityApi>();
        services.AddSingleton<ISessionActivityInspector, WindowsSessionActivityInspector>();
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

    public static string? ResolvePairingToken(
        IConfiguration configuration,
        string? environmentPairingToken) =>
        string.IsNullOrWhiteSpace(environmentPairingToken)
            ? configuration[PairingTokenConfigurationKey]
            : environmentPairingToken;
}

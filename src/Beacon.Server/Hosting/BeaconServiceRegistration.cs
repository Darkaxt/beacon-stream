using Beacon.Core.Displays;
using Beacon.Core.Diagnostics;
using Beacon.Core.Games;
using Beacon.Core.Games.Artwork;
using Beacon.Core.Input;
using Beacon.Core.Recovery;
using Beacon.Core.Sessions;
using Beacon.Core.Streaming;
using Beacon.Platform.Windows.Displays;
using Beacon.Platform.Windows.Games;
using Beacon.Platform.Windows.Recovery;
using Beacon.Platform.Windows.Sessions;
using Beacon.Platform.Windows.Streaming;
using Beacon.Server.State;

namespace Beacon.Server.Hosting;

public static class BeaconServiceRegistration
{
    public const string HostModeConfigurationKey = "Beacon:HostMode";
    public const string HostModeEnvironmentVariable = "BEACON_HOST_MODE";
    public const string StreamingBackendConfigurationKey = "Beacon:Streaming:Backend";
    public const string StreamingBackendEnvironmentVariable = "BEACON_STREAMING_BACKEND";
    public const string ExternalStreamingExecutableConfigurationKey = "Beacon:Streaming:ExternalProcess:ExecutablePath";
    public const string ExternalStreamingExecutableEnvironmentVariable = "BEACON_EXTERNAL_STREAMING_EXECUTABLE";
    public const string ExternalStreamingManifestConfigurationKey = "Beacon:Streaming:ExternalProcess:ManifestPath";
    public const string ExternalStreamingManifestEnvironmentVariable = "BEACON_EXTERNAL_STREAMING_MANIFEST";
    public const string ExternalStreamingConnectionProtocolConfigurationKey = "Beacon:Streaming:ExternalProcess:Connection:Protocol";
    public const string ExternalStreamingConnectionLaunchUriConfigurationKey = "Beacon:Streaming:ExternalProcess:Connection:LaunchUri";
    public const string ExternalStreamingConnectionProtocolEnvironmentVariable = "BEACON_EXTERNAL_STREAMING_CONNECTION_PROTOCOL";
    public const string ExternalStreamingConnectionLaunchUriEnvironmentVariable = "BEACON_EXTERNAL_STREAMING_CONNECTION_LAUNCH_URI";
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
            Environment.GetEnvironmentVariable(StreamingBackendEnvironmentVariable),
            Environment.GetEnvironmentVariable(ExternalStreamingExecutableEnvironmentVariable),
            Environment.GetEnvironmentVariable(ExternalStreamingConnectionProtocolEnvironmentVariable),
            Environment.GetEnvironmentVariable(ExternalStreamingConnectionLaunchUriEnvironmentVariable),
            Environment.GetEnvironmentVariable(ExternalStreamingManifestEnvironmentVariable),
            Environment.GetEnvironmentVariable(ClientProfilesPathEnvironmentVariable),
            Environment.GetEnvironmentVariable(PairingTokenEnvironmentVariable));

    public static IServiceCollection AddBeaconServices(
        this IServiceCollection services,
        IConfiguration configuration,
        string? environmentHostMode,
        string? environmentStreamingBackend = null,
        string? environmentExternalStreamingExecutable = null,
        string? environmentExternalStreamingConnectionProtocol = null,
        string? environmentExternalStreamingConnectionLaunchUri = null,
        string? environmentExternalStreamingManifest = null,
        string? environmentClientProfilesPath = null,
        string? environmentPairingToken = null)
    {
        BeaconHostMode mode = ResolveHostMode(configuration, environmentHostMode);
        BeaconStreamingBackendMode streamingBackendMode = ResolveStreamingBackendMode(configuration, environmentStreamingBackend);
        services.AddSingleton(BeaconHostOptions.Create(mode, streamingBackendMode));
        services.AddSingleton<IClientProfileRepository>(_ => CreateClientProfileRepository(configuration, environmentClientProfilesPath));
        services.AddSingleton(new ClientPairingOptions(ResolvePairingToken(configuration, environmentPairingToken)));
        services.AddSingleton<InMemoryDiagnosticEventJournal>();
        services.AddSingleton<IDiagnosticEventSink>(sp => sp.GetRequiredService<InMemoryDiagnosticEventJournal>());
        services.AddSingleton<IDiagnosticEventSource>(sp => sp.GetRequiredService<InMemoryDiagnosticEventJournal>());
        services.AddSingleton<InMemoryClientStore>();
        services.AddSingleton<InMemorySessionStore>();
        services.AddSingleton<DisplayLeaseManager>();
        services.AddSingleton<ISessionOwnershipTracker, SessionOwnershipTracker>();
        services.AddSingleton<IClientInputSink, NoOpClientInputSink>();
        services.AddSingleton<IGameLibraryProvider>(_ => new StaticGameLibraryProvider(
            "seed",
            [
                new GameDescriptor(
                    "steam-shortcut:3767414131",
                    "Dispatch",
                    "steam-shortcut",
                    new GameLaunchIntent("steam-rungameid", "steam://rungameid/16180920483166814208"),
                    new GameArtwork(null, "none"),
                    Installed: true,
                    new GameProcessHints(null, null))
            ]));
        services.AddSingleton<IArtworkProvider, NoArtworkProvider>();
        services.AddSingleton(sp => new GameLibraryService(
            sp.GetServices<IGameLibraryProvider>().ToArray(),
            sp.GetRequiredService<IArtworkProvider>()));

        AddHostBoundaries(services, mode);
        AddStreamingBackend(
            services,
            configuration,
            streamingBackendMode,
            environmentExternalStreamingExecutable,
            environmentExternalStreamingConnectionProtocol,
            environmentExternalStreamingConnectionLaunchUri,
            environmentExternalStreamingManifest);
        return services;
    }

    public static BeaconHostMode ResolveHostMode(IConfiguration configuration, string? environmentHostMode)
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
                $"Unsupported Beacon host mode '{configuredMode}'. Set {HostModeConfigurationKey} or {HostModeEnvironmentVariable} to one of: fake, windows.")
        };
    }

    public static BeaconStreamingBackendMode ResolveStreamingBackendMode(
        IConfiguration configuration,
        string? environmentStreamingBackend)
    {
        string? configuredMode = string.IsNullOrWhiteSpace(environmentStreamingBackend)
            ? configuration[StreamingBackendConfigurationKey]
            : environmentStreamingBackend;

        if (string.IsNullOrWhiteSpace(configuredMode))
        {
            return BeaconStreamingBackendMode.Fake;
        }

        return configuredMode.Trim().ToLowerInvariant() switch
        {
            "fake" => BeaconStreamingBackendMode.Fake,
            "external-process" => BeaconStreamingBackendMode.ExternalProcess,
            _ => throw new InvalidOperationException(
                $"Unsupported Beacon streaming backend '{configuredMode}'. Set {StreamingBackendConfigurationKey} or {StreamingBackendEnvironmentVariable} to one of: fake, external-process.")
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
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported Beacon host mode.");
        }
    }

    private static IServiceCollection AddFakeHostBoundaries(this IServiceCollection services)
    {
        services.AddSingleton<IDisplayBackend, FakeDisplayBackend>();
        services.AddSingleton<IRecoveryBackend, FakeRecoveryBackend>();
        services.AddSingleton<IGameLauncher, FakeGameLauncher>();
        services.AddSingleton<FakeSessionActivityInspector>();
        services.AddSingleton<ISessionActivityInspector>(sp => sp.GetRequiredService<FakeSessionActivityInspector>());
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
        return services;
    }

    private static void AddStreamingBackend(
        IServiceCollection services,
        IConfiguration configuration,
        BeaconStreamingBackendMode mode,
        string? environmentExternalStreamingExecutable,
        string? environmentExternalStreamingConnectionProtocol,
        string? environmentExternalStreamingConnectionLaunchUri,
        string? environmentExternalStreamingManifest)
    {
        switch (mode)
        {
            case BeaconStreamingBackendMode.Fake:
                services.AddSingleton<IStreamingBackend, FakeStreamingBackend>();
                break;
            case BeaconStreamingBackendMode.ExternalProcess:
                services.AddSingleton(CreateExternalProcessStreamingOptions(
                    configuration,
                    environmentExternalStreamingExecutable,
                    environmentExternalStreamingConnectionProtocol,
                    environmentExternalStreamingConnectionLaunchUri,
                    environmentExternalStreamingManifest));
                services.AddSingleton<IExternalStreamingProcessRunner, WindowsExternalStreamingProcessRunner>();
                services.AddSingleton<IExternalStreamingManifestReader, WindowsExternalStreamingManifestReader>();
                services.AddSingleton<IStreamingBackend, ExternalProcessStreamingBackend>();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported Beacon streaming backend mode.");
        }
    }

    private static string? ResolveExternalStreamingExecutable(
        IConfiguration configuration,
        string? environmentExternalStreamingExecutable) =>
        string.IsNullOrWhiteSpace(environmentExternalStreamingExecutable)
            ? configuration[ExternalStreamingExecutableConfigurationKey]
            : environmentExternalStreamingExecutable;

    private static ExternalProcessStreamingOptions CreateExternalProcessStreamingOptions(
        IConfiguration configuration,
        string? environmentExternalStreamingExecutable,
        string? environmentExternalStreamingConnectionProtocol,
        string? environmentExternalStreamingConnectionLaunchUri,
        string? environmentExternalStreamingManifest)
    {
        string? protocol = string.IsNullOrWhiteSpace(environmentExternalStreamingConnectionProtocol)
            ? configuration[ExternalStreamingConnectionProtocolConfigurationKey]
            : environmentExternalStreamingConnectionProtocol;
        string? launchUri = string.IsNullOrWhiteSpace(environmentExternalStreamingConnectionLaunchUri)
            ? configuration[ExternalStreamingConnectionLaunchUriConfigurationKey]
            : environmentExternalStreamingConnectionLaunchUri;
        string? manifestPath = string.IsNullOrWhiteSpace(environmentExternalStreamingManifest)
            ? configuration[ExternalStreamingManifestConfigurationKey]
            : environmentExternalStreamingManifest;
        Dictionary<string, string> endpoints = configuration
            .GetSection("Beacon:Streaming:ExternalProcess:Connection:Endpoints")
            .GetChildren()
            .Where(child => !string.IsNullOrWhiteSpace(child.Key) && !string.IsNullOrWhiteSpace(child.Value))
            .ToDictionary(child => child.Key, child => child.Value!, StringComparer.OrdinalIgnoreCase);

        return new ExternalProcessStreamingOptions(
            ResolveExternalStreamingExecutable(configuration, environmentExternalStreamingExecutable),
            protocol,
            launchUri,
            endpoints,
            manifestPath);
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

using Beacon.Core.Displays;
using Beacon.Core.Games;
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
using Beacon.Server.Hosting;
using Beacon.Server.State;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Beacon.Server.Tests;

public sealed class BeaconServiceRegistrationTests
{
    [Fact]
    public void DefaultRegistrationUsesFakeHostMode()
    {
        using ServiceProvider provider = BuildProvider();

        BeaconHostOptions options = provider.GetRequiredService<BeaconHostOptions>();

        Assert.Equal(BeaconHostMode.Fake, options.Mode);
        Assert.Equal(BeaconStreamingBackendMode.Fake, options.StreamingBackendMode);
        Assert.Equal("fake", options.ModeName);
        Assert.Equal("fake", options.StreamingBackendModeName);
        Assert.IsType<FakeDisplayBackend>(provider.GetRequiredService<IDisplayBackend>());
        Assert.IsType<FakeGameLauncher>(provider.GetRequiredService<IGameLauncher>());
        Assert.IsType<FakeStreamingBackend>(provider.GetRequiredService<IStreamingBackend>());
        Assert.IsType<FakeSessionActivityInspector>(provider.GetRequiredService<ISessionActivityInspector>());
        Assert.IsType<NoOpClientInputSink>(provider.GetRequiredService<IClientInputSink>());
        ClientInputHealth inputHealth = provider.GetRequiredService<IClientInputHealthProvider>().GetHealth();
        Assert.Equal("no-op", inputHealth.Backend);
        Assert.Contains("pointer", inputHealth.SupportedEventTypes);
        Assert.Contains("keyboard", inputHealth.SupportedEventTypes);
        Assert.Contains("press", inputHealth.SupportedKeyboardActions);
        Assert.IsType<InMemoryClientProfileRepository>(provider.GetRequiredService<IClientProfileRepository>());
        Assert.False(provider.GetRequiredService<ClientPairingOptions>().Enabled);
    }

    [Fact]
    public void WindowsRegistrationUsesWindowsHostBoundaries()
    {
        using ServiceProvider provider = BuildProvider(new KeyValuePair<string, string?>(
            BeaconServiceRegistration.HostModeConfigurationKey,
            "windows"));

        BeaconHostOptions options = provider.GetRequiredService<BeaconHostOptions>();

        Assert.Equal(BeaconHostMode.Windows, options.Mode);
        Assert.Equal(BeaconStreamingBackendMode.Fake, options.StreamingBackendMode);
        Assert.Equal("windows", options.ModeName);
        Assert.IsType<WindowsDisplayApi>(provider.GetRequiredService<IWindowsDisplayApi>());
        Assert.IsType<WindowsDisplayBackend>(provider.GetRequiredService<IDisplayBackend>());
        Assert.IsType<WindowsRecoveryApi>(provider.GetRequiredService<IWindowsRecoveryApi>());
        Assert.IsType<WindowsRecoveryBackend>(provider.GetRequiredService<IRecoveryBackend>());
        Assert.IsType<WindowsGameLauncher>(provider.GetRequiredService<IGameLauncher>());
        Assert.IsType<WindowsSessionActivityApi>(provider.GetRequiredService<IWindowsSessionActivityApi>());
        Assert.IsType<WindowsSessionActivityInspector>(provider.GetRequiredService<ISessionActivityInspector>());
        Assert.IsType<WindowsInputApi>(provider.GetRequiredService<IWindowsInputApi>());
        Assert.IsType<WindowsClientInputSink>(provider.GetRequiredService<IClientInputSink>());
        ClientInputHealth inputHealth = provider.GetRequiredService<IClientInputHealthProvider>().GetHealth();
        Assert.Equal("windows-sendinput", inputHealth.Backend);
        Assert.Contains("tap", inputHealth.SupportedPointerActions);
        Assert.Contains("keyboard", inputHealth.SupportedEventTypes);
        Assert.Contains("press", inputHealth.SupportedKeyboardActions);
        Assert.IsType<FakeStreamingBackend>(provider.GetRequiredService<IStreamingBackend>());
    }

    [Fact]
    public void ExternalProcessStreamingRegistrationUsesExplicitBackendAndOptions()
    {
        using ServiceProvider provider = BuildProvider(
            new KeyValuePair<string, string?>(BeaconServiceRegistration.StreamingBackendConfigurationKey, "external-process"),
            new KeyValuePair<string, string?>(BeaconServiceRegistration.ExternalStreamingExecutableConfigurationKey, "C:\\Tools\\beacon-stream-wrapper.exe"));

        BeaconHostOptions options = provider.GetRequiredService<BeaconHostOptions>();

        Assert.Equal(BeaconStreamingBackendMode.ExternalProcess, options.StreamingBackendMode);
        Assert.Equal("external-process", options.StreamingBackendModeName);
        Assert.IsType<ExternalProcessStreamingBackend>(provider.GetRequiredService<IStreamingBackend>());
        Assert.IsType<WindowsExternalStreamingProcessRunner>(provider.GetRequiredService<IExternalStreamingProcessRunner>());
        Assert.IsType<WindowsExternalStreamingSessionDescriptorStore>(provider.GetRequiredService<IExternalStreamingSessionDescriptorStore>());
        Assert.Equal(
            "C:\\Tools\\beacon-stream-wrapper.exe",
            provider.GetRequiredService<ExternalProcessStreamingOptions>().ExecutablePath);
    }

    [Fact]
    public void ExternalProcessConnectionOptionsUseConfiguration()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [BeaconServiceRegistration.StreamingBackendConfigurationKey] = "external-process",
                [BeaconServiceRegistration.ExternalStreamingExecutableConfigurationKey] = "C:\\Tools\\sunshine-wrapper.exe",
                ["Beacon:Streaming:ExternalProcess:Connection:Protocol"] = "gamestream",
                ["Beacon:Streaming:ExternalProcess:Connection:LaunchUri"] = "moonlight://beacon/session",
                ["Beacon:Streaming:ExternalProcess:Connection:Endpoints:rtsp"] = "rtsp://127.0.0.1:48010/beacon"
            })
            .Build();

        using ServiceProvider provider = new ServiceCollection()
            .AddBeaconServices(configuration, environmentHostMode: null)
            .BuildServiceProvider();

        ExternalProcessStreamingOptions options = provider.GetRequiredService<ExternalProcessStreamingOptions>();
        Assert.Equal("gamestream", options.ConnectionProtocol);
        Assert.Equal("moonlight://beacon/session", options.ConnectionLaunchUri);
        Assert.Equal("rtsp://127.0.0.1:48010/beacon", options.ConnectionEndpoints?["rtsp"]);
    }

    [Fact]
    public void ExternalProcessSunshineEndpointProfileUsesConfiguration()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [BeaconServiceRegistration.StreamingBackendConfigurationKey] = "external-process",
                [BeaconServiceRegistration.ExternalStreamingExecutableConfigurationKey] = "C:\\Tools\\sunshine-wrapper.exe",
                [BeaconServiceRegistration.ExternalStreamingConnectionSunshineHostConfigurationKey] = "192.168.1.50",
                [BeaconServiceRegistration.ExternalStreamingConnectionSunshineBasePortConfigurationKey] = "48000"
            })
            .Build();

        using ServiceProvider provider = new ServiceCollection()
            .AddBeaconServices(configuration, environmentHostMode: null)
            .BuildServiceProvider();

        ExternalProcessStreamingOptions options = provider.GetRequiredService<ExternalProcessStreamingOptions>();
        Assert.Equal("192.168.1.50", options.SunshineProfile?.Host);
        Assert.Equal(48000, options.SunshineProfile?.BasePort);
    }

    [Fact]
    public void ExternalProcessSunshineEndpointProfileUsesEnvironmentOverrides()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [BeaconServiceRegistration.StreamingBackendConfigurationKey] = "external-process",
                [BeaconServiceRegistration.ExternalStreamingExecutableConfigurationKey] = "C:\\Tools\\sunshine-wrapper.exe",
                [BeaconServiceRegistration.ExternalStreamingConnectionSunshineHostConfigurationKey] = "192.168.1.50",
                [BeaconServiceRegistration.ExternalStreamingConnectionSunshineBasePortConfigurationKey] = "48000"
            })
            .Build();

        using ServiceProvider provider = new ServiceCollection()
            .AddBeaconServices(
                configuration,
                environmentHostMode: null,
                environmentExternalStreamingConnectionSunshineHost: "10.0.0.20",
                environmentExternalStreamingConnectionSunshineBasePort: "49000")
            .BuildServiceProvider();

        ExternalProcessStreamingOptions options = provider.GetRequiredService<ExternalProcessStreamingOptions>();
        Assert.Equal("10.0.0.20", options.SunshineProfile?.Host);
        Assert.Equal(49000, options.SunshineProfile?.BasePort);
    }

    [Fact]
    public void ExternalProcessManifestPathUsesConfiguration()
    {
        using ServiceProvider provider = BuildProvider(
            new KeyValuePair<string, string?>(BeaconServiceRegistration.StreamingBackendConfigurationKey, "external-process"),
            new KeyValuePair<string, string?>(BeaconServiceRegistration.ExternalStreamingExecutableConfigurationKey, "C:\\Tools\\sunshine-wrapper.exe"),
            new KeyValuePair<string, string?>(BeaconServiceRegistration.ExternalStreamingManifestConfigurationKey, "C:\\Tools\\beacon-streaming.json"));

        ExternalProcessStreamingOptions options = provider.GetRequiredService<ExternalProcessStreamingOptions>();

        Assert.Equal("C:\\Tools\\beacon-streaming.json", options.ManifestPath);
        Assert.IsType<WindowsExternalStreamingManifestReader>(provider.GetRequiredService<IExternalStreamingManifestReader>());
    }

    [Fact]
    public void ExternalProcessWrapperChildOptionsUseConfiguration()
    {
        using ServiceProvider provider = BuildProvider(
            new KeyValuePair<string, string?>(BeaconServiceRegistration.StreamingBackendConfigurationKey, "external-process"),
            new KeyValuePair<string, string?>(BeaconServiceRegistration.ExternalStreamingExecutableConfigurationKey, "C:\\Tools\\beacon-streaming-probe.exe"),
            new KeyValuePair<string, string?>(BeaconServiceRegistration.ExternalStreamingWrapperChildExecutableConfigurationKey, "C:\\Tools\\sunshine.exe"),
            new KeyValuePair<string, string?>(BeaconServiceRegistration.ExternalStreamingWrapperChildArgumentsConfigurationKey, "--config sunshine.json"));

        ExternalProcessStreamingOptions options = provider.GetRequiredService<ExternalProcessStreamingOptions>();

        Assert.Equal("C:\\Tools\\sunshine.exe", options.WrapperChildExecutablePath);
        Assert.Equal("--config sunshine.json", options.WrapperChildArguments);
    }

    [Fact]
    public void ExternalProcessArgumentTemplateUsesConfiguration()
    {
        using ServiceProvider provider = BuildProvider(
            new KeyValuePair<string, string?>(BeaconServiceRegistration.StreamingBackendConfigurationKey, "external-process"),
            new KeyValuePair<string, string?>(BeaconServiceRegistration.ExternalStreamingExecutableConfigurationKey, "C:\\Tools\\beacon-streaming-probe.exe"),
            new KeyValuePair<string, string?>(BeaconServiceRegistration.ExternalStreamingArgumentTemplateConfigurationKey, "--session {sessionId} --display {displayId}"));

        ExternalProcessStreamingOptions options = provider.GetRequiredService<ExternalProcessStreamingOptions>();

        Assert.Equal("--session {sessionId} --display {displayId}", options.ArgumentTemplate);
    }

    [Fact]
    public void ExternalProcessWrapperChildOptionsUseEnvironmentOverrides()
    {
        IConfiguration configuration = CreateConfiguration(
            new KeyValuePair<string, string?>(BeaconServiceRegistration.StreamingBackendConfigurationKey, "external-process"),
            new KeyValuePair<string, string?>(BeaconServiceRegistration.ExternalStreamingExecutableConfigurationKey, "C:\\Tools\\beacon-streaming-probe.exe"),
            new KeyValuePair<string, string?>(BeaconServiceRegistration.ExternalStreamingWrapperChildExecutableConfigurationKey, "C:\\Tools\\configured-sunshine.exe"),
            new KeyValuePair<string, string?>(BeaconServiceRegistration.ExternalStreamingWrapperChildArgumentsConfigurationKey, "--configured"));

        using ServiceProvider provider = new ServiceCollection()
            .AddBeaconServices(
                configuration,
                environmentHostMode: null,
                environmentExternalStreamingWrapperChildExecutable: "C:\\Tools\\env-sunshine.exe",
                environmentExternalStreamingWrapperChildArguments: "--env")
            .BuildServiceProvider();

        ExternalProcessStreamingOptions options = provider.GetRequiredService<ExternalProcessStreamingOptions>();

        Assert.Equal("C:\\Tools\\env-sunshine.exe", options.WrapperChildExecutablePath);
        Assert.Equal("--env", options.WrapperChildArguments);
    }

    [Fact]
    public void ExternalProcessArgumentTemplateUsesEnvironmentOverride()
    {
        IConfiguration configuration = CreateConfiguration(
            new KeyValuePair<string, string?>(BeaconServiceRegistration.StreamingBackendConfigurationKey, "external-process"),
            new KeyValuePair<string, string?>(BeaconServiceRegistration.ExternalStreamingExecutableConfigurationKey, "C:\\Tools\\beacon-streaming-probe.exe"),
            new KeyValuePair<string, string?>(BeaconServiceRegistration.ExternalStreamingArgumentTemplateConfigurationKey, "--configured {sessionId}"));

        using ServiceProvider provider = new ServiceCollection()
            .AddBeaconServices(
                configuration,
                environmentHostMode: null,
                environmentExternalStreamingArgumentTemplate: "--env {displayId}")
            .BuildServiceProvider();

        ExternalProcessStreamingOptions options = provider.GetRequiredService<ExternalProcessStreamingOptions>();

        Assert.Equal("--env {displayId}", options.ArgumentTemplate);
    }

    [Fact]
    public void ClientProfilesPathUsesFileRepositoryAndPairingToken()
    {
        string profilePath = Path.Combine(Path.GetTempPath(), $"beacon-profiles-{Guid.NewGuid():N}.json");
        using ServiceProvider provider = BuildProvider(
            new KeyValuePair<string, string?>(BeaconServiceRegistration.ClientProfilesPathConfigurationKey, profilePath),
            new KeyValuePair<string, string?>(BeaconServiceRegistration.PairingTokenConfigurationKey, "pair-me"));

        IClientProfileRepository repository = provider.GetRequiredService<IClientProfileRepository>();
        ClientPairingOptions pairing = provider.GetRequiredService<ClientPairingOptions>();

        Assert.IsType<FileClientProfileRepository>(repository);
        Assert.Equal(profilePath, repository.Location);
        Assert.True(pairing.Enabled);
        Assert.True(pairing.Allows("pair-me"));
        Assert.False(pairing.Allows("wrong"));
    }

    [Fact]
    public void EnvironmentHostModeOverridesConfiguration()
    {
        IConfiguration configuration = CreateConfiguration(new KeyValuePair<string, string?>(
            BeaconServiceRegistration.HostModeConfigurationKey,
            "fake"));

        BeaconHostMode mode = BeaconServiceRegistration.ResolveHostMode(configuration, "windows");

        Assert.Equal(BeaconHostMode.Windows, mode);
    }

    [Fact]
    public void EnvironmentStreamingModeOverridesConfiguration()
    {
        IConfiguration configuration = CreateConfiguration(new KeyValuePair<string, string?>(
            BeaconServiceRegistration.StreamingBackendConfigurationKey,
            "fake"));

        BeaconStreamingBackendMode mode = BeaconServiceRegistration.ResolveStreamingBackendMode(configuration, "external-process");

        Assert.Equal(BeaconStreamingBackendMode.ExternalProcess, mode);
    }

    [Fact]
    public void EnvironmentProfilePathAndPairingTokenOverrideConfiguration()
    {
        IConfiguration configuration = CreateConfiguration(
            new KeyValuePair<string, string?>(BeaconServiceRegistration.ClientProfilesPathConfigurationKey, "config.json"),
            new KeyValuePair<string, string?>(BeaconServiceRegistration.PairingTokenConfigurationKey, "config-token"));

        Assert.Equal("env.json", BeaconServiceRegistration.ResolveClientProfilesPath(configuration, "env.json"));
        Assert.Equal("env-token", BeaconServiceRegistration.ResolvePairingToken(configuration, "env-token"));
    }

    [Fact]
    public void UnknownHostModeFailsWithClearConfigurationError()
    {
        IConfiguration configuration = CreateConfiguration(new KeyValuePair<string, string?>(
            BeaconServiceRegistration.HostModeConfigurationKey,
            "broken"));
        var services = new ServiceCollection();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddBeaconServices(configuration, environmentHostMode: null));

        Assert.Contains("Unsupported Beacon host mode 'broken'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("fake, windows", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownStreamingModeFailsWithClearConfigurationError()
    {
        IConfiguration configuration = CreateConfiguration(new KeyValuePair<string, string?>(
            BeaconServiceRegistration.StreamingBackendConfigurationKey,
            "broken"));
        var services = new ServiceCollection();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddBeaconServices(configuration, environmentHostMode: null));

        Assert.Contains("Unsupported Beacon streaming backend 'broken'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("fake, external-process", exception.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider BuildProvider(params KeyValuePair<string, string?>[] values)
    {
        var services = new ServiceCollection();
        services.AddBeaconServices(CreateConfiguration(values), environmentHostMode: null);
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    private static IConfiguration CreateConfiguration(params KeyValuePair<string, string?>[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
}

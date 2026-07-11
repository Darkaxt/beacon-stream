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
using Beacon.Server.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Beacon.Server.Tests;

public sealed class BeaconServiceRegistrationTests
{
    [Fact]
    public void DefaultRegistrationUsesFakeHostAndFakeStreaming()
    {
        using ServiceProvider provider = BuildProvider();

        BeaconHostOptions options = provider.GetRequiredService<BeaconHostOptions>();

        Assert.Equal(BeaconHostMode.Fake, options.Mode);
        Assert.Equal("fake", options.ModeName);
        Assert.Equal(nameof(FakeStreamingBackend), options.StreamingBackendName);
        Assert.IsType<FakeDisplayBackend>(provider.GetRequiredService<IDisplayBackend>());
        Assert.IsType<FakeGameLauncher>(provider.GetRequiredService<IGameLauncher>());
        Assert.IsType<FakeStreamingBackend>(provider.GetRequiredService<IStreamingBackend>());
        Assert.Single(provider.GetServices<IStreamingBackend>());
        Assert.IsType<FakeSessionActivityInspector>(provider.GetRequiredService<ISessionActivityInspector>());
        Assert.IsType<NoOpClientInputSink>(provider.GetRequiredService<IClientInputSink>());
        ClientInputHealth inputHealth = provider.GetRequiredService<IClientInputHealthProvider>().GetHealth();
        Assert.Equal("no-op", inputHealth.Backend);
        Assert.Contains("pointer", inputHealth.SupportedEventTypes);
        Assert.Contains("keyboard", inputHealth.SupportedEventTypes);
        Assert.Contains("press", inputHealth.SupportedKeyboardActions);
        Assert.IsType<InMemoryClientProfileRepository>(provider.GetRequiredService<IClientProfileRepository>());
        Assert.IsType<ClientCredentialService>(provider.GetRequiredService<ClientCredentialService>());
    }

    [Fact]
    public async Task WindowsRegistrationUsesWindowsHostAndWorkerStreaming()
    {
        await using ServiceProvider provider = BuildProvider(new KeyValuePair<string, string?>(
            BeaconServiceRegistration.HostModeConfigurationKey,
            "windows"));

        BeaconHostOptions options = provider.GetRequiredService<BeaconHostOptions>();

        Assert.Equal(BeaconHostMode.Windows, options.Mode);
        Assert.Equal("windows", options.ModeName);
        Assert.Equal(nameof(StreamWorkerStreamingBackend), options.StreamingBackendName);
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
        Assert.IsType<StreamWorkerStreamingBackend>(provider.GetRequiredService<IStreamingBackend>());
        Assert.Same(
            provider.GetRequiredService<StreamWorkerProcessHost>(),
            provider.GetRequiredService<IStreamWorkerHost>());
        Assert.Single(provider.GetServices<IStreamingBackend>());
    }

    [Fact]
    public async Task FakeHostCanUseWorkerStreamingWithoutWindowsSideEffects()
    {
        string workerPath = Path.Combine(Path.GetTempPath(), "acceptance", "Beacon.StreamWorker.exe");
        await using ServiceProvider provider = BuildProvider(
            new KeyValuePair<string, string?>(
                BeaconServiceRegistration.HostModeConfigurationKey,
                "fake"),
            new KeyValuePair<string, string?>(
                BeaconServiceRegistration.StreamingModeConfigurationKey,
                "worker"),
            new KeyValuePair<string, string?>(
                BeaconServiceRegistration.StreamWorkerPathConfigurationKey,
                workerPath));

        BeaconHostOptions options = provider.GetRequiredService<BeaconHostOptions>();

        Assert.Equal(BeaconHostMode.Fake, options.Mode);
        Assert.Equal(nameof(StreamWorkerStreamingBackend), options.StreamingBackendName);
        Assert.IsType<FakeDisplayBackend>(provider.GetRequiredService<IDisplayBackend>());
        Assert.IsType<FakeRecoveryBackend>(provider.GetRequiredService<IRecoveryBackend>());
        Assert.IsType<FakeGameLauncher>(provider.GetRequiredService<IGameLauncher>());
        Assert.IsType<FakeSessionActivityInspector>(provider.GetRequiredService<ISessionActivityInspector>());
        Assert.IsType<NoOpClientInputSink>(provider.GetRequiredService<IClientInputSink>());
        Assert.IsType<StreamWorkerStreamingBackend>(provider.GetRequiredService<IStreamingBackend>());
        Assert.Equal(
            workerPath,
            provider.GetRequiredService<StreamWorkerProcessHostOptions>().ExecutablePath);
        Assert.Single(provider.GetServices<IStreamingBackend>());
    }

    [Fact]
    public void ClientProfilesPathUsesFileRepository()
    {
        string profilePath = Path.Combine(Path.GetTempPath(), $"beacon-profiles-{Guid.NewGuid():N}.json");
        using ServiceProvider provider = BuildProvider(
            new KeyValuePair<string, string?>(BeaconServiceRegistration.ClientProfilesPathConfigurationKey, profilePath));

        IClientProfileRepository repository = provider.GetRequiredService<IClientProfileRepository>();

        Assert.IsType<FileClientProfileRepository>(repository);
        Assert.Equal(profilePath, repository.Location);
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
    public void EnvironmentProfilePathOverridesConfiguration()
    {
        IConfiguration configuration = CreateConfiguration(
            new KeyValuePair<string, string?>(BeaconServiceRegistration.ClientProfilesPathConfigurationKey, "config.json"));

        Assert.Equal("env.json", BeaconServiceRegistration.ResolveClientProfilesPath(configuration, "env.json"));
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
            BeaconServiceRegistration.StreamingModeConfigurationKey,
            "broken"));
        var services = new ServiceCollection();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddBeaconServices(configuration, environmentHostMode: null));

        Assert.Contains("Unsupported Beacon streaming mode 'broken'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("fake, worker", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfiguredWorkerPathIsUsed()
    {
        string workerPath = Path.Combine(Path.GetTempPath(), "configured", "Beacon.StreamWorker.exe");
        await using ServiceProvider provider = BuildProvider(
            new KeyValuePair<string, string?>(
                BeaconServiceRegistration.StreamingModeConfigurationKey,
                "worker"),
            new KeyValuePair<string, string?>(
                BeaconServiceRegistration.StreamWorkerPathConfigurationKey,
                workerPath));

        Assert.Equal(
            workerPath,
            provider.GetRequiredService<StreamWorkerProcessHostOptions>().ExecutablePath);
    }

    [Fact]
    public async Task EnvironmentWorkerPathOverridesConfiguration()
    {
        string configuredPath = Path.Combine(Path.GetTempPath(), "configured", "Beacon.StreamWorker.exe");
        string environmentPath = Path.Combine(Path.GetTempPath(), "environment", "Beacon.StreamWorker.exe");
        var services = new ServiceCollection();
        services.AddBeaconServices(
            CreateConfiguration(
                new KeyValuePair<string, string?>(
                    BeaconServiceRegistration.StreamingModeConfigurationKey,
                    "worker"),
                new KeyValuePair<string, string?>(
                    BeaconServiceRegistration.StreamWorkerPathConfigurationKey,
                    configuredPath)),
            environmentHostMode: null,
            environmentStreamWorkerPath: environmentPath);
        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        Assert.Equal(
            environmentPath,
            provider.GetRequiredService<StreamWorkerProcessHostOptions>().ExecutablePath);
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

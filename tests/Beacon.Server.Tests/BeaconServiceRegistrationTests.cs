using Beacon.Core.Displays;
using Beacon.Core.Games;
using Beacon.Core.Recovery;
using Beacon.Core.Sessions;
using Beacon.Core.Streaming;
using Beacon.Platform.Windows.Displays;
using Beacon.Platform.Windows.Games;
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
        Assert.Equal(
            "C:\\Tools\\beacon-stream-wrapper.exe",
            provider.GetRequiredService<ExternalProcessStreamingOptions>().ExecutablePath);
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

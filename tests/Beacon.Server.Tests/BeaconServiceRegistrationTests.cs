using Beacon.Core.Displays;
using Beacon.Core.Games;
using Beacon.Core.Sessions;
using Beacon.Core.Streaming;
using Beacon.Platform.Windows.Displays;
using Beacon.Platform.Windows.Games;
using Beacon.Platform.Windows.Sessions;
using Beacon.Server.Hosting;
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
        Assert.Equal("fake", options.ModeName);
        Assert.IsType<FakeDisplayBackend>(provider.GetRequiredService<IDisplayBackend>());
        Assert.IsType<FakeGameLauncher>(provider.GetRequiredService<IGameLauncher>());
        Assert.IsType<FakeStreamingBackend>(provider.GetRequiredService<IStreamingBackend>());
        Assert.IsType<FakeSessionActivityInspector>(provider.GetRequiredService<ISessionActivityInspector>());
    }

    [Fact]
    public void WindowsRegistrationUsesWindowsHostBoundaries()
    {
        using ServiceProvider provider = BuildProvider(new KeyValuePair<string, string?>(
            BeaconServiceRegistration.HostModeConfigurationKey,
            "windows"));

        BeaconHostOptions options = provider.GetRequiredService<BeaconHostOptions>();

        Assert.Equal(BeaconHostMode.Windows, options.Mode);
        Assert.Equal("windows", options.ModeName);
        Assert.IsType<WindowsDisplayApi>(provider.GetRequiredService<IWindowsDisplayApi>());
        Assert.IsType<WindowsDisplayBackend>(provider.GetRequiredService<IDisplayBackend>());
        Assert.IsType<WindowsGameLauncher>(provider.GetRequiredService<IGameLauncher>());
        Assert.IsType<WindowsSessionActivityApi>(provider.GetRequiredService<IWindowsSessionActivityApi>());
        Assert.IsType<WindowsSessionActivityInspector>(provider.GetRequiredService<ISessionActivityInspector>());
        Assert.IsType<FakeStreamingBackend>(provider.GetRequiredService<IStreamingBackend>());
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

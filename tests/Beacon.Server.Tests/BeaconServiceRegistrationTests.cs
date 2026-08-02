using System.Reflection;
using Beacon.Core.Benchmarks;
using Beacon.Core.Displays;
using Beacon.Core.Games;
using Beacon.Core.Games.Heroic;
using Beacon.Core.Games.Hydra;
using Beacon.Core.Games.Manual;
using Beacon.Core.Games.Steam;
using Beacon.Core.Input;
using Beacon.Core.Recovery;
using Beacon.Core.Sessions;
using Beacon.Core.Streaming;
using Beacon.Platform.Windows.Displays;
using Beacon.Platform.Windows.Games;
using Beacon.Platform.Windows.HostAgent;
using Beacon.Platform.Windows.Input;
using Beacon.Platform.Windows.Recovery;
using Beacon.Platform.Windows.Sessions;
using Beacon.Platform.Windows.Streaming;
using Beacon.Server.Hosting;
using Beacon.Server.Security;
using Beacon.Server.State;
using Beacon.Server.Streaming;
using Beacon.Server.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Beacon.Server.Tests;

[Collection(BeaconServiceRegistrationEnvironmentCollection.Name)]
public sealed class BeaconServiceRegistrationTests
{
    [Fact]
    public void WindowsHostBoundaryDefersPlatformAccessUntilResolution()
    {
        var services = new ServiceCollection();

        services.AddBeaconServices(
            CreateConfiguration(),
            environmentClientProfilesPath: null,
            environmentStreamWorkerPath: null,
            environmentBenchmarkEvidencePath: null);

        ServiceDescriptor connection = Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(HostAgentConnection));
        Assert.Null(connection.ImplementationInstance);
        Assert.NotNull(connection.ImplementationFactory);
    }

    [Fact]
    public async Task ProductionRegistrationUsesWindowsHostAndWorkerStreaming()
    {
        await using ServiceProvider provider = BuildProvider();

        BeaconHostOptions options = provider.GetRequiredService<BeaconHostOptions>();

        Assert.Equal("windows", options.ModeName);
        Assert.Equal(nameof(StreamWorkerStreamingBackend), options.StreamingBackendName);
        HostAgentWindowsDisplayApi displayApi = Assert.IsType<HostAgentWindowsDisplayApi>(
            provider.GetRequiredService<IWindowsDisplayApi>());
        Assert.Same(displayApi, provider.GetRequiredService<IWindowsDisplayLeaseSession>());
        Assert.Same(displayApi, provider.GetRequiredService<IWindowsDisplayNameResolver>());
        Assert.IsType<HostAgentConnection>(provider.GetRequiredService<IHostAgentConnection>());
        Assert.IsType<HostAgentDriverUpdateClient>(
            provider.GetRequiredService<IHostAgentDriverUpdateClient>());
        Assert.IsType<WindowsDisplayBackend>(provider.GetRequiredService<IDisplayBackend>());
        Assert.IsType<WindowsRecoveryApi>(provider.GetRequiredService<IWindowsRecoveryApi>());
        Assert.IsType<WindowsRecoveryBackend>(provider.GetRequiredService<IRecoveryBackend>());
        Assert.IsType<WindowsGameLauncher>(provider.GetRequiredService<IGameLauncher>());
        Assert.IsType<WindowsSessionActivityApi>(provider.GetRequiredService<IWindowsSessionActivityApi>());
        Assert.IsType<WindowsSessionActivityInspector>(provider.GetRequiredService<ISessionActivityInspector>());
        Assert.IsType<WindowsSessionOwnedWorkTerminator>(provider.GetRequiredService<ISessionOwnedWorkTerminator>());
        Assert.IsType<WindowsInputApi>(provider.GetRequiredService<IWindowsInputApi>());
        WindowsClientInputSink input = Assert.IsType<WindowsClientInputSink>(
            provider.GetRequiredService<IClientInputSink>());
        Assert.Same(input, provider.GetRequiredService<IClientInputHealthProvider>());
        Assert.Same(input, provider.GetRequiredService<IClientInputSessionLifecycle>());
        Assert.IsType<StreamWorkerStreamingBackend>(provider.GetRequiredService<IStreamingBackend>());
        Assert.Same(
            provider.GetRequiredService<StreamWorkerProcessHost>(),
            provider.GetRequiredService<IStreamWorkerHost>());
        Assert.Single(provider.GetServices<IStreamingBackend>());
        Assert.Contains(provider.GetServices<IHostedService>(), service => service is StreamWorkerEventRelay);
        Assert.Contains(
            provider.GetServices<IHostedService>(),
            service => service is HostAgentConnectionHostedService);
        Assert.Same(
            provider.GetRequiredService<StreamWorkerStreamingBackend>(),
            provider.GetRequiredService<IStreamWorkerRuntimeEvents>());
        Assert.IsType<InMemoryClientProfileRepository>(provider.GetRequiredService<IClientProfileRepository>());
        Assert.IsType<ClientCredentialService>(provider.GetRequiredService<ClientCredentialService>());
        Assert.NotNull(provider.GetRequiredService<StreamSessionLaunchService>());
        Assert.NotNull(provider.GetRequiredService<StreamSessionReconnectService>());
        IGameLibraryProvider[] games = provider.GetServices<IGameLibraryProvider>().ToArray();
        Assert.Contains(games, gameProvider => gameProvider is SteamGameLibraryProvider);
        Assert.Contains(games, gameProvider => gameProvider is HeroicGameLibraryProvider);
        Assert.Contains(games, gameProvider => gameProvider is HydraGameLibraryProvider);
        Assert.Contains(games, gameProvider => gameProvider is ManualGameLibraryProvider);
        Assert.DoesNotContain(games, gameProvider => gameProvider is StaticGameLibraryProvider);
    }

    [Fact]
    public void FakeRuntimeCompositionIsOwnedByTheTestHost()
    {
        var services = new ServiceCollection();
        services.AddBeaconServices(
            CreateConfiguration(),
            environmentClientProfilesPath: null,
            environmentStreamWorkerPath: null,
            environmentBenchmarkEvidencePath: null);
        services.UseBeaconFakeRuntime();
        using ServiceProvider provider = BuildServiceProvider(services);

        BeaconHostOptions options = provider.GetRequiredService<BeaconHostOptions>();

        Assert.Equal("fake", options.ModeName);
        Assert.Equal(nameof(FakeStreamingBackend), options.StreamingBackendName);
        Assert.IsType<FakeDisplayBackend>(provider.GetRequiredService<IDisplayBackend>());
        Assert.IsType<FakeRecoveryBackend>(provider.GetRequiredService<IRecoveryBackend>());
        Assert.IsType<FakeGameLauncher>(provider.GetRequiredService<IGameLauncher>());
        Assert.IsType<FakeSessionActivityInspector>(provider.GetRequiredService<ISessionActivityInspector>());
        Assert.IsType<FakeSessionOwnedWorkTerminator>(provider.GetRequiredService<ISessionOwnedWorkTerminator>());
        NoOpClientInputSink input = Assert.IsType<NoOpClientInputSink>(
            provider.GetRequiredService<IClientInputSink>());
        Assert.Same(input, provider.GetRequiredService<IClientInputHealthProvider>());
        Assert.Same(input, provider.GetRequiredService<IClientInputSessionLifecycle>());
        Assert.IsType<FakeStreamingBackend>(provider.GetRequiredService<IStreamingBackend>());
        Assert.IsType<FakeBenchmarkRuntime>(provider.GetRequiredService<IBenchmarkRuntime>());
        Assert.Empty(provider.GetServices<IStreamWorkerHost>());
        Assert.Empty(provider.GetServices<IStreamWorkerRuntimeEvents>());
        Assert.DoesNotContain(provider.GetServices<IHostedService>(), service => service is StreamWorkerEventRelay);
        Assert.DoesNotContain(
            provider.GetServices<IHostedService>(),
            service => service is HostAgentConnectionHostedService);
    }

    [Fact]
    public async Task TestHostCanKeepProductionWorkerWithFakeMachineBoundaries()
    {
        IConfiguration configuration = CreateConfiguration(
            new KeyValuePair<string, string?>(
                BeaconTestRuntimeServices.ProductionStreamWorkerConfigurationKey,
                bool.TrueString),
            new KeyValuePair<string, string?>(
                BeaconServiceRegistration.StreamWorkerPathConfigurationKey,
                Path.Combine(Path.GetTempPath(), "Beacon.StreamWorker.exe")));
        var services = new ServiceCollection();
        services.AddBeaconServices(
            configuration,
            environmentClientProfilesPath: null,
            environmentStreamWorkerPath: null,
            environmentBenchmarkEvidencePath: null);

        services.UseBeaconFakeRuntime(configuration);
        await using ServiceProvider provider = BuildServiceProvider(services);

        BeaconHostOptions options = provider.GetRequiredService<BeaconHostOptions>();
        Assert.Equal("fake-worker", options.ModeName);
        Assert.Equal(nameof(StreamWorkerStreamingBackend), options.StreamingBackendName);
        Assert.IsType<FakeDisplayBackend>(provider.GetRequiredService<IDisplayBackend>());
        Assert.IsType<FakeGameLauncher>(provider.GetRequiredService<IGameLauncher>());
        Assert.IsType<StreamWorkerProcessHost>(provider.GetRequiredService<IStreamWorkerHost>());
        Assert.IsType<StreamWorkerStreamingBackend>(provider.GetRequiredService<IStreamingBackend>());
        Assert.Same(
            provider.GetRequiredService<IStreamingBackend>(),
            provider.GetRequiredService<IBenchmarkRuntime>());
        Assert.Same(
            provider.GetRequiredService<IStreamingBackend>(),
            provider.GetRequiredService<IStreamWorkerRuntimeEvents>());
        Assert.Contains(provider.GetServices<IHostedService>(), service => service is StreamWorkerEventRelay);
        Assert.Single(provider.GetServices<IStreamingBackend>());
    }

    [Fact]
    public async Task HostedWorkerConfigurationReplacesOnlyBenchmarkRuntimeAndAuthorization()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"beacon-hosted-worker-registration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string identityPath = Path.Combine(directory, "identity.pfx");
        IConfiguration configuration = CreateConfiguration(
            new KeyValuePair<string, string?>(
                HostedBenchmarkWorkerOptions.ExecutablePathConfigurationKey,
                typeof(TestHostProgram).Assembly.Location),
            new KeyValuePair<string, string?>(
                BeaconServiceRegistration.SecurityIdentityPathConfigurationKey,
                identityPath),
            new KeyValuePair<string, string?>(
                BeaconServiceRegistration.SecurityTestHostConfigurationKey,
                bool.TrueString));

        try
        {
            var services = new ServiceCollection();
            services.AddBeaconServices(
                configuration,
                environmentClientProfilesPath: null,
                environmentStreamWorkerPath: null,
                environmentBenchmarkEvidencePath: null);
            services.UseBeaconFakeRuntime(configuration);
            await using ServiceProvider provider = BuildServiceProvider(services);

            Assert.IsType<FakeStreamingBackend>(provider.GetRequiredService<IStreamingBackend>());
            Assert.IsType<HostedBenchmarkWorkerProcessHost>(provider.GetRequiredService<IStreamWorkerHost>());
            Assert.IsType<StreamWorkerStreamingBackend>(provider.GetRequiredService<IBenchmarkRuntime>());
            Assert.IsType<StreamWorkerSessionAuthorizer>(provider.GetRequiredService<IStreamSessionAuthorizer>());
            Assert.Same(
                provider.GetRequiredService<IBenchmarkRuntime>(),
                provider.GetRequiredService<IStreamWorkerRuntimeEvents>());
            Assert.Contains(provider.GetServices<IHostedService>(), service => service is StreamWorkerEventRelay);
            Assert.Single(provider.GetServices<IStreamingBackend>());
            Assert.IsType<HostedBenchmarkWorkerOptions>(
                provider.GetRequiredService<HostedBenchmarkWorkerOptions>());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LegacyModeConfigurationCannotReplaceProductionBoundaries()
    {
        await using ServiceProvider provider = BuildProvider(
            new KeyValuePair<string, string?>("Beacon:HostMode", "fake"),
            new KeyValuePair<string, string?>("Beacon:StreamingMode", "fake"));

        Assert.Equal("windows", provider.GetRequiredService<BeaconHostOptions>().ModeName);
        Assert.IsType<WindowsDisplayBackend>(provider.GetRequiredService<IDisplayBackend>());
        Assert.IsType<StreamWorkerStreamingBackend>(provider.GetRequiredService<IStreamingBackend>());
        Assert.Single(provider.GetServices<IStreamingBackend>());
    }

    [Fact]
    public void RegistrationExposesOnlyWorkerPathStreamingConfiguration()
    {
        string[] values = typeof(BeaconServiceRegistration)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => Assert.IsType<string>(field.GetRawConstantValue()))
            .ToArray();
        string[] streamingValues = values
            .Where(value =>
                value.StartsWith("Beacon:Streaming", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("BEACON_STREAMING", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("BEACON_STREAM_WORKER", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] expectedStreamingValues =
        [
            BeaconServiceRegistration.StreamWorkerPathConfigurationKey,
            BeaconServiceRegistration.StreamWorkerPathEnvironmentVariable
        ];

        Assert.Equal(
            expectedStreamingValues.Order(StringComparer.Ordinal),
            streamingValues);
        Assert.DoesNotContain(values, value => value.Contains("HostMode", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(values, value => value.Contains("StreamingMode", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(values, value => value.Contains("Pairing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ControlledCompositionRequiresAllEnvironmentPathArguments()
    {
        MethodInfo? compositionOverload = typeof(BeaconServiceRegistration).GetMethod(
            nameof(BeaconServiceRegistration.AddBeaconServices),
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            [
                typeof(IServiceCollection),
                typeof(IConfiguration),
                typeof(string),
                typeof(string),
                typeof(string)
            ],
            modifiers: null);

        Assert.NotNull(compositionOverload);
        Assert.All(
            compositionOverload.GetParameters().Skip(2),
            parameter => Assert.False(parameter.HasDefaultValue));
    }

    [Fact]
    public void ControlledCompositionIgnoresAmbientBenchmarkPathUnlessExplicitlySupplied()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"beacon-ambient-benchmarks-{Guid.NewGuid():N}");
        string ambientPath = Path.Combine(directory, "benchmark-evidence.json");
        string? previousPath = Environment.GetEnvironmentVariable(
            BeaconServiceRegistration.BenchmarkEvidencePathEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(
                BeaconServiceRegistration.BenchmarkEvidencePathEnvironmentVariable,
                ambientPath);
            var controlledServices = new ServiceCollection();
            controlledServices.AddBeaconServices(
                CreateConfiguration(),
                environmentClientProfilesPath: null,
                environmentStreamWorkerPath: null,
                environmentBenchmarkEvidencePath: null);
            using ServiceProvider controlledProvider = BuildServiceProvider(controlledServices);

            Assert.IsType<InMemoryBenchmarkEvidenceRepository>(
                controlledProvider.GetRequiredService<IBenchmarkEvidenceRepository>());

            var explicitServices = new ServiceCollection();
            explicitServices.AddBeaconServices(
                CreateConfiguration(),
                environmentClientProfilesPath: null,
                environmentStreamWorkerPath: null,
                environmentBenchmarkEvidencePath: ambientPath);
            using ServiceProvider explicitProvider = BuildServiceProvider(explicitServices);
            IBenchmarkEvidenceRepository explicitRepository =
                explicitProvider.GetRequiredService<IBenchmarkEvidenceRepository>();

            Assert.IsType<FileBenchmarkEvidenceRepository>(explicitRepository);
            Assert.Equal(ambientPath, explicitRepository.Location);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                BeaconServiceRegistration.BenchmarkEvidencePathEnvironmentVariable,
                previousPath);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void ClientProfilesPathUsesFileRepository()
    {
        string profilePath = Path.Combine(Path.GetTempPath(), $"beacon-profiles-{Guid.NewGuid():N}.json");
        using ServiceProvider provider = BuildProvider(
            new KeyValuePair<string, string?>(
                BeaconServiceRegistration.ClientProfilesPathConfigurationKey,
                profilePath));

        IClientProfileRepository repository = provider.GetRequiredService<IClientProfileRepository>();

        Assert.IsType<FileClientProfileRepository>(repository);
        Assert.Equal(profilePath, repository.Location);
    }

    [Fact]
    public void BenchmarkEvidencePathUsesIndependentFileRepository()
    {
        string evidencePath = Path.Combine(Path.GetTempPath(), $"beacon-benchmarks-{Guid.NewGuid():N}.json");
        using ServiceProvider provider = BuildProvider(
            new KeyValuePair<string, string?>(
                BeaconServiceRegistration.BenchmarkEvidencePathConfigurationKey,
                evidencePath));

        IBenchmarkEvidenceRepository repository = provider.GetRequiredService<IBenchmarkEvidenceRepository>();

        Assert.IsType<FileBenchmarkEvidenceRepository>(repository);
        Assert.Equal(evidencePath, repository.Location);
    }

    [Fact]
    public void ProductionEvidenceStartsEmptyAndTestHostSeedsMeasuredEvidence()
    {
        using ServiceProvider production = BuildProvider();
        Assert.Empty(production.GetRequiredService<IBenchmarkEvidenceRepository>().LoadEvidence());

        var services = new ServiceCollection();
        services.AddBeaconServices(
            CreateConfiguration(),
            environmentClientProfilesPath: null,
            environmentStreamWorkerPath: null,
            environmentBenchmarkEvidencePath: null);
        services.UseBeaconFakeRuntime();
        using ServiceProvider testHost = BuildServiceProvider(services);
        BenchmarkEvidence seeded = Assert.Single(
            testHost.GetRequiredService<IBenchmarkEvidenceRepository>().LoadEvidence());

        Assert.Equal("z-fold-7", seeded.ClientId.Value);
        Assert.NotNull(seeded.CompletedAt);
        Assert.NotNull(seeded.SelectedResult);
    }

    [Fact]
    public void EnvironmentProfilePathOverridesConfiguration()
    {
        IConfiguration configuration = CreateConfiguration(
            new KeyValuePair<string, string?>(
                BeaconServiceRegistration.ClientProfilesPathConfigurationKey,
                "config.json"));

        Assert.Equal("env.json", BeaconServiceRegistration.ResolveClientProfilesPath(configuration, "env.json"));
    }

    [Fact]
    public async Task ConfiguredWorkerPathIsUsed()
    {
        string workerPath = Path.Combine(Path.GetTempPath(), "configured", "Beacon.StreamWorker.exe");
        await using ServiceProvider provider = BuildProvider(
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
            CreateConfiguration(new KeyValuePair<string, string?>(
                BeaconServiceRegistration.StreamWorkerPathConfigurationKey,
                configuredPath)),
            environmentClientProfilesPath: null,
            environmentStreamWorkerPath: environmentPath,
            environmentBenchmarkEvidencePath: null);
        await using ServiceProvider provider = BuildServiceProvider(services);

        Assert.Equal(
            environmentPath,
            provider.GetRequiredService<StreamWorkerProcessHostOptions>().ExecutablePath);
    }

    private static ServiceProvider BuildProvider(params KeyValuePair<string, string?>[] values)
    {
        var services = new ServiceCollection();
        services.AddBeaconServices(
            CreateConfiguration(values),
            environmentClientProfilesPath: null,
            environmentStreamWorkerPath: null,
            environmentBenchmarkEvidencePath: null);
        return BuildServiceProvider(services);
    }

    private static ServiceProvider BuildServiceProvider(IServiceCollection services) =>
        services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

    private static IConfiguration CreateConfiguration(params KeyValuePair<string, string?>[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BeaconServiceRegistrationEnvironmentCollection
{
    public const string Name = "Beacon service registration environment";
}

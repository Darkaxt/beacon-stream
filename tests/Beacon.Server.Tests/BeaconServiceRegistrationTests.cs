using System.Reflection;
using Beacon.Core.Benchmarks;
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
using Microsoft.Extensions.Hosting;
using Beacon.Server.Streaming;

namespace Beacon.Server.Tests;

[Collection(BeaconServiceRegistrationEnvironmentCollection.Name)]
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
        Assert.DoesNotContain(provider.GetServices<IHostedService>(), service => service is StreamWorkerEventRelay);
        Assert.Empty(provider.GetServices<IStreamWorkerRuntimeEvents>());
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
        Assert.Same(
            provider.GetRequiredService<StreamWorkerProcessHost>(),
            provider.GetRequiredService<IGenerationBoundStreamWorkerHost>());
        Assert.Single(provider.GetServices<IStreamingBackend>());
        Assert.Contains(provider.GetServices<IHostedService>(), service => service is StreamWorkerEventRelay);
        Assert.Same(
            provider.GetRequiredService<StreamWorkerStreamingBackend>(),
            provider.GetRequiredService<IStreamWorkerRuntimeEvents>());
    }

    [Fact]
    public void WindowsHostCanUseFakeStreamingWithoutWorkerServices()
    {
        using ServiceProvider provider = BuildProvider(
            new KeyValuePair<string, string?>(
                BeaconServiceRegistration.HostModeConfigurationKey,
                "windows"),
            new KeyValuePair<string, string?>(
                BeaconServiceRegistration.StreamingModeConfigurationKey,
                "fake"));

        BeaconHostOptions options = provider.GetRequiredService<BeaconHostOptions>();

        Assert.Equal(BeaconHostMode.Windows, options.Mode);
        Assert.Equal(nameof(FakeStreamingBackend), options.StreamingBackendName);
        Assert.IsType<WindowsDisplayBackend>(provider.GetRequiredService<IDisplayBackend>());
        Assert.IsType<WindowsRecoveryBackend>(provider.GetRequiredService<IRecoveryBackend>());
        Assert.IsType<WindowsGameLauncher>(provider.GetRequiredService<IGameLauncher>());
        Assert.IsType<WindowsSessionActivityInspector>(provider.GetRequiredService<ISessionActivityInspector>());
        Assert.IsType<WindowsClientInputSink>(provider.GetRequiredService<IClientInputSink>());
        Assert.IsType<FakeStreamingBackend>(provider.GetRequiredService<IStreamingBackend>());
        Assert.Single(provider.GetServices<IStreamingBackend>());
        Assert.Empty(provider.GetServices<IStreamWorkerHost>());
        Assert.Empty(provider.GetServices<IGenerationBoundStreamWorkerHost>());
        Assert.DoesNotContain(provider.GetServices<IHostedService>(), service => service is StreamWorkerEventRelay);
        Assert.Empty(provider.GetServices<StreamWorkerProcessHostOptions>());
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
        Assert.Contains(provider.GetServices<IHostedService>(), service => service is StreamWorkerEventRelay);
    }

    [Fact]
    public void RegistrationExposesOnlyApprovedStreamingConfigurationConstants()
    {
        string[] values = typeof(BeaconServiceRegistration)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => Assert.IsType<string>(field.GetRawConstantValue()))
            .ToArray();
        string[] approvedStreamingValues =
        [
            BeaconServiceRegistration.StreamingModeConfigurationKey,
            BeaconServiceRegistration.StreamWorkerPathConfigurationKey,
            BeaconServiceRegistration.StreamingModeEnvironmentVariable,
            BeaconServiceRegistration.StreamWorkerPathEnvironmentVariable
        ];
        string[] streamingValues = values
            .Where(value =>
                value.StartsWith("Beacon:Streaming", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("BEACON_STREAMING", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("BEACON_STREAM_WORKER", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("BEACON_EXTERNAL_STREAMING", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(approvedStreamingValues.Order(StringComparer.Ordinal), streamingValues);
        Assert.DoesNotContain(values, value =>
            value.Contains("Pairing", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(values, value =>
            value.StartsWith("Beacon:Streaming:ExternalProcess", StringComparison.OrdinalIgnoreCase)
            || value.Contains("ExternalWrapper", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("BEACON_EXTERNAL_STREAMING_", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RegistrationPreservesLegacyOverloadAndRequiresAllCompositionArguments()
    {
        MethodInfo? legacyOverload = typeof(BeaconServiceRegistration).GetMethod(
            nameof(BeaconServiceRegistration.AddBeaconServices),
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            [
                typeof(IServiceCollection),
                typeof(IConfiguration),
                typeof(string),
                typeof(string)
            ],
            modifiers: null);
        MethodInfo? compositionOverload = typeof(BeaconServiceRegistration).GetMethod(
            nameof(BeaconServiceRegistration.AddBeaconServices),
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            [
                typeof(IServiceCollection),
                typeof(IConfiguration),
                typeof(string),
                typeof(string),
                typeof(string),
                typeof(string),
                typeof(string)
            ],
            modifiers: null);

        Assert.NotNull(legacyOverload);
        MethodInfo legacy = legacyOverload;
        ParameterInfo legacyProfilesPath = legacy.GetParameters()[3];
        Assert.True(legacyProfilesPath.HasDefaultValue);
        Assert.Null(legacyProfilesPath.DefaultValue);

        Assert.NotNull(compositionOverload);
        MethodInfo composition = compositionOverload;
        Assert.All(composition.GetParameters(), parameter => Assert.False(parameter.HasDefaultValue));
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
                environmentHostMode: null,
                environmentClientProfilesPath: null,
                environmentStreamingMode: null,
                environmentStreamWorkerPath: null,
                environmentBenchmarkEvidencePath: null);
            using ServiceProvider controlledProvider = controlledServices.BuildServiceProvider();

            Assert.IsType<InMemoryBenchmarkEvidenceRepository>(
                controlledProvider.GetRequiredService<IBenchmarkEvidenceRepository>());

            var explicitServices = new ServiceCollection();
            explicitServices.AddBeaconServices(
                CreateConfiguration(),
                environmentHostMode: null,
                environmentClientProfilesPath: null,
                environmentStreamingMode: null,
                environmentStreamWorkerPath: null,
                environmentBenchmarkEvidencePath: ambientPath);
            using ServiceProvider explicitProvider = explicitServices.BuildServiceProvider();
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
            new KeyValuePair<string, string?>(BeaconServiceRegistration.ClientProfilesPathConfigurationKey, profilePath));

        IClientProfileRepository repository = provider.GetRequiredService<IClientProfileRepository>();

        Assert.IsType<FileClientProfileRepository>(repository);
        Assert.Equal(profilePath, repository.Location);
    }

    [Fact]
    public void BenchmarkEvidencePathUsesIndependentFileRepository()
    {
        string evidencePath = Path.Combine(Path.GetTempPath(), $"beacon-benchmarks-{Guid.NewGuid():N}.json");
        using ServiceProvider provider = BuildProvider(
            new KeyValuePair<string, string?>(BeaconServiceRegistration.BenchmarkEvidencePathConfigurationKey, evidencePath));

        IBenchmarkEvidenceRepository repository = provider.GetRequiredService<IBenchmarkEvidenceRepository>();

        Assert.IsType<FileBenchmarkEvidenceRepository>(repository);
        Assert.Equal(evidencePath, repository.Location);
    }

    [Fact]
    public void FakeHostSeedsMeasuredZFoldEvidenceWithoutChangingExplicitStore()
    {
        using ServiceProvider fakeProvider = BuildProvider();
        IBenchmarkEvidenceRepository fakeRepository = fakeProvider.GetRequiredService<IBenchmarkEvidenceRepository>();
        BenchmarkEvidence seeded = Assert.Single(fakeRepository.LoadEvidence());

        Assert.Equal("z-fold-7", seeded.ClientId.Value);
        Assert.NotNull(seeded.CompletedAt);
        Assert.NotNull(seeded.SelectedResult);

        string evidencePath = Path.Combine(Path.GetTempPath(), $"beacon-benchmarks-{Guid.NewGuid():N}.json");
        using ServiceProvider explicitProvider = BuildProvider(
            new KeyValuePair<string, string?>(BeaconServiceRegistration.BenchmarkEvidencePathConfigurationKey, evidencePath));
        IBenchmarkEvidenceRepository explicitRepository = explicitProvider.GetRequiredService<IBenchmarkEvidenceRepository>();

        Assert.Empty(explicitRepository.LoadEvidence());
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
        var services = new ServiceCollection();
        services.AddBeaconServices(
            CreateConfiguration(new KeyValuePair<string, string?>(
                BeaconServiceRegistration.StreamingModeConfigurationKey,
                "worker")),
            environmentHostMode: null,
            environmentClientProfilesPath: null,
            environmentStreamingMode: "fake",
            environmentStreamWorkerPath: null,
            environmentBenchmarkEvidencePath: null);
        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        Assert.IsType<FakeStreamingBackend>(provider.GetRequiredService<IStreamingBackend>());
        Assert.Empty(provider.GetServices<IStreamWorkerHost>());
        Assert.Single(provider.GetServices<IStreamingBackend>());
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
            environmentClientProfilesPath: null,
            environmentStreamingMode: null,
            environmentStreamWorkerPath: environmentPath,
            environmentBenchmarkEvidencePath: null);
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

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BeaconServiceRegistrationEnvironmentCollection
{
    public const string Name = "Beacon service registration environment";
}

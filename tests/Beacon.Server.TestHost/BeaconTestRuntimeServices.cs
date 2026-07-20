using Beacon.Core.Benchmarks;
using Beacon.Core.Displays;
using Beacon.Core.Games;
using Beacon.Core.Input;
using Beacon.Core.Recovery;
using Beacon.Core.Sessions;
using Beacon.Core.Streaming;
using Beacon.Platform.Windows.Displays;
using Beacon.Platform.Windows.Streaming;
using Beacon.Server.Benchmarks;
using Beacon.Server.Hosting;
using Beacon.Server.Security;
using Beacon.Server.State;
using Beacon.Server.Streaming;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Beacon.Server.TestHost;

public static class BeaconTestRuntimeServices
{
    public const string ProductionStreamWorkerConfigurationKey =
        "Beacon:TestHost:UseProductionStreamWorker";

    public static IServiceCollection UseBeaconFakeRuntime(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        bool useProductionStreamWorker = bool.TryParse(
            configuration?[ProductionStreamWorkerConfigurationKey],
            out bool configuredProductionStreamWorker)
            && configuredProductionStreamWorker;
        if (!useProductionStreamWorker)
        {
            RemoveWorkerRelay(services);
        }
        services.RemoveAll<BeaconHostOptions>();
        services.RemoveAll<IDisplayBackend>();
        services.RemoveAll<IRecoveryBackend>();
        services.RemoveAll<IGameLauncher>();
        services.RemoveAll<ISessionActivityInspector>();
        services.RemoveAll<ISessionOwnedWorkTerminator>();
        services.RemoveAll<IClientInputSink>();
        services.RemoveAll<IClientInputHealthProvider>();
        services.RemoveAll<NoOpClientInputSink>();
        if (useProductionStreamWorker)
        {
            services.RemoveAll<IWindowsDisplayNameResolver>();
            services.RemoveAll<IStreamWorkerRuntimeEvents>();
            services.RemoveAll<StreamWorkerStreamingBackend>();
            services.RemoveAll<IStreamingBackend>();
            services.RemoveAll<IBenchmarkRuntime>();
        }
        else
        {
            services.RemoveAll<IStreamWorkerHost>();
            services.RemoveAll<IStreamWorkerRuntimeEvents>();
            services.RemoveAll<StreamWorkerProcessHost>();
            services.RemoveAll<StreamWorkerProcessHostOptions>();
            services.RemoveAll<StreamWorkerStreamingBackend>();
            services.RemoveAll<IStreamSessionAuthorizer>();
            services.RemoveAll<IStreamingBackend>();
            services.RemoveAll<IBenchmarkRuntime>();
        }
        services.RemoveAll<FakeBenchmarkRuntime>();
        services.RemoveAll<IBenchmarkEvidenceRepository>();
        services.RemoveAll<IGameLibraryProvider>();

        services.AddSingleton(new BeaconHostOptions(
            useProductionStreamWorker ? "fake-worker" : "fake",
            nameof(FakeDisplayBackend),
            nameof(FakeGameLauncher),
            nameof(FakeSessionActivityInspector),
            useProductionStreamWorker
                ? nameof(StreamWorkerStreamingBackend)
                : nameof(FakeStreamingBackend)));
        services.AddSingleton<IDisplayBackend, FakeDisplayBackend>();
        services.AddSingleton<IRecoveryBackend, FakeRecoveryBackend>();
        services.AddSingleton<IGameLauncher, FakeGameLauncher>();
        services.AddSingleton<FakeSessionActivityInspector>();
        services.AddSingleton<ISessionActivityInspector>(sp =>
            sp.GetRequiredService<FakeSessionActivityInspector>());
        services.AddSingleton<ISessionOwnedWorkTerminator>(sp =>
            new FakeSessionOwnedWorkTerminator(
                sp.GetRequiredService<FakeSessionActivityInspector>()));
        services.AddSingleton<NoOpClientInputSink>();
        services.AddSingleton<IClientInputSink>(sp =>
            sp.GetRequiredService<NoOpClientInputSink>());
        services.AddSingleton<IClientInputHealthProvider>(sp =>
            sp.GetRequiredService<NoOpClientInputSink>());
        if (useProductionStreamWorker)
        {
            services.AddSingleton<IWindowsDisplayNameResolver, TestHostPrimaryDisplayNameResolver>();
            services.AddSingleton(sp => new StreamWorkerStreamingBackend(
                sp.GetRequiredService<IStreamWorkerHost>(),
                sp.GetRequiredService<IWindowsDisplayNameResolver>()));
            services.AddSingleton<IStreamingBackend>(sp =>
                sp.GetRequiredService<StreamWorkerStreamingBackend>());
            services.AddSingleton<IBenchmarkRuntime>(sp =>
                sp.GetRequiredService<StreamWorkerStreamingBackend>());
            services.AddSingleton<IStreamWorkerRuntimeEvents>(sp =>
                sp.GetRequiredService<StreamWorkerStreamingBackend>());
        }
        else
        {
            services.AddSingleton<IStreamSessionAuthorizer, FakeStreamSessionAuthorizer>();
            services.AddSingleton<IStreamingBackend, FakeStreamingBackend>();
            services.AddSingleton<FakeBenchmarkRuntime>();
            services.AddSingleton<IBenchmarkRuntime>(sp =>
                sp.GetRequiredService<FakeBenchmarkRuntime>());
        }
        services.AddSingleton<IBenchmarkEvidenceRepository>(_ =>
            new InMemoryBenchmarkEvidenceRepository(
                [FakeBenchmarkEvidence.CreateZFold7(DateTimeOffset.UtcNow)]));
        services.AddSingleton<IGameLibraryProvider>(_ => new StaticGameLibraryProvider(
            "test-seed",
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

        string? hostedWorkerPath = configuration?[HostedBenchmarkWorkerOptions.ExecutablePathConfigurationKey];
        if (!useProductionStreamWorker && !string.IsNullOrWhiteSpace(hostedWorkerPath))
        {
            UseHostedBenchmarkWorker(services, hostedWorkerPath);
        }
        return services;
    }

    private sealed class TestHostPrimaryDisplayNameResolver(IWindowsDisplayApi displayApi) :
        IWindowsDisplayNameResolver
    {
        public bool TryResolveDisplayName(string displayId, out string? displayName)
        {
            displayName = null;
            try
            {
                DisplayTopologySnapshot topology = displayApi
                    .QueryTopologyAsync(CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                DisplayPathSnapshot? path = topology.Paths.FirstOrDefault(candidate =>
                        candidate.Kind == DisplayPathKind.Physical && candidate.IsPrimary)
                    ?? topology.Paths.FirstOrDefault(candidate => candidate.IsPrimary)
                    ?? topology.Paths.FirstOrDefault(candidate => candidate.Kind == DisplayPathKind.Physical)
                    ?? topology.Paths.FirstOrDefault();
                displayName = path?.DisplayId;
                return !string.IsNullOrWhiteSpace(displayName);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                return false;
            }
        }
    }

    private static void UseHostedBenchmarkWorker(
        IServiceCollection services,
        string executablePath)
    {
        services.RemoveAll<IStreamWorkerHost>();
        services.RemoveAll<IStreamWorkerRuntimeEvents>();
        services.RemoveAll<IStreamSessionAuthorizer>();
        services.RemoveAll<IBenchmarkRuntime>();
        services.RemoveAll<FakeBenchmarkRuntime>();

        services.AddSingleton(sp => HostedBenchmarkWorkerOptions.Create(
            executablePath,
            sp.GetRequiredService<BeaconServerIdentity>().IdentityPath));
        services.AddSingleton<HostedBenchmarkWorkerProcessHost>();
        services.AddSingleton<IStreamWorkerHost>(sp =>
            sp.GetRequiredService<HostedBenchmarkWorkerProcessHost>());
        services.AddSingleton<StreamWorkerStreamingBackend>(sp =>
            new StreamWorkerStreamingBackend(sp.GetRequiredService<IStreamWorkerHost>()));
        services.AddSingleton<IBenchmarkRuntime>(sp =>
            sp.GetRequiredService<StreamWorkerStreamingBackend>());
        services.AddSingleton<IStreamWorkerRuntimeEvents>(sp =>
            sp.GetRequiredService<StreamWorkerStreamingBackend>());
        services.AddSingleton<IStreamSessionAuthorizer, StreamWorkerSessionAuthorizer>();
        services.AddHostedService<StreamWorkerEventRelay>();
    }

    private static void RemoveWorkerRelay(IServiceCollection services)
    {
        for (int index = services.Count - 1; index >= 0; index--)
        {
            ServiceDescriptor descriptor = services[index];
            if (descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(StreamWorkerEventRelay))
            {
                services.RemoveAt(index);
            }
        }
    }
}

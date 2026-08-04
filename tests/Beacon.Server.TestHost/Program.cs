using System.Text.Json.Serialization;
using Beacon.Server.Api;
using Beacon.Server.Hosting;
using Beacon.Server.Security;
using Beacon.Platform.Windows.Streaming;
using Beacon.Core.Displays;
using Beacon.Core.Games;

namespace Beacon.Server.TestHost;

public static class TestHostProgram
{
    public static void Main(string[] args)
    {
        if (FakeHostedBenchmarkWorkerProgram.IsInvocation(args))
        {
            Environment.ExitCode = FakeHostedBenchmarkWorkerProgram.Run(args);
            return;
        }

        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        builder.Services.AddBeaconServices(builder.Configuration);
        builder.Services.UseBeaconFakeRuntime(builder.Configuration);
        builder.Services.AddSingleton<HostedThinApkRequestEvidence>();
        builder.WebHost.ConfigureKestrel(options =>
            options.ConfigureHttpsDefaults(https =>
                https.ServerCertificate = options.ApplicationServices
                    .GetRequiredService<BeaconServerIdentity>()
                    .Certificate));

        WebApplication app = builder.Build();

        app.UseBeaconSecurity();
        app.Use(async (context, next) =>
        {
            await next(context);
            context.RequestServices
                .GetRequiredService<HostedThinApkRequestEvidence>()
                .Record(
                    context.Request.Method,
                    context.Request.Path.Value ?? "/",
                    context.Response.StatusCode);
        });
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        app.MapGet("/identity", (BeaconServerIdentity identity) => Results.Ok(new
        {
            algorithm = identity.Algorithm,
            publicKeyFingerprint = identity.PublicKeyFingerprint,
        }));
        app.MapGet("/hosted-benchmark-worker/snapshot", (IServiceProvider services) =>
        {
            HostedBenchmarkWorkerProcessHost? worker =
                services.GetService<HostedBenchmarkWorkerProcessHost>();
            StreamWorkerRuntimeSnapshot? runtime = services
                .GetService<StreamWorkerStreamingBackend>()?
                .GetRuntimeSnapshot();
            return worker is null
                ? Results.NotFound(new { error = "Hosted benchmark Worker is not configured." })
                : Results.Ok(new
                {
                    isReady = worker.IsReady,
                    processGeneration = worker.CurrentProcessGeneration,
                    processHasExited = worker.ProcessHasExited,
                    processExitCode = worker.ProcessExitCode,
                    clientTerminalError = worker.ClientTerminalError,
                    diagnostics = worker.Diagnostics,
                    runtime,
                });
        });
        app.MapGet("/hosted-thin-apk-flow/snapshot", (IServiceProvider services) =>
        {
            HostedBenchmarkWorkerProcessHost? worker =
                services.GetService<HostedBenchmarkWorkerProcessHost>();
            StreamWorkerRuntimeSnapshot? runtime = services
                .GetService<StreamWorkerStreamingBackend>()?
                .GetRuntimeSnapshot();
            FakeDisplayBackend display = (FakeDisplayBackend)services
                .GetRequiredService<IDisplayBackend>();
            FakeGameLauncher launcher = (FakeGameLauncher)services
                .GetRequiredService<IGameLauncher>();
            BeaconHostOptions host = services.GetRequiredService<BeaconHostOptions>();
            return Results.Ok(new
            {
                boundary = "hosted-fake-no-topology",
                host = new
                {
                    mode = host.ModeName,
                    displayBackend = host.DisplayBackendName,
                    gameLauncher = host.GameLauncherName,
                    streamingBackend = host.StreamingBackendName,
                },
                requests = services
                    .GetRequiredService<HostedThinApkRequestEvidence>()
                    .Snapshot(),
                display = new
                {
                    prepareCalls = display.PrepareCalls,
                    ensureCalls = display.EnsureCalls,
                    restoreCalls = display.RestoreCalls,
                    removeCalls = display.RemoveCalls,
                },
                launches = launcher.Requests.Select(request => new
                {
                    sessionId = request.Plan.SessionId,
                    appId = request.Game.Id,
                    displayId = request.DisplayId,
                }),
                worker = worker is null
                    ? null
                    : new
                    {
                        isReady = worker.IsReady,
                        processGeneration = worker.CurrentProcessGeneration,
                        processHasExited = worker.ProcessHasExited,
                        processExitCode = worker.ProcessExitCode,
                        clientTerminalError = worker.ClientTerminalError,
                        diagnostics = worker.Diagnostics,
                    },
                runtime,
            });
        });
        app.MapAdminEndpoints();
        app.MapGameEndpoints();
        app.MapClientEndpoints();

        app.Run();
    }
}

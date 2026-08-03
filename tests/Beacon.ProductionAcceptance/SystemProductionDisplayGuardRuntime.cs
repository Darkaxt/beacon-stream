using System.ComponentModel;
using System.Diagnostics;

namespace Beacon.ProductionAcceptance;

internal interface IProductionDisplayGuardEventSource : IDisposable
{
    bool AcceptanceRunning { get; }

    Task<ProductionDisplayGuardTrigger> WaitForTriggerAsync(CancellationToken cancellationToken);

    Task TerminateAcceptanceAsync(CancellationToken cancellationToken);
}

internal interface IProductionDisplayGuardCommandRunner
{
    Task<ProductionDisplayGuardCommandResult> RunAsync(
        string name,
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}

internal sealed class SystemProductionDisplayGuardRuntime(
    ProductionDisplayGuardOptions options,
    IProductionDisplayGuardEventSource events,
    IProductionDisplayGuardCommandRunner commands,
    TextWriter output) : IProductionDisplayGuardRuntime
{
    private static readonly TimeSpan RecoveryHeartbeat = TimeSpan.FromSeconds(2);
    private readonly string displayProbePath = ResolveDisplayProbePath(options.RepositoryRoot);
    private readonly string hostAgentControlPath = ResolveHostAgentControlPath(options.RepositoryRoot);

    public bool AcceptanceRunning => events.AcceptanceRunning;

    public Task<ProductionDisplayGuardTrigger> WaitForTriggerAsync(CancellationToken cancellationToken) =>
        events.WaitForTriggerAsync(cancellationToken);

    public Task TerminateAcceptanceAsync(CancellationToken cancellationToken) =>
        events.TerminateAcceptanceAsync(cancellationToken);

    public async Task ForcePhysicalOutputAsync(CancellationToken cancellationToken)
    {
        string displaySwitchPath = ResolveDisplaySwitchPath();
        ProductionDisplayGuardCommandResult result = await commands.RunAsync(
            "force-physical",
            displaySwitchPath,
            ["/internal"],
            cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync(
            $"BEACON_GATE5_DISPLAY_GUARD_FORCE_PHYSICAL exit={result.ExitCode}")
            .ConfigureAwait(false);
    }

    public async Task<ProductionDisplayRecoveryResult> RecoverAsync(CancellationToken cancellationToken)
    {
        ProductionDisplayGuardCommandResult[] results =
        [
            await commands.RunAsync(
                "restore-before-remove",
                displayProbePath,
                ["restore-physical"],
                cancellationToken).ConfigureAwait(false),
            await commands.RunAsync(
                "remove",
                displayProbePath,
                ["remove", "--client", options.ClientId],
                cancellationToken).ConfigureAwait(false),
            await commands.RunAsync(
                "restore-after-remove",
                displayProbePath,
                ["restore-physical"],
                cancellationToken).ConfigureAwait(false),
            await commands.RunAsync(
                "display-status",
                displayProbePath,
                ["status"],
                cancellationToken).ConfigureAwait(false),
            await commands.RunAsync(
                "host-agent-status",
                hostAgentControlPath,
                ["status"],
                cancellationToken).ConfigureAwait(false),
        ];

        return ProductionDisplayRecoveryVerifier.Verify(results, options.ClientId);
    }

    public Task WaitForRecoveryHeartbeatAsync(CancellationToken cancellationToken) =>
        Task.Delay(RecoveryHeartbeat, cancellationToken);

    internal static string ResolveDisplaySwitchPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        "System32",
        "DisplaySwitch.exe");

    internal static string ResolveDisplayProbePath(string repositoryRoot) => Path.Combine(
        repositoryRoot,
        "src", "Beacon.DisplayProbe", "bin", "Release", "net10.0-windows",
        "Beacon.DisplayProbe.exe");

    internal static string ResolveHostAgentControlPath(string repositoryRoot) => Path.Combine(
        repositoryRoot,
        "src", "Beacon.HostAgent.Control", "bin", "Release", "net10.0-windows",
        "Beacon.HostAgent.Control.exe");
}

internal sealed class ProductionDisplayGuardCommandRunner : IProductionDisplayGuardCommandRunner
{
    public async Task<ProductionDisplayGuardCommandResult> RunAsync(
        string name,
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return new ProductionDisplayGuardCommandResult(
                    name,
                    ExitCode: -1,
                    Output: string.Empty,
                    Error: $"Could not start {fileName}.");
            }

            Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(output, error).ConfigureAwait(false);
            return new ProductionDisplayGuardCommandResult(
                name,
                process.ExitCode,
                await output.ConfigureAwait(false),
                await error.ConfigureAwait(false));
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            return new ProductionDisplayGuardCommandResult(
                name,
                ExitCode: -1,
                Output: string.Empty,
                Error: error.Message);
        }
    }
}

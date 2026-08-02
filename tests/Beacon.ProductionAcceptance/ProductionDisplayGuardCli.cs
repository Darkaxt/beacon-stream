namespace Beacon.ProductionAcceptance;

internal static class ProductionDisplayGuardCli
{
    public static async Task<int> RunAsync(IReadOnlyList<string> args)
    {
        ProductionDisplayGuardOptions options = ProductionDisplayGuardOptions.Parse(args);
        string? logDirectory = Path.GetDirectoryName(options.LogPath);
        if (string.IsNullOrWhiteSpace(logDirectory))
        {
            throw new ArgumentException("The display guard log path must have a parent directory.");
        }
        Directory.CreateDirectory(logDirectory);

        await using var log = new StreamWriter(options.LogPath, append: true)
        {
            AutoFlush = true,
        };
        try
        {
            RequireRecoveryArtifact(SystemProductionDisplayGuardRuntime.ResolveDisplaySwitchPath());
            RequireRecoveryArtifact(SystemProductionDisplayGuardRuntime.ResolveDisplayProbePath(options.RepositoryRoot));
            RequireRecoveryArtifact(SystemProductionDisplayGuardRuntime.ResolveHostAgentControlPath(options.RepositoryRoot));

            using var events = new WindowsProductionDisplayGuardEventSource(
                options.AcceptanceProcessId,
                options.CompletionEventName);
            var runtime = new SystemProductionDisplayGuardRuntime(
                options,
                events,
                new ProductionDisplayGuardCommandRunner(),
                log);
            var guard = new ProductionDisplayGuard(runtime, log);

            using EventWaitHandle ready = EventWaitHandle.OpenExisting(options.ReadyEventName);
            ready.Set();
            await log.WriteLineAsync(
                "BEACON_GATE5_DISPLAY_GUARD_READY emergencyHotkey=Ctrl+Alt+Shift+F12")
                .ConfigureAwait(false);
            return await guard.RunAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            await log.WriteLineAsync($"BEACON_GATE5_DISPLAY_GUARD_FAILED {error}").ConfigureAwait(false);
            Console.Error.WriteLine(error);
            return 2;
        }
    }

    private static void RequireRecoveryArtifact(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Required Gate 5 display recovery artifact is unavailable: {path}",
                path);
        }
    }
}

using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace Beacon.ProductionAcceptance;

internal static class ProductionDisplayGuardHost
{
    public static async Task RunAsync(
        AcceptanceOptions options,
        Func<AcceptanceOptions, Task> acceptance)
    {
        string evidenceDirectory = options.ResolveEvidenceDirectory();
        Directory.CreateDirectory(evidenceDirectory);
        string readyEventName = $"Local\\Beacon.Gate5.Ready.{options.RunId}";
        string completionEventName = $"Local\\Beacon.Gate5.Complete.{options.RunId}";
        using var ready = new EventWaitHandle(
            initialState: false,
            EventResetMode.ManualReset,
            readyEventName,
            out bool readyCreated);
        using var completion = new EventWaitHandle(
            initialState: false,
            EventResetMode.ManualReset,
            completionEventName,
            out bool completionCreated);
        if (!readyCreated || !completionCreated)
        {
            throw new InvalidOperationException("Could not create unique Gate 5 display guard events.");
        }

        using Process guard = StartGuard(
            options,
            readyEventName,
            completionEventName,
            Path.Combine(evidenceDirectory, "display-guard.log"));
        File.WriteAllText(
            Path.Combine(evidenceDirectory, "display-guard.pid"),
            guard.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));

        Task guardExit = guard.WaitForExitAsync();
        TaskCompletionSource readySource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RegisteredWaitHandle readyRegistration = ThreadPool.RegisterWaitForSingleObject(
            ready,
            static (state, _) => ((TaskCompletionSource)state!).TrySetResult(),
            readySource,
            Timeout.Infinite,
            executeOnlyOnce: true);
        Task armed;
        try
        {
            armed = await Task.WhenAny(readySource.Task, guardExit).ConfigureAwait(false);
        }
        finally
        {
            readyRegistration.Unregister(null);
        }
        if (ReferenceEquals(armed, guardExit))
        {
            await guardExit.ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Gate 5 display guard exited before arming. ExitCode={guard.ExitCode}.");
        }
        if (guard.HasExited)
        {
            throw new InvalidOperationException(
                $"Gate 5 display guard exited immediately after arming. ExitCode={guard.ExitCode}.");
        }
        Console.WriteLine("BEACON_GATE5_DISPLAY_GUARD_ARMED emergencyHotkey=Ctrl+Alt+Shift+F12");

        Exception? acceptanceError = null;
        try
        {
            await acceptance(options).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            acceptanceError = error;
        }
        finally
        {
            completion.Set();
            await guardExit.ConfigureAwait(false);
        }

        if (acceptanceError is not null)
        {
            ExceptionDispatchInfo.Capture(acceptanceError).Throw();
        }
        if (guard.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Gate 5 display guard did not verify cleanup. ExitCode={guard.ExitCode}.");
        }

        Console.WriteLine("BEACON_GATE5_PRODUCTION_SESSION_OK");
        Console.WriteLine($"BEACON_GATE5_EVIDENCE {evidenceDirectory}");
    }

    private static Process StartGuard(
        AcceptanceOptions options,
        string readyEventName,
        string completionEventName,
        string logPath)
    {
        string executablePath = Path.Combine(
            AppContext.BaseDirectory,
            OperatingSystem.IsWindows()
                ? "Beacon.ProductionAcceptance.exe"
                : "Beacon.ProductionAcceptance");
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                $"Gate 5 acceptance executable is unavailable: {executablePath}",
                executablePath);
        }

        return Process.Start(CreateStartInfo(
            options,
            executablePath,
            readyEventName,
            completionEventName,
            logPath))
            ?? throw new InvalidOperationException("Could not start the Gate 5 display guard process.");
    }

    internal static ProcessStartInfo CreateStartInfo(
        AcceptanceOptions options,
        string executablePath,
        string readyEventName,
        string completionEventName,
        string logPath)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in new[]
                 {
                     "display-guard",
                     "--acceptance-process-id", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     "--client-id", options.ClientId,
                     "--repository-root", Path.GetFullPath(options.RepositoryRoot),
                     "--ready-event", readyEventName,
                     "--completion-event", completionEventName,
                     "--log-path", logPath,
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }
}

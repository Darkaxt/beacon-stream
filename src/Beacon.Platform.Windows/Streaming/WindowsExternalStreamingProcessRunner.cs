using System.Diagnostics;

namespace Beacon.Platform.Windows.Streaming;

public sealed class WindowsExternalStreamingProcessRunner : IExternalStreamingProcessRunner
{
    private readonly Lock gate = new();
    private readonly Dictionary<int, Process> processes = [];

    public bool FileExists(string path) => File.Exists(path);

    public ExternalStreamingProcess Start(ExternalStreamingCommand command)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command.FileName,
            Arguments = command.Arguments,
            UseShellExecute = false
        };

        foreach ((string key, string value) in command.Environment)
        {
            startInfo.Environment[key] = value;
        }

        Process? process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException($"Windows did not return a process for '{command.FileName}'.");
        }

        lock (gate)
        {
            processes[process.Id] = process;
        }

        return new ExternalStreamingProcess(process.Id);
    }

    public ExternalStreamingProcessStatus GetStatus(ExternalStreamingProcess process)
    {
        Process? trackedProcess;
        lock (gate)
        {
            processes.TryGetValue(process.ProcessId, out trackedProcess);
        }

        if (trackedProcess is null)
        {
            return ExternalStreamingProcessStatus.Exited(null);
        }

        try
        {
            if (!trackedProcess.HasExited)
            {
                return ExternalStreamingProcessStatus.Running();
            }

            return ExternalStreamingProcessStatus.Exited(trackedProcess.ExitCode);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return ExternalStreamingProcessStatus.Unknown(
                $"Unable to query external streaming process {process.ProcessId}: {ex.Message}");
        }
    }

    public ExternalStreamingProcessStopResult Stop(ExternalStreamingProcess process)
    {
        Process? trackedProcess;
        lock (gate)
        {
            processes.TryGetValue(process.ProcessId, out trackedProcess);
        }

        if (trackedProcess is null)
        {
            return ExternalStreamingProcessStopResult.Ok();
        }

        try
        {
            if (!trackedProcess.HasExited)
            {
                trackedProcess.Kill(entireProcessTree: true);
            }

            lock (gate)
            {
                processes.Remove(process.ProcessId);
            }

            trackedProcess.Dispose();
            return ExternalStreamingProcessStopResult.Ok();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return ExternalStreamingProcessStopResult.Fail($"Unable to stop external streaming process {process.ProcessId}: {ex.Message}");
        }
    }
}

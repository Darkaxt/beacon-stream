using System.Diagnostics;

namespace Beacon.Platform.Windows.Streaming;

public sealed class WindowsExternalStreamingProcessRunner : IExternalStreamingProcessRunner
{
    private const int MaxCapturedOutputLines = 40;
    private readonly Lock gate = new();
    private readonly Dictionary<int, Process> processes = [];
    private readonly Dictionary<int, Queue<string>> outputLinesByProcessId = [];

    public bool FileExists(string path) => File.Exists(path);

    public ExternalStreamingProcess Start(ExternalStreamingCommand command)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command.FileName,
            Arguments = command.Arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
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
            outputLinesByProcessId[process.Id] = new Queue<string>();
        }

        process.OutputDataReceived += (_, args) => AppendOutput(process.Id, "stdout", args.Data);
        process.ErrorDataReceived += (_, args) => AppendOutput(process.Id, "stderr", args.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

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
                return ExternalStreamingProcessStatus.Running(GetOutputSnapshot(process.ProcessId));
            }

            return ExternalStreamingProcessStatus.Exited(trackedProcess.ExitCode, GetOutputSnapshot(process.ProcessId));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return ExternalStreamingProcessStatus.Unknown(
                $"Unable to query external streaming process {process.ProcessId}: {ex.Message}",
                GetOutputSnapshot(process.ProcessId));
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
                outputLinesByProcessId.Remove(process.ProcessId);
            }

            trackedProcess.Dispose();
            return ExternalStreamingProcessStopResult.Ok();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return ExternalStreamingProcessStopResult.Fail($"Unable to stop external streaming process {process.ProcessId}: {ex.Message}");
        }
    }

    private void AppendOutput(int processId, string streamName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        lock (gate)
        {
            if (!outputLinesByProcessId.TryGetValue(processId, out Queue<string>? outputLines))
            {
                return;
            }

            outputLines.Enqueue($"{streamName}: {value.Trim()}");
            while (outputLines.Count > MaxCapturedOutputLines)
            {
                outputLines.Dequeue();
            }
        }
    }

    private IReadOnlyList<string> GetOutputSnapshot(int processId)
    {
        lock (gate)
        {
            return outputLinesByProcessId.TryGetValue(processId, out Queue<string>? outputLines)
                ? outputLines.ToArray()
                : [];
        }
    }
}

using System.Diagnostics;

namespace Beacon.StreamingProbe;

public sealed record StreamingProbeChildCommand(
    string FileName,
    string Arguments,
    IReadOnlyDictionary<string, string> Environment);

public sealed record StreamingProbeChildProcess(int ProcessId);

public interface IStreamingProbeChildProcessRunner
{
    bool FileExists(string path);

    StreamingProbeChildProcess Start(StreamingProbeChildCommand command);

    Task<int?> WaitForExitAsync(StreamingProbeChildProcess process);

    void Stop(StreamingProbeChildProcess process);
}

public sealed class WindowsStreamingProbeChildProcessRunner : IStreamingProbeChildProcessRunner
{
    private readonly Lock gate = new();
    private readonly Dictionary<int, TrackedChildProcess> processes = [];
    private readonly Dictionary<int, Task<int?>> completedExitTasks = [];

    public bool FileExists(string path) => File.Exists(path);

    public StreamingProbeChildProcess Start(StreamingProbeChildCommand command)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command.FileName,
            Arguments = command.Arguments,
            UseShellExecute = false
        };

        string? workingDirectory = Path.GetDirectoryName(command.FileName);
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        foreach ((string key, string value) in command.Environment)
        {
            startInfo.Environment[key] = value;
        }

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException($"Windows did not return a child process for '{command.FileName}'.");
        }

        var tracked = new TrackedChildProcess(process);
        process.Exited += (_, _) => CompleteExit(process.Id);
        lock (gate)
        {
            processes[process.Id] = tracked;
        }

        if (process.HasExited)
        {
            CompleteExit(process.Id);
        }

        return new StreamingProbeChildProcess(process.Id);
    }

    public Task<int?> WaitForExitAsync(StreamingProbeChildProcess process)
    {
        lock (gate)
        {
            if (processes.TryGetValue(process.ProcessId, out TrackedChildProcess? tracked))
            {
                return tracked.ExitTask;
            }

            if (completedExitTasks.Remove(process.ProcessId, out Task<int?>? completedExitTask))
            {
                return completedExitTask;
            }
        }

        return Task.FromResult<int?>(null);
    }

    public void Stop(StreamingProbeChildProcess process)
    {
        TrackedChildProcess? tracked = GetProcess(process);
        if (tracked is null)
        {
            return;
        }

        try
        {
            if (!tracked.Process.HasExited)
            {
                tracked.Process.Kill(entireProcessTree: true);
            }

            tracked.Process.WaitForExit();
        }
        finally
        {
            CompleteExit(process.ProcessId);
        }
    }

    private TrackedChildProcess? GetProcess(StreamingProbeChildProcess process)
    {
        lock (gate)
        {
            return processes.GetValueOrDefault(process.ProcessId);
        }
    }

    private void CompleteExit(int processId)
    {
        lock (gate)
        {
            if (!processes.Remove(processId, out TrackedChildProcess? tracked))
            {
                return;
            }

            tracked.Complete();
            completedExitTasks[processId] = tracked.ExitTask;
        }
    }

    private sealed class TrackedChildProcess(Process process)
    {
        private readonly TaskCompletionSource<int?> exit = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Process Process { get; } = process;

        public Task<int?> ExitTask => exit.Task;

        public void Complete()
        {
            int? exitCode = null;
            try
            {
                exitCode = Process.ExitCode;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                exitCode = null;
            }

            exit.TrySetResult(exitCode);
            Process.Dispose();
        }
    }
}

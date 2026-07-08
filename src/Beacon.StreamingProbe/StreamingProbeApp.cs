using System.Text.Json;
using Beacon.Platform.Windows.Streaming;

namespace Beacon.StreamingProbe;

public interface IStreamingProbeLifetime
{
    Task WaitForStopAsync();
}

public static class StreamingProbeApp
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static Task<int> RunAsync(
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string> environment,
        TextWriter output,
        TextWriter error,
        IStreamingProbeLifetime lifetime,
        IStreamingProbeChildProcessRunner? childRunner = null)
    {
        try
        {
            StreamingProbeCommand command = StreamingProbeCommandLine.Parse(args, environment);
            return RunAsync(command, output, error, lifetime, childRunner);
        }
        catch (ArgumentException ex)
        {
            error.WriteLine($"Streaming probe failed: {ex.Message}");
            return Task.FromResult(2);
        }
    }

    public static async Task<int> RunAsync(
        StreamingProbeCommand command,
        TextWriter output,
        TextWriter error,
        IStreamingProbeLifetime lifetime,
        IStreamingProbeChildProcessRunner? childRunner = null)
    {
        childRunner ??= new WindowsStreamingProbeChildProcessRunner();
        StreamingProbeChildProcess? childProcess = null;
        try
        {
            Task<int?>? childExitTask = null;
            if (!string.IsNullOrWhiteSpace(command.ChildExecutable))
            {
                string childExecutable = command.ChildExecutable.Trim();
                if (!childRunner.FileExists(childExecutable))
                {
                    error.WriteLine($"Streaming probe failed: child executable '{childExecutable}' does not exist.");
                    return 2;
                }

                var childCommand = new StreamingProbeChildCommand(
                    childExecutable,
                    string.IsNullOrWhiteSpace(command.ChildArguments) ? string.Empty : command.ChildArguments.Trim(),
                    command.ChildEnvironment ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
                childProcess = childRunner.Start(childCommand);
                childExitTask = childRunner.WaitForExitAsync(childProcess);
                output.WriteLine($"childProcessId={childProcess.ProcessId}");
            }

            WriteDescriptor(command);
            output.WriteLine($"descriptorPath={command.DescriptorPath}");
            output.WriteLine($"launchUri={command.LaunchUri}");

            if (!command.Once)
            {
                if (childExitTask is null)
                {
                    await lifetime.WaitForStopAsync();
                }
                else
                {
                    Task stopTask = lifetime.WaitForStopAsync();
                    Task completed = await Task.WhenAny(childExitTask, stopTask);
                    if (ReferenceEquals(completed, childExitTask))
                    {
                        int? childExitCode = await childExitTask;
                        output.WriteLine(childExitCode.HasValue
                            ? $"childExitCode={childExitCode.Value}"
                            : "childExitCode=");
                        childProcess = null;
                        return childExitCode.GetValueOrDefault(0);
                    }
                }
            }

            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException
            or IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidOperationException
            or System.ComponentModel.Win32Exception)
        {
            error.WriteLine($"Streaming probe failed: {ex.Message}");
            return 2;
        }
        finally
        {
            if (childProcess is not null)
            {
                StopChildProcess(childRunner, childProcess, error);
            }
        }
    }

    private static void WriteDescriptor(StreamingProbeCommand command)
    {
        string? directory = Path.GetDirectoryName(command.DescriptorPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var descriptor = new ExternalStreamingSessionDescriptor(
            command.Protocol,
            command.LaunchUri,
            command.Endpoints,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["sessionId"] = command.SessionId,
                ["displayId"] = command.DisplayId,
                ["wrapper"] = "Beacon.StreamingProbe"
            },
            ["streaming probe descriptor ready"]);
        string json = JsonSerializer.Serialize(descriptor, JsonOptions);
        File.WriteAllText(command.DescriptorPath, json);
    }

    private static void StopChildProcess(
        IStreamingProbeChildProcessRunner childRunner,
        StreamingProbeChildProcess childProcess,
        TextWriter error)
    {
        try
        {
            childRunner.Stop(childProcess);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            error.WriteLine($"Streaming probe failed to stop child process {childProcess.ProcessId}: {ex.Message}");
        }
    }
}

public sealed class ConsoleStreamingProbeLifetime : IStreamingProbeLifetime, IDisposable
{
    private readonly TaskCompletionSource stop = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ConsoleStreamingProbeLifetime()
    {
        Console.CancelKeyPress += OnCancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    public Task WaitForStopAsync() => stop.Task;

    public void Dispose()
    {
        Console.CancelKeyPress -= OnCancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs args)
    {
        args.Cancel = true;
        stop.TrySetResult();
    }

    private void OnProcessExit(object? sender, EventArgs args)
    {
        stop.TrySetResult();
    }
}

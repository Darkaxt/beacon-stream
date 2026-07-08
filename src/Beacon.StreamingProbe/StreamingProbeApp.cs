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
        IStreamingProbeLifetime lifetime)
    {
        try
        {
            StreamingProbeCommand command = StreamingProbeCommandLine.Parse(args, environment);
            return RunAsync(command, output, error, lifetime);
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
        IStreamingProbeLifetime lifetime)
    {
        try
        {
            WriteDescriptor(command);
            output.WriteLine($"descriptorPath={command.DescriptorPath}");
            output.WriteLine($"launchUri={command.LaunchUri}");

            if (!command.Once)
            {
                await lifetime.WaitForStopAsync();
            }

            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or JsonException)
        {
            error.WriteLine($"Streaming probe failed: {ex.Message}");
            return 2;
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
}

public sealed class ConsoleStreamingProbeLifetime : IStreamingProbeLifetime, IDisposable
{
    private readonly ManualResetEventSlim stop = new();

    public ConsoleStreamingProbeLifetime()
    {
        Console.CancelKeyPress += OnCancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    public Task WaitForStopAsync()
    {
        stop.Wait();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        Console.CancelKeyPress -= OnCancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        stop.Dispose();
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs args)
    {
        args.Cancel = true;
        stop.Set();
    }

    private void OnProcessExit(object? sender, EventArgs args)
    {
        stop.Set();
    }
}

using System.Diagnostics;
using System.Threading.Channels;
using Beacon.Platform.Windows.Streaming;
using Beacon.StreamWorker.Contracts.Worker.V1;

namespace Beacon.Server.TestHost;

public sealed class HostedBenchmarkWorkerProcessHost : IStreamWorkerHost, IAsyncDisposable
{
    private readonly HostedBenchmarkWorkerOptions options;
    private readonly Func<HostedBenchmarkWorkerOptions, ProcessStartInfo> createStartInfo;
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private readonly Channel<StreamWorkerEvent> eventChannel;
    private readonly CancellationTokenSource disposal = new();
    private readonly Lock diagnosticGate = new();
    private readonly Queue<string> diagnostics = [];
    private readonly int diagnosticCapacity;
    private readonly Action<string>? diagnosticSink;
    private ActiveWorker? activeWorker;
    private long nextProcessGeneration;
    private int disposed;

    public HostedBenchmarkWorkerProcessHost(HostedBenchmarkWorkerOptions options)
        : this(
            options,
            CreateStartInfo,
            eventCapacity: 64,
            diagnosticCapacity: 16,
            static marker => Console.Error.WriteLine(marker))
    {
    }

    internal HostedBenchmarkWorkerProcessHost(
        HostedBenchmarkWorkerOptions options,
        Func<HostedBenchmarkWorkerOptions, ProcessStartInfo> createStartInfo,
        int eventCapacity,
        int diagnosticCapacity,
        Action<string>? diagnosticSink = null)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.createStartInfo = createStartInfo ?? throw new ArgumentNullException(nameof(createStartInfo));
        if (eventCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(eventCapacity));
        }
        if (diagnosticCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(diagnosticCapacity));
        }
        this.diagnosticCapacity = diagnosticCapacity;
        this.diagnosticSink = diagnosticSink;
        eventChannel = Channel.CreateBounded<StreamWorkerEvent>(new BoundedChannelOptions(eventCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
        });
    }

    public bool IsReady =>
        Volatile.Read(ref disposed) == 0
        && Volatile.Read(ref activeWorker) is { Process.HasExited: false, Client.IsReady: true };

    public int ProcessId => Volatile.Read(ref activeWorker)?.Process.Id ?? 0;

    public bool ProcessHasExited =>
        Volatile.Read(ref activeWorker) is not { } active || active.Process.HasExited;

    public int? ProcessExitCode
    {
        get
        {
            ActiveWorker? active = Volatile.Read(ref activeWorker);
            return active is not null && active.Process.HasExited
                ? active.Process.ExitCode
                : null;
        }
    }

    public string? ClientTerminalError
    {
        get
        {
            Exception? error = Volatile.Read(ref activeWorker)?.Client?.TerminalError;
            return error is null ? null : $"{error.GetType().Name}: {error.Message}";
        }
    }

    public ReadOnlyMemory<byte> WorkerInstanceId =>
        Volatile.Read(ref activeWorker)?.Client?.WorkerInstanceId ?? ReadOnlyMemory<byte>.Empty;

    public WorkerCapabilities Capabilities =>
        Volatile.Read(ref activeWorker)?.Client?.Capabilities ?? new WorkerCapabilities();

    public ChannelReader<StreamWorkerEvent> Events => eventChannel.Reader;

    public long CurrentProcessGeneration =>
        Volatile.Read(ref activeWorker)?.ProcessGeneration ?? 0;

    public IReadOnlyList<string> Diagnostics
    {
        get
        {
            lock (diagnosticGate)
            {
                return diagnostics.ToArray();
            }
        }
    }

    public bool IsCurrentProcessGeneration(long processGeneration) =>
        processGeneration > 0
        && Volatile.Read(ref activeWorker)?.ProcessGeneration == processGeneration;

    public async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (IsReady)
        {
            return;
        }

        await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            if (IsReady)
            {
                return;
            }

            ActiveWorker? stale = Volatile.Read(ref activeWorker);
            if (stale is not null)
            {
                await ReleaseWorkerAsync(stale, force: !stale.Process.HasExited).ConfigureAwait(false);
            }
            await StartWorkerAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public async Task<StreamWorkerCommandResponse> SendAsync(
        long expectedProcessGeneration,
        WorkerIpcEnvelope command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            disposal.Token);
        await lifecycleGate.WaitAsync(operationCancellation.Token).ConfigureAwait(false);
        try
        {
            ActiveWorker? active = Volatile.Read(ref activeWorker);
            if (Volatile.Read(ref disposed) != 0
                || active is null
                || active.ProcessGeneration != expectedProcessGeneration
                || active.Process.HasExited
                || active.Client?.IsReady != true)
            {
                throw new StreamWorkerGenerationChangedException(expectedProcessGeneration);
            }

            return await active.Client.SendAsync(command, operationCancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ShutdownWorkerCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            try
            {
                await ShutdownWorkerCoreAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                ActiveWorker? active = Volatile.Read(ref activeWorker);
                if (active is not null)
                {
                    await ReleaseWorkerAsync(active, force: true).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            lifecycleGate.Release();
            disposal.Cancel();
            eventChannel.Writer.TryComplete();
            lifecycleGate.Dispose();
            disposal.Dispose();
        }
    }

    internal static ProcessStartInfo CreateStartInfo(HostedBenchmarkWorkerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var startInfo = new ProcessStartInfo(options.ExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("--identity");
        startInfo.ArgumentList.Add(options.IdentityPath);
        if (options.VideoEnabled)
        {
            startInfo.ArgumentList.Add("--video-720p");
            startInfo.ArgumentList.Add(options.Video720pPath!);
            startInfo.ArgumentList.Add("--video-360p");
            startInfo.ArgumentList.Add(options.Video360pPath!);
        }
        return startInfo;
    }

    private async Task StartWorkerAsync(CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = createStartInfo(options);
        if (startInfo.UseShellExecute
            || !startInfo.RedirectStandardInput
            || !startInfo.RedirectStandardOutput
            || !startInfo.RedirectStandardError)
        {
            throw new InvalidOperationException(
                "Hosted benchmark Worker requires redirected binary control and diagnostic streams.");
        }

        var process = new Process { StartInfo = startInfo };
        ActiveWorker? active = null;
        bool started = false;
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Hosted benchmark Worker process did not start.");
            }
            started = true;

            long generation = Interlocked.Increment(ref nextProcessGeneration);
            Task<int> processExit = ObserveExitAsync(process);
            Task stderrDrain = DrainDiagnosticsAsync(process.StandardError);
            Task exitPublication = PublishProcessExitAsync(processExit, generation);
            var control = new ReadWriteDuplexStream(
                process.StandardOutput.BaseStream,
                process.StandardInput.BaseStream);
            active = new ActiveWorker(
                generation,
                process,
                processExit,
                stderrDrain,
                exitPublication,
                control);
            Volatile.Write(ref activeWorker, active);

            var client = new StreamWorkerNamedPipeClient(
                control,
                processExit,
                checked((uint)process.Id),
                generation,
                eventChannel.Writer);
            active.Client = client;
            await client.InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (active is not null)
            {
                await ReleaseWorkerAsync(active, force: !active.Process.HasExited).ConfigureAwait(false);
            }
            else
            {
                if (started && !process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
                process.Dispose();
            }
            throw;
        }
    }

    private async Task ShutdownWorkerCoreAsync(CancellationToken cancellationToken)
    {
        ActiveWorker? active = Volatile.Read(ref activeWorker);
        if (active is null)
        {
            return;
        }

        Exception? failure = null;
        bool force = false;
        bool cleanExitMayHaveWonCompletion = false;
        if (!active.Process.HasExited)
        {
            if (active.Client?.IsReady == true)
            {
                try
                {
                    StreamWorkerCommandResponse response = await active.Client.SendAsync(
                        new WorkerIpcEnvelope { ShutdownWorker = new ShutdownWorker() },
                        cancellationToken).ConfigureAwait(false);
                    WorkerCompletion completion = response.Completion.WorkerCompletion;
                    if (!completion.Succeeded || completion.ErrorCode != WorkerErrorCode.None)
                    {
                        throw new StreamWorkerProtocolException(
                            "Hosted benchmark Worker rejected explicit shutdown.");
                    }

                    int exitCode = await active.ProcessExit
                        .WaitAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (exitCode != 0)
                    {
                        throw new StreamWorkerProcessExitedException(exitCode);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (StreamWorkerProcessExitedException error) when (error.ExitCode == 0)
                {
                    cleanExitMayHaveWonCompletion = true;
                }
                catch (EndOfStreamException)
                {
                    cleanExitMayHaveWonCompletion = true;
                }
                catch (Exception error)
                {
                    failure = error;
                    force = true;
                }
            }
            else
            {
                force = true;
            }
        }

        await ReleaseWorkerAsync(active, force).ConfigureAwait(false);
        if (cleanExitMayHaveWonCompletion)
        {
            int exitCode = await active.ProcessExit.ConfigureAwait(false);
            if (exitCode != 0)
            {
                failure = new StreamWorkerProcessExitedException(exitCode);
            }
        }
        if (failure is not null)
        {
            throw failure;
        }
    }

    private async Task ReleaseWorkerAsync(ActiveWorker active, bool force)
    {
        if (!ReferenceEquals(
                Interlocked.CompareExchange(ref activeWorker, null, active),
                active))
        {
            return;
        }

        if (force && !active.Process.HasExited)
        {
            try
            {
                active.Process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (active.Process.HasExited)
            {
            }
        }

        try
        {
            if (active.Client is not null)
            {
                await active.Client.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                await active.Control.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            try
            {
                _ = await active.ProcessExit.ConfigureAwait(false);
            }
            finally
            {
                await Task.WhenAll(active.StderrDrain, active.ExitPublication).ConfigureAwait(false);
                active.Process.Dispose();
            }
        }
    }

    private async Task DrainDiagnosticsAsync(StreamReader reader)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            if (!IsSafeMarker(line))
            {
                continue;
            }
            lock (diagnosticGate)
            {
                while (diagnostics.Count >= diagnosticCapacity)
                {
                    diagnostics.Dequeue();
                }
                diagnostics.Enqueue(line);
            }
            try
            {
                diagnosticSink?.Invoke(line);
            }
            catch (Exception)
            {
            }
        }
    }

    private async Task PublishProcessExitAsync(Task<int> processExit, long processGeneration)
    {
        int exitCode = await processExit.ConfigureAwait(false);
        try
        {
            await eventChannel.Writer.WriteAsync(
                new StreamWorkerProcessExited(processGeneration, exitCode),
                disposal.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (disposal.IsCancellationRequested)
        {
        }
        catch (ChannelClosedException)
        {
        }
    }

    private static bool IsSafeMarker(string value)
    {
        if (value.Length is 0 or > 128
            || !value.StartsWith("BEACON_", StringComparison.Ordinal))
        {
            return false;
        }

        return value.All(character =>
            character is >= 'A' and <= 'Z'
            || character is >= '0' and <= '9'
            || character is '_' or ' ');
    }

    private static async Task<int> ObserveExitAsync(Process process)
    {
        await process.WaitForExitAsync().ConfigureAwait(false);
        return process.ExitCode;
    }

    private sealed class ActiveWorker(
        long processGeneration,
        Process process,
        Task<int> processExit,
        Task stderrDrain,
        Task exitPublication,
        ReadWriteDuplexStream control)
    {
        public long ProcessGeneration { get; } = processGeneration;

        public Process Process { get; } = process;

        public Task<int> ProcessExit { get; } = processExit;

        public Task StderrDrain { get; } = stderrDrain;

        public Task ExitPublication { get; } = exitPublication;

        public ReadWriteDuplexStream Control { get; } = control;

        public StreamWorkerNamedPipeClient? Client { get; set; }
    }
}

using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading.Channels;
using Beacon.StreamWorker.Contracts.Worker.V1;

namespace Beacon.Platform.Windows.Streaming;

public sealed record StreamWorkerProcessHostOptions(string ExecutablePath, string IdentityPath)
{
    public static StreamWorkerProcessHostOptions CreateDefault(string identityPath) =>
        Create(null, identityPath);

    public static StreamWorkerProcessHostOptions Create(string? executablePath, string identityPath) =>
        new(
            string.IsNullOrWhiteSpace(executablePath)
                ? Path.Combine(AppContext.BaseDirectory, "Beacon.StreamWorker.exe")
                : executablePath,
            identityPath);
}

public interface IStreamWorkerHost
{
    bool IsReady { get; }

    ReadOnlyMemory<byte> WorkerInstanceId { get; }

    ChannelReader<StreamWorkerEvent> Events { get; }

    long CurrentProcessGeneration { get; }

    bool IsCurrentProcessGeneration(long processGeneration);

    Task EnsureReadyAsync(CancellationToken cancellationToken);

    Task<StreamWorkerCommandResponse> SendAsync(
        WorkerIpcEnvelope command,
        CancellationToken cancellationToken);

    Task ShutdownAsync(CancellationToken cancellationToken);
}

public sealed class StreamWorkerProcessHost : IStreamWorkerHost, IAsyncDisposable
{
    private readonly StreamWorkerProcessHostOptions options;
    private readonly InteractiveStreamWorkerLauncher launcher;
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private readonly StreamWorkerEventBuffer eventBuffer = new(capacity: 64);
    private ActiveWorker? activeWorker;
    private int disposed;

    public StreamWorkerProcessHost(StreamWorkerProcessHostOptions options)
        : this(options, new InteractiveStreamWorkerLauncher())
    {
    }

    internal StreamWorkerProcessHost(
        StreamWorkerProcessHostOptions options,
        InteractiveStreamWorkerLauncher launcher)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
    }

    public bool IsReady =>
        Volatile.Read(ref disposed) == 0
        && Volatile.Read(ref activeWorker) is { Client.IsReady: true, Process.HasExited: false };

    public int ProcessId => Volatile.Read(ref activeWorker)?.Process.Id ?? 0;

    public bool HasExited => Volatile.Read(ref activeWorker) is not { Process.HasExited: false };

    public ReadOnlyMemory<byte> WorkerInstanceId =>
        Volatile.Read(ref activeWorker)?.Client.WorkerInstanceId ?? ReadOnlyMemory<byte>.Empty;

    public ChannelReader<StreamWorkerEvent> Events => eventBuffer.Reader;

    public long CurrentProcessGeneration =>
        Volatile.Read(ref activeWorker)?.ProcessGeneration ?? 0;

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

            await ReleaseUnreadyWorkerAsync().ConfigureAwait(false);
            await StartWorkerAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public async Task<StreamWorkerCommandResponse> SendAsync(
        WorkerIpcEnvelope command,
        CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        StreamWorkerNamedPipeClient activeClient = Volatile.Read(ref activeWorker)?.Client
            ?? throw new InvalidOperationException("StreamWorker did not become ready.");
        return await activeClient.SendAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
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

        await eventBuffer.DisposeAsync().ConfigureAwait(false);
        await lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await ShutdownWorkerCoreAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            lifecycleGate.Release();
            lifecycleGate.Dispose();
        }
    }

    internal static PipeSecurity CreatePipeSecurity(SecurityIdentifier owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var localSystem = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(owner);
        security.AddAccessRule(new PipeAccessRule(owner, PipeAccessRights.FullControl, AccessControlType.Allow));
        if (!owner.Equals(localSystem))
        {
            security.AddAccessRule(new PipeAccessRule(
                localSystem,
                PipeAccessRights.FullControl,
                AccessControlType.Allow));
        }
        return security;
    }

    private async Task StartWorkerAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(options.ExecutablePath))
        {
            throw new FileNotFoundException("Beacon StreamWorker executable was not found.", options.ExecutablePath);
        }
        if (!File.Exists(options.IdentityPath))
        {
            throw new FileNotFoundException("Beacon server identity was not found.", options.IdentityPath);
        }

        string pipeName = $"beacon-stream-worker-{Guid.NewGuid():N}";
        SecurityIdentifier owner = launcher.GetInteractiveUserSid();
        PipeSecurity security = CreatePipeSecurity(owner);
        var newPipe = NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            security,
            HandleInheritability.None,
            additionalAccessRights: 0);
        var newJob = new WorkerJobObject();
        Process? newProcess = null;
        StreamWorkerNamedPipeClient? newClient = null;
        try
        {
            string pipePath = $@"\\.\pipe\{pipeName}";
            newProcess = launcher.Launch(
                options.ExecutablePath,
                ["--pipe", pipePath, "--identity", options.IdentityPath],
                newJob);
            Task<int> newProcessExit = ObserveExitAsync(newProcess);
            Task connection = newPipe.WaitForConnectionAsync(cancellationToken);
            Task winner = await Task.WhenAny(connection, newProcessExit)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            if (winner == newProcessExit)
            {
                throw new StreamWorkerProcessExitedException(await newProcessExit.ConfigureAwait(false));
            }
            await connection.ConfigureAwait(false);

            long processGeneration = eventBuffer.ActivateNextGeneration();
            newClient = new StreamWorkerNamedPipeClient(
                newPipe,
                newProcessExit,
                checked((uint)newProcess.Id),
                processGeneration,
                eventBuffer.Writer);
            Volatile.Write(
                ref activeWorker,
                new ActiveWorker(
                    processGeneration,
                    newProcess,
                    newProcessExit,
                    newPipe,
                    newJob,
                    newClient));
            await newClient.InitializeAsync(cancellationToken).ConfigureAwait(false);
            _ = PublishProcessExitAsync(newProcessExit, processGeneration);
        }
        catch
        {
            if (newClient is not null
                && ReferenceEquals(Volatile.Read(ref activeWorker)?.Client, newClient))
            {
                await ReleaseWorkerAsync().ConfigureAwait(false);
                newClient = null;
                newProcess = null;
            }
            else if (newClient is not null)
            {
                await newClient.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                await newPipe.DisposeAsync().ConfigureAwait(false);
            }
            newJob.Dispose();
            newProcess?.Dispose();
            throw;
        }
    }

    private async Task ShutdownWorkerCoreAsync(CancellationToken cancellationToken)
    {
        ActiveWorker? active = Volatile.Read(ref activeWorker);
        StreamWorkerNamedPipeClient? activeClient = active?.Client;
        Task<int>? activeExit = active?.ProcessExit;
        try
        {
            if (active is { Process.HasExited: false } && activeClient?.IsReady == true)
            {
                StreamWorkerCommandResponse response = await activeClient.SendAsync(
                    new WorkerIpcEnvelope { ShutdownWorker = new ShutdownWorker() },
                    cancellationToken).ConfigureAwait(false);
                if (!response.Completion.WorkerCompletion.Succeeded)
                {
                    throw new StreamWorkerProtocolException("StreamWorker rejected explicit shutdown.");
                }
                if (activeExit is not null)
                {
                    _ = await activeExit.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            else if (active is { Process.HasExited: false })
            {
                active.Job.Dispose();
                if (activeExit is not null)
                {
                    _ = await activeExit.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            active?.Job.Dispose();
            if (activeExit is not null)
            {
                _ = await activeExit.ConfigureAwait(false);
            }
            throw;
        }
        finally
        {
            await ReleaseWorkerAsync().ConfigureAwait(false);
        }
    }

    private async Task ReleaseUnreadyWorkerAsync()
    {
        ActiveWorker? active = Volatile.Read(ref activeWorker);
        if (active is { Process.HasExited: false })
        {
            active.Job.Dispose();
            _ = await active.ProcessExit.ConfigureAwait(false);
        }

        if (!IsReady)
        {
            await ReleaseWorkerAsync().ConfigureAwait(false);
        }
    }

    private async Task ReleaseWorkerAsync()
    {
        ActiveWorker? old = Interlocked.Exchange(ref activeWorker, null);
        if (old is null)
        {
            return;
        }
        eventBuffer.Deactivate(old.ProcessGeneration);
        await old.Client.DisposeAsync().ConfigureAwait(false);
        old.Job.Dispose();
        old.Process.Dispose();
    }

    private static async Task<int> ObserveExitAsync(Process process)
    {
        await process.WaitForExitAsync().ConfigureAwait(false);
        return process.ExitCode;
    }

    private async Task PublishProcessExitAsync(Task<int> exit, long processGeneration)
    {
        try
        {
            int exitCode = await exit.ConfigureAwait(false);
            await eventBuffer.PublishProcessExitedAsync(processGeneration, exitCode).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (Volatile.Read(ref disposed) != 0)
        {
        }
        catch (ChannelClosedException) when (Volatile.Read(ref disposed) != 0)
        {
        }
    }

    private sealed record ActiveWorker(
        long ProcessGeneration,
        Process Process,
        Task<int> ProcessExit,
        NamedPipeServerStream Pipe,
        WorkerJobObject Job,
        StreamWorkerNamedPipeClient Client);
}

internal sealed class StreamWorkerEventBuffer : IAsyncDisposable
{
    private readonly Channel<StreamWorkerEvent> channel;
    private readonly CancellationTokenSource disposal = new();
    private readonly Lock exitGate = new();
    private readonly HashSet<long> publishedExitGenerations = [];
    private long nextGeneration;
    private long currentGeneration;
    private int disposed;

    public StreamWorkerEventBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }
        channel = Channel.CreateBounded<StreamWorkerEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    }

    public ChannelReader<StreamWorkerEvent> Reader => channel.Reader;

    public ChannelWriter<StreamWorkerEvent> Writer => channel.Writer;

    public long CurrentGeneration => Interlocked.Read(ref currentGeneration);

    public long ActivateNextGeneration()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        long generation = Interlocked.Increment(ref nextGeneration);
        Interlocked.Exchange(ref currentGeneration, generation);
        return generation;
    }

    public void Deactivate(long generation)
    {
        if (generation > 0)
        {
            Interlocked.CompareExchange(ref currentGeneration, 0, generation);
        }
    }

    public bool IsCurrentGeneration(long generation) =>
        generation > 0 && CurrentGeneration == generation;

    public async ValueTask PublishProcessExitedAsync(long generation, int exitCode)
    {
        lock (exitGate)
        {
            if (!publishedExitGenerations.Add(generation))
            {
                return;
            }
        }

        try
        {
            await channel.Writer.WriteAsync(
                new StreamWorkerProcessExited(generation, exitCode),
                disposal.Token).ConfigureAwait(false);
        }
        catch (ChannelClosedException) when (disposal.IsCancellationRequested)
        {
            throw new OperationCanceledException(disposal.Token);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            disposal.Cancel();
            channel.Writer.TryComplete();
            disposal.Dispose();
        }
        return ValueTask.CompletedTask;
    }
}

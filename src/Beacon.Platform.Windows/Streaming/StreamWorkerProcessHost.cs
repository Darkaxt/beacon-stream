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

    Task EnsureReadyAsync(CancellationToken cancellationToken);

    bool IsCurrentProcessGeneration(long processGeneration);

    Task<StreamWorkerCommandResponse> SendAsync(
        long expectedProcessGeneration,
        WorkerIpcEnvelope command,
        CancellationToken cancellationToken);

    Task ShutdownAsync(CancellationToken cancellationToken);
}

public sealed class StreamWorkerProcessHost :
    IStreamWorkerHost,
    IAsyncDisposable
{
    private readonly StreamWorkerProcessHostOptions options;
    private readonly IStreamWorkerLaunchFactory launchFactory;
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private readonly StreamWorkerEventBuffer eventBuffer;
    private readonly CancellationTokenSource disposal = new();
    private readonly Func<long, Task> beforeProcessExitPublication;
    private readonly Action<long>? processExitPublicationStarted;
    private ActiveWorker? activeWorker;
    private int disposed;

    public StreamWorkerProcessHost(StreamWorkerProcessHostOptions options)
        : this(
            options,
            new DefaultStreamWorkerLaunchFactory(new InteractiveStreamWorkerLauncher()))
    {
    }

    internal StreamWorkerProcessHost(
        StreamWorkerProcessHostOptions options,
        InteractiveStreamWorkerLauncher launcher)
        : this(options, new DefaultStreamWorkerLaunchFactory(launcher))
    {
    }

    internal StreamWorkerProcessHost(
        StreamWorkerProcessHostOptions options,
        IStreamWorkerLaunchFactory launchFactory)
        : this(options, launchFactory, eventCapacity: 64)
    {
    }

    internal StreamWorkerProcessHost(
        StreamWorkerProcessHostOptions options,
        IStreamWorkerLaunchFactory launchFactory,
        int eventCapacity,
        Func<long, Task>? beforeProcessExitPublication = null,
        Action<long>? processExitPublicationStarted = null)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.launchFactory = launchFactory ?? throw new ArgumentNullException(nameof(launchFactory));
        eventBuffer = new StreamWorkerEventBuffer(eventCapacity);
        this.beforeProcessExitPublication = beforeProcessExitPublication ?? (_ => Task.CompletedTask);
        this.processExitPublicationStarted = processExitPublicationStarted;
    }

    public bool IsReady =>
        Volatile.Read(ref disposed) == 0
        && Volatile.Read(ref activeWorker) is { Client.IsReady: true, Launch.Process.HasExited: false };

    public int ProcessId => Volatile.Read(ref activeWorker)?.Launch.Process.Id ?? 0;

    public bool HasExited => Volatile.Read(ref activeWorker) is not { Launch.Process.HasExited: false };

    public ReadOnlyMemory<byte> WorkerInstanceId =>
        Volatile.Read(ref activeWorker)?.Client?.WorkerInstanceId ?? ReadOnlyMemory<byte>.Empty;

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
        long expectedProcessGeneration,
        WorkerIpcEnvelope command,
        CancellationToken cancellationToken)
    {
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
                || active.Client?.IsReady != true
                || active.Launch.Process.HasExited)
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

        disposal.Cancel();
        try
        {
            await lifecycleGate.WaitAsync().ConfigureAwait(false);
            try
            {
                await ShutdownWorkerCoreAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                lifecycleGate.Release();
            }
        }
        finally
        {
            await eventBuffer.DisposeAsync().ConfigureAwait(false);
            lifecycleGate.Dispose();
            disposal.Dispose();
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
        IStreamWorkerLaunch? launch = null;
        try
        {
            launch = launchFactory.Launch(options);
            long processGeneration = eventBuffer.ActivateNextGeneration();
            Task<int> processExit = ObserveExitAsync(launch.Process);
            var publicationArmed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task processExitPublication = PublishProcessExitAsync(
                processExit,
                processGeneration,
                publicationArmed.Task);
            var pending = new ActiveWorker(
                processGeneration,
                launch,
                processExit,
                processExitPublication,
                Client: null);
            Volatile.Write(ref activeWorker, pending);
            publicationArmed.SetResult();

            using var connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                disposal.Token);
            Task<Stream> connection = launch.ConnectAsync(connectionCancellation.Token);
            Task winner = await Task.WhenAny(connection, processExit)
                .WaitAsync(cancellationToken).ConfigureAwait(false);
            if (winner == processExit)
            {
                connectionCancellation.Cancel();
                await ObserveCompletionAsync(connection).ConfigureAwait(false);
                throw new StreamWorkerProcessExitedException(await processExit.ConfigureAwait(false));
            }
            Stream pipe = await connection.ConfigureAwait(false);

            var client = new StreamWorkerNamedPipeClient(
                pipe,
                processExit,
                checked((uint)launch.Process.Id),
                processGeneration,
                eventBuffer.Writer,
                () => eventBuffer.HasSubscriber);
            Volatile.Write(ref activeWorker, pending with { Client = client });
            await client.InitializeAsync(connectionCancellation.Token).ConfigureAwait(false);
        }
        catch
        {
            if (launch is not null
                && ReferenceEquals(Volatile.Read(ref activeWorker)?.Launch, launch))
            {
                await ReleaseWorkerAsync().ConfigureAwait(false);
            }
            else if (launch is not null)
            {
                launch.Terminate();
                await launch.DisposeAsync().ConfigureAwait(false);
            }
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
            if (active is { Launch.Process.HasExited: false } && activeClient?.IsReady == true)
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
        }
        finally
        {
            await ReleaseWorkerAsync().ConfigureAwait(false);
        }
    }

    private async Task ReleaseUnreadyWorkerAsync()
    {
        await ReleaseWorkerAsync().ConfigureAwait(false);
    }

    private async Task ReleaseWorkerAsync()
    {
        ActiveWorker? old = Interlocked.Exchange(ref activeWorker, null);
        if (old is null)
        {
            return;
        }
        eventBuffer.Deactivate(old.ProcessGeneration);
        old.Launch.Terminate();
        try
        {
            if (old.Client is not null)
            {
                await old.Client.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            try
            {
                try
                {
                    _ = await old.ProcessExit.ConfigureAwait(false);
                }
                finally
                {
                    await old.ProcessExitPublication.ConfigureAwait(false);
                }
            }
            finally
            {
                await old.Launch.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<int> ObserveExitAsync(Process process)
    {
        await process.WaitForExitAsync().ConfigureAwait(false);
        return process.ExitCode;
    }

    private async Task PublishProcessExitAsync(
        Task<int> exit,
        long processGeneration,
        Task publicationArmed)
    {
        await publicationArmed.ConfigureAwait(false);
        int exitCode = await exit.ConfigureAwait(false);
        await beforeProcessExitPublication(processGeneration).ConfigureAwait(false);
        await eventBuffer.PublishProcessExitedAsync(
            processGeneration,
            exitCode,
            () => processExitPublicationStarted?.Invoke(processGeneration)).ConfigureAwait(false);
    }

    private static async Task ObserveCompletionAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private sealed record ActiveWorker(
        long ProcessGeneration,
        IStreamWorkerLaunch Launch,
        Task<int> ProcessExit,
        Task ProcessExitPublication,
        StreamWorkerNamedPipeClient? Client);
}

public sealed class StreamWorkerGenerationChangedException : Exception
{
    public StreamWorkerGenerationChangedException(long expectedProcessGeneration)
        : base("Beacon StreamWorker process generation changed.")
    {
        ExpectedProcessGeneration = expectedProcessGeneration;
    }

    public long ExpectedProcessGeneration { get; }
}

internal interface IStreamWorkerLaunchFactory
{
    IStreamWorkerLaunch Launch(StreamWorkerProcessHostOptions options);
}

internal interface IStreamWorkerLaunch : IAsyncDisposable
{
    Process Process { get; }

    Task<Stream> ConnectAsync(CancellationToken cancellationToken);

    void Terminate();
}

internal sealed class DefaultStreamWorkerLaunchFactory(InteractiveStreamWorkerLauncher launcher) :
    IStreamWorkerLaunchFactory
{
    public IStreamWorkerLaunch Launch(StreamWorkerProcessHostOptions options)
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
        PipeSecurity security = StreamWorkerProcessHost.CreatePipeSecurity(launcher.GetInteractiveUserSid());
        var pipe = NamedPipeServerStreamAcl.Create(
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
        var job = new WorkerJobObject();
        try
        {
            Process process = launcher.Launch(
                options.ExecutablePath,
                ["--pipe", $@"\\.\pipe\{pipeName}", "--identity", options.IdentityPath],
                job);
            return new DefaultStreamWorkerLaunch(process, pipe, job);
        }
        catch
        {
            job.Dispose();
            pipe.Dispose();
            throw;
        }
    }
}

internal sealed class DefaultStreamWorkerLaunch(
    Process process,
    NamedPipeServerStream pipe,
    WorkerJobObject job) : IStreamWorkerLaunch
{
    private int disposed;

    public Process Process { get; } = process;

    public async Task<Stream> ConnectAsync(CancellationToken cancellationToken)
    {
        await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
        return pipe;
    }

    public void Terminate() => job.Dispose();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }
        await pipe.DisposeAsync().ConfigureAwait(false);
        Process.Dispose();
    }
}

internal sealed class StreamWorkerEventBuffer : IAsyncDisposable
{
    private readonly Channel<StreamWorkerEvent> channel;
    private readonly CancellationTokenSource disposal = new();
    private readonly Lock exitGate = new();
    private readonly HashSet<long> publishedExitGenerations = [];
    private long nextGeneration;
    private long currentGeneration;
    private int hasSubscriber;
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

    public ChannelReader<StreamWorkerEvent> Reader
    {
        get
        {
            Volatile.Write(ref hasSubscriber, 1);
            return channel.Reader;
        }
    }

    public ChannelWriter<StreamWorkerEvent> Writer => channel.Writer;

    public bool HasSubscriber => Volatile.Read(ref hasSubscriber) != 0;

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

    public async ValueTask PublishProcessExitedAsync(
        long generation,
        int exitCode,
        Action? publicationStarted = null)
    {
        lock (exitGate)
        {
            if (!publishedExitGenerations.Add(generation))
            {
                return;
            }
        }
        if (!HasSubscriber)
        {
            return;
        }

        try
        {
            ValueTask write = channel.Writer.WriteAsync(
                new StreamWorkerProcessExited(generation, exitCode),
                disposal.Token);
            publicationStarted?.Invoke();
            await write.ConfigureAwait(false);
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

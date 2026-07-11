using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
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
    private StreamWorkerNamedPipeClient? client;
    private NamedPipeServerStream? pipe;
    private Process? process;
    private Task<int>? processExit;
    private WorkerJobObject? job;
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
        && client?.IsReady == true
        && process is { HasExited: false };

    public int ProcessId => process?.Id ?? 0;

    public bool HasExited => process is null || process.HasExited;

    public ReadOnlyMemory<byte> WorkerInstanceId => client?.WorkerInstanceId ?? ReadOnlyMemory<byte>.Empty;

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
        StreamWorkerNamedPipeClient activeClient = client
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

            newClient = new StreamWorkerNamedPipeClient(newPipe, newProcessExit, checked((uint)newProcess.Id));
            await newClient.InitializeAsync(cancellationToken).ConfigureAwait(false);

            pipe = newPipe;
            job = newJob;
            process = newProcess;
            processExit = newProcessExit;
            client = newClient;
        }
        catch
        {
            if (newClient is not null)
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
        StreamWorkerNamedPipeClient? activeClient = client;
        Task<int>? activeExit = processExit;
        try
        {
            if (process is { HasExited: false } && activeClient?.IsReady == true)
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
            else if (process is { HasExited: false })
            {
                job?.Dispose();
                if (activeExit is not null)
                {
                    _ = await activeExit.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            job?.Dispose();
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
        if (process is { HasExited: false })
        {
            Task<int>? activeExit = processExit;
            job?.Dispose();
            if (activeExit is not null)
            {
                _ = await activeExit.ConfigureAwait(false);
            }
        }

        if (!IsReady)
        {
            await ReleaseWorkerAsync().ConfigureAwait(false);
        }
    }

    private async Task ReleaseWorkerAsync()
    {
        StreamWorkerNamedPipeClient? oldClient = Interlocked.Exchange(ref client, null);
        NamedPipeServerStream? oldPipe = Interlocked.Exchange(ref pipe, null);
        Process? oldProcess = Interlocked.Exchange(ref process, null);
        WorkerJobObject? oldJob = Interlocked.Exchange(ref job, null);
        processExit = null;

        if (oldClient is not null)
        {
            await oldClient.DisposeAsync().ConfigureAwait(false);
        }
        else if (oldPipe is not null)
        {
            await oldPipe.DisposeAsync().ConfigureAwait(false);
        }
        oldJob?.Dispose();
        oldProcess?.Dispose();
    }

    private static async Task<int> ObserveExitAsync(Process process)
    {
        await process.WaitForExitAsync().ConfigureAwait(false);
        return process.ExitCode;
    }
}

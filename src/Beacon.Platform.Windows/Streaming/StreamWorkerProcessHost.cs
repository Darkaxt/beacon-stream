using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Beacon.StreamWorker.Contracts.Worker.V1;
using Microsoft.Win32.SafeHandles;

namespace Beacon.Platform.Windows.Streaming;

public sealed record StreamWorkerProcessHostOptions(string ExecutablePath)
{
    public static StreamWorkerProcessHostOptions CreateDefault() =>
        new(Path.Combine(AppContext.BaseDirectory, "Beacon.StreamWorker.exe"));
}

public interface IStreamWorkerHost
{
    bool IsReady { get; }

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

            await ReleaseExitedWorkerAsync().ConfigureAwait(false);
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
            newProcess = launcher.Launch(options.ExecutablePath, ["--pipe", pipePath], newJob);
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
            if (activeClient?.IsReady == true && process is { HasExited: false })
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

    private async Task ReleaseExitedWorkerAsync()
    {
        if (process is null || process.HasExited)
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

internal sealed class InteractiveStreamWorkerLauncher
{
    public SecurityIdentifier GetInteractiveUserSid()
    {
        if (Process.GetCurrentProcess().SessionId != 0)
        {
            return WindowsIdentity.GetCurrent().User
                ?? throw new InvalidOperationException("Current Windows identity has no user SID.");
        }

        using SafeAccessTokenHandle token = QueryActiveUserToken();
        using var identity = new WindowsIdentity(token.DangerousGetHandle());
        return identity.User
            ?? throw new InvalidOperationException("Active Windows identity has no user SID.");
    }

    public Process Launch(string executablePath, IReadOnlyList<string> arguments, WorkerJobObject job)
    {
        if (Process.GetCurrentProcess().SessionId == 0)
        {
            return LaunchInActiveSession(executablePath, arguments, job);
        }

        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            ErrorDialog = false,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Beacon StreamWorker process did not start.");
        try
        {
            job.Assign(process.SafeHandle);
            return process;
        }
        catch
        {
            process.Kill(entireProcessTree: true);
            process.Dispose();
            throw;
        }
    }

    private static Process LaunchInActiveSession(
        string executablePath,
        IReadOnlyList<string> arguments,
        WorkerJobObject job)
    {
        using SafeAccessTokenHandle impersonationToken = QueryActiveUserToken();
        if (!NativeMethods.DuplicateTokenEx(
                impersonationToken,
                NativeMethods.MaximumAllowed,
                IntPtr.Zero,
                NativeMethods.SecurityImpersonation,
                NativeMethods.TokenPrimary,
                out SafeAccessTokenHandle primaryToken))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create an active-user process token.");
        }
        using (primaryToken)
        {
            if (!NativeMethods.CreateEnvironmentBlock(out IntPtr environment, primaryToken, false))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the active-user environment.");
            }
            try
            {
                var startup = new NativeMethods.StartupInfo
                {
                    Size = Marshal.SizeOf<NativeMethods.StartupInfo>(),
                    Desktop = @"winsta0\default",
                };
                string commandLine = BuildCommandLine(executablePath, arguments);
                if (!NativeMethods.CreateProcessAsUser(
                        primaryToken,
                        null,
                        commandLine,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        false,
                        NativeMethods.CreateSuspended | NativeMethods.CreateUnicodeEnvironment,
                        environment,
                        Path.GetDirectoryName(executablePath),
                        ref startup,
                        out NativeMethods.ProcessInformation processInfo))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not launch StreamWorker in the active session.");
                }
                try
                {
                    job.Assign(processInfo.Process);
                    if (NativeMethods.ResumeThread(processInfo.Thread) == uint.MaxValue)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not resume StreamWorker.");
                    }
                    return Process.GetProcessById(checked((int)processInfo.ProcessId));
                }
                finally
                {
                    processInfo.Thread.Dispose();
                    processInfo.Process.Dispose();
                }
            }
            finally
            {
                _ = NativeMethods.DestroyEnvironmentBlock(environment);
            }
        }
    }

    private static SafeAccessTokenHandle QueryActiveUserToken()
    {
        uint sessionId = NativeMethods.WTSGetActiveConsoleSessionId();
        if (sessionId == uint.MaxValue || !NativeMethods.WTSQueryUserToken(sessionId, out SafeAccessTokenHandle token))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not obtain the active Windows user token.");
        }
        return token;
    }

    private static string BuildCommandLine(string executablePath, IReadOnlyList<string> arguments) =>
        string.Join(' ', new[] { executablePath }.Concat(arguments).Select(QuoteArgument));

    private static string QuoteArgument(string value) =>
        '"' + value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal) + '"';
}

internal sealed class WorkerJobObject : IDisposable
{
    private readonly SafeFileHandle handle;

    public WorkerJobObject()
    {
        handle = NativeMethods.CreateJobObject(IntPtr.Zero, null);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create StreamWorker Job Object.");
        }
        var limits = new NativeMethods.JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new NativeMethods.JobObjectBasicLimitInformation
            {
                LimitFlags = NativeMethods.JobObjectLimitKillOnJobClose,
            },
        };
        int size = Marshal.SizeOf<NativeMethods.JobObjectExtendedLimitInformation>();
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(limits, buffer, false);
            if (!NativeMethods.SetInformationJobObject(
                    handle,
                    NativeMethods.JobObjectExtendedLimitInformationClass,
                    buffer,
                    checked((uint)size)))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not configure StreamWorker Job Object.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Assign(SafeHandle processHandle)
    {
        if (!NativeMethods.AssignProcessToJobObject(handle, processHandle))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not assign StreamWorker to its Job Object.");
        }
    }

    public void Dispose() => handle.Dispose();
}

internal static class NativeMethods
{
    internal const uint MaximumAllowed = 0x02000000;
    internal const int SecurityImpersonation = 2;
    internal const int TokenPrimary = 1;
    internal const uint CreateSuspended = 0x00000004;
    internal const uint CreateUnicodeEnvironment = 0x00000400;
    internal const uint JobObjectLimitKillOnJobClose = 0x00002000;
    internal const int JobObjectExtendedLimitInformationClass = 9;

    [DllImport("kernel32.dll", EntryPoint = "WTSGetActiveConsoleSessionId")]
    internal static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WTSQueryUserToken(uint sessionId, out SafeAccessTokenHandle token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DuplicateTokenEx(
        SafeAccessTokenHandle existingToken,
        uint desiredAccess,
        IntPtr tokenAttributes,
        int impersonationLevel,
        int tokenType,
        out SafeAccessTokenHandle newToken);

    [DllImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreateEnvironmentBlock(
        out IntPtr environment,
        SafeAccessTokenHandle token,
        [MarshalAs(UnmanagedType.Bool)] bool inherit);

    [DllImport("userenv.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyEnvironmentBlock(IntPtr environment);

    [DllImport("advapi32.dll", EntryPoint = "CreateProcessAsUserW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreateProcessAsUser(
        SafeAccessTokenHandle token,
        string? applicationName,
        string commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string? currentDirectory,
        ref StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint ResumeThread(SafeFileHandle thread);

    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern SafeFileHandle CreateJobObject(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetInformationJobObject(
        SafeFileHandle job,
        int informationClass,
        IntPtr information,
        uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeHandle process);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct StartupInfo
    {
        internal int Size;
        internal string? Reserved;
        internal string? Desktop;
        internal string? Title;
        internal int X;
        internal int Y;
        internal int XSize;
        internal int YSize;
        internal int XCountChars;
        internal int YCountChars;
        internal int FillAttribute;
        internal int Flags;
        internal short ShowWindow;
        internal short Reserved2Size;
        internal IntPtr Reserved2;
        internal IntPtr StandardInput;
        internal IntPtr StandardOutput;
        internal IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInformation
    {
        internal SafeFileHandle Process;
        internal SafeFileHandle Thread;
        internal uint ProcessId;
        internal uint ThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JobObjectBasicLimitInformation
    {
        internal long PerProcessUserTimeLimit;
        internal long PerJobUserTimeLimit;
        internal uint LimitFlags;
        internal UIntPtr MinimumWorkingSetSize;
        internal UIntPtr MaximumWorkingSetSize;
        internal uint ActiveProcessLimit;
        internal UIntPtr Affinity;
        internal uint PriorityClass;
        internal uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct IoCounters
    {
        internal ulong ReadOperationCount;
        internal ulong WriteOperationCount;
        internal ulong OtherOperationCount;
        internal ulong ReadTransferCount;
        internal ulong WriteTransferCount;
        internal ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JobObjectExtendedLimitInformation
    {
        internal JobObjectBasicLimitInformation BasicLimitInformation;
        internal IoCounters IoInfo;
        internal UIntPtr ProcessMemoryLimit;
        internal UIntPtr JobMemoryLimit;
        internal UIntPtr PeakProcessMemoryUsed;
        internal UIntPtr PeakJobMemoryUsed;
    }
}

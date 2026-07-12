using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Beacon.Platform.Windows.Streaming;

internal sealed class InteractiveStreamWorkerLauncher
{
    private readonly IInteractiveStreamWorkerProcessApi processApi;

    public InteractiveStreamWorkerLauncher()
        : this(WindowsInteractiveStreamWorkerProcessApi.Instance)
    {
    }

    internal InteractiveStreamWorkerLauncher(IInteractiveStreamWorkerProcessApi processApi)
    {
        this.processApi = processApi;
    }

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

    internal static string QuoteArgument(string value)
    {
        if (value.Length > 0 && !value.Any(character => char.IsWhiteSpace(character) || character == '"'))
        {
            return value;
        }

        var result = new StringBuilder(value.Length + 2);
        result.Append('"');
        int backslashes = 0;
        foreach (char character in value)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }
            if (character == '"')
            {
                result.Append('\\', (backslashes * 2) + 1);
                result.Append('"');
                backslashes = 0;
                continue;
            }
            result.Append('\\', backslashes);
            backslashes = 0;
            result.Append(character);
        }
        result.Append('\\', backslashes * 2);
        result.Append('"');
        return result.ToString();
    }

    private Process LaunchInActiveSession(
        string executablePath,
        IReadOnlyList<string> arguments,
        WorkerJobObject job)
    {
        using SafeAccessTokenHandle impersonationToken = QueryActiveUserToken();
        if (!StreamWorkerNativeMethods.DuplicateTokenEx(
                impersonationToken,
                StreamWorkerNativeMethods.MaximumAllowed,
                IntPtr.Zero,
                StreamWorkerNativeMethods.SecurityImpersonation,
                StreamWorkerNativeMethods.TokenPrimary,
                out SafeAccessTokenHandle primaryToken))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create an active-user process token.");
        }
        using (primaryToken)
        {
            if (!StreamWorkerNativeMethods.CreateEnvironmentBlock(out IntPtr environment, primaryToken, false))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the active-user environment.");
            }
            try
            {
                var startup = new StreamWorkerNativeMethods.StartupInfo
                {
                    Size = Marshal.SizeOf<StreamWorkerNativeMethods.StartupInfo>(),
                    Desktop = @"winsta0\default",
                };
                string commandLine = BuildCommandLine(executablePath, arguments);
                if (!StreamWorkerNativeMethods.CreateProcessAsUser(
                        primaryToken,
                        null,
                        commandLine,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        false,
                        StreamWorkerNativeMethods.CreateSuspended
                            | StreamWorkerNativeMethods.CreateUnicodeEnvironment,
                        environment,
                        Path.GetDirectoryName(executablePath),
                        ref startup,
                        out StreamWorkerNativeMethods.ProcessInformation processInfo))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "Could not launch StreamWorker in the active session.");
                }

                using var processHandle = new SafeFileHandle(processInfo.Process, ownsHandle: true);
                using var threadHandle = new SafeFileHandle(processInfo.Thread, ownsHandle: true);
                AssignAndResumeSuspendedProcess(processHandle, threadHandle, job.Assign);
                return Process.GetProcessById(checked((int)processInfo.ProcessId));
            }
            finally
            {
                _ = StreamWorkerNativeMethods.DestroyEnvironmentBlock(environment);
            }
        }
    }

    internal void AssignAndResumeSuspendedProcess(
        SafeFileHandle processHandle,
        SafeFileHandle threadHandle,
        Action<SafeHandle> assignToJob)
    {
        try
        {
            assignToJob(processHandle);
        }
        catch
        {
            processApi.TerminateProcess(processHandle);
            processApi.WaitForExit(processHandle);
            throw;
        }

        if (processApi.ResumeThread(threadHandle) == uint.MaxValue)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not resume StreamWorker.");
        }
    }

    private static SafeAccessTokenHandle QueryActiveUserToken()
    {
        uint sessionId = StreamWorkerNativeMethods.WTSGetActiveConsoleSessionId();
        if (sessionId == uint.MaxValue
            || !StreamWorkerNativeMethods.WTSQueryUserToken(sessionId, out SafeAccessTokenHandle token))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Could not obtain the active Windows user token.");
        }
        return token;
    }

    private static string BuildCommandLine(string executablePath, IReadOnlyList<string> arguments) =>
        string.Join(' ', new[] { executablePath }.Concat(arguments).Select(QuoteArgument));
}

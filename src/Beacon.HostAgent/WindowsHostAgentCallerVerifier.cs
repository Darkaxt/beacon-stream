using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace Beacon.HostAgent;

internal sealed class WindowsHostAgentCallerVerifier(SecurityIdentifier owner) :
    IHostAgentCallerVerifier
{
    private const int ComputerNameCapacity = 256;
    private const int ErrorPipeLocal = 229;

    public HostAgentCallerVerification Verify(NamedPipeServerStream pipe)
    {
        ArgumentNullException.ThrowIfNull(pipe);

        if (!NativeMethods.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint processId))
        {
            return HostAgentCallerVerification.Reject(
                $"Unable to identify Host Agent caller process. Win32={Marshal.GetLastWin32Error()}.");
        }

        if (!IsLocalClient(pipe, out string computerDiagnostic))
        {
            return HostAgentCallerVerification.Reject(computerDiagnostic, processId);
        }

        SecurityIdentifier? callerSid = null;
        try
        {
            pipe.RunAsClient(() =>
            {
                using WindowsIdentity? identity = WindowsIdentity.GetCurrent(ifImpersonating: true);
                callerSid = identity?.User;
            });
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or Win32Exception)
        {
            return HostAgentCallerVerification.Reject(
                "Unable to authenticate Host Agent caller token.",
                processId);
        }

        if (callerSid is null || !callerSid.Equals(owner))
        {
            return HostAgentCallerVerification.Reject(
                "Host Agent caller does not match the configured owner SID.",
                processId,
                callerSid);
        }

        try
        {
            using Process caller = Process.GetProcessById(checked((int)processId));
            if (caller.SessionId != Process.GetCurrentProcess().SessionId)
            {
                return HostAgentCallerVerification.Reject(
                    "Host Agent caller is not in the Agent interactive session.",
                    processId,
                    callerSid);
            }
        }
        catch (Exception error) when (
            error is ArgumentException or InvalidOperationException or Win32Exception or OverflowException)
        {
            return HostAgentCallerVerification.Reject(
                "Unable to verify Host Agent caller session.",
                processId,
                callerSid);
        }

        return HostAgentCallerVerification.Accept(processId, callerSid);
    }

    private static bool IsLocalClient(
        NamedPipeServerStream pipe,
        out string diagnostic)
    {
        var computerName = new StringBuilder(ComputerNameCapacity);
        if (!NativeMethods.GetNamedPipeClientComputerName(
                pipe.SafePipeHandle,
                computerName,
                checked((uint)computerName.Capacity)))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == ErrorPipeLocal)
            {
                diagnostic = string.Empty;
                return true;
            }

            diagnostic =
                $"Unable to verify Host Agent caller computer. Win32={error}.";
            return false;
        }

        string callerComputer = computerName.ToString().TrimStart('\\');
        if (!string.Equals(callerComputer, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            diagnostic = "Remote Host Agent callers are rejected.";
            return false;
        }

        diagnostic = string.Empty;
        return true;
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetNamedPipeClientProcessId(
            Microsoft.Win32.SafeHandles.SafePipeHandle pipe,
            out uint clientProcessId);

        [DllImport(
            "kernel32.dll",
            EntryPoint = "GetNamedPipeClientComputerNameW",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetNamedPipeClientComputerName(
            Microsoft.Win32.SafeHandles.SafePipeHandle pipe,
            StringBuilder clientComputerName,
            uint clientComputerNameLength);
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Beacon.Platform.Windows.Displays;

internal interface IWindowsExplorerShellExecutor
{
    DisplayApiResult Execute(string fileName, string arguments);
}

internal sealed record WindowsExplorerProcessCandidate(
    uint ProcessId,
    int SessionId,
    DateTime StartTimeUtc);

internal sealed class WindowsExplorerShellExecutor(
    WindowsChildProcessCompletionGate? completionGate = null) : IWindowsExplorerShellExecutor
{
    private const uint ProcessCreateProcess = 0x00000080;
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint CreateNoWindow = 0x08000000;
    private static readonly UIntPtr ParentProcessAttribute = new(0x00020000);
    private readonly WindowsChildProcessCompletionGate completionGate =
        completionGate ?? new WindowsChildProcessCompletionGate();

    public DisplayApiResult Execute(string fileName, string arguments)
    {
        uint? shellProcessId = ResolveExplorerProcessId();
        if (shellProcessId is null)
        {
            return DisplayApiResult.Fail(
                "Windows Explorer shell process is unavailable in the Host Agent session.");
        }

        IntPtr shellProcess = NativeMethods.OpenProcess(
            ProcessCreateProcess,
            inheritHandle: false,
            shellProcessId.Value);
        if (shellProcess == IntPtr.Zero)
        {
            return DisplayApiResult.Fail(
                $"Unable to open Windows Explorer as the display transition parent. Win32={Marshal.GetLastWin32Error()}.");
        }

        IntPtr attributeList = IntPtr.Zero;
        try
        {
            IntPtr attributeListSize = IntPtr.Zero;
            _ = NativeMethods.InitializeProcThreadAttributeList(
                IntPtr.Zero,
                attributeCount: 1,
                flags: 0,
                ref attributeListSize);
            if (attributeListSize == IntPtr.Zero)
            {
                return DisplayApiResult.Fail(
                    $"Unable to size the Explorer process attribute list. Win32={Marshal.GetLastWin32Error()}.");
            }

            attributeList = Marshal.AllocHGlobal(attributeListSize);
            if (!NativeMethods.InitializeProcThreadAttributeList(
                attributeList,
                attributeCount: 1,
                flags: 0,
                ref attributeListSize))
            {
                return DisplayApiResult.Fail(
                    $"Unable to initialize the Explorer process attribute list. Win32={Marshal.GetLastWin32Error()}.");
            }

            if (!NativeMethods.UpdateProcThreadAttribute(
                attributeList,
                flags: 0,
                ParentProcessAttribute,
                ref shellProcess,
                (IntPtr)IntPtr.Size,
                IntPtr.Zero,
                IntPtr.Zero))
            {
                return DisplayApiResult.Fail(
                    $"Unable to bind the display transition to Windows Explorer. Win32={Marshal.GetLastWin32Error()}.");
            }

            var startup = new StartupInfoEx
            {
                StartupInfo = new StartupInfo
                {
                    Size = Marshal.SizeOf<StartupInfoEx>()
                },
                AttributeList = attributeList
            };
            var commandLine = new StringBuilder($"\"{fileName}\" {arguments}");
            if (!NativeMethods.CreateProcess(
                fileName,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                inheritHandles: false,
                ExtendedStartupInfoPresent | CreateNoWindow,
                IntPtr.Zero,
                Path.GetDirectoryName(fileName),
                ref startup,
                out ProcessInformation process))
            {
                return DisplayApiResult.Fail(
                    $"Unable to launch the display transition under Windows Explorer. Win32={Marshal.GetLastWin32Error()}.");
            }

            _ = NativeMethods.CloseHandle(process.Thread);
            try
            {
                return completionGate.Wait(process.Process);
            }
            finally
            {
                _ = NativeMethods.CloseHandle(process.Process);
            }
        }
        catch (Exception error)
        {
            return DisplayApiResult.Fail(
                $"Unable to request extended topology under Windows Explorer: {error.Message}");
        }
        finally
        {
            if (attributeList != IntPtr.Zero)
            {
                NativeMethods.DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }

            _ = NativeMethods.CloseHandle(shellProcess);
        }
    }

    internal static uint? SelectExplorerProcessId(
        int currentSessionId,
        IReadOnlyList<WindowsExplorerProcessCandidate> candidates) =>
        candidates
            .Where(candidate => candidate.SessionId == currentSessionId)
            .OrderBy(candidate => candidate.StartTimeUtc)
            .ThenBy(candidate => candidate.ProcessId)
            .Select(candidate => (uint?)candidate.ProcessId)
            .FirstOrDefault();

    private static uint? ResolveExplorerProcessId()
    {
        IntPtr shellWindow = NativeMethods.GetShellWindow();
        if (shellWindow != IntPtr.Zero)
        {
            _ = NativeMethods.GetWindowThreadProcessId(shellWindow, out uint shellProcessId);
            if (shellProcessId != 0)
            {
                return shellProcessId;
            }
        }

        int currentSessionId = Process.GetCurrentProcess().SessionId;
        var candidates = new List<WindowsExplorerProcessCandidate>();
        foreach (Process process in Process.GetProcessesByName("explorer"))
        {
            try
            {
                candidates.Add(new WindowsExplorerProcessCandidate(
                    checked((uint)process.Id),
                    process.SessionId,
                    process.StartTime.ToUniversalTime()));
            }
            catch (Exception error) when (
                error is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return SelectExplorerProcessId(currentSessionId, candidates);
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(
            uint desiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
            uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool InitializeProcThreadAttributeList(
            IntPtr attributeList,
            int attributeCount,
            int flags,
            ref IntPtr size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UpdateProcThreadAttribute(
            IntPtr attributeList,
            uint flags,
            UIntPtr attribute,
            ref IntPtr value,
            IntPtr size,
            IntPtr previousValue,
            IntPtr returnSize);

        [DllImport("kernel32.dll")]
        public static extern void DeleteProcThreadAttributeList(IntPtr attributeList);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreateProcess(
            string applicationName,
            StringBuilder commandLine,
            IntPtr processAttributes,
            IntPtr threadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
            uint creationFlags,
            IntPtr environment,
            string? currentDirectory,
            ref StartupInfoEx startupInfo,
            out ProcessInformation processInformation);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2Size;
        public IntPtr Reserved2;
        public IntPtr StandardInput;
        public IntPtr StandardOutput;
        public IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public uint ProcessId;
        public uint ThreadId;
    }
}

internal sealed class WindowsShellExtendedTopologyActivator(
    IWindowsExplorerShellExecutor? shell = null,
    string? userTopologyHelperExecutable = null,
    Action<string>? diagnostic = null)
{
    private readonly IWindowsExplorerShellExecutor shell =
        shell ?? new WindowsExplorerShellExecutor();

    public DisplayApiResult Apply()
    {
        if (!string.IsNullOrWhiteSpace(userTopologyHelperExecutable))
        {
            DisplayApiResult helperResult = shell.Execute(
                userTopologyHelperExecutable,
                WindowsUserDisplayTopologyTransition.CommandArgument);
            WriteDiagnostic(
                $"topology-launch command=user-helper executable={userTopologyHelperExecutable} success={helperResult.Success} error={helperResult.Error ?? "none"}");
            return helperResult;
        }

        string displaySwitch = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "DisplaySwitch.exe");
        DisplayApiResult displaySwitchResult = shell.Execute(displaySwitch, "/extend");
        WriteDiagnostic(
            $"topology-launch command=display-switch executable={displaySwitch} success={displaySwitchResult.Success} error={displaySwitchResult.Error ?? "none"}");
        return displaySwitchResult;
    }

    private void WriteDiagnostic(string message)
    {
        try
        {
            diagnostic?.Invoke(message);
        }
        catch
        {
        }
    }
}

using System.Reflection;
using System.Runtime.InteropServices;

namespace Beacon.Platform.Windows.Displays;

internal interface IWindowsExplorerShellExecutor
{
    DisplayApiResult Execute(string fileName, string arguments);
}

internal sealed class WindowsExplorerShellExecutor : IWindowsExplorerShellExecutor
{
    public DisplayApiResult Execute(string fileName, string arguments)
    {
        Type? shellType = Type.GetTypeFromProgID("Shell.Application", throwOnError: false);
        if (shellType is null)
        {
            return DisplayApiResult.Fail("Windows Explorer shell automation is unavailable.");
        }

        object? shell = null;
        try
        {
            shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("Windows Explorer shell automation did not start.");
            _ = shellType.InvokeMember(
                "ShellExecute",
                BindingFlags.InvokeMethod,
                binder: null,
                shell,
                [fileName, arguments, string.Empty, "open", 0]);
            return DisplayApiResult.Ok();
        }
        catch (Exception error)
        {
            return DisplayApiResult.Fail(
                $"Unable to request extended topology through Windows Explorer: {error.Message}");
        }
        finally
        {
            if (shell is not null && Marshal.IsComObject(shell))
            {
                _ = Marshal.FinalReleaseComObject(shell);
            }
        }
    }
}

internal sealed class WindowsShellExtendedTopologyActivator(
    IWindowsExplorerShellExecutor? shell = null)
{
    private readonly IWindowsExplorerShellExecutor shell =
        shell ?? new WindowsExplorerShellExecutor();

    public DisplayApiResult Apply()
    {
        string displaySwitch = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "DisplaySwitch.exe");
        return shell.Execute(displaySwitch, "/extend");
    }
}

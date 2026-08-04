using Beacon.Platform.Windows.Displays;

namespace Beacon.HostAgent;

internal static class HostAgentUserDisplayTopologyCommand
{
    private const int TransitionFailedExitCode = 5;
    private const int ElevatedProcessExitCode = 6;

    public static bool TryRun(
        string[] args,
        Func<bool> isElevated,
        Func<DisplayApiResult> applyExtended,
        out int exitCode)
    {
        if (args.Length != 1
            || !string.Equals(
                args[0],
                WindowsUserDisplayTopologyTransition.CommandArgument,
                StringComparison.Ordinal))
        {
            exitCode = 0;
            return false;
        }

        if (isElevated())
        {
            exitCode = ElevatedProcessExitCode;
            return true;
        }

        DisplayApiResult result = applyExtended();
        exitCode = result.Success ? 0 : TransitionFailedExitCode;
        return true;
    }
}

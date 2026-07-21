using Beacon.Platform.Windows.Displays;

namespace Beacon.HostAgent;

internal static class HostAgentUserDisplayTopologyCommand
{
    private const int TransitionFailedExitCode = 5;

    public static bool TryRun(
        string[] args,
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

        DisplayApiResult result = applyExtended();
        exitCode = result.Success ? 0 : TransitionFailedExitCode;
        return true;
    }
}

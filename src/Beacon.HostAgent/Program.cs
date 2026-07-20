using System.Diagnostics;
using System.Security.Principal;
using Beacon.Platform.Windows.Displays;

namespace Beacon.HostAgent;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            HostAgentOptions options = HostAgentOptions.Parse(args);
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            if (identity.User is null || !identity.User.Equals(options.Owner))
            {
                return Fail("Host Agent owner SID does not match the current Windows user.", 2);
            }

            if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            {
                return Fail("Host Agent must run elevated.", 3);
            }

            if (Process.GetCurrentProcess().SessionId == 0)
            {
                return Fail("Host Agent must run in the interactive user session.", 4);
            }

            using var instance = new Mutex(
                initiallyOwned: true,
                $"Local\\BeaconStream.HostAgent.{options.Owner.Value}",
                out bool ownsInstance);
            if (!ownsInstance)
            {
                return 0;
            }

            var displayNames = new WindowsDisplayNameMap(WindowsDisplayNameMapStore.Default);
            await using var displayApi = new WindowsDisplayApi(displayNames);
            var executor = new WindowsHostAgentDisplayExecutor(displayApi, displayNames);
            var dispatcher = new HostAgentDispatcher(executor);
            var server = new HostAgentPipeServer(options.Owner, dispatcher);
            HostAgentDiagnostics.Write(
                $"started owner={options.Owner.Value} session={Process.GetCurrentProcess().SessionId}");
            await server.RunAsync(CancellationToken.None).ConfigureAwait(false);
            GC.KeepAlive(instance);
            return 0;
        }
        catch (Exception error)
        {
            HostAgentDiagnostics.Write($"fatal type={error.GetType().Name} message={error.Message}");
            return 1;
        }
    }

    private static int Fail(string message, int exitCode)
    {
        HostAgentDiagnostics.Write($"startup-rejected message={message}");
        return exitCode;
    }
}

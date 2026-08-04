using System.Diagnostics;
using System.Security.Principal;
using Beacon.HostAgent.Update;

namespace Beacon.HostAgent.Bootstrap;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            BootstrapOptions options = BootstrapOptions.Parse(args);
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            if (identity.User is null || !identity.User.Equals(options.Owner))
            {
                return 2;
            }
            if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            {
                return 3;
            }
            if (Process.GetCurrentProcess().SessionId == 0)
            {
                return 4;
            }

            using var instance = new Mutex(
                initiallyOwned: true,
                $"Local\\BeaconStream.HostAgent.Bootstrap.{options.Owner.Value}",
                out bool ownsInstance);
            if (!ownsInstance)
            {
                return 0;
            }

            BootstrapStorage storage = BootstrapStorage.Default;
            Version bootstrapVersion = typeof(Program).Assembly.GetName().Version
                ?? new Version(1, 0, 0);
            var validator = new HostAgentUpdatePackageValidator(
                HostAgentUpdateTrust.PublicKeyPem,
                bootstrapVersion);
            var state = new HostAgentUpdateStateStore(storage.StateRoot);
            var journal = new HostAgentUpdateJournal(storage.TransactionsRoot);
            var activator = new HostAgentUpdateActivator(storage, validator);
            var supervisor = new BootstrapSupervisor(
                options.Owner,
                storage,
                state,
                journal,
                activator,
                new WindowsBootstrapPlatform());
            BootstrapDiagnostics.Write(
                $"started owner={options.Owner.Value} session={Process.GetCurrentProcess().SessionId}");
            int result = await supervisor.RunAsync().ConfigureAwait(false);
            BootstrapDiagnostics.Write($"stopped exitCode={result}");
            GC.KeepAlive(instance);
            return result;
        }
        catch (Exception error)
        {
            BootstrapDiagnostics.Write(
                $"fatal type={error.GetType().Name} message={error.Message}");
            return 1;
        }
    }
}

using System.Diagnostics;
using System.Security.Principal;
using Beacon.HostAgent.DriverUpdates;
using Beacon.HostAgent.HostUpdates;
using Beacon.HostAgent.Update;
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
            HostAgentDriverUpdateStorage storage = HostAgentDriverUpdateStorage.Default;
            var signatureVerifier = new WindowsSudoVdaSignatureVerifier();
            string activeDriverBinary = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32",
                "drivers",
                "UMDF",
                "SudoVDA.dll");
            SudoVdaPackagePolicy policy = new SudoVdaPackagePolicyStore(
                storage.DriverPolicyPath).LoadOrCreate(
                    signatureVerifier.Verify(activeDriverBinary));
            var driverValidator = new SudoVdaPackageValidator(
                new SudoVdaPackagePaths(storage.Inbox, storage.Staged),
                policy,
                signatureVerifier);
            var driverPlatform = new WindowsSudoVdaDriverPlatform(
                new WindowsSudoVdaDeviceInventory(),
                signatureVerifier,
                new HostAgentSudoVdaDisplayUpdateGuard(executor),
                new WindowsPnpUtilRunner());
            var driverUpdates = new SudoVdaUpdateCoordinator(
                driverValidator,
                new SudoVdaUpdateJournal(storage.Transactions),
                driverPlatform,
                storage.InstalledEvidence);
            HostAgentUpdateStorage hostUpdateStorage = HostAgentUpdateStorage.Default;
            var hostUpdateState = new HostAgentUpdateStateStore(hostUpdateStorage.State);
            var hostUpdates = new HostAgentUpdateCoordinator(
                hostUpdateStorage,
                new HostAgentUpdatePackageValidator(
                    HostAgentUpdateTrust.PublicKeyPem,
                new Version(1, 0, 0)),
                new HostAgentUpdateJournal(hostUpdateStorage.Transactions),
                hostUpdateState,
                new HostAgentDisplayUpdateGuard(executor));
            var dispatcher = new HostAgentDispatcher(executor, driverUpdates, hostUpdates);
            var server = new HostAgentPipeServer(options.Owner, dispatcher);
            HostAgentDiagnostics.Write(
                $"started owner={options.Owner.Value} session={Process.GetCurrentProcess().SessionId}");
            Func<CancellationToken, Task>? readiness = options.BootstrapReadyPipe is not null
                && options.VersionId is not null
                    ? cancellationToken => BootstrapReadinessClient.SignalAsync(
                        options.BootstrapReadyPipe,
                        options.VersionId,
                        cancellationToken)
                    : null;
            HostAgentServerExitReason reason = await server.RunAsync(
                readiness,
                CancellationToken.None).ConfigureAwait(false);
            GC.KeepAlive(instance);
            return reason == HostAgentServerExitReason.ApplyUpdate
                ? HostAgentExitCodes.ApplyUpdate
                : 0;
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

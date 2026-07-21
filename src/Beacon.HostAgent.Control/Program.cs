using System.Security.Principal;
using System.Text;
using Beacon.HostAgent.Contracts;

namespace Beacon.HostAgent.Control;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            HostAgentControlOptions options = HostAgentControlOptions.Parse(args);
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            SecurityIdentifier owner = identity.User
                ?? throw new InvalidOperationException("Current Windows identity has no SID.");
            var client = new HostAgentControlClient(owner);
            HostAgentResponse response = options.Command switch
            {
                HostAgentControlCommand.Status => await client.SendAsync(
                    HostAgentOperation.GetStatus,
                    new EmptyHostAgentPayload(),
                    CancellationToken.None).ConfigureAwait(false),
                HostAgentControlCommand.Install => await client.SendAsync(
                    HostAgentOperation.InstallStagedHostAgentPackage,
                    new InstallStagedHostAgentPackagePayload(
                        options.PackageId!,
                        options.TransactionId!.Value),
                    CancellationToken.None).ConfigureAwait(false),
                HostAgentControlCommand.Query => await client.SendAsync(
                    HostAgentOperation.QueryHostAgentUpdate,
                    new QueryHostAgentUpdatePayload(options.TransactionId!.Value),
                    CancellationToken.None).ConfigureAwait(false),
                _ => throw new InvalidOperationException("Unsupported Host Agent control command.")
            };
            Console.WriteLine(Encoding.UTF8.GetString(HostAgentProtocol.SerializeResponse(response)));
            return response.Success ? 0 : 3;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 2;
        }
    }
}

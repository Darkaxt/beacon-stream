using Beacon.DisplayProbe;
using Beacon.Platform.Windows.HostAgent;
using System.Security.Principal;

using WindowsIdentity identity = WindowsIdentity.GetCurrent();
SecurityIdentifier owner = identity.User
    ?? throw new InvalidOperationException("The display probe user has no Windows SID.");
await using var connection = new HostAgentConnection(owner);
Task connectionRun = connection.RunAsync(CancellationToken.None);

while (!connection.State.Connected)
{
    Task<HostAgentConnectionState> stateChanged = connection.WaitForStateChangeAsync(
        connection.State.Revision,
        CancellationToken.None);
    Task completed = await Task.WhenAny(stateChanged, connectionRun);
    if (ReferenceEquals(completed, connectionRun))
    {
        await connectionRun;
        Console.Error.WriteLine("Beacon Host Agent connection stopped before becoming ready.");
        return 2;
    }

    _ = await stateChanged;
}

var api = new HostAgentWindowsDisplayApi(connection);
int result = await DisplayProbeApp.RunAsync(api, args, Console.Out, Console.Error);
await connection.DisposeAsync();
await connectionRun;
return result;

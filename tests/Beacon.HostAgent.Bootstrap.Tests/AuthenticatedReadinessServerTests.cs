using System.IO.Pipes;
using System.Security.Principal;
using Beacon.HostAgent.Update;

namespace Beacon.HostAgent.Bootstrap.Tests;

public sealed class AuthenticatedReadinessServerTests
{
    [Fact]
    public async Task ExactPipeClientProcessAndVersionAreAccepted()
    {
        SecurityIdentifier owner = CurrentOwner();
        await using var server = new AuthenticatedReadinessServer(owner, "agent-current");
        Task waiting = server.WaitAsync(Environment.ProcessId);

        await SendAsync(server.PipeName, "agent-current", Environment.ProcessId);
        await waiting;
    }

    [Fact]
    public async Task DifferentExpectedChildProcessIsRejected()
    {
        SecurityIdentifier owner = CurrentOwner();
        await using var server = new AuthenticatedReadinessServer(owner, "agent-current");
        Task waiting = server.WaitAsync(Environment.ProcessId + 1);

        await SendAsync(server.PipeName, "agent-current", Environment.ProcessId);
        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() => waiting);

        Assert.Contains("process", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task SendAsync(string pipeName, string versionId, int processId)
    {
        await using var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.Out,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough);
        await client.ConnectAsync(CancellationToken.None);
        await HostAgentBootstrapReadinessProtocol.WriteAsync(
            client,
            new HostAgentBootstrapReadiness(versionId, processId),
            CancellationToken.None);
    }

    private static SecurityIdentifier CurrentOwner() =>
        WindowsIdentity.GetCurrent().User
        ?? throw new InvalidOperationException("Current Windows identity has no SID.");
}

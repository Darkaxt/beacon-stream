using System.IO.Pipes;
using Beacon.HostAgent.Update;

namespace Beacon.HostAgent.Tests;

public sealed class BootstrapReadinessClientTests
{
    [Fact]
    public async Task ClientSendsVersionAndCurrentProcessIdentity()
    {
        string pipeName = $"beacon-bootstrap-readiness-test-{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.In,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        Task<HostAgentBootstrapReadiness> receiving = ReceiveAsync(server);

        await BootstrapReadinessClient.SignalAsync(
            pipeName,
            "agent-0123456789abcdef",
            CancellationToken.None);
        HostAgentBootstrapReadiness readiness = await receiving;

        Assert.Equal("agent-0123456789abcdef", readiness.VersionId);
        Assert.Equal(Environment.ProcessId, readiness.ProcessId);
    }

    private static async Task<HostAgentBootstrapReadiness> ReceiveAsync(
        NamedPipeServerStream server)
    {
        await server.WaitForConnectionAsync(CancellationToken.None);
        return await HostAgentBootstrapReadinessProtocol.ReadAsync(
            server,
            CancellationToken.None);
    }
}

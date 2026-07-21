using System.IO.Pipes;
using Beacon.HostAgent.Contracts;

namespace Beacon.HostAgent.Control.Tests;

public sealed class HostAgentControlClientTests
{
    [Fact]
    public async Task InstallSendsOnlyTypedIdentifiersAndReturnsCorrelatedResponse()
    {
        string pipeName = $"beacon-host-agent-control-test-{Guid.NewGuid():N}";
        Guid transactionId = Guid.NewGuid();
        Task<HostAgentRequest> server = ServeOneAsync(pipeName);
        var client = new HostAgentControlClient(pipeName);

        HostAgentResponse response = await client.SendAsync(
            HostAgentOperation.InstallStagedHostAgentPackage,
            new InstallStagedHostAgentPackagePayload(
                "agent-0123456789abcdef",
                transactionId),
            CancellationToken.None);
        HostAgentRequest request = await server;

        Assert.True(response.Success);
        Assert.Equal(request.RequestId, response.RequestId);
        InstallStagedHostAgentPackagePayload payload =
            HostAgentProtocol.ReadPayload<InstallStagedHostAgentPackagePayload>(request.Payload);
        Assert.Equal("agent-0123456789abcdef", payload.PackageId);
        Assert.Equal(transactionId, payload.TransactionId);
        Assert.DoesNotContain("path", request.Payload.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<HostAgentRequest> ServeOneAsync(string pipeName)
    {
        await using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough);
        await server.WaitForConnectionAsync(CancellationToken.None);
        HostAgentRequest request = await HostAgentFrameCodec.ReadRequestAsync(
            server,
            CancellationToken.None);
        var response = new HostAgentResponse(
            HostAgentProtocol.CurrentVersion,
            request.RequestId,
            Success: true,
            ResultCode: "ok",
            Diagnostic: string.Empty,
            HostAgentProtocol.CreatePayload(new EmptyHostAgentPayload()));
        await HostAgentFrameCodec.WriteResponseAsync(server, response, CancellationToken.None);
        return request;
    }
}

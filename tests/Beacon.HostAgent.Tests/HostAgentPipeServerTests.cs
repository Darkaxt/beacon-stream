using System.IO.Pipes;
using System.Security.Principal;
using Beacon.HostAgent.Contracts;

namespace Beacon.HostAgent.Tests;

public sealed class HostAgentPipeServerTests
{
    [Fact]
    public async Task VerifiedLocalClientReceivesCorrelatedResponse()
    {
        SecurityIdentifier owner = CurrentOwner();
        int dispatchCount = 0;
        var server = new HostAgentPipeServer(
            owner,
            (request, _) =>
            {
                dispatchCount++;
                return Task.FromResult(Success(request));
            });
        using var shutdown = new CancellationTokenSource();
        Task serving = server.RunAsync(shutdown.Token);
        await using var client = CreateClient(owner);
        await client.ConnectAsync(CancellationToken.None);
        HostAgentRequest request = Request();

        await HostAgentFrameCodec.WriteRequestAsync(client, request, CancellationToken.None);
        HostAgentResponse response = await HostAgentFrameCodec.ReadResponseAsync(
            client,
            CancellationToken.None);
        shutdown.Cancel();
        await serving;

        Assert.True(response.Success);
        Assert.Equal(request.RequestId, response.RequestId);
        Assert.Equal(1, dispatchCount);
    }

    [Fact]
    public async Task RejectedCallerIsClosedWithoutDispatch()
    {
        SecurityIdentifier owner = CurrentOwner();
        int dispatchCount = 0;
        var server = new HostAgentPipeServer(
            owner,
            (request, _) =>
            {
                dispatchCount++;
                return Task.FromResult(Success(request));
            },
            new RejectingCallerVerifier());
        using var shutdown = new CancellationTokenSource();
        Task serving = server.RunAsync(shutdown.Token);
        await using var client = CreateClient(owner);
        await client.ConnectAsync(CancellationToken.None);

        await Assert.ThrowsAsync<EndOfStreamException>(() =>
            HostAgentFrameCodec.ReadResponseAsync(client, CancellationToken.None));
        shutdown.Cancel();
        await serving;

        Assert.Equal(0, dispatchCount);
    }

    private static NamedPipeClientStream CreateClient(SecurityIdentifier owner) =>
        new(
            ".",
            HostAgentPipeIdentity.CreateName(owner),
            PipeDirection.InOut,
            PipeOptions.Asynchronous,
            TokenImpersonationLevel.Identification);

    private static SecurityIdentifier CurrentOwner() =>
        WindowsIdentity.GetCurrent().User
        ?? throw new InvalidOperationException("Current Windows identity has no SID.");

    private static HostAgentRequest Request() =>
        new(
            HostAgentProtocol.CurrentVersion,
            Guid.NewGuid(),
            HostAgentOperation.GetStatus,
            HostAgentProtocol.CreatePayload(new EmptyHostAgentPayload()));

    private static HostAgentResponse Success(HostAgentRequest request) =>
        new(
            HostAgentProtocol.CurrentVersion,
            request.RequestId,
            Success: true,
            ResultCode: "ok",
            Diagnostic: string.Empty,
            HostAgentProtocol.CreatePayload(new EmptyHostAgentPayload()));

    private sealed class RejectingCallerVerifier : IHostAgentCallerVerifier
    {
        public HostAgentCallerVerification Verify(NamedPipeServerStream pipe) =>
            HostAgentCallerVerification.Reject("rejected by test");
    }
}

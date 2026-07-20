using Beacon.HostAgent.Contracts;

namespace Beacon.HostAgent.Tests;

public sealed class HostAgentConnectionSessionTests
{
    [Fact]
    public async Task UnauthorizedConnectionIsDroppedBeforeReadingOrDispatching()
    {
        HostAgentRequest request = Request(HostAgentOperation.GetStatus);
        await using MemoryStream input = await FramedInputAsync(request);
        await using var output = new MemoryStream();
        int dispatchCount = 0;
        var session = new HostAgentConnectionSession((value, _) =>
        {
            dispatchCount++;
            return Task.FromResult(Success(value));
        });

        await session.RunAsync(input, output, callerAccepted: false, CancellationToken.None);

        Assert.Equal(0, dispatchCount);
        Assert.Equal(0, input.Position);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public async Task AuthorizedConnectionPreservesRequestIdInResponse()
    {
        HostAgentRequest request = Request(HostAgentOperation.GetStatus);
        await using MemoryStream input = await FramedInputAsync(request);
        await using var output = new MemoryStream();
        var session = new HostAgentConnectionSession((value, _) =>
            Task.FromResult(Success(value)));

        await session.RunAsync(input, output, callerAccepted: true, CancellationToken.None);
        output.Position = 0;
        HostAgentResponse response = await HostAgentFrameCodec.ReadResponseAsync(
            output,
            CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(request.RequestId, response.RequestId);
    }

    [Fact]
    public async Task EndOfStreamDoesNotSynthesizeLifecycleOperations()
    {
        HostAgentRequest request = Request(HostAgentOperation.QueryTopology);
        await using MemoryStream input = await FramedInputAsync(request);
        await using var output = new MemoryStream();
        var operations = new List<HostAgentOperation>();
        var session = new HostAgentConnectionSession((value, _) =>
        {
            operations.Add(value.Operation);
            return Task.FromResult(Success(value));
        });

        await session.RunAsync(input, output, callerAccepted: true, CancellationToken.None);

        Assert.Equal(new[] { HostAgentOperation.QueryTopology }, operations);
        Assert.DoesNotContain(HostAgentOperation.ReleaseDisplayLease, operations);
        Assert.DoesNotContain(HostAgentOperation.RemoveVirtualDisplay, operations);
        Assert.DoesNotContain(HostAgentOperation.RestorePhysicalPrimary, operations);
    }

    private static async Task<MemoryStream> FramedInputAsync(HostAgentRequest request)
    {
        var stream = new MemoryStream();
        await HostAgentFrameCodec.WriteRequestAsync(stream, request, CancellationToken.None);
        stream.Position = 0;
        return stream;
    }

    private static HostAgentRequest Request(HostAgentOperation operation) =>
        new(
            HostAgentProtocol.CurrentVersion,
            Guid.NewGuid(),
            operation,
            HostAgentProtocol.CreatePayload(new EmptyHostAgentPayload()));

    private static HostAgentResponse Success(HostAgentRequest request) =>
        new(
            HostAgentProtocol.CurrentVersion,
            request.RequestId,
            Success: true,
            ResultCode: "ok",
            Diagnostic: string.Empty,
            HostAgentProtocol.CreatePayload(new EmptyHostAgentPayload()));
}

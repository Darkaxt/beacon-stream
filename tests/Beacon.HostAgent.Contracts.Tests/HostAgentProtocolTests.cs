using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Beacon.HostAgent.Contracts;

namespace Beacon.HostAgent.Contracts.Tests;

public sealed class HostAgentProtocolTests
{
    [Fact]
    public void PipeNameUsesNormalizedOwnerSid()
    {
        Assert.Equal(
            "beacon-host-agent-S-1-5-21-100-200-300-1001-v1",
            HostAgentPipeName.Create("S-1-5-21-100-200-300-1001"));
        Assert.Throws<ArgumentException>(() => HostAgentPipeName.Create("  "));
    }

    [Fact]
    public async Task RequestRoundTripsThroughBoundedFrame()
    {
        Guid requestId = Guid.NewGuid();
        HostAgentRequest request = new(
            HostAgentProtocol.CurrentVersion,
            requestId,
            HostAgentOperation.HoldDisplayLease,
            HostAgentProtocol.CreatePayload(new HoldDisplayLeasePayload("client-z-fold-7")));
        await using var stream = new MemoryStream();

        await HostAgentFrameCodec.WriteRequestAsync(stream, request, CancellationToken.None);
        stream.Position = 0;
        HostAgentRequest decoded = await HostAgentFrameCodec.ReadRequestAsync(
            stream,
            CancellationToken.None);

        Assert.Equal(HostAgentProtocol.CurrentVersion, decoded.ProtocolVersion);
        Assert.Equal(requestId, decoded.RequestId);
        Assert.Equal(HostAgentOperation.HoldDisplayLease, decoded.Operation);
        HoldDisplayLeasePayload payload = HostAgentProtocol.ReadPayload<HoldDisplayLeasePayload>(
            decoded.Payload);
        Assert.Equal("client-z-fold-7", payload.DisplayId);
    }

    [Fact]
    public async Task ResponseRoundTripsThroughBoundedFrame()
    {
        Guid requestId = Guid.NewGuid();
        HostAgentResponse response = new(
            HostAgentProtocol.CurrentVersion,
            requestId,
            Success: false,
            ResultCode: "display-access-denied",
            Diagnostic: "SetDisplayConfig Result=5.",
            HostAgentProtocol.CreatePayload(new EmptyHostAgentPayload()));
        await using var stream = new MemoryStream();

        await HostAgentFrameCodec.WriteResponseAsync(stream, response, CancellationToken.None);
        stream.Position = 0;
        HostAgentResponse decoded = await HostAgentFrameCodec.ReadResponseAsync(
            stream,
            CancellationToken.None);

        Assert.Equal(response.ProtocolVersion, decoded.ProtocolVersion);
        Assert.Equal(response.RequestId, decoded.RequestId);
        Assert.Equal(response.Success, decoded.Success);
        Assert.Equal(response.ResultCode, decoded.ResultCode);
        Assert.Equal(response.Diagnostic, decoded.Diagnostic);
        Assert.NotNull(HostAgentProtocol.ReadPayload<EmptyHostAgentPayload>(decoded.Payload));
    }

    [Fact]
    public void UnknownRequestMemberIsRejected()
    {
        string json = $$"""
            {
              "protocolVersion": {{HostAgentProtocol.CurrentVersion}},
              "requestId": "{{Guid.NewGuid():D}}",
              "operation": "getStatus",
              "payload": {},
              "unexpected": true
            }
            """;

        Assert.Throws<JsonException>(() => HostAgentProtocol.DeserializeRequest(
            Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void UnknownOperationIsRejected()
    {
        string json = $$"""
            {
              "protocolVersion": {{HostAgentProtocol.CurrentVersion}},
              "requestId": "{{Guid.NewGuid():D}}",
              "operation": "launchApollo",
              "payload": {}
            }
            """;

        Assert.Throws<JsonException>(() => HostAgentProtocol.DeserializeRequest(
            Encoding.UTF8.GetBytes(json)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(65_537)]
    public async Task InvalidFrameLengthIsRejected(int frameLength)
    {
        byte[] prefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, frameLength);
        await using var stream = new MemoryStream(prefix);

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            HostAgentFrameCodec.ReadRequestAsync(stream, CancellationToken.None));

        Assert.Contains("frame length", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TruncatedFrameIsRejected()
    {
        byte[] frame = new byte[sizeof(int) + 3];
        BinaryPrimitives.WriteInt32LittleEndian(frame, 10);
        await using var stream = new MemoryStream(frame);

        await Assert.ThrowsAsync<EndOfStreamException>(() =>
            HostAgentFrameCodec.ReadRequestAsync(stream, CancellationToken.None));
    }
}

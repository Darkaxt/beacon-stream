using System.Buffers.Binary;
using Beacon.StreamWorker.Contracts.Framing;
using Beacon.StreamWorker.Contracts.Worker.V1;
using Google.Protobuf;

namespace Beacon.StreamWorker.Contracts.Tests;

public sealed class ProtobufLengthFrameCodecTests
{
    [Fact]
    public void Encode_UsesNetworkOrderLengthAndStableProtobufBytes()
    {
        var message = new WorkerIpcEnvelope
        {
            ProtocolVersion = 1,
            RequestId = 7,
            SessionId = "s",
            WorkerHello = new WorkerHello
            {
                WorkerInstanceId = ByteString.CopyFromUtf8("w"),
                ProcessId = 7,
            },
        };

        var frame = ProtobufLengthFrameCodec.Encode(message);

        Assert.Equal("0000000e080110071a017352050a01771007", Convert.ToHexString(frame).ToLowerInvariant());
        Assert.Equal((uint)(frame.Length - sizeof(uint)), BinaryPrimitives.ReadUInt32BigEndian(frame));
    }

    [Fact]
    public void Decode_RejectsDeclaredMessagesAboveTheLimitBeforeParsing()
    {
        var frame = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(frame, ProtobufLengthFrameCodec.MaximumMessageBytes + 1u);

        Assert.Throws<ProtobufFrameException>(() => ProtobufLengthFrameCodec.ReadMessageLength(frame));
        var error = Assert.Throws<ProtobufFrameException>(
            () => ProtobufLengthFrameCodec.Decode(frame, WorkerIpcEnvelope.Parser));

        Assert.Contains("maximum", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Encode_RejectsMessagesAboveTheLimitBeforeAllocatingAFrame()
    {
        var message = new AuthorizeTicket
        {
            TicketHash = ByteString.CopyFrom(new byte[ProtobufLengthFrameCodec.MaximumMessageBytes]),
        };

        var error = Assert.Throws<ProtobufFrameException>(() => ProtobufLengthFrameCodec.Encode(message));

        Assert.Contains("maximum", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Decode_ToleratesUnknownProtobufFields()
    {
        var known = new WorkerHello
        {
            WorkerInstanceId = ByteString.CopyFromUtf8("worker"),
            ProcessId = 42,
        }.ToByteArray();
        var withUnknownField = known.Concat(new byte[] { 0x98, 0x06, 0x2a }).ToArray();
        var frame = new byte[sizeof(uint) + withUnknownField.Length];
        BinaryPrimitives.WriteUInt32BigEndian(frame, (uint)withUnknownField.Length);
        withUnknownField.CopyTo(frame, sizeof(uint));

        var decoded = ProtobufLengthFrameCodec.Decode(frame, WorkerHello.Parser);

        Assert.Equal("worker", decoded.WorkerInstanceId.ToStringUtf8());
        Assert.Equal(42u, decoded.ProcessId);
    }

    [Fact]
    public void Decode_RejectsTruncatedAndTrailingFrames()
    {
        var frame = ProtobufLengthFrameCodec.Encode(new WorkerHello { ProcessId = 42 });

        Assert.Throws<ProtobufFrameException>(
            () => ProtobufLengthFrameCodec.Decode(frame.AsSpan(0, frame.Length - 1), WorkerHello.Parser));
        Assert.Throws<ProtobufFrameException>(
            () => ProtobufLengthFrameCodec.Decode(frame.Concat(new byte[] { 0 }).ToArray(), WorkerHello.Parser));
    }
}

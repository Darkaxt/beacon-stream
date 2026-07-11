using System.Buffers.Binary;
using Google.Protobuf;

namespace Beacon.StreamWorker.Contracts.Framing;

public static class ProtobufLengthFrameCodec
{
    public const uint MaximumMessageBytes = 1024 * 1024;

    public static byte[] Encode(IMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var messageSize = message.CalculateSize();
        if (messageSize > MaximumMessageBytes)
        {
            throw new ProtobufFrameException(
                $"Message size {messageSize} exceeds the maximum {MaximumMessageBytes} bytes.");
        }

        var frame = new byte[sizeof(uint) + messageSize];
        BinaryPrimitives.WriteUInt32BigEndian(frame, (uint)messageSize);
        message.WriteTo(frame.AsSpan(sizeof(uint)));
        return frame;
    }

    public static int ReadMessageLength(ReadOnlySpan<byte> lengthPrefix)
    {
        if (lengthPrefix.Length != sizeof(uint))
        {
            throw new ProtobufFrameException("A message length prefix must contain exactly four bytes.");
        }

        var declaredSize = BinaryPrimitives.ReadUInt32BigEndian(lengthPrefix);
        if (declaredSize > MaximumMessageBytes)
        {
            throw new ProtobufFrameException(
                $"Declared message size {declaredSize} exceeds the maximum {MaximumMessageBytes} bytes.");
        }

        return checked((int)declaredSize);
    }

    public static T Decode<T>(ReadOnlySpan<byte> frame, MessageParser<T> parser)
        where T : IMessage<T>
    {
        ArgumentNullException.ThrowIfNull(parser);

        if (frame.Length < sizeof(uint))
        {
            throw new ProtobufFrameException("Frame does not contain a complete length prefix.");
        }

        var declaredSize = ReadMessageLength(frame[..sizeof(uint)]);

        if (declaredSize != frame.Length - sizeof(uint))
        {
            throw new ProtobufFrameException(
                $"Declared message size {declaredSize} does not match the available frame bytes.");
        }

        try
        {
            return parser.ParseFrom(frame[sizeof(uint)..].ToArray());
        }
        catch (InvalidProtocolBufferException error)
        {
            throw new ProtobufFrameException("Frame contains an invalid Protobuf message.", error);
        }
    }
}

public sealed class ProtobufFrameException : Exception
{
    public ProtobufFrameException(string message)
        : base(message)
    {
    }

    public ProtobufFrameException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

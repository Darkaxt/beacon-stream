using System.Buffers.Binary;

namespace Beacon.HostAgent.Contracts;

public static class HostAgentFrameCodec
{
    public static Task WriteRequestAsync(
        Stream stream,
        HostAgentRequest request,
        CancellationToken cancellationToken) =>
        WriteFrameAsync(stream, HostAgentProtocol.SerializeRequest(request), cancellationToken);

    public static async Task<HostAgentRequest> ReadRequestAsync(
        Stream stream,
        CancellationToken cancellationToken) =>
        HostAgentProtocol.DeserializeRequest(await ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false));

    public static Task WriteResponseAsync(
        Stream stream,
        HostAgentResponse response,
        CancellationToken cancellationToken) =>
        WriteFrameAsync(stream, HostAgentProtocol.SerializeResponse(response), cancellationToken);

    public static async Task<HostAgentResponse> ReadResponseAsync(
        Stream stream,
        CancellationToken cancellationToken) =>
        HostAgentProtocol.DeserializeResponse(await ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false));

    private static async Task WriteFrameAsync(
        Stream stream,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ValidateFrameLength(payload.Length);

        byte[] prefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, payload.Length);
        await stream.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadFrameAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        byte[] prefix = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(prefix, cancellationToken).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        ValidateFrameLength(length);

        byte[] payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        return payload;
    }

    private static void ValidateFrameLength(int length)
    {
        if (length is <= 0 or > HostAgentProtocol.MaximumFrameBytes)
        {
            throw new InvalidDataException(
                $"Host Agent frame length must be between 1 and {HostAgentProtocol.MaximumFrameBytes} bytes.");
        }
    }
}

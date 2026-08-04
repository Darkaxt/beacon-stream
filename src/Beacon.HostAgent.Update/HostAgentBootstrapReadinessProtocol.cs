using System.Buffers.Binary;
using System.Text.Json;

namespace Beacon.HostAgent.Update;

public sealed record HostAgentBootstrapReadiness(string VersionId, int ProcessId);

public static class HostAgentBootstrapReadinessProtocol
{
    public const int MaximumFrameBytes = 4_096;

    public static async Task WriteAsync(
        Stream stream,
        HostAgentBootstrapReadiness readiness,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(readiness);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(readiness, HostAgentUpdateJson.Options);
        ValidateLength(payload.Length);
        byte[] prefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, payload.Length);
        await stream.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<HostAgentBootstrapReadiness> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        byte[] prefix = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(prefix, cancellationToken).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        ValidateLength(length);
        byte[] payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize<HostAgentBootstrapReadiness>(
                payload,
                HostAgentUpdateJson.Options)
                ?? throw new InvalidDataException("Bootstrap readiness payload is empty.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("Bootstrap readiness payload is invalid.", error);
        }
    }

    private static void ValidateLength(int length)
    {
        if (length is <= 0 or > MaximumFrameBytes)
        {
            throw new InvalidDataException("Bootstrap readiness frame length is invalid.");
        }
    }
}

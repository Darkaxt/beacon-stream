namespace Beacon.StreamWorker.Contracts.Framing;

public static class ProtocolVersion
{
    public const uint Current = 1;

    public static void EnsureSupported(uint receivedVersion)
    {
        if (receivedVersion != Current)
        {
            throw new UnsupportedProtocolVersionException(receivedVersion, Current);
        }
    }
}

public sealed class UnsupportedProtocolVersionException : Exception
{
    public UnsupportedProtocolVersionException(uint receivedVersion, uint supportedVersion)
        : base($"Protocol version {receivedVersion} is unsupported; this build accepts {supportedVersion}.")
    {
        ReceivedVersion = receivedVersion;
        SupportedVersion = supportedVersion;
    }

    public uint ReceivedVersion { get; }

    public uint SupportedVersion { get; }
}

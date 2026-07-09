using System.Buffers.Binary;
using System.Text;

namespace Beacon.Server.Api;

internal static class BeaconAnnexBSampleEnvelope
{
    public const string ContentType = "application/vnd.beacon.annexb-samples";

    public static readonly byte[] Magic = Encoding.ASCII.GetBytes("BEACONANNEXB1\n");

    public static byte[] Create(byte[] annexBBytes, int fps)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fps);

        IReadOnlyList<byte[]> samples = AnnexBAccessUnitSplitter.Split(annexBBytes);
        using var output = new MemoryStream();
        output.Write(Magic);

        long frameDurationUs = 1_000_000L / fps;
        Span<byte> header = stackalloc byte[12];
        for (int index = 0; index < samples.Count; index++)
        {
            byte[] sample = samples[index];
            BinaryPrimitives.WriteInt64LittleEndian(header[..8], index * frameDurationUs);
            BinaryPrimitives.WriteInt32LittleEndian(header[8..], sample.Length);
            output.Write(header);
            output.Write(sample);
        }

        return output.ToArray();
    }
}

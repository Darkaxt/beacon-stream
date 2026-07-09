namespace Beacon.Server.Api;

internal static class AnnexBAccessUnitSplitter
{
    private const string MissingStartCode = "Encoded video bytes are not Annex B start-code delimited.";

    public static IReadOnlyList<byte[]> Split(byte[] bytes)
    {
        if (bytes.Length < 4 || GetStartCodeLength(bytes, 0) == 0)
        {
            throw new InvalidOperationException(MissingStartCode);
        }

        List<AnnexBStartCode> starts = FindStartCodes(bytes);
        if (starts.Count == 0)
        {
            throw new InvalidOperationException(MissingStartCode);
        }

        List<byte[]> samples = [];
        int sampleStart = starts[0].Index;
        bool currentSampleHasVcl = false;

        foreach (AnnexBStartCode start in starts)
        {
            int payload = start.Index + start.Length;
            bool vcl = payload < bytes.Length && IsVclNal(bytes[payload]);
            if (vcl && currentSampleHasVcl)
            {
                samples.Add(bytes[sampleStart..start.Index]);
                sampleStart = start.Index;
            }

            currentSampleHasVcl |= vcl;
        }

        if (sampleStart < bytes.Length)
        {
            samples.Add(bytes[sampleStart..]);
        }

        return samples;
    }

    private static List<AnnexBStartCode> FindStartCodes(byte[] bytes)
    {
        var starts = new List<AnnexBStartCode>();
        for (int index = 0; index <= bytes.Length - 3; index++)
        {
            int length = GetStartCodeLength(bytes, index);
            if (length > 0)
            {
                starts.Add(new AnnexBStartCode(index, length));
                index += length - 1;
            }
        }

        return starts;
    }

    private static int GetStartCodeLength(byte[] bytes, int index)
    {
        if (index <= bytes.Length - 4 &&
            bytes[index] == 0 &&
            bytes[index + 1] == 0 &&
            bytes[index + 2] == 0 &&
            bytes[index + 3] == 1)
        {
            return 4;
        }

        if (index <= bytes.Length - 3 &&
            bytes[index] == 0 &&
            bytes[index + 1] == 0 &&
            bytes[index + 2] == 1)
        {
            return 3;
        }

        return 0;
    }

    private static bool IsVclNal(byte value)
    {
        int type = value & 0x1F;
        return type >= 1 && type <= 5;
    }

    private readonly record struct AnnexBStartCode(int Index, int Length);
}

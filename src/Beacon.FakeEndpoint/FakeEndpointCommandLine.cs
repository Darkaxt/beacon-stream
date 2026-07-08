using System.Globalization;

namespace Beacon.FakeEndpoint;

public sealed record FakeEndpointCommandLineOptions(Uri ServerUri, FakeEndpointScript Script);

public static class FakeEndpointCommandLine
{
    public static FakeEndpointCommandLineOptions Parse(IReadOnlyList<string> args)
    {
        Dictionary<string, string> values = ParsePairs(args);
        FakeEndpointScript defaults = FakeEndpointScript.CreateZFold7Default();
        FakeEndpointScript profiled = defaults.ApplyTelemetryProfile(ReadString(values, "telemetry-profile", defaults.TelemetryProfile));

        var script = profiled with
        {
            ClientId = ReadString(values, "client-id", profiled.ClientId),
            Name = ReadString(values, "name", profiled.Name),
            PairingToken = ReadOptionalString(values, "pairing-token", profiled.PairingToken),
            Width = ReadInt(values, "width", profiled.Width),
            Height = ReadInt(values, "height", profiled.Height),
            RefreshHz = ReadInt(values, "refresh", profiled.RefreshHz),
            BitrateCapMbps = ReadOptionalInt(values, "bitrate-cap", profiled.BitrateCapMbps),
            MaxFps = ReadInt(values, "max-fps", profiled.MaxFps),
            RttMs = ReadInt(values, "rtt-ms", profiled.RttMs),
            PacketLossPercent = ReadDouble(values, "packet-loss", profiled.PacketLossPercent),
            DecoderLoadPercent = ReadOptionalInt(values, "decoder-load", profiled.DecoderLoadPercent),
            EstimatedBandwidthMbps = ReadOptionalInt(values, "estimated-bandwidth", profiled.EstimatedBandwidthMbps),
            WifiBand = ReadOptionalString(values, "wifi-band", profiled.WifiBand),
            BatteryPercent = ReadOptionalInt(values, "battery", profiled.BatteryPercent),
            ThermalState = ReadOptionalString(values, "thermal-state", profiled.ThermalState),
            AppId = ReadString(values, "app-id", profiled.AppId),
            Title = ReadString(values, "title", profiled.Title),
            Source = ReadString(values, "source", profiled.Source),
            RequireStreamConnection = ReadBool(values, "require-stream-connection", profiled.RequireStreamConnection),
            EndAfterStreamConnection = ReadBool(values, "end-after-stream-connection", profiled.EndAfterStreamConnection)
        };

        return new FakeEndpointCommandLineOptions(
            new Uri(ReadString(values, "server", "http://localhost:5000")),
            script);
    }

    private static Dictionary<string, string> ParsePairs(IReadOnlyList<string> args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < args.Count; index++)
        {
            string key = args[index];
            if (!key.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            if (index + 1 >= args.Count)
            {
                throw new ArgumentException($"Missing value for '{key}'.");
            }

            values[key[2..]] = args[index + 1];
            index++;
        }

        return values;
    }

    private static int ReadInt(IReadOnlyDictionary<string, string> values, string key, int fallback) =>
        values.TryGetValue(key, out string? value) && int.TryParse(value, out int parsed)
            ? parsed
            : fallback;

    private static int? ReadOptionalInt(IReadOnlyDictionary<string, string> values, string key, int? fallback) =>
        values.TryGetValue(key, out string? value) && int.TryParse(value, out int parsed)
            ? parsed
            : fallback;

    private static double ReadDouble(IReadOnlyDictionary<string, string> values, string key, double fallback) =>
        values.TryGetValue(key, out string? value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : fallback;

    private static bool ReadBool(IReadOnlyDictionary<string, string> values, string key, bool fallback) =>
        values.TryGetValue(key, out string? value) && bool.TryParse(value, out bool parsed)
            ? parsed
            : fallback;

    private static string ReadString(IReadOnlyDictionary<string, string> values, string key, string fallback) =>
        values.GetValueOrDefault(key) ?? fallback;

    private static string? ReadOptionalString(IReadOnlyDictionary<string, string> values, string key, string? fallback) =>
        values.GetValueOrDefault(key) ?? fallback;
}

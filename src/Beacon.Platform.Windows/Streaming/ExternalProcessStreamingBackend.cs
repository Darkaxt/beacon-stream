using System.Globalization;
using Beacon.Core.Sessions;

namespace Beacon.Platform.Windows.Streaming;

public sealed record ExternalStreamingCommand(
    string FileName,
    string Arguments,
    IReadOnlyDictionary<string, string> Environment);

public sealed class ExternalProcessStreamingBackend
{
    public static ExternalStreamingCommand CreateStartCommand(string executablePath, SessionPlan plan)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["BEACON_SESSION_ID"] = plan.SessionId,
            ["BEACON_CLIENT_ID"] = plan.ClientId.Value,
            ["BEACON_APP_ID"] = plan.AppId,
            ["BEACON_DISPLAY_ID"] = plan.Display.DisplayId,
            ["BEACON_STREAM_CODEC"] = plan.Stream.Codec,
            ["BEACON_STREAM_FPS"] = plan.Stream.Fps.ToString(CultureInfo.InvariantCulture),
            ["BEACON_STREAM_BITRATE_MBPS"] = plan.Stream.InitialBitrateMbps.ToString(CultureInfo.InvariantCulture),
            ["BEACON_STREAM_TRANSPORT"] = plan.Stream.Transport
        };

        string arguments = $"--session \"{plan.SessionId}\" --display \"{plan.Display.DisplayId}\"";
        return new ExternalStreamingCommand(executablePath, arguments, environment);
    }
}

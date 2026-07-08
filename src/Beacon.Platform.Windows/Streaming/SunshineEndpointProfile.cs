using System.Globalization;

namespace Beacon.Platform.Windows.Streaming;

public sealed record SunshineEndpointProfile(string Host, int BasePort)
{
    public const int DefaultBasePort = 47989;
    public const int MinimumBasePort = 1029;
    public const int MaximumBasePort = 65514;

    public IReadOnlyDictionary<string, string> CreateEndpoints()
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            throw new InvalidOperationException("Sunshine endpoint profile host is required.");
        }

        if (BasePort is < MinimumBasePort or > MaximumBasePort)
        {
            throw new InvalidOperationException(
                $"Sunshine endpoint profile base port must be between {MinimumBasePort.ToString(CultureInfo.InvariantCulture)} and {MaximumBasePort.ToString(CultureInfo.InvariantCulture)}.");
        }

        string host = FormatHost(Host);
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["https"] = Endpoint("https", host, BasePort - 5),
            ["http"] = Endpoint("http", host, BasePort),
            ["web"] = Endpoint("https", host, BasePort + 1),
            ["rtsp"] = Endpoint("rtsp", host, BasePort + 21),
            ["video"] = Endpoint("udp", host, BasePort + 9),
            ["control"] = Endpoint("udp", host, BasePort + 10),
            ["audio"] = Endpoint("udp", host, BasePort + 11),
            ["mic"] = Endpoint("udp", host, BasePort + 13)
        };
    }

    private static string Endpoint(string scheme, string host, int port) =>
        $"{scheme}://{host}:{port.ToString(CultureInfo.InvariantCulture)}";

    private static string FormatHost(string host)
    {
        string trimmed = host.Trim();
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            return trimmed;
        }

        return trimmed.Contains(':', StringComparison.Ordinal)
            ? $"[{trimmed}]"
            : trimmed;
    }
}

namespace Beacon.FakeEndpoint;

public sealed record FakeEndpointCommandLineOptions(Uri ServerUri, FakeEndpointScript Script);

public static class FakeEndpointCommandLine
{
    public static FakeEndpointCommandLineOptions Parse(IReadOnlyList<string> args)
    {
        Dictionary<string, string> values = ParsePairs(args);
        FakeEndpointScript defaults = FakeEndpointScript.CreateZFold7Default();

        var script = defaults with
        {
            Width = ReadInt(values, "width", defaults.Width),
            Height = ReadInt(values, "height", defaults.Height),
            RefreshHz = ReadInt(values, "refresh", defaults.RefreshHz),
            AppId = ReadString(values, "app-id", defaults.AppId),
            Title = ReadString(values, "title", defaults.Title),
            Source = ReadString(values, "source", defaults.Source)
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

    private static string ReadString(IReadOnlyDictionary<string, string> values, string key, string fallback) =>
        values.GetValueOrDefault(key) ?? fallback;
}

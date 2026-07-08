using System.Collections;
using Beacon.StreamingProbe;

using var lifetime = new ConsoleStreamingProbeLifetime();
return await StreamingProbeApp.RunAsync(
    args,
    ReadEnvironment(),
    Console.Out,
    Console.Error,
    lifetime);

static IReadOnlyDictionary<string, string> ReadEnvironment()
{
    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
    {
        if (entry.Key is string key && entry.Value is string value)
        {
            values[key] = value;
        }
    }

    return values;
}

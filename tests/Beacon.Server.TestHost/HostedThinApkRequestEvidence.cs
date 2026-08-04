namespace Beacon.Server.TestHost;

public sealed record HostedThinApkRequestEvent(
    long Sequence,
    string Method,
    string Path,
    int StatusCode);

public sealed class HostedThinApkRequestEvidence
{
    private readonly Lock gate = new();
    private readonly List<HostedThinApkRequestEvent> events = [];
    private long nextSequence = 1;

    public void Record(string method, string path, int statusCode)
    {
        if (string.IsNullOrWhiteSpace(method) || string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Hosted request evidence is incomplete.");
        }
        lock (gate)
        {
            events.Add(new HostedThinApkRequestEvent(
                nextSequence++,
                method,
                path,
                statusCode));
        }
    }

    public IReadOnlyList<HostedThinApkRequestEvent> Snapshot()
    {
        lock (gate)
        {
            return events.ToArray();
        }
    }
}

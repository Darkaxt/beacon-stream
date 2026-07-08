namespace Beacon.Core.Diagnostics;

public static class DiagnosticSeverity
{
    public const string Information = "information";
    public const string Warning = "warning";
    public const string Error = "error";
}

public sealed record DiagnosticEvent(
    string Id,
    DateTimeOffset TimestampUtc,
    string Severity,
    string Category,
    string Operation,
    string Message,
    string? ClientId,
    string? SessionId,
    string? DisplayId,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static DiagnosticEvent Create(
        string severity,
        string category,
        string operation,
        string message,
        string? clientId = null,
        string? sessionId = null,
        string? displayId = null,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            severity,
            category,
            operation,
            message,
            clientId,
            sessionId,
            displayId,
            metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}

public interface IDiagnosticEventSink
{
    void Publish(DiagnosticEvent diagnosticEvent);
}

public interface IDiagnosticEventSource
{
    IReadOnlyList<DiagnosticEvent> GetRecent(int count);
}

public sealed class InMemoryDiagnosticEventJournal : IDiagnosticEventSink, IDiagnosticEventSource
{
    private readonly Lock gate = new();
    private readonly Queue<DiagnosticEvent> events = new();
    private readonly int capacity;

    public InMemoryDiagnosticEventJournal(int capacity = 200)
    {
        this.capacity = capacity <= 0 ? 200 : capacity;
    }

    public void Publish(DiagnosticEvent diagnosticEvent)
    {
        lock (gate)
        {
            events.Enqueue(diagnosticEvent);
            while (events.Count > capacity)
            {
                events.Dequeue();
            }
        }
    }

    public IReadOnlyList<DiagnosticEvent> GetRecent(int count)
    {
        int take = count <= 0 ? capacity : count;
        lock (gate)
        {
            return events
                .Reverse()
                .Take(take)
                .ToArray();
        }
    }
}

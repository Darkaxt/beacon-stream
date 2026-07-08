using Beacon.Core.Diagnostics;

namespace Beacon.Core.Tests.Diagnostics;

public sealed class DiagnosticEventJournalTests
{
    [Fact]
    public void JournalReturnsNewestEventsFirstAndRespectsCapacity()
    {
        var journal = new InMemoryDiagnosticEventJournal(capacity: 2);
        journal.Publish(Create("display", "first"));
        journal.Publish(Create("stream", "second"));
        journal.Publish(Create("recovery", "third"));

        IReadOnlyList<DiagnosticEvent> events = journal.GetRecent(10);

        Assert.Equal(["third", "second"], events.Select(evt => evt.Message));
    }

    [Fact]
    public void JournalPreservesContextMetadata()
    {
        var journal = new InMemoryDiagnosticEventJournal(capacity: 10);

        journal.Publish(new DiagnosticEvent(
            "evt-1",
            DateTimeOffset.UnixEpoch,
            DiagnosticSeverity.Warning,
            "display",
            "lease.ensure",
            "Virtual display repair failed.",
            ClientId: "z-fold-7",
            SessionId: "session-1",
            DisplayId: "client-z-fold-7",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["width"] = "2560",
                ["height"] = "1600",
                ["refreshHz"] = "120"
            }));

        DiagnosticEvent evt = Assert.Single(journal.GetRecent(1));

        Assert.Equal("z-fold-7", evt.ClientId);
        Assert.Equal("client-z-fold-7", evt.DisplayId);
        Assert.Equal("2560", evt.Metadata["width"]);
    }

    private static DiagnosticEvent Create(string category, string message) =>
        new(
            $"evt-{message}",
            DateTimeOffset.UnixEpoch,
            DiagnosticSeverity.Information,
            category,
            "test",
            message,
            ClientId: null,
            SessionId: null,
            DisplayId: null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}

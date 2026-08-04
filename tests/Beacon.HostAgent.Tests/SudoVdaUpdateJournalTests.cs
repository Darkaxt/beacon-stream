using Beacon.HostAgent.Contracts;
using Beacon.HostAgent.DriverUpdates;

namespace Beacon.HostAgent.Tests;

public sealed class SudoVdaUpdateJournalTests
{
    [Fact]
    public void WrittenTransactionIsReadableAfterJournalRecreation()
    {
        using var directory = new TemporaryDirectory();
        Guid transactionId = Guid.NewGuid();
        SudoVdaUpdatePayload expected = Payload(
            transactionId,
            SudoVdaUpdateState.Installing,
            "installing");
        var writer = new SudoVdaUpdateJournal(directory.Path);

        writer.Write(expected);
        var reader = new SudoVdaUpdateJournal(directory.Path);

        Assert.True(reader.TryRead(transactionId, out SudoVdaUpdatePayload? actual));
        Assert.Equal(expected, actual);
        Assert.Single(Directory.EnumerateFiles(directory.Path, "*.json"));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public void RewriteAtomicallyReplacesPreviousState()
    {
        using var directory = new TemporaryDirectory();
        Guid transactionId = Guid.NewGuid();
        var journal = new SudoVdaUpdateJournal(directory.Path);
        journal.Write(Payload(transactionId, SudoVdaUpdateState.Accepted, "accepted"));

        journal.Write(Payload(transactionId, SudoVdaUpdateState.Succeeded, "succeeded"));

        Assert.True(journal.TryRead(transactionId, out SudoVdaUpdatePayload? actual));
        Assert.Equal(SudoVdaUpdateState.Succeeded, actual?.State);
        Assert.Equal("succeeded", actual?.Diagnostic);
        Assert.Single(Directory.EnumerateFiles(directory.Path, "*.json"));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp"));
    }

    private static SudoVdaUpdatePayload Payload(
        Guid transactionId,
        SudoVdaUpdateState state,
        string diagnostic) =>
        new(
            transactionId,
            "sudovda-22.48.58.193",
            state,
            diagnostic,
            PreviousEvidence: null,
            ActiveEvidence: null);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"beacon-update-journal-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}

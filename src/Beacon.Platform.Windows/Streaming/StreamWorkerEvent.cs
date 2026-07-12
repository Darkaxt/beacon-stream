using Beacon.Core.Input;

namespace Beacon.Platform.Windows.Streaming;

public abstract record StreamWorkerEvent(long ProcessGeneration, string? SessionId);

public sealed record StreamWorkerTransportAuthenticated(
    long ProcessGeneration,
    string SessionId,
    ulong WorkerSessionGeneration,
    uint MaximumDatagramBytes) : StreamWorkerEvent(ProcessGeneration, SessionId);

public sealed record StreamWorkerTransportDisconnected(
    long ProcessGeneration,
    string SessionId,
    ulong WorkerSessionGeneration) : StreamWorkerEvent(ProcessGeneration, SessionId);

public sealed record StreamWorkerInputReceived(
    long ProcessGeneration,
    string SessionId,
    ulong WorkerSessionGeneration,
    ulong Sequence,
    IReadOnlyList<ClientInputEvent> Events) : StreamWorkerEvent(ProcessGeneration, SessionId)
{
    public override string ToString() =>
        $"StreamWorkerInputReceived {{ ProcessGeneration = {ProcessGeneration}, EventCount = {Events.Count}, Payload = [redacted] }}";
}

public enum StreamWorkerFeedbackKind
{
    RenderedFrame,
    DatagramLoss,
    Decoder,
    QueueDepth,
    Benchmark,
}

public sealed record StreamWorkerFeedbackReceived(
    long ProcessGeneration,
    string SessionId,
    ulong WorkerSessionGeneration,
    ulong Sequence,
    StreamWorkerFeedbackKind Kind,
    ulong PrimaryValue,
    ulong SecondaryValue,
    uint Count) : StreamWorkerEvent(ProcessGeneration, SessionId);

public sealed record StreamWorkerMediaEvidence(
    long ProcessGeneration,
    string SessionId,
    ulong WorkerSessionGeneration,
    ulong Sequence,
    ulong PresentationTimeUs,
    uint DatagramBytes) : StreamWorkerEvent(ProcessGeneration, SessionId);

public sealed record StreamWorkerConnectionObserved(
    long ProcessGeneration,
    ulong ConnectionGeneration) : StreamWorkerEvent(ProcessGeneration, SessionId: null);

public sealed record StreamWorkerConnectionConfigured(
    long ProcessGeneration,
    ulong ConnectionGeneration) : StreamWorkerEvent(ProcessGeneration, SessionId: null);

public sealed record StreamWorkerTransportConnected(
    long ProcessGeneration,
    ulong ConnectionGeneration) : StreamWorkerEvent(ProcessGeneration, SessionId: null);

public sealed record StreamWorkerTransportFailed(
    long ProcessGeneration,
    ulong ConnectionGeneration,
    uint PlatformStatusCode) : StreamWorkerEvent(ProcessGeneration, SessionId: null);

public sealed record StreamWorkerProcessExited(
    long ProcessGeneration,
    int ExitCode) : StreamWorkerEvent(ProcessGeneration, SessionId: null);

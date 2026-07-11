using System.Globalization;
using Beacon.Core.Diagnostics;
using Beacon.Core.Input;
using Beacon.Platform.Windows.Streaming;

namespace Beacon.Server.Streaming;

public sealed class StreamWorkerEventRelay(
    IGenerationBoundStreamWorkerHost eventSource,
    IStreamWorkerRuntimeEvents runtimeEvents,
    IClientInputSink inputSink,
    IDiagnosticEventSink diagnostics) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (StreamWorkerEvent workerEvent in eventSource.Events.ReadAllAsync(stoppingToken))
        {
            try
            {
                await RelayAsync(workerEvent, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                Publish(
                    workerEvent is StreamWorkerInputReceived ? "input.dispatch_failed" : "worker.event_failed",
                    workerEvent is StreamWorkerInputReceived
                        ? "Worker input dispatch failed."
                        : "Worker event handling failed.",
                    workerEvent,
                    DiagnosticSeverity.Error);
            }
        }
    }

    private async Task RelayAsync(StreamWorkerEvent workerEvent, CancellationToken cancellationToken)
    {
        switch (workerEvent)
        {
            case StreamWorkerTransportAuthenticated authenticated:
                bool bound = runtimeEvents.TryBind(authenticated);
                Publish(
                    bound
                        ? "worker.transport_authenticated"
                        : "worker.transport_rejected",
                    bound ? "Worker transport authenticated." : "Worker transport event rejected.",
                    authenticated,
                    DiagnosticSeverity.Information,
                    ("workerSessionGeneration", authenticated.WorkerSessionGeneration),
                    ("maximumDatagramBytes", authenticated.MaximumDatagramBytes));
                break;
            case StreamWorkerInputReceived input:
                if (!runtimeEvents.TryResolveInput(input, out ClientInputBatch? batch) || batch is null)
                {
                    Publish(
                        "input.rejected",
                        "Worker input event rejected.",
                        input,
                        DiagnosticSeverity.Warning,
                        ("sequence", input.Sequence),
                        ("eventCount", input.Events.Count));
                    break;
                }

                ClientInputResult result = await inputSink.ForwardAsync(batch, cancellationToken).ConfigureAwait(false);
                Publish(
                    result.Success ? "input.forwarded" : "input.rejected",
                    result.Success ? "Worker input forwarded." : "Worker input sink rejected the batch.",
                    input,
                    result.Success ? DiagnosticSeverity.Information : DiagnosticSeverity.Warning,
                    ("sequence", input.Sequence),
                    ("eventCount", input.Events.Count));
                break;
            case StreamWorkerFeedbackReceived feedback:
                bool feedbackAccepted = runtimeEvents.IsCurrent(feedback);
                Publish(
                    feedbackAccepted ? "worker.feedback" : "worker.feedback_rejected",
                    feedbackAccepted
                        ? "Worker feedback accepted."
                        : "Worker feedback rejected.",
                    feedback,
                    DiagnosticSeverity.Information,
                    ("workerSessionGeneration", feedback.WorkerSessionGeneration),
                    ("sequence", feedback.Sequence),
                    ("kind", feedback.Kind),
                    ("primaryValue", feedback.PrimaryValue),
                    ("secondaryValue", feedback.SecondaryValue),
                    ("count", feedback.Count));
                break;
            case StreamWorkerMediaEvidence media:
                bool mediaAccepted = runtimeEvents.IsCurrent(media);
                Publish(
                    mediaAccepted ? "worker.media" : "worker.media_rejected",
                    mediaAccepted
                        ? "Worker media evidence accepted."
                        : "Worker media evidence rejected.",
                    media,
                    DiagnosticSeverity.Information,
                    ("workerSessionGeneration", media.WorkerSessionGeneration),
                    ("sequence", media.Sequence),
                    ("presentationTimeUs", media.PresentationTimeUs),
                    ("datagramBytes", media.DatagramBytes));
                break;
            case StreamWorkerTransportDisconnected disconnected:
                bool disconnectedAccepted = runtimeEvents.TryDisconnect(disconnected);
                Publish(
                    disconnectedAccepted
                        ? "worker.transport_disconnected"
                        : "worker.disconnect_rejected",
                    disconnectedAccepted
                        ? "Worker transport disconnected."
                        : "Worker disconnect event rejected.",
                    disconnected,
                    DiagnosticSeverity.Information,
                    ("workerSessionGeneration", disconnected.WorkerSessionGeneration));
                break;
            case StreamWorkerProcessExited exited:
                runtimeEvents.ProcessExited(exited);
                Publish(
                    "worker.process_exited",
                    "Worker process exited.",
                    exited,
                    DiagnosticSeverity.Warning,
                    ("exitCode", exited.ExitCode));
                break;
        }
    }

    private void Publish(
        string operation,
        string message,
        StreamWorkerEvent workerEvent,
        string severity,
        params (string Key, object Value)[] metadata)
    {
        try
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["processGeneration"] = workerEvent.ProcessGeneration.ToString(CultureInfo.InvariantCulture),
            };
            foreach ((string key, object value) in metadata)
            {
                values[key] = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }
            diagnostics.Publish(DiagnosticEvent.Create(
                severity,
                "stream-worker",
                operation,
                message,
                sessionId: workerEvent.SessionId,
                metadata: values));
        }
        catch (Exception)
        {
        }
    }
}

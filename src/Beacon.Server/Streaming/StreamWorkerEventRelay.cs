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
    private readonly CancellationTokenSource relayCancellation = new();
    private readonly TaskCompletionSource gracefulStopRequested =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock stopGate = new();
    private Task? stopTask;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using CancellationTokenRegistration stoppingRegistration = stoppingToken.Register(() =>
        {
            if (!gracefulStopRequested.Task.IsCompleted)
            {
                relayCancellation.Cancel();
            }
        });
        try
        {
            while (true)
            {
                while (eventSource.Events.TryRead(out StreamWorkerEvent? workerEvent))
                {
                    await HandleAsync(workerEvent, relayCancellation.Token).ConfigureAwait(false);
                }
                if (gracefulStopRequested.Task.IsCompleted)
                {
                    return;
                }

                Task<bool> eventAvailable = eventSource.Events
                    .WaitToReadAsync(relayCancellation.Token)
                    .AsTask();
                Task completed = await Task.WhenAny(
                    eventAvailable,
                    gracefulStopRequested.Task).ConfigureAwait(false);
                if (completed == gracefulStopRequested.Task)
                {
                    relayCancellation.Cancel();
                    await ObserveCancellationAsync(eventAvailable).ConfigureAwait(false);
                    while (eventSource.Events.TryRead(out StreamWorkerEvent? workerEvent))
                    {
                        await HandleAsync(workerEvent, CancellationToken.None).ConfigureAwait(false);
                    }
                    return;
                }
                if (!await eventAvailable.ConfigureAwait(false))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (relayCancellation.IsCancellationRequested)
        {
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        lock (stopGate)
        {
            return stopTask ??= StopCoreAsync(cancellationToken);
        }
    }

    public override void Dispose()
    {
        relayCancellation.Cancel();
        base.Dispose();
        relayCancellation.Dispose();
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        bool graceful = false;
        if (eventSource is IStreamWorkerHost lifecycleHost)
        {
            try
            {
                await lifecycleHost.ShutdownAsync(cancellationToken).ConfigureAwait(false);
                graceful = true;
            }
            catch (Exception)
            {
                PublishShutdownFailure();
            }
        }

        if (graceful)
        {
            gracefulStopRequested.TrySetResult();
        }
        else
        {
            relayCancellation.Cancel();
        }
        await base.StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task HandleAsync(StreamWorkerEvent workerEvent, CancellationToken cancellationToken)
    {
        try
        {
            await RelayAsync(workerEvent, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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

    private async Task RelayAsync(StreamWorkerEvent workerEvent, CancellationToken cancellationToken)
    {
        switch (workerEvent)
        {
            case StreamWorkerConnectionObserved observed:
                Publish(
                    "worker.connection_observed",
                    "Worker transport connection observed.",
                    observed,
                    DiagnosticSeverity.Information,
                    ("connectionGeneration", observed.ConnectionGeneration));
                break;
            case StreamWorkerConnectionConfigured configured:
                Publish(
                    "worker.connection_configured",
                    "Worker transport connection configured.",
                    configured,
                    DiagnosticSeverity.Information,
                    ("connectionGeneration", configured.ConnectionGeneration));
                break;
            case StreamWorkerTransportConnected connected:
                Publish(
                    "worker.transport_connected",
                    "Worker transport connected.",
                    connected,
                    DiagnosticSeverity.Information,
                    ("connectionGeneration", connected.ConnectionGeneration));
                break;
            case StreamWorkerTransportFailed failed:
                Publish(
                    "worker.transport_failed",
                    "Worker transport failed.",
                    failed,
                    DiagnosticSeverity.Warning,
                    ("connectionGeneration", failed.ConnectionGeneration),
                    ("platformStatusCode", failed.PlatformStatusCode));
                break;
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

    private void PublishShutdownFailure()
    {
        try
        {
            diagnostics.Publish(DiagnosticEvent.Create(
                DiagnosticSeverity.Error,
                "stream-worker",
                "worker.shutdown_failed",
                "Worker shutdown failed.",
                metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["processGeneration"] = eventSource.CurrentProcessGeneration.ToString(
                        CultureInfo.InvariantCulture)
                }));
        }
        catch (Exception)
        {
        }
    }

    private static async Task ObserveCancellationAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }
}

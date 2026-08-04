using Beacon.StreamWorker.Contracts.Framing;
using Beacon.StreamWorker.Contracts.Worker.V1;
using Google.Protobuf;

namespace Beacon.Server.TestHost;

internal static class FakeHostedBenchmarkWorkerProgram
{
    public const string InvocationArgument = "--fake-hosted-worker";

    public static bool IsInvocation(IReadOnlyList<string> arguments) =>
        arguments.Count > 0
        && string.Equals(arguments[0], InvocationArgument, StringComparison.Ordinal);

    public static int Run(IReadOnlyList<string> arguments)
    {
        try
        {
            return RunAsync(arguments).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            Console.Error.WriteLine("BEACON_FAKE_HOSTED_WORKER_FAILURE 1");
            return 70;
        }
    }

    private static async Task<int> RunAsync(IReadOnlyList<string> arguments)
    {
        if (arguments.Count != 3
            || !string.Equals(arguments[1], "--identity", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(arguments[2]))
        {
            return 64;
        }

        string mode = (await File.ReadAllTextAsync(arguments[2]).ConfigureAwait(false)).Trim();
        await using Stream input = Console.OpenStandardInput();
        await using Stream output = Console.OpenStandardOutput();
        ByteString workerInstanceId = ByteString.CopyFrom([1, 2, 3, 4]);

        await WriteAsync(output, new WorkerIpcEnvelope
        {
            ProtocolVersion = ProtocolVersion.Current,
            WorkerHello = new WorkerHello
            {
                ProcessId = checked((uint)Environment.ProcessId),
                WorkerInstanceId = workerInstanceId,
            },
        }).ConfigureAwait(false);
        if (string.Equals(mode, "stall-on-initialize", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("BEACON_FAKE_HOSTED_WORKER_INITIALIZATION_STALLED");
            await input.CopyToAsync(Stream.Null).ConfigureAwait(false);
            return 26;
        }
        await WriteAsync(output, Capabilities(workerInstanceId)).ConfigureAwait(false);
        await WriteAsync(output, new WorkerIpcEnvelope
        {
            ProtocolVersion = ProtocolVersion.Current,
            WorkerReady = new WorkerReady { WorkerInstanceId = workerInstanceId },
        }).ConfigureAwait(false);
        Console.Error.WriteLine("BEACON_FAKE_HOSTED_WORKER_READY");

        if (string.Equals(mode, "diagnostics", StringComparison.Ordinal))
        {
            for (int index = 0; index < 8; index++)
            {
                Console.Error.WriteLine($"BEACON_FAKE_HOSTED_WORKER_DIAGNOSTIC {index}");
            }
            Console.Error.WriteLine("not-safe-marker");
        }
        if (string.Equals(mode, "normal", StringComparison.Ordinal)
            || string.Equals(mode, "diagnostics", StringComparison.Ordinal))
        {
            await WriteAsync(output, ConnectionObserved()).ConfigureAwait(false);
        }

        while (true)
        {
            WorkerIpcEnvelope request = await ReadAsync(input).ConfigureAwait(false);
            if (string.Equals(mode, "exit-on-command", StringComparison.Ordinal))
            {
                return 23;
            }
            if (string.Equals(mode, "break-on-command", StringComparison.Ordinal))
            {
                await output.WriteAsync(new byte[] { 0xff, 0xff, 0xff, 0xff }).ConfigureAwait(false);
                await output.FlushAsync().ConfigureAwait(false);
                byte[] waitForParentClosure = new byte[1];
                _ = await input.ReadAsync(waitForParentClosure).ConfigureAwait(false);
                return 24;
            }
            if (string.Equals(mode, "clean-exit-on-shutdown", StringComparison.Ordinal)
                && request.BodyCase == WorkerIpcEnvelope.BodyOneofCase.ShutdownWorker)
            {
                Console.Error.WriteLine("BEACON_FAKE_HOSTED_WORKER_STOPPED");
                return 0;
            }
            if (string.Equals(mode, "stall-on-shutdown", StringComparison.Ordinal)
                && request.BodyCase == WorkerIpcEnvelope.BodyOneofCase.ShutdownWorker)
            {
                Console.Error.WriteLine("BEACON_FAKE_HOSTED_WORKER_SHUTDOWN_STALLED");
                await input.CopyToAsync(Stream.Null).ConfigureAwait(false);
                return 25;
            }

            await WriteAsync(output, Completion(request)).ConfigureAwait(false);
            if (request.BodyCase == WorkerIpcEnvelope.BodyOneofCase.ShutdownWorker)
            {
                Console.Error.WriteLine("BEACON_FAKE_HOSTED_WORKER_STOPPED");
                return 0;
            }
        }
    }

    private static WorkerIpcEnvelope Capabilities(ByteString workerInstanceId)
    {
        var envelope = new WorkerIpcEnvelope
        {
            ProtocolVersion = ProtocolVersion.Current,
            WorkerCapabilities = new WorkerCapabilities
            {
                WorkerInstanceId = workerInstanceId,
                QuicDatagrams = true,
                MaximumSessions = 1,
                MaximumFramesPerSecond = 120,
                VideoAvailable = false,
                VideoUnavailableBoundary = DiagnosticBoundary.Encoder,
                VideoUnavailableCode = 1,
            },
        };
        envelope.WorkerCapabilities.VideoCodecs.Add(WorkerVideoCodec.H264);
        envelope.WorkerCapabilities.VideoEncoders.Add(WorkerVideoEncoder.Nvenc);
        envelope.WorkerCapabilities.CaptureMethods.Add(WorkerCaptureMethod.WindowsGraphicsCapture);
        return envelope;
    }

    private static WorkerIpcEnvelope ConnectionObserved() => new()
    {
        ProtocolVersion = ProtocolVersion.Current,
        WorkerDiagnostic = new WorkerDiagnostic
        {
            Severity = DiagnosticSeverity.Information,
            Boundary = DiagnosticBoundary.Transport,
            Code = DiagnosticCode.ConnectionObserved,
            NumericValue = 41,
        },
    };

    private static WorkerIpcEnvelope Completion(WorkerIpcEnvelope request) => new()
    {
        ProtocolVersion = ProtocolVersion.Current,
        RequestId = request.RequestId,
        SessionId = request.SessionId,
        WorkerCompletion = new WorkerCompletion
        {
            Succeeded = true,
            ErrorCode = WorkerErrorCode.None,
        },
    };

    private static async Task WriteAsync(Stream output, WorkerIpcEnvelope envelope)
    {
        await output.WriteAsync(ProtobufLengthFrameCodec.Encode(envelope)).ConfigureAwait(false);
        await output.FlushAsync().ConfigureAwait(false);
    }

    private static async Task<WorkerIpcEnvelope> ReadAsync(Stream input)
    {
        byte[] prefix = new byte[sizeof(uint)];
        await input.ReadExactlyAsync(prefix).ConfigureAwait(false);
        int messageLength = ProtobufLengthFrameCodec.ReadMessageLength(prefix);
        byte[] frame = new byte[sizeof(uint) + messageLength];
        prefix.CopyTo(frame, 0);
        if (messageLength > 0)
        {
            await input.ReadExactlyAsync(frame.AsMemory(sizeof(uint), messageLength)).ConfigureAwait(false);
        }
        return ProtobufLengthFrameCodec.Decode(frame, WorkerIpcEnvelope.Parser);
    }
}

using System.Threading.Channels;
using Beacon.Core.Streaming;
using Beacon.Platform.Windows.Streaming;
using Beacon.StreamWorker.Contracts.Framing;
using Beacon.StreamWorker.Contracts.Worker.V1;

namespace Beacon.Platform.Windows.Tests.Streaming;

public sealed class StreamWorkerSessionAuthorizerTests
{
    [Fact]
    public async Task AuthorizationUsesTheCapturedRuntimeGeneration()
    {
        var host = new RecordingHost();
        var authorizer = new StreamWorkerSessionAuthorizer(host);
        StreamRuntimeAuthorizationContext context = await authorizer.GetContextAsync(
            CancellationToken.None);

        StreamRuntimeAuthorizationResult result = await authorizer.AuthorizeAsync(
            Authorization(context),
            CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal([7L], host.GenerationBoundAttempts);
    }

    [Fact]
    public async Task AuthorizationCannotMoveToAReplacementRuntime()
    {
        var host = new RecordingHost();
        var authorizer = new StreamWorkerSessionAuthorizer(host);
        StreamRuntimeAuthorizationContext context = await authorizer.GetContextAsync(
            CancellationToken.None);
        host.ReplaceRuntime();

        StreamWorkerGenerationChangedException error =
            await Assert.ThrowsAsync<StreamWorkerGenerationChangedException>(() =>
                authorizer.AuthorizeAsync(Authorization(context), CancellationToken.None));

        Assert.Equal(7, error.ExpectedProcessGeneration);
        Assert.Equal([7L], host.GenerationBoundAttempts);
    }

    [Fact]
    public async Task RevocationOfARetiredRuntimeDoesNotContactItsReplacement()
    {
        var host = new RecordingHost();
        var authorizer = new StreamWorkerSessionAuthorizer(host);
        StreamRuntimeAuthorizationContext context = await authorizer.GetContextAsync(
            CancellationToken.None);
        host.ReplaceRuntime();

        StreamRuntimeAuthorizationResult result = await authorizer.RevokeAsync(
            new StreamRuntimeRevocation("session", [4, 5, 6], context.RuntimeGeneration),
            CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal([7L], host.GenerationBoundAttempts);
    }

    private static StreamRuntimeAuthorization Authorization(
        StreamRuntimeAuthorizationContext context) =>
        new(
            "session",
            "client",
            8,
            [1, 2, 3],
            context.RuntimeInstanceId,
            context.RuntimeGeneration,
            DateTimeOffset.UtcNow.AddMinutes(1));

    private sealed class RecordingHost : IStreamWorkerHost
    {
        private readonly Channel<StreamWorkerEvent> events =
            Channel.CreateUnbounded<StreamWorkerEvent>();
        private byte[] instanceId = [1, 2, 3, 4];

        public bool IsReady { get; private set; } = true;

        public ReadOnlyMemory<byte> WorkerInstanceId => instanceId;

        public ChannelReader<StreamWorkerEvent> Events => events.Reader;

        public long CurrentProcessGeneration { get; private set; } = 7;

        public List<long> GenerationBoundAttempts { get; } = [];

        public Task EnsureReadyAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public bool IsCurrentProcessGeneration(long processGeneration) =>
            IsReady && processGeneration == CurrentProcessGeneration;

        public Task<StreamWorkerCommandResponse> SendAsync(
            long expectedProcessGeneration,
            WorkerIpcEnvelope command,
            CancellationToken cancellationToken)
        {
            GenerationBoundAttempts.Add(expectedProcessGeneration);
            if (!IsCurrentProcessGeneration(expectedProcessGeneration))
            {
                throw new StreamWorkerGenerationChangedException(expectedProcessGeneration);
            }
            return Task.FromResult(Success(command));
        }

        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            IsReady = false;
            return Task.CompletedTask;
        }

        public void ReplaceRuntime()
        {
            CurrentProcessGeneration++;
            instanceId = [9, 8, 7, 6];
        }

        private static StreamWorkerCommandResponse Success(WorkerIpcEnvelope command) =>
            new(
                new WorkerIpcEnvelope
                {
                    ProtocolVersion = ProtocolVersion.Current,
                    RequestId = 1,
                    SessionId = command.SessionId,
                    WorkerCompletion = new WorkerCompletion
                    {
                        Succeeded = true,
                        ErrorCode = WorkerErrorCode.None,
                    },
                },
                []);
    }
}

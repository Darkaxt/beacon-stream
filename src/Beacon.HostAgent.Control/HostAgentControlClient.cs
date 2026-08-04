using System.IO.Pipes;
using System.Security.Principal;
using Beacon.HostAgent.Contracts;

namespace Beacon.HostAgent.Control;

internal sealed class HostAgentControlClient
{
    private readonly string pipeName;

    public HostAgentControlClient(SecurityIdentifier owner)
        : this(HostAgentPipeName.Create(
            (owner ?? throw new ArgumentNullException(nameof(owner))).Value))
    {
    }

    internal HostAgentControlClient(string pipeName)
    {
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            throw new ArgumentException("Host Agent pipe name is required.", nameof(pipeName));
        }
        this.pipeName = pipeName;
    }

    public async Task<HostAgentResponse> SendAsync<TPayload>(
        HostAgentOperation operation,
        TPayload payload,
        CancellationToken cancellationToken)
    {
        var request = new HostAgentRequest(
            HostAgentProtocol.CurrentVersion,
            Guid.NewGuid(),
            operation,
            HostAgentProtocol.CreatePayload(payload));
        await using var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough,
            TokenImpersonationLevel.Identification);
        await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
        await HostAgentFrameCodec.WriteRequestAsync(pipe, request, cancellationToken)
            .ConfigureAwait(false);
        HostAgentResponse response = await HostAgentFrameCodec.ReadResponseAsync(
            pipe,
            cancellationToken).ConfigureAwait(false);
        if (response.ProtocolVersion != HostAgentProtocol.CurrentVersion
            || response.RequestId != request.RequestId)
        {
            throw new InvalidDataException("Host Agent returned an uncorrelated response.");
        }
        return response;
    }
}

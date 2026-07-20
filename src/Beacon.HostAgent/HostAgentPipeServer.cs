using System.IO.Pipes;
using System.Security.Principal;
using Beacon.HostAgent.Contracts;

namespace Beacon.HostAgent;

internal sealed class HostAgentPipeServer
{
    private readonly SecurityIdentifier owner;
    private readonly Func<HostAgentRequest, CancellationToken, Task<HostAgentResponse>> dispatch;
    private readonly IHostAgentCallerVerifier callerVerifier;

    public HostAgentPipeServer(
        SecurityIdentifier owner,
        HostAgentDispatcher dispatcher)
        : this(owner, dispatcher.DispatchAsync, new WindowsHostAgentCallerVerifier(owner))
    {
    }

    internal HostAgentPipeServer(
        SecurityIdentifier owner,
        Func<HostAgentRequest, CancellationToken, Task<HostAgentResponse>> dispatch,
        IHostAgentCallerVerifier? callerVerifier = null)
    {
        this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
        this.dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        this.callerVerifier = callerVerifier ?? new WindowsHostAgentCallerVerifier(owner);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using NamedPipeServerStream pipe = CreatePipe();
            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                HostAgentCallerVerification caller = callerVerifier.Verify(pipe);
                var session = new HostAgentConnectionSession(dispatch);
                await session.RunAsync(
                    pipe,
                    pipe,
                    caller.Accepted,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException)
            {
            }
        }
    }

    private NamedPipeServerStream CreatePipe() =>
        NamedPipeServerStreamAcl.Create(
            HostAgentPipeIdentity.CreateName(owner),
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough,
            inBufferSize: HostAgentProtocol.MaximumFrameBytes,
            outBufferSize: HostAgentProtocol.MaximumFrameBytes,
            HostAgentPipeIdentity.CreateSecurity(owner),
            HandleInheritability.None,
            additionalAccessRights: 0);
}

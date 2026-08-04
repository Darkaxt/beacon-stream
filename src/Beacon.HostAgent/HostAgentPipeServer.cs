using System.IO.Pipes;
using System.Security.Principal;
using Beacon.HostAgent.Contracts;

namespace Beacon.HostAgent;

internal sealed class HostAgentPipeServer
{
    private readonly SecurityIdentifier owner;
    private readonly string pipeName;
    private readonly Func<HostAgentRequest, CancellationToken, Task<HostAgentDispatchOutcome>> dispatch;
    private readonly IHostAgentCallerVerifier callerVerifier;

    public HostAgentPipeServer(
        SecurityIdentifier owner,
        HostAgentDispatcher dispatcher)
        : this(owner, dispatcher.DispatchWithOutcomeAsync, new WindowsHostAgentCallerVerifier(owner))
    {
    }

    internal HostAgentPipeServer(
        SecurityIdentifier owner,
        Func<HostAgentRequest, CancellationToken, Task<HostAgentResponse>> dispatch,
        IHostAgentCallerVerifier? callerVerifier = null,
        string? pipeName = null)
        : this(
            owner,
            async (request, cancellationToken) => new HostAgentDispatchOutcome(
                await dispatch(request, cancellationToken).ConfigureAwait(false),
                HostAgentPostResponseAction.None),
            callerVerifier,
            pipeName)
    {
    }

    internal HostAgentPipeServer(
        SecurityIdentifier owner,
        Func<HostAgentRequest, CancellationToken, Task<HostAgentDispatchOutcome>> dispatch,
        IHostAgentCallerVerifier? callerVerifier = null,
        string? pipeName = null)
    {
        this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
        this.pipeName = string.IsNullOrWhiteSpace(pipeName)
            ? HostAgentPipeIdentity.CreateName(owner)
            : pipeName;
        this.dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        this.callerVerifier = callerVerifier ?? new WindowsHostAgentCallerVerifier(owner);
    }

    public Task<HostAgentServerExitReason> RunAsync(CancellationToken cancellationToken) =>
        RunAsync(onListening: null, cancellationToken);

    public async Task<HostAgentServerExitReason> RunAsync(
        Func<CancellationToken, Task>? onListening,
        CancellationToken cancellationToken)
    {
        bool listeningSignaled = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            await using NamedPipeServerStream pipe = CreatePipe();
            try
            {
                if (!listeningSignaled && onListening is not null)
                {
                    await onListening(cancellationToken).ConfigureAwait(false);
                    listeningSignaled = true;
                }
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                HostAgentCallerVerification caller = callerVerifier.Verify(pipe);
                var session = new HostAgentConnectionSession(dispatch);
                HostAgentPostResponseAction action = await session.RunAsync(
                    pipe,
                    pipe,
                    caller.Accepted,
                    cancellationToken).ConfigureAwait(false);
                if (action == HostAgentPostResponseAction.ApplyUpdate)
                {
                    return HostAgentServerExitReason.ApplyUpdate;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return HostAgentServerExitReason.Stopped;
            }
            catch (IOException)
            {
            }
        }
        return HostAgentServerExitReason.Stopped;
    }

    private NamedPipeServerStream CreatePipe() =>
        NamedPipeServerStreamAcl.Create(
            pipeName,
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

internal enum HostAgentServerExitReason
{
    Stopped,
    ApplyUpdate
}

using System.Text.Json;
using Beacon.HostAgent.Contracts;

namespace Beacon.HostAgent;

internal sealed class HostAgentConnectionSession
{
    private readonly Func<HostAgentRequest, CancellationToken, Task<HostAgentDispatchOutcome>> dispatch;

    public HostAgentConnectionSession(
        Func<HostAgentRequest, CancellationToken, Task<HostAgentResponse>> dispatch)
        : this(async (request, cancellationToken) => new HostAgentDispatchOutcome(
            await dispatch(request, cancellationToken).ConfigureAwait(false),
            HostAgentPostResponseAction.None))
    {
    }

    public HostAgentConnectionSession(
        Func<HostAgentRequest, CancellationToken, Task<HostAgentDispatchOutcome>> dispatch)
    {
        this.dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
    }

    public async Task<HostAgentPostResponseAction> RunAsync(
        Stream input,
        Stream output,
        bool callerAccepted,
        CancellationToken cancellationToken)
    {
        if (!callerAccepted)
        {
            return HostAgentPostResponseAction.None;
        }

        while (true)
        {
            HostAgentRequest request;
            try
            {
                request = await HostAgentFrameCodec.ReadRequestAsync(input, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception error) when (
                error is EndOfStreamException or IOException or JsonException or InvalidDataException)
            {
                return HostAgentPostResponseAction.None;
            }

            HostAgentDispatchOutcome outcome = await dispatch(request, cancellationToken)
                .ConfigureAwait(false);
            await HostAgentFrameCodec.WriteResponseAsync(
                output,
                outcome.Response,
                cancellationToken)
                .ConfigureAwait(false);
            if (outcome.Action != HostAgentPostResponseAction.None)
            {
                return outcome.Action;
            }
        }
    }
}

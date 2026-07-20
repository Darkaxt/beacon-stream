using System.Text.Json;
using Beacon.HostAgent.Contracts;

namespace Beacon.HostAgent;

internal sealed class HostAgentConnectionSession(
    Func<HostAgentRequest, CancellationToken, Task<HostAgentResponse>> dispatch)
{
    public async Task RunAsync(
        Stream input,
        Stream output,
        bool callerAccepted,
        CancellationToken cancellationToken)
    {
        if (!callerAccepted)
        {
            return;
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
                return;
            }

            HostAgentResponse response = await dispatch(request, cancellationToken)
                .ConfigureAwait(false);
            await HostAgentFrameCodec.WriteResponseAsync(output, response, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}

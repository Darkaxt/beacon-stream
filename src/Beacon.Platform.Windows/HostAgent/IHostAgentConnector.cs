using System.IO.Pipes;
using System.Security.Principal;
using Beacon.HostAgent.Contracts;

namespace Beacon.Platform.Windows.HostAgent;

internal interface IHostAgentConnector
{
    Task<Stream> ConnectAsync(CancellationToken cancellationToken);
}

internal sealed class WindowsHostAgentConnector(SecurityIdentifier owner) : IHostAgentConnector
{
    public async Task<Stream> ConnectAsync(CancellationToken cancellationToken)
    {
        var pipe = new NamedPipeClientStream(
            ".",
            HostAgentPipeName.Create(owner.Value),
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough,
            TokenImpersonationLevel.Identification);
        try
        {
            await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
            return pipe;
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}

using System.IO.Pipes;
using Beacon.HostAgent.Update;

namespace Beacon.HostAgent;

internal static class BootstrapReadinessClient
{
    public static async Task SignalAsync(
        string pipeName,
        string versionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pipeName) || string.IsNullOrWhiteSpace(versionId))
        {
            throw new ArgumentException("Bootstrap readiness identity is required.");
        }

        await using var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.Out,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough);
        await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
        await HostAgentBootstrapReadinessProtocol.WriteAsync(
            pipe,
            new HostAgentBootstrapReadiness(versionId, Environment.ProcessId),
            cancellationToken).ConfigureAwait(false);
    }
}

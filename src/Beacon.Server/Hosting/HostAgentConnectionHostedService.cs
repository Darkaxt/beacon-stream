using Beacon.Platform.Windows.HostAgent;

namespace Beacon.Server.Hosting;

public sealed class HostAgentConnectionHostedService(IHostAgentConnection connection)
    : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        connection.RunAsync(stoppingToken);
}

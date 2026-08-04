using System.Security.Principal;
using Beacon.HostAgent.Update;

namespace Beacon.HostAgent.Bootstrap;

internal interface IBootstrapPlatform
{
    IBootstrapChildLaunch Launch(
        SecurityIdentifier owner,
        HostAgentSelectedVersion version,
        string executablePath);
}

internal interface IBootstrapChildLaunch : IAsyncDisposable
{
    Task Readiness { get; }

    Task<int> ExitCode { get; }
}

internal interface IHostAgentUpdateActivator
{
    Task<HostAgentSelectedVersion> ActivateAsync(
        HostAgentPendingUpdate pending,
        HostAgentSelectedVersion current);
}

internal static class BootstrapExitCodes
{
    public const int ApplyUpdate = 42;
    public const int InvalidState = 50;
    public const int ActivationFailed = 51;
}

using Beacon.HostAgent.Contracts;

namespace Beacon.HostAgent;

internal enum HostAgentPostResponseAction
{
    None,
    ApplyUpdate
}

internal sealed record HostAgentDispatchOutcome(
    HostAgentResponse Response,
    HostAgentPostResponseAction Action);

internal static class HostAgentExitCodes
{
    public const int ApplyUpdate = 42;
}

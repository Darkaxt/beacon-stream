using System.IO.Pipes;
using System.Security.Principal;

namespace Beacon.HostAgent;

internal interface IHostAgentCallerVerifier
{
    HostAgentCallerVerification Verify(NamedPipeServerStream pipe);
}

internal sealed record HostAgentCallerVerification(
    bool Accepted,
    uint ProcessId,
    SecurityIdentifier? CallerSid,
    string Diagnostic)
{
    public static HostAgentCallerVerification Accept(
        uint processId,
        SecurityIdentifier callerSid) =>
        new(true, processId, callerSid, "Host Agent caller verified.");

    public static HostAgentCallerVerification Reject(
        string diagnostic,
        uint processId = 0,
        SecurityIdentifier? callerSid = null) =>
        new(false, processId, callerSid, diagnostic);
}

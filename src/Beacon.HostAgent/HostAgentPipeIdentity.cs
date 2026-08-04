using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Beacon.HostAgent.Contracts;

namespace Beacon.HostAgent;

internal static class HostAgentPipeIdentity
{
    public static string CreateName(SecurityIdentifier owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return HostAgentPipeName.Create(owner.Value);
    }

    public static PipeSecurity CreateSecurity(SecurityIdentifier owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var localSystem = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(owner);
        AddFullControl(security, owner);
        AddFullControl(security, localSystem);
        AddFullControl(security, administrators);
        return security;
    }

    private static void AddFullControl(PipeSecurity security, SecurityIdentifier identity) =>
        security.AddAccessRule(new PipeAccessRule(
            identity,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
}

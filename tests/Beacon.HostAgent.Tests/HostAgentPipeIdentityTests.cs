using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Beacon.HostAgent.Tests;

public sealed class HostAgentPipeIdentityTests
{
    [Fact]
    public void PipeNameIsDeterministicFromOwnerSid()
    {
        var owner = new SecurityIdentifier("S-1-5-21-100-200-300-1001");

        string name = HostAgentPipeIdentity.CreateName(owner);

        Assert.Equal("beacon-host-agent-S-1-5-21-100-200-300-1001-v1", name);
    }

    [Fact]
    public void PipeAclIsProtectedAndLimitedToOwnerSystemAndAdministrators()
    {
        var owner = new SecurityIdentifier("S-1-5-21-100-200-300-1001");

        PipeSecurity security = HostAgentPipeIdentity.CreateSecurity(owner);

        Assert.True(security.AreAccessRulesProtected);
        Assert.Equal(owner, security.GetOwner(typeof(SecurityIdentifier)));
        AuthorizationRuleCollection rules = security.GetAccessRules(
            includeExplicit: true,
            includeInherited: false,
            typeof(SecurityIdentifier));
        PipeAccessRule[] accessRules = rules.Cast<PipeAccessRule>().ToArray();
        Assert.Equal(3, accessRules.Length);
        Assert.All(accessRules, rule =>
        {
            Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
            Assert.Equal(PipeAccessRights.FullControl, rule.PipeAccessRights);
            Assert.False(rule.IsInherited);
        });
        Assert.Equal(
            new[]
            {
                owner.Value,
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value,
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value
            }.Order(StringComparer.Ordinal),
            accessRules
                .Select(rule => ((SecurityIdentifier)rule.IdentityReference).Value)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void MissingOwnerSidIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => HostAgentPipeIdentity.CreateName(null!));
        Assert.Throws<ArgumentNullException>(() => HostAgentPipeIdentity.CreateSecurity(null!));
    }
}

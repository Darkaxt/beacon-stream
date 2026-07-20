using System.Security.Principal;

namespace Beacon.HostAgent.Tests;

public sealed class HostAgentOptionsTests
{
    [Fact]
    public void ParseRequiresOneExplicitOwnerSid()
    {
        string sid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null).Value;

        HostAgentOptions options = HostAgentOptions.Parse(["--owner-sid", sid]);

        Assert.Equal(sid, options.Owner.Value);
    }

    [Theory]
    [InlineData()]
    [InlineData("--owner-sid")]
    [InlineData("--unknown", "value")]
    [InlineData("--owner-sid", "not-a-sid")]
    [InlineData("--owner-sid", "S-1-5-32-545", "extra")]
    public void ParseRejectsIncompleteOrUnknownArguments(params string[] arguments)
    {
        Assert.Throws<ArgumentException>(() => HostAgentOptions.Parse(arguments));
    }
}

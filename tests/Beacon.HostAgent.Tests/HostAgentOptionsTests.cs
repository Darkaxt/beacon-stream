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
        Assert.Null(options.BootstrapReadyPipe);
        Assert.Null(options.VersionId);
    }

    [Fact]
    public void ParseAcceptsAuthenticatedBootstrapReadinessArguments()
    {
        string sid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null).Value;

        HostAgentOptions options = HostAgentOptions.Parse([
            "--owner-sid", sid,
            "--bootstrap-ready-pipe", "beacon-bootstrap-ready-0123456789abcdef",
            "--version-id", "agent-0123456789abcdef"
        ]);

        Assert.Equal("beacon-bootstrap-ready-0123456789abcdef", options.BootstrapReadyPipe);
        Assert.Equal("agent-0123456789abcdef", options.VersionId);
    }

    [Theory]
    [InlineData()]
    [InlineData("--owner-sid")]
    [InlineData("--unknown", "value")]
    [InlineData("--owner-sid", "not-a-sid")]
    [InlineData("--owner-sid", "S-1-5-32-545", "extra")]
    [InlineData("--owner-sid", "S-1-5-32-545", "--bootstrap-ready-pipe", "ready")]
    [InlineData("--owner-sid", "S-1-5-32-545", "--version-id", "agent-one")]
    public void ParseRejectsIncompleteOrUnknownArguments(params string[] arguments)
    {
        Assert.Throws<ArgumentException>(() => HostAgentOptions.Parse(arguments));
    }
}

using Beacon.HostAgent;
using Beacon.Platform.Windows.Displays;

namespace Beacon.HostAgent.Tests;

public sealed class HostAgentUserDisplayTopologyCommandTests
{
    [Fact]
    public void ExactHelperArgumentRunsTheFixedTransition()
    {
        int invocationCount = 0;

        bool handled = HostAgentUserDisplayTopologyCommand.TryRun(
            [WindowsUserDisplayTopologyTransition.CommandArgument],
            () => false,
            () =>
            {
                invocationCount++;
                return DisplayApiResult.Ok();
            },
            out int exitCode);

        Assert.True(handled);
        Assert.Equal(0, exitCode);
        Assert.Equal(1, invocationCount);
    }

    [Fact]
    public void AdditionalArgumentsCannotEnterTheHelperMode()
    {
        int invocationCount = 0;

        bool handled = HostAgentUserDisplayTopologyCommand.TryRun(
            [WindowsUserDisplayTopologyTransition.CommandArgument, "unexpected"],
            () => false,
            () =>
            {
                invocationCount++;
                return DisplayApiResult.Ok();
            },
            out int exitCode);

        Assert.False(handled);
        Assert.Equal(0, exitCode);
        Assert.Equal(0, invocationCount);
    }

    [Fact]
    public void FailedTransitionProducesAHelperFailureExitCode()
    {
        bool handled = HostAgentUserDisplayTopologyCommand.TryRun(
            [WindowsUserDisplayTopologyTransition.CommandArgument],
            () => false,
            () => DisplayApiResult.Fail("Windows rejected the transition."),
            out int exitCode);

        Assert.True(handled);
        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public void ElevatedProcessCannotRunTheUserTopologyTransition()
    {
        int invocationCount = 0;

        bool handled = HostAgentUserDisplayTopologyCommand.TryRun(
            [WindowsUserDisplayTopologyTransition.CommandArgument],
            () => true,
            () =>
            {
                invocationCount++;
                return DisplayApiResult.Ok();
            },
            out int exitCode);

        Assert.True(handled);
        Assert.NotEqual(0, exitCode);
        Assert.Equal(0, invocationCount);
    }
}

using Beacon.Platform.Windows.Displays;

namespace Beacon.Platform.Windows.Tests.Displays;

public sealed class WindowsUserDisplayTopologyTransitionTests
{
    [Fact]
    public void ApplyExtendedRequestsOnlyTheWindowsExtendedTopology()
    {
        uint? observedFlags = null;
        var transition = new WindowsUserDisplayTopologyTransition(flags =>
        {
            observedFlags = flags;
            return 0;
        });

        DisplayApiResult result = transition.ApplyExtended();

        Assert.True(result.Success);
        Assert.Equal(0x00000084u, observedFlags);
    }

    [Fact]
    public void ApplyExtendedReportsTheWindowsFailureCode()
    {
        var transition = new WindowsUserDisplayTopologyTransition(_ => 87);

        DisplayApiResult result = transition.ApplyExtended();

        Assert.False(result.Success);
        Assert.Contains("Result=87", result.Error, StringComparison.Ordinal);
    }
}

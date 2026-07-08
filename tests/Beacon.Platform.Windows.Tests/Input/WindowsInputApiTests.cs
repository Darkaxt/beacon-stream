using Beacon.Platform.Windows.Input;

namespace Beacon.Platform.Windows.Tests.Input;

public sealed class WindowsInputApiTests
{
    [Fact]
    public void AbsoluteCoordinateMappingUsesVirtualDesktopBounds()
    {
        Assert.Equal(0, WindowsInputApi.ToAbsoluteCoordinate(-1280, origin: -1280, size: 3840));
        Assert.Equal(65535, WindowsInputApi.ToAbsoluteCoordinate(2559, origin: -1280, size: 3840));
        Assert.Equal(21851, WindowsInputApi.ToAbsoluteCoordinate(0, origin: -1280, size: 3840));
    }
}

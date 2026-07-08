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

    [Theory]
    [InlineData("KeyW", "w", 0x57)]
    [InlineData("Digit1", "1", 0x31)]
    [InlineData("Escape", "Escape", 0x1B)]
    [InlineData("Space", " ", 0x20)]
    [InlineData("ArrowUp", "ArrowUp", 0x26)]
    public void KeyboardCodeMappingUsesWindowsVirtualKeys(string code, string key, ushort expectedVirtualKey)
    {
        Assert.True(WindowsInputApi.TryMapKeyboardVirtualKey(code, key, out ushort virtualKey));
        Assert.Equal(expectedVirtualKey, virtualKey);
    }
}

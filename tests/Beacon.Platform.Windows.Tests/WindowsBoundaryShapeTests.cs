using System.Reflection;
using Beacon.Platform.Windows.Input;
using Beacon.Platform.Windows.Streaming;
using Beacon.StreamWorker.Contracts.Worker.V1;

namespace Beacon.Platform.Windows.Tests;

public sealed class WindowsBoundaryShapeTests
{
    [Fact]
    public void WindowsInputAndNamedPipeConstructorsRemainAvailable()
    {
        Assert.NotNull(typeof(WindowsInputCommand).GetConstructor(
        [
            typeof(WindowsInputCommandKind),
            typeof(int?),
            typeof(int?),
            typeof(string),
            typeof(bool?),
            typeof(string),
            typeof(string)
        ]));
        Assert.NotNull(typeof(StreamWorkerNamedPipeClient).GetConstructor(
        [
            typeof(Stream),
            typeof(Task<int>),
            typeof(uint)
        ]));
        var command = new WindowsInputCommand(
            Kind: WindowsInputCommandKind.KeyboardKey,
            X: null,
            Y: null,
            Button: null,
            Pressed: true,
            Key: "w",
            Code: "KeyW");
        Assert.Equal("KeyW", command.Code);
        Assert.Null(command.WheelDelta);
        Assert.Null(command.ScanCode);
    }

    [Fact]
    public void WindowsInputCommandRetainsExactSevenOutputDeconstruct()
    {
        MethodInfo? deconstruct = typeof(WindowsInputCommand).GetMethod(
            "Deconstruct",
            [
                typeof(WindowsInputCommandKind).MakeByRefType(),
                typeof(int?).MakeByRefType(),
                typeof(int?).MakeByRefType(),
                typeof(string).MakeByRefType(),
                typeof(bool?).MakeByRefType(),
                typeof(string).MakeByRefType(),
                typeof(string).MakeByRefType()
            ]);

        Assert.NotNull(deconstruct);
        Assert.True(deconstruct.IsPublic);
        Assert.Equal(typeof(void), deconstruct.ReturnType);
    }
}

using Beacon.Core.Input;
using Beacon.Platform.Windows.Displays;
using Beacon.Platform.Windows.Input;
using Beacon.Platform.Windows.Tests.Displays;

namespace Beacon.Platform.Windows.Tests.Input;

public sealed class WindowsClientInputSinkTests
{
    [Fact]
    public async Task PointerTapTargetsTheLeasedDisplay()
    {
        var displayApi = new FakeWindowsDisplayApi
        {
            CurrentTopology = new DisplayTopologySnapshot(
                [
                    new DisplayPathSnapshot(
                        "physical-laptop-panel",
                        DisplayPathKind.Physical,
                        2560,
                        1600,
                        240,
                        IsPrimary: false,
                        X: 0,
                        Y: 0),
                    new DisplayPathSnapshot(
                        "client-z-fold-7",
                        DisplayPathKind.Virtual,
                        2560,
                        1600,
                        120,
                        IsPrimary: true,
                        X: 2560,
                        Y: 0)
                ],
                IsMirrorMode: false)
        };
        var inputApi = new FakeWindowsInputApi();
        var sink = new WindowsClientInputSink(displayApi, inputApi);
        var batch = new ClientInputBatch(
            "z-fold-7",
            "session-1",
            "client-z-fold-7",
            10,
            [
                new ClientInputEvent(
                    Type: "pointer",
                    Action: "tap",
                    PointerId: 1,
                    X: 0.25,
                    Y: 0.5)
            ]);

        ClientInputResult result = await sink.ForwardAsync(batch, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, result.EventCount);
        Assert.Collection(
            inputApi.Commands,
            command =>
            {
                Assert.Equal(WindowsInputCommandKind.PointerMove, command.Kind);
                Assert.Equal(3200, command.X);
                Assert.Equal(800, command.Y);
            },
            command =>
            {
                Assert.Equal(WindowsInputCommandKind.PointerButton, command.Kind);
                Assert.Equal("left", command.Button);
                Assert.True(command.Pressed);
            },
            command =>
            {
                Assert.Equal(WindowsInputCommandKind.PointerButton, command.Kind);
                Assert.Equal("left", command.Button);
                Assert.False(command.Pressed);
            });
    }

    [Fact]
    public async Task KeyboardPressTargetsTheActiveLeasedDisplay()
    {
        var displayApi = new FakeWindowsDisplayApi
        {
            CurrentTopology = DisplayTopologySnapshot.Extended(
                "physical-laptop-panel",
                "client-z-fold-7",
                2560,
                1600,
                120,
                virtualPrimary: true)
        };
        var inputApi = new FakeWindowsInputApi();
        var sink = new WindowsClientInputSink(displayApi, inputApi);
        var batch = new ClientInputBatch(
            "z-fold-7",
            "session-1",
            "client-z-fold-7",
            15,
            [
                new ClientInputEvent(
                    Type: "keyboard",
                    Action: "press",
                    Key: "w",
                    Code: "KeyW")
            ]);

        ClientInputResult result = await sink.ForwardAsync(batch, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal(1, result.EventCount);
        Assert.Collection(
            inputApi.Commands,
            command =>
            {
                Assert.Equal(WindowsInputCommandKind.KeyboardKey, command.Kind);
                Assert.Equal("KeyW", command.Code);
                Assert.Equal("w", command.Key);
                Assert.True(command.Pressed);
            },
            command =>
            {
                Assert.Equal(WindowsInputCommandKind.KeyboardKey, command.Kind);
                Assert.Equal("KeyW", command.Code);
                Assert.Equal("w", command.Key);
                Assert.False(command.Pressed);
            });
    }

    [Fact]
    public async Task MissingDisplayFailsWithoutSendingInput()
    {
        var displayApi = new FakeWindowsDisplayApi
        {
            CurrentTopology = DisplayTopologySnapshot.PhysicalOnly(
                "physical-laptop-panel",
                2560,
                1600,
                240)
        };
        var inputApi = new FakeWindowsInputApi();
        var sink = new WindowsClientInputSink(displayApi, inputApi);
        var batch = new ClientInputBatch(
            "z-fold-7",
            "session-1",
            "client-z-fold-7",
            11,
            [new ClientInputEvent("pointer", "tap", PointerId: 1, X: 0.5, Y: 0.5)]);

        ClientInputResult result = await sink.ForwardAsync(batch, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("client-z-fold-7", result.Error, StringComparison.Ordinal);
        Assert.Empty(inputApi.Commands);
    }

    [Fact]
    public async Task UnsupportedInputFailsWithoutSendingPartialCommands()
    {
        var displayApi = new FakeWindowsDisplayApi
        {
            CurrentTopology = DisplayTopologySnapshot.Extended(
                "physical-laptop-panel",
                "client-z-fold-7",
                2560,
                1600,
                120,
                virtualPrimary: true)
        };
        var inputApi = new FakeWindowsInputApi();
        var sink = new WindowsClientInputSink(displayApi, inputApi);
        var batch = new ClientInputBatch(
            "z-fold-7",
            "session-1",
            "client-z-fold-7",
            12,
            [new ClientInputEvent("controller", "press", Key: "A", Code: "KeyA")]);

        ClientInputResult result = await sink.ForwardAsync(batch, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Unsupported input event", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(inputApi.Commands);
    }

    [Fact]
    public async Task PointerCoordinatesMustBeNormalized()
    {
        var displayApi = new FakeWindowsDisplayApi
        {
            CurrentTopology = DisplayTopologySnapshot.Extended(
                "physical-laptop-panel",
                "client-z-fold-7",
                2560,
                1600,
                120,
                virtualPrimary: true)
        };
        var inputApi = new FakeWindowsInputApi();
        var sink = new WindowsClientInputSink(displayApi, inputApi);
        var batch = new ClientInputBatch(
            "z-fold-7",
            "session-1",
            "client-z-fold-7",
            13,
            [new ClientInputEvent("pointer", "tap", PointerId: 1, X: 1.5, Y: 0.5)]);

        ClientInputResult result = await sink.ForwardAsync(batch, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("normalized", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(inputApi.Commands);
    }

    [Fact]
    public async Task UnsupportedButtonMaskFailsWithoutSendingInput()
    {
        var displayApi = new FakeWindowsDisplayApi
        {
            CurrentTopology = DisplayTopologySnapshot.Extended(
                "physical-laptop-panel",
                "client-z-fold-7",
                2560,
                1600,
                120,
                virtualPrimary: true)
        };
        var inputApi = new FakeWindowsInputApi();
        var sink = new WindowsClientInputSink(displayApi, inputApi);
        var batch = new ClientInputBatch(
            "z-fold-7",
            "session-1",
            "client-z-fold-7",
            14,
            [new ClientInputEvent("pointer", "tap", PointerId: 1, X: 0.5, Y: 0.5, Buttons: 8)]);

        ClientInputResult result = await sink.ForwardAsync(batch, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("button mask", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(inputApi.Commands);
    }

    private sealed class FakeWindowsInputApi : IWindowsInputApi
    {
        public List<WindowsInputCommand> Commands { get; } = [];

        public Task<WindowsInputResult> SendAsync(
            IReadOnlyList<WindowsInputCommand> commands,
            CancellationToken cancellationToken)
        {
            Commands.AddRange(commands);
            return Task.FromResult(WindowsInputResult.Ok(commands.Count));
        }
    }
}

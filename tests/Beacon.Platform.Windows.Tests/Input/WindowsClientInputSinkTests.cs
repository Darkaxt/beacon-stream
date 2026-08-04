using Beacon.Core.Input;
using Beacon.Platform.Windows.Displays;
using Beacon.Platform.Windows.Input;
using Beacon.Platform.Windows.Tests.Displays;

namespace Beacon.Platform.Windows.Tests.Input;

public sealed class WindowsClientInputSinkTests
{
    [Fact]
    public async Task StreamPointerMoveAndButtonTransitionsMapExactly()
    {
        var displayApi = new FakeWindowsDisplayApi
        {
            CurrentTopology = new DisplayTopologySnapshot(
                [new DisplayPathSnapshot("client-z", DisplayPathKind.Virtual, 100, 80, 60, true, 300, -20)],
                IsMirrorMode: false)
        };
        var inputApi = new FakeWindowsInputApi();
        var sink = new WindowsClientInputSink(displayApi, inputApi);

        ClientInputResult result = await sink.ForwardAsync(
            new ClientInputBatch(
                "client",
                "session",
                "client-z",
                1,
                [
                    ClientInputEvent.StreamPointer(ClientPointerAction.Move, 1, 2, 0, 0),
                    ClientInputEvent.StreamPointer(ClientPointerAction.ButtonDown, 3, 4, 0, 2),
                    ClientInputEvent.StreamPointer(ClientPointerAction.ButtonUp, 5, 6, 0, 2)
                ]),
            CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal(
            [
                (WindowsInputCommandKind.PointerMove, 300, -20, null, null),
                (WindowsInputCommandKind.PointerMove, 300, -20, null, null),
                (WindowsInputCommandKind.PointerButton, null, null, "right", true),
                (WindowsInputCommandKind.PointerMove, 300, -20, null, null),
                (WindowsInputCommandKind.PointerButton, null, null, "right", false)
            ],
            inputApi.Commands.Select(command =>
                (command.Kind, command.X, command.Y, command.Button, command.Pressed)));
    }

    [Fact]
    public async Task StreamPointerWheelAndScanCodeMapToExactWindowsCommands()
    {
        var displayApi = new FakeWindowsDisplayApi
        {
            CurrentTopology = new DisplayTopologySnapshot(
                [new DisplayPathSnapshot("client-z", DisplayPathKind.Virtual, 100, 80, 60, true, 300, -20)],
                IsMirrorMode: false)
        };
        var inputApi = new FakeWindowsInputApi();
        var sink = new WindowsClientInputSink(displayApi, inputApi);
        var batch = new ClientInputBatch(
            "client", "session", "client-z", 1,
            [
                ClientInputEvent.StreamPointer(ClientPointerAction.Scroll, 11, 13, -120, 2),
                ClientInputEvent.StreamKeyboard(0xE04D, pressed: false)
            ]);

        ClientInputResult result = await sink.ForwardAsync(batch, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Collection(
            inputApi.Commands,
            command =>
            {
                Assert.Equal(WindowsInputCommandKind.PointerMove, command.Kind);
                Assert.Equal(300, command.X);
                Assert.Equal(-20, command.Y);
            },
            command =>
            {
                Assert.Equal(WindowsInputCommandKind.PointerWheel, command.Kind);
                Assert.Equal(-120, command.WheelDelta);
            },
            command =>
            {
                Assert.Equal(WindowsInputCommandKind.KeyboardScanCode, command.Kind);
                Assert.Equal(0xE04Du, command.ScanCode);
                Assert.False(command.Pressed);
            });
    }

    [Theory]
    [InlineData(0, -300, 200)]
    [InlineData(32768, -250, 240)]
    [InlineData(65535, -200, 280)]
    public async Task StreamPointerFixedPointCoordinatesStayWithinTargetDisplay(
        int fixedPoint,
        int expectedX,
        int expectedY)
    {
        var displayApi = new FakeWindowsDisplayApi
        {
            CurrentTopology = new DisplayTopologySnapshot(
                [
                    new DisplayPathSnapshot("other", DisplayPathKind.Physical, 400, 300, 60, false, -700, 0),
                    new DisplayPathSnapshot("client-z", DisplayPathKind.Virtual, 101, 81, 60, true, -300, 200)
                ],
                IsMirrorMode: false)
        };
        var inputApi = new FakeWindowsInputApi();
        var sink = new WindowsClientInputSink(displayApi, inputApi);

        ClientInputResult result = await sink.ForwardAsync(
            new ClientInputBatch(
                "client",
                "session",
                "client-z",
                1,
                [ClientInputEvent.StreamPointer(ClientPointerAction.Move, fixedPoint, fixedPoint, 0, 0)]),
            CancellationToken.None);

        Assert.True(result.Success, result.Error);
        WindowsInputCommand move = Assert.Single(inputApi.Commands);
        Assert.Equal(expectedX, move.X);
        Assert.Equal(expectedY, move.Y);
        Assert.InRange(move.X!.Value, -300, -200);
        Assert.InRange(move.Y!.Value, 200, 280);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    public async Task StreamPointerRejectsOutOfRangeFixedPointCoordinates(int fixedPoint)
    {
        var displayApi = new FakeWindowsDisplayApi
        {
            CurrentTopology = new DisplayTopologySnapshot(
                [new DisplayPathSnapshot("client-z", DisplayPathKind.Virtual, 101, 81, 60, true, -300, 200)],
                IsMirrorMode: false)
        };
        var inputApi = new FakeWindowsInputApi();
        var sink = new WindowsClientInputSink(displayApi, inputApi);

        ClientInputResult result = await sink.ForwardAsync(
            new ClientInputBatch(
                "client",
                "session",
                "client-z",
                1,
                [ClientInputEvent.StreamPointer(ClientPointerAction.Move, fixedPoint, 0, 0, 0)]),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Pointer coordinates must be fixed-point values between 0 and 65535.", result.Error);
        Assert.Empty(inputApi.Commands);
    }

    [Fact]
    public async Task UnsupportedTouchCategoryReturnsFixedSanitizedError()
    {
        var displayApi = new FakeWindowsDisplayApi
        {
            CurrentTopology = DisplayTopologySnapshot.Extended(
                "physical", "client-z", 100, 80, 60, virtualPrimary: true)
        };
        var inputApi = new FakeWindowsInputApi();
        var sink = new WindowsClientInputSink(displayApi, inputApi);
        ClientInputEvent input =
            ClientInputEvent.StreamTouch(938475, ClientTouchAction.Down, 123456, 2, 3, 4, 5);

        ClientInputResult result = await sink.ForwardAsync(
            new ClientInputBatch("client", "session", "client-z", 1, [input]),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Unsupported input category: touch.", result.Error);
        Assert.DoesNotContain("938475", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("123456", result.Error, StringComparison.Ordinal);
        Assert.Empty(inputApi.Commands);
    }

    [Fact]
    public async Task ControllerBatchActivatesSessionAndUsesVirtualControllerOnly()
    {
        var displayApi = new FakeWindowsDisplayApi
        {
            CurrentTopology = DisplayTopologySnapshot.Extended(
                "physical", "client-z", 100, 80, 60, virtualPrimary: true)
        };
        var operations = new List<string>();
        var inputApi = new FakeWindowsInputApi(operations);
        var target = new FakeWindowsSessionInputTargetActivator(operations);
        var controller = new FakeWindowsVirtualControllerApi(operations);
        var sink = new WindowsClientInputSink(displayApi, inputApi, target, controller);

        ClientInputResult result = await sink.ForwardAsync(
            new ClientInputBatch(
                "client",
                "session",
                "client-z",
                1,
                [
                    ClientInputEvent.StreamController(0, 12, 1),
                    ClientInputEvent.StreamController(0, 18, -32768),
                ]),
            CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal(2, result.EventCount);
        Assert.Equal(["activate:session:client-z", "controller:session:2"], operations);
        Assert.Empty(inputApi.Commands);
        Assert.Equal(2, Assert.Single(controller.Batches).Count);
    }

    [Fact]
    public async Task VirtualControllerFailurePreservesActionableResultCode()
    {
        var displayApi = new FakeWindowsDisplayApi
        {
            CurrentTopology = DisplayTopologySnapshot.Extended(
                "physical", "client-z", 100, 80, 60, virtualPrimary: true)
        };
        var controller = new FakeWindowsVirtualControllerApi([])
        {
            Result = WindowsVirtualControllerResult.Fail(
                "ViGEmBus is unavailable.",
                "vigem-unavailable"),
        };
        var sink = new WindowsClientInputSink(
            displayApi,
            new FakeWindowsInputApi(),
            new FakeWindowsSessionInputTargetActivator([]),
            controller);

        ClientInputResult result = await sink.ForwardAsync(
            new ClientInputBatch(
                "client",
                "session",
                "client-z",
                1,
                [ClientInputEvent.StreamController(0, 12, 1)]),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("vigem-unavailable", result.ResultCode);
        Assert.Contains("ViGEmBus", result.Error, StringComparison.Ordinal);
    }
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
    public async Task SessionTargetActivatesBeforeWindowsInputIsSent()
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
        var operations = new List<string>();
        var inputApi = new FakeWindowsInputApi(operations);
        var target = new FakeWindowsSessionInputTargetActivator(operations);
        var sink = new WindowsClientInputSink(displayApi, inputApi, target);
        var batch = new ClientInputBatch(
            "z-fold-7",
            "session-1",
            "client-z-fold-7",
            15,
            [ClientInputEvent.StreamKeyboard(0x58, pressed: true)]);

        ClientInputResult result = await sink.ForwardAsync(batch, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal(["activate:session-1:client-z-fold-7", "send"], operations);
        Assert.Same(batch, Assert.Single(target.Batches));
    }

    [Fact]
    public async Task FailedSessionTargetActivationRejectsInputWithoutInjection()
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
        var target = new FakeWindowsSessionInputTargetActivator([])
        {
            Result = WindowsSessionInputTargetResult.Fail(
                "No verified session-owned window is active on the target display.")
        };
        var sink = new WindowsClientInputSink(displayApi, inputApi, target);

        ClientInputResult result = await sink.ForwardAsync(
            new ClientInputBatch(
                "z-fold-7",
                "session-1",
                "client-z-fold-7",
                16,
                [ClientInputEvent.StreamKeyboard(0x58, pressed: true)]),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("session-owned window", result.Error, StringComparison.Ordinal);
        Assert.Equal("session-target-activation-failed", result.ResultCode);
        Assert.Empty(inputApi.Commands);
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

    private sealed class FakeWindowsInputApi(List<string>? operations = null) : IWindowsInputApi
    {
        public List<WindowsInputCommand> Commands { get; } = [];

        public Task<WindowsInputResult> SendAsync(
            IReadOnlyList<WindowsInputCommand> commands,
            CancellationToken cancellationToken)
        {
            operations?.Add("send");
            Commands.AddRange(commands);
            return Task.FromResult(WindowsInputResult.Ok(commands.Count));
        }
    }

    private sealed class FakeWindowsSessionInputTargetActivator(List<string> operations)
        : IWindowsSessionInputTargetActivator
    {
        public WindowsSessionInputTargetResult Result { get; set; } =
            WindowsSessionInputTargetResult.Activated(processId: 100, windowHandle: 200);

        public List<ClientInputBatch> Batches { get; } = [];

        public Task<WindowsSessionInputTargetResult> ActivateAsync(
            ClientInputBatch batch,
            CancellationToken cancellationToken)
        {
            Batches.Add(batch);
            operations.Add($"activate:{batch.SessionId}:{batch.DisplayId}");
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeWindowsVirtualControllerApi(List<string> operations)
        : IWindowsVirtualControllerApi
    {
        public int ActiveSessionCount => Batches.Count == 0 ? 0 : 1;

        public Task PrepareSessionAsync(string sessionId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public WindowsVirtualControllerResult Result { get; init; } =
            WindowsVirtualControllerResult.Ok();

        public List<IReadOnlyList<ClientControllerInput>> Batches { get; } = [];

        public Task<WindowsVirtualControllerResult> ApplyAsync(
            string sessionId,
            IReadOnlyList<ClientControllerInput> events,
            CancellationToken cancellationToken)
        {
            Batches.Add(events);
            operations.Add($"controller:{sessionId}:{events.Count}");
            return Task.FromResult(Result);
        }

        public Task ReleaseSessionAsync(string sessionId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

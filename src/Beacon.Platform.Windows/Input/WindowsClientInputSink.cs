using Beacon.Core.Input;
using Beacon.Platform.Windows.Displays;

namespace Beacon.Platform.Windows.Input;

public sealed class WindowsClientInputSink : IClientInputSink, IClientInputHealthProvider
{
    private static readonly string[] EventTypes = ["pointer", "keyboard", "controller"];
    private static readonly string[] PointerActions = ["move", "down", "up", "tap"];
    private static readonly string[] KeyboardActions = ["down", "up", "press"];
    private readonly IWindowsDisplayApi displayApi;
    private readonly IWindowsInputApi inputApi;
    private readonly IWindowsSessionInputTargetActivator targetActivator;
    private readonly IWindowsVirtualControllerApi controllerApi;

    public WindowsClientInputSink(
        IWindowsDisplayApi displayApi,
        IWindowsInputApi inputApi,
        IWindowsSessionInputTargetActivator targetActivator,
        IWindowsVirtualControllerApi controllerApi)
    {
        this.displayApi = displayApi;
        this.inputApi = inputApi;
        this.targetActivator = targetActivator;
        this.controllerApi = controllerApi;
    }

    internal WindowsClientInputSink(
        IWindowsDisplayApi displayApi,
        IWindowsInputApi inputApi)
        : this(
            displayApi,
            inputApi,
            TestSessionInputTargetActivator.Instance,
            TestWindowsVirtualControllerApi.Instance)
    {
    }

    internal WindowsClientInputSink(
        IWindowsDisplayApi displayApi,
        IWindowsInputApi inputApi,
        IWindowsSessionInputTargetActivator targetActivator)
        : this(displayApi, inputApi, targetActivator, TestWindowsVirtualControllerApi.Instance)
    {
    }

    public async Task<ClientInputResult> ForwardAsync(ClientInputBatch batch, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        DisplayTopologySnapshot topology = await displayApi.QueryTopologyAsync(cancellationToken);
        DisplayPathSnapshot? display = topology.Paths.FirstOrDefault(path =>
            string.Equals(path.DisplayId, batch.DisplayId, StringComparison.Ordinal));
        if (display is null)
        {
            return ClientInputResult.Fail(
                $"Display '{batch.DisplayId}' is not active; refusing to send input for session '{batch.SessionId}'.",
                "display-inactive");
        }

        if (display.Width <= 0 || display.Height <= 0)
        {
            return ClientInputResult.Fail(
                $"Display '{batch.DisplayId}' has invalid geometry {display.Width}x{display.Height}; refusing to send input.",
                "display-inactive");
        }

        List<WindowsInputCommand> commands = [];
        List<ClientControllerInput> controllerEvents = [];
        foreach (ClientInputEvent inputEvent in batch.Events)
        {
            if (!TryAppendCommands(
                    inputEvent,
                    display,
                    commands,
                    controllerEvents,
                    out string? error))
            {
                return ClientInputResult.Fail(error, "input-invalid");
            }
        }

        WindowsSessionInputTargetResult target = await targetActivator.ActivateAsync(
            batch,
            cancellationToken).ConfigureAwait(false);
        if (!target.Success)
        {
            return ClientInputResult.Fail(
                target.Error ?? "The session-owned Windows input target could not be activated.",
                "session-target-activation-failed");
        }

        if (commands.Count > 0)
        {
            WindowsInputResult send = await inputApi.SendAsync(commands, cancellationToken);
            if (!send.Success)
            {
                return ClientInputResult.Fail(
                send.Error ?? "Windows input dispatch failed.",
                "sendinput-failed");
            }
        }
        if (controllerEvents.Count > 0)
        {
            WindowsVirtualControllerResult controller = await controllerApi.ApplyAsync(
                batch.SessionId,
                controllerEvents,
                cancellationToken).ConfigureAwait(false);
            if (!controller.Success)
            {
                return ClientInputResult.Fail(
                    controller.Error ?? "Windows virtual controller dispatch failed.",
                    controller.ResultCode);
            }
        }
        return ClientInputResult.Ok(batch.Events.Count);
    }

    public ClientInputHealth GetHealth() =>
        new(
            Ready: true,
            Backend: "windows-sendinput",
            Diagnostic: "Session-targeted Windows input sink ready; Xbox controller targets are created lazily through ViGEm.",
            SupportedEventTypes: EventTypes,
            SupportedPointerActions: PointerActions,
            SupportedKeyboardActions: KeyboardActions);

    private static bool TryAppendCommands(
        ClientInputEvent inputEvent,
        DisplayPathSnapshot display,
        List<WindowsInputCommand> commands,
        List<ClientControllerInput> controllerEvents,
        out string error)
    {
        if (inputEvent.Pointer is not null)
        {
            return TryAppendStreamPointerCommands(inputEvent.Pointer, display, commands, out error);
        }
        if (inputEvent.Keyboard is not null)
        {
            commands.Add(WindowsInputCommand.KeyboardScanCode(
                inputEvent.Keyboard.ScanCode,
                inputEvent.Keyboard.Pressed));
            error = string.Empty;
            return true;
        }
        if (inputEvent.Controller is not null)
        {
            controllerEvents.Add(inputEvent.Controller);
            error = string.Empty;
            return true;
        }
        if (inputEvent.Touch is not null)
        {
            error = "Unsupported input category: touch.";
            return false;
        }

        string eventType = inputEvent.Type?.Trim().ToLowerInvariant() ?? string.Empty;
        return eventType switch
        {
            "pointer" => TryAppendPointerCommands(inputEvent, display, commands, out error),
            "keyboard" => TryAppendKeyboardCommands(inputEvent, commands, out error),
            _ => UnsupportedEventType(inputEvent, out error)
        };
    }

    private static bool TryAppendStreamPointerCommands(
        ClientPointerInput pointer,
        DisplayPathSnapshot display,
        List<WindowsInputCommand> commands,
        out string error)
    {
        if (!IsFixedPointCoordinate(pointer.X) || !IsFixedPointCoordinate(pointer.Y))
        {
            error = "Pointer coordinates must be fixed-point values between 0 and 65535.";
            return false;
        }

        int x = ToDisplayFixedPointPixel(pointer.X, display.X, display.Width);
        int y = ToDisplayFixedPointPixel(pointer.Y, display.Y, display.Height);
        commands.Add(WindowsInputCommand.PointerMove(x, y));
        switch (pointer.Action)
        {
            case ClientPointerAction.Move:
                error = string.Empty;
                return true;
            case ClientPointerAction.ButtonDown:
            case ClientPointerAction.ButtonUp:
                if (!TryStreamButtonName(pointer.Button, out string button))
                {
                    commands.RemoveAt(commands.Count - 1);
                    error = "Unsupported pointer button category.";
                    return false;
                }
                commands.Add(WindowsInputCommand.PointerButton(
                    button,
                    pointer.Action == ClientPointerAction.ButtonDown));
                error = string.Empty;
                return true;
            case ClientPointerAction.Scroll:
                commands.Add(WindowsInputCommand.PointerWheel(pointer.WheelDelta));
                error = string.Empty;
                return true;
            default:
                commands.RemoveAt(commands.Count - 1);
                error = "Unsupported pointer action category.";
                return false;
        }
    }

    private static bool TryStreamButtonName(uint button, out string name)
    {
        name = button switch
        {
            0 or 1 => "left",
            2 => "right",
            3 or 4 => "middle",
            _ => string.Empty,
        };
        return name.Length != 0;
    }

    private static bool TryAppendPointerCommands(
        ClientInputEvent inputEvent,
        DisplayPathSnapshot display,
        List<WindowsInputCommand> commands,
        out string error)
    {
        if (!TryMapPointerCoordinate(inputEvent, display, out int x, out int y, out error))
        {
            return false;
        }

        string action = inputEvent.Action?.Trim().ToLowerInvariant() ?? string.Empty;
        switch (action)
        {
            case "move":
                commands.Add(WindowsInputCommand.PointerMove(x, y));
                return true;
            case "down":
                if (!TryButtonName(inputEvent.Buttons, out string downButton, out error))
                {
                    return false;
                }

                commands.Add(WindowsInputCommand.PointerMove(x, y));
                commands.Add(WindowsInputCommand.PointerButton(downButton, pressed: true));
                return true;
            case "up":
                if (!TryButtonName(inputEvent.Buttons, out string upButton, out error))
                {
                    return false;
                }

                commands.Add(WindowsInputCommand.PointerMove(x, y));
                commands.Add(WindowsInputCommand.PointerButton(upButton, pressed: false));
                return true;
            case "tap":
                if (!TryButtonName(inputEvent.Buttons, out string tapButton, out error))
                {
                    return false;
                }

                commands.Add(WindowsInputCommand.PointerMove(x, y));
                commands.Add(WindowsInputCommand.PointerButton(tapButton, pressed: true));
                commands.Add(WindowsInputCommand.PointerButton(tapButton, pressed: false));
                return true;
            default:
                error = $"Unsupported input event action '{inputEvent.Action}'.";
                return false;
        }
    }

    private static bool TryAppendKeyboardCommands(
        ClientInputEvent inputEvent,
        List<WindowsInputCommand> commands,
        out string error)
    {
        if (!TryKeyboardIdentity(inputEvent, out string? code, out string? key, out error))
        {
            return false;
        }

        string action = inputEvent.Action?.Trim().ToLowerInvariant() ?? string.Empty;
        switch (action)
        {
            case "down":
                commands.Add(WindowsInputCommand.KeyboardKey(code, key, pressed: true));
                return true;
            case "up":
                commands.Add(WindowsInputCommand.KeyboardKey(code, key, pressed: false));
                return true;
            case "press":
                commands.Add(WindowsInputCommand.KeyboardKey(code, key, pressed: true));
                commands.Add(WindowsInputCommand.KeyboardKey(code, key, pressed: false));
                return true;
            default:
                error = $"Unsupported keyboard input action '{inputEvent.Action}'.";
                return false;
        }
    }

    private static bool TryKeyboardIdentity(
        ClientInputEvent inputEvent,
        out string? code,
        out string? key,
        out string error)
    {
        code = string.IsNullOrWhiteSpace(inputEvent.Code) ? null : inputEvent.Code.Trim();
        key = string.IsNullOrWhiteSpace(inputEvent.Key) ? null : inputEvent.Key.Trim();
        if (code is not null || key is not null)
        {
            error = string.Empty;
            return true;
        }

        error = "Keyboard input requires a code or key value.";
        return false;
    }

    private static bool UnsupportedEventType(ClientInputEvent inputEvent, out string error)
    {
        error = $"Unsupported input event type '{inputEvent.Type}'.";
        return false;
    }

    private static bool TryMapPointerCoordinate(
        ClientInputEvent inputEvent,
        DisplayPathSnapshot display,
        out int x,
        out int y,
        out string error)
    {
        x = 0;
        y = 0;

        if (inputEvent.X is null || inputEvent.Y is null)
        {
            error = "Pointer input requires normalized x and y coordinates.";
            return false;
        }

        if (!IsNormalized(inputEvent.X.Value) || !IsNormalized(inputEvent.Y.Value))
        {
            error = $"Pointer coordinates must be normalized between 0 and 1. Received x={inputEvent.X}, y={inputEvent.Y}.";
            return false;
        }

        x = display.X + ToDisplayPixel(inputEvent.X.Value, display.Width);
        y = display.Y + ToDisplayPixel(inputEvent.Y.Value, display.Height);
        error = string.Empty;
        return true;
    }

    private static bool IsNormalized(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0 && value <= 1;

    private static bool IsFixedPointCoordinate(int value) => value is >= 0 and <= ushort.MaxValue;

    private static int ToDisplayFixedPointPixel(int fixedPoint, int origin, int size) =>
        checked(origin + (int)(((long)fixedPoint * (size - 1) + (ushort.MaxValue / 2)) / ushort.MaxValue));

    private static int ToDisplayPixel(double normalized, int size) =>
        size <= 1
            ? 0
            : (int)Math.Round(normalized * (size - 1), MidpointRounding.AwayFromZero);

    private static bool TryButtonName(int? buttons, out string button, out string error)
    {
        button = buttons switch
        {
            null or 0 or 1 => "left",
            2 => "right",
            4 => "middle",
            _ => string.Empty
        };

        if (!string.IsNullOrEmpty(button))
        {
            error = string.Empty;
            return true;
        }

        error = $"Unsupported pointer button mask '{buttons}'.";
        return false;
    }

    private sealed class TestSessionInputTargetActivator : IWindowsSessionInputTargetActivator
    {
        public static TestSessionInputTargetActivator Instance { get; } = new();

        public Task<WindowsSessionInputTargetResult> ActivateAsync(
            ClientInputBatch batch,
            CancellationToken cancellationToken) =>
            Task.FromResult(WindowsSessionInputTargetResult.Activated(0, 0));
    }

    private sealed class TestWindowsVirtualControllerApi : IWindowsVirtualControllerApi
    {
        public static TestWindowsVirtualControllerApi Instance { get; } = new();

        public Task<WindowsVirtualControllerResult> ApplyAsync(
            string sessionId,
            IReadOnlyList<ClientControllerInput> events,
            CancellationToken cancellationToken) =>
            Task.FromResult(WindowsVirtualControllerResult.Ok());

        public Task ReleaseSessionAsync(string sessionId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

namespace Beacon.Core.Input;

public sealed record ClientInputEvent(
    string Type,
    string Action,
    int? PointerId = null,
    double? X = null,
    double? Y = null,
    int? Buttons = null,
    string? Key = null,
    string? Code = null,
    double? Value = null)
{
    public ClientPointerInput? Pointer { get; init; }

    public ClientKeyboardInput? Keyboard { get; init; }

    public ClientControllerInput? Controller { get; init; }

    public ClientTouchInput? Touch { get; init; }

    public static ClientInputEvent StreamPointer(
        ClientPointerAction action,
        int x,
        int y,
        int wheelDelta,
        uint button) =>
        new("pointer", StreamActionName(action))
        {
            Pointer = new ClientPointerInput(action, x, y, wheelDelta, button),
        };

    public static ClientInputEvent StreamKeyboard(uint scanCode, bool pressed) =>
        new("keyboard", pressed ? "down" : "up")
        {
            Keyboard = new ClientKeyboardInput(scanCode, pressed),
        };

    public static ClientInputEvent StreamController(uint controllerIndex, uint controlId, int value) =>
        new("controller", "value")
        {
            Controller = new ClientControllerInput(controllerIndex, controlId, value),
        };

    public static ClientInputEvent StreamTouch(
        uint contactId,
        ClientTouchAction action,
        uint xNumerator,
        uint yNumerator,
        uint coordinateDenominator,
        uint pressureNumerator,
        uint pressureDenominator) =>
        new("touch", action.ToString().ToLowerInvariant())
        {
            Touch = new ClientTouchInput(
                contactId,
                action,
                xNumerator,
                yNumerator,
                coordinateDenominator,
                pressureNumerator,
                pressureDenominator),
        };

    public override string ToString() =>
        $"ClientInputEvent {{ Type = {SafeCategory(Type)}, Action = {SafeCategory(Action)}, Payload = [redacted] }}";

    private static string StreamActionName(ClientPointerAction action) => action switch
    {
        ClientPointerAction.Move => "move",
        ClientPointerAction.ButtonDown => "down",
        ClientPointerAction.ButtonUp => "up",
        ClientPointerAction.Scroll => "scroll",
        _ => "unknown",
    };

    private static string SafeCategory(string? value) => value switch
    {
        "pointer" => "pointer",
        "keyboard" => "keyboard",
        "controller" => "controller",
        "touch" => "touch",
        "move" => "move",
        "down" => "down",
        "up" => "up",
        "tap" => "tap",
        "press" => "press",
        "scroll" => "scroll",
        "value" => "value",
        _ => "unknown",
    };
}

public enum ClientPointerAction
{
    Move,
    ButtonDown,
    ButtonUp,
    Scroll,
}

public enum ClientTouchAction
{
    Down,
    Move,
    Up,
    Cancel,
}

public sealed record ClientPointerInput(
    ClientPointerAction Action,
    int X,
    int Y,
    int WheelDelta,
    uint Button)
{
    public override string ToString() => "ClientPointerInput { Payload = [redacted] }";
}

public sealed record ClientKeyboardInput(uint ScanCode, bool Pressed)
{
    public override string ToString() => "ClientKeyboardInput { Payload = [redacted] }";
}

public sealed record ClientControllerInput(uint ControllerIndex, uint ControlId, int Value)
{
    public override string ToString() => "ClientControllerInput { Payload = [redacted] }";
}

public sealed record ClientTouchInput(
    uint ContactId,
    ClientTouchAction Action,
    uint XNumerator,
    uint YNumerator,
    uint CoordinateDenominator,
    uint PressureNumerator,
    uint PressureDenominator)
{
    public override string ToString() => "ClientTouchInput { Payload = [redacted] }";
}

public sealed record ClientInputBatch(
    string ClientId,
    string SessionId,
    string DisplayId,
    long Sequence,
    IReadOnlyList<ClientInputEvent> Events);

public sealed record ClientInputResult(bool Success, int EventCount, string? Error)
{
    public string ResultCode { get; init; } = Success ? "input-forwarded" : "input-rejected";

    public static ClientInputResult Ok(int eventCount) =>
        new(true, eventCount, null) { ResultCode = "input-forwarded" };

    public static ClientInputResult Fail(string error, string resultCode = "input-rejected") =>
        new(false, 0, error) { ResultCode = resultCode };
}

public sealed record ClientInputHealth(
    bool Ready,
    string Backend,
    string Diagnostic,
    IReadOnlyList<string> SupportedEventTypes,
    IReadOnlyList<string> SupportedPointerActions,
    IReadOnlyList<string> SupportedKeyboardActions)
{
    public static ClientInputHealth Unknown(string diagnostic) =>
        new(false, "unknown", diagnostic, [], [], []);
}

public interface IClientInputSink
{
    Task<ClientInputResult> ForwardAsync(ClientInputBatch batch, CancellationToken cancellationToken);
}

public interface IClientInputHealthProvider
{
    ClientInputHealth GetHealth();
}

public interface IClientInputSessionLifecycle
{
    Task ReleaseSessionAsync(string sessionId, CancellationToken cancellationToken);
}

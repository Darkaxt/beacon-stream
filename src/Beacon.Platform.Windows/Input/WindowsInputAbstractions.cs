namespace Beacon.Platform.Windows.Input;

public interface IWindowsInputApi
{
    Task<WindowsInputResult> SendAsync(
        IReadOnlyList<WindowsInputCommand> commands,
        CancellationToken cancellationToken);
}

public sealed record WindowsInputCommand(
    WindowsInputCommandKind Kind,
    int? X = null,
    int? Y = null,
    string? Button = null,
    bool? Pressed = null,
    string? Key = null,
    string? Code = null)
{
    public static WindowsInputCommand PointerMove(int x, int y) =>
        new(WindowsInputCommandKind.PointerMove, X: x, Y: y);

    public static WindowsInputCommand PointerButton(string button, bool pressed) =>
        new(WindowsInputCommandKind.PointerButton, Button: button, Pressed: pressed);

    public static WindowsInputCommand KeyboardKey(string? code, string? key, bool pressed) =>
        new(WindowsInputCommandKind.KeyboardKey, Pressed: pressed, Key: key, Code: code);
}

public enum WindowsInputCommandKind
{
    PointerMove,
    PointerButton,
    KeyboardKey
}

public sealed record WindowsInputResult(bool Success, int CommandCount, string? Error)
{
    public static WindowsInputResult Ok(int commandCount) => new(true, commandCount, null);

    public static WindowsInputResult Fail(string error) => new(false, 0, error);
}

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
    string? Code = null,
    int? WheelDelta = null,
    uint? ScanCode = null)
{
    public WindowsInputCommand(
        WindowsInputCommandKind Kind,
        int? X,
        int? Y,
        string? Button,
        bool? Pressed,
        string? Key,
        string? Code)
        : this(Kind, X, Y, Button, Pressed, Key, Code, WheelDelta: null, ScanCode: null)
    {
    }

    public void Deconstruct(
        out WindowsInputCommandKind Kind,
        out int? X,
        out int? Y,
        out string? Button,
        out bool? Pressed,
        out string? Key,
        out string? Code)
    {
        Kind = this.Kind;
        X = this.X;
        Y = this.Y;
        Button = this.Button;
        Pressed = this.Pressed;
        Key = this.Key;
        Code = this.Code;
    }

    public static WindowsInputCommand PointerMove(int x, int y) =>
        new(WindowsInputCommandKind.PointerMove, X: x, Y: y);

    public static WindowsInputCommand PointerButton(string button, bool pressed) =>
        new(WindowsInputCommandKind.PointerButton, Button: button, Pressed: pressed);

    public static WindowsInputCommand PointerWheel(int wheelDelta) =>
        new(WindowsInputCommandKind.PointerWheel, WheelDelta: wheelDelta);

    public static WindowsInputCommand KeyboardKey(string? code, string? key, bool pressed) =>
        new(WindowsInputCommandKind.KeyboardKey, Pressed: pressed, Key: key, Code: code);

    public static WindowsInputCommand KeyboardScanCode(uint scanCode, bool pressed) =>
        new(WindowsInputCommandKind.KeyboardScanCode, Pressed: pressed, ScanCode: scanCode);
}

public enum WindowsInputCommandKind
{
    PointerMove,
    PointerButton,
    PointerWheel,
    KeyboardKey,
    KeyboardScanCode,
}

internal enum WindowsInputEncodingKind
{
    Mouse,
    Keyboard,
}

[Flags]
internal enum WindowsInputFlags : uint
{
    None = 0,
    ExtendedKey = 0x0001,
    KeyUp = 0x0002,
    ScanCode = 0x0008,
    Wheel = 0x0800,
}

internal sealed record WindowsInputEncoding(
    WindowsInputEncodingKind Kind,
    WindowsInputFlags Flags,
    int MouseData = 0,
    ushort ScanCode = 0);

public sealed record WindowsInputResult(bool Success, int CommandCount, string? Error)
{
    public static WindowsInputResult Ok(int commandCount) => new(true, commandCount, null);

    public static WindowsInputResult Fail(string error) => new(false, 0, error);
}

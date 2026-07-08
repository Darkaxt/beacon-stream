using System.Runtime.InteropServices;

namespace Beacon.Platform.Windows.Input;

public sealed class WindowsInputApi : IWindowsInputApi
{
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseEventMove = 0x0001;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;
    private const uint MouseEventMiddleDown = 0x0020;
    private const uint MouseEventMiddleUp = 0x0040;
    private const uint MouseEventVirtualDesk = 0x4000;
    private const uint MouseEventAbsolute = 0x8000;
    private const uint KeyEventKeyUp = 0x0002;

    public Task<WindowsInputResult> SendAsync(
        IReadOnlyList<WindowsInputCommand> commands,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (commands.Count == 0)
        {
            return Task.FromResult(WindowsInputResult.Ok(0));
        }

        VirtualDesktopBounds bounds = GetVirtualDesktopBounds();
        Input[] inputs = new Input[commands.Count];
        for (int index = 0; index < commands.Count; index++)
        {
            if (!TryCreateInput(commands[index], bounds, out inputs[index], out string? error))
            {
                return Task.FromResult(WindowsInputResult.Fail(error));
            }
        }

        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            return Task.FromResult(WindowsInputResult.Fail(
                $"SendInput sent {sent} of {inputs.Length} commands. Win32={Marshal.GetLastWin32Error()}."));
        }

        return Task.FromResult(WindowsInputResult.Ok(inputs.Length));
    }

    public static int ToAbsoluteCoordinate(int coordinate, int origin, int size)
    {
        if (size <= 1)
        {
            return 0;
        }

        int offset = Math.Clamp(coordinate - origin, 0, size - 1);
        return (int)Math.Round(offset * 65535d / (size - 1), MidpointRounding.AwayFromZero);
    }

    private static bool TryCreateInput(
        WindowsInputCommand command,
        VirtualDesktopBounds bounds,
        out Input input,
        out string error)
    {
        input = new Input { Type = InputMouse };
        error = string.Empty;

        switch (command.Kind)
        {
            case WindowsInputCommandKind.PointerMove:
                if (command.X is null || command.Y is null)
                {
                    error = "Pointer move command is missing coordinates.";
                    return false;
                }

                input.Union.Mouse = new MouseInput
                {
                    Dx = ToAbsoluteCoordinate(command.X.Value, bounds.X, bounds.Width),
                    Dy = ToAbsoluteCoordinate(command.Y.Value, bounds.Y, bounds.Height),
                    DwFlags = MouseEventMove | MouseEventAbsolute | MouseEventVirtualDesk
                };
                return true;
            case WindowsInputCommandKind.PointerButton:
                if (!TryMapButton(command.Button, command.Pressed == true, out uint flags))
                {
                    error = $"Unsupported pointer button '{command.Button}'.";
                    return false;
                }

                input.Union.Mouse = new MouseInput { DwFlags = flags };
                return true;
            case WindowsInputCommandKind.KeyboardKey:
                if (!TryMapKeyboardVirtualKey(command.Code, command.Key, out ushort virtualKey))
                {
                    error = $"Unsupported keyboard key code '{command.Code ?? command.Key}'.";
                    return false;
                }

                input.Type = InputKeyboard;
                input.Union.Keyboard = new KeyboardInput
                {
                    WVk = virtualKey,
                    DwFlags = command.Pressed == false ? KeyEventKeyUp : 0
                };
                return true;
            default:
                error = $"Unsupported Windows input command '{command.Kind}'.";
                return false;
        }
    }

    public static bool TryMapKeyboardVirtualKey(string? code, string? key, out ushort virtualKey)
    {
        virtualKey = 0;
        string normalizedCode = code?.Trim() ?? string.Empty;
        if (TryMapKeyboardCode(normalizedCode, out virtualKey))
        {
            return true;
        }

        string normalizedKey = key?.Trim() ?? string.Empty;
        if (normalizedKey.Length == 1)
        {
            char ch = normalizedKey[0];
            if (ch is >= 'a' and <= 'z')
            {
                virtualKey = (ushort)char.ToUpperInvariant(ch);
                return true;
            }

            if (ch is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                virtualKey = ch;
                return true;
            }

            if (ch == ' ')
            {
                virtualKey = 0x20;
                return true;
            }
        }

        return TryMapKeyboardCode(normalizedKey, out virtualKey);
    }

    private static bool TryMapKeyboardCode(string code, out ushort virtualKey)
    {
        virtualKey = 0;
        if (code.Length == 4 && code.StartsWith("Key", StringComparison.OrdinalIgnoreCase))
        {
            char letter = char.ToUpperInvariant(code[3]);
            if (letter is >= 'A' and <= 'Z')
            {
                virtualKey = letter;
                return true;
            }
        }

        if (code.Length == 6 && code.StartsWith("Digit", StringComparison.OrdinalIgnoreCase))
        {
            char digit = code[5];
            if (digit is >= '0' and <= '9')
            {
                virtualKey = digit;
                return true;
            }
        }

        if (code.StartsWith("Numpad", StringComparison.OrdinalIgnoreCase) &&
            code.Length == 7 &&
            code[6] is >= '0' and <= '9')
        {
            virtualKey = (ushort)(0x60 + (code[6] - '0'));
            return true;
        }

        if (code.Length is >= 2 and <= 3 &&
            code[0] is 'F' or 'f' &&
            int.TryParse(code[1..], out int functionKey) &&
            functionKey is >= 1 and <= 12)
        {
            virtualKey = (ushort)(0x70 + functionKey - 1);
            return true;
        }

        virtualKey = code.ToLowerInvariant() switch
        {
            "backspace" => 0x08,
            "tab" => 0x09,
            "enter" => 0x0D,
            "shiftleft" or "shiftright" or "shift" => 0x10,
            "controlleft" or "controlright" or "control" or "ctrl" => 0x11,
            "altleft" or "altright" or "alt" => 0x12,
            "pause" => 0x13,
            "capslock" => 0x14,
            "escape" or "esc" => 0x1B,
            "space" => 0x20,
            "pageup" => 0x21,
            "pagedown" => 0x22,
            "end" => 0x23,
            "home" => 0x24,
            "arrowleft" or "left" => 0x25,
            "arrowup" or "up" => 0x26,
            "arrowright" or "right" => 0x27,
            "arrowdown" or "down" => 0x28,
            "printscreen" => 0x2C,
            "insert" => 0x2D,
            "delete" or "del" => 0x2E,
            "metaleft" or "metaright" or "osleft" or "osright" or "win" => 0x5B,
            _ => 0
        };

        return virtualKey != 0;
    }

    private static bool TryMapButton(string? button, bool pressed, out uint flags)
    {
        flags = 0;
        switch (button?.Trim().ToLowerInvariant())
        {
            case "left":
                flags = pressed ? MouseEventLeftDown : MouseEventLeftUp;
                return true;
            case "right":
                flags = pressed ? MouseEventRightDown : MouseEventRightUp;
                return true;
            case "middle":
                flags = pressed ? MouseEventMiddleDown : MouseEventMiddleUp;
                return true;
            default:
                return false;
        }
    }

    private static VirtualDesktopBounds GetVirtualDesktopBounds() =>
        new(
            NativeMethods.GetSystemMetrics(SmXVirtualScreen),
            NativeMethods.GetSystemMetrics(SmYVirtualScreen),
            NativeMethods.GetSystemMetrics(SmCxVirtualScreen),
            NativeMethods.GetSystemMetrics(SmCyVirtualScreen));

    private sealed record VirtualDesktopBounds(int X, int Y, int Width, int Height);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint DwFlags;
        public uint Time;
        public UIntPtr DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort WVk;
        public ushort WScan;
        public uint DwFlags;
        public uint Time;
        public UIntPtr DwExtraInfo;
    }

    private static partial class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);
    }
}

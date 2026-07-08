using System.Runtime.InteropServices;

namespace Beacon.Platform.Windows.Input;

public sealed class WindowsInputApi : IWindowsInputApi
{
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private const uint InputMouse = 0;
    private const uint MouseEventMove = 0x0001;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;
    private const uint MouseEventMiddleDown = 0x0020;
    private const uint MouseEventMiddleUp = 0x0040;
    private const uint MouseEventVirtualDesk = 0x4000;
    private const uint MouseEventAbsolute = 0x8000;

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

                input.Mouse = new MouseInput
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

                input.Mouse = new MouseInput { DwFlags = flags };
                return true;
            default:
                error = $"Unsupported Windows input command '{command.Kind}'.";
                return false;
        }
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
        public MouseInput Mouse;
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

    private static partial class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);
    }
}

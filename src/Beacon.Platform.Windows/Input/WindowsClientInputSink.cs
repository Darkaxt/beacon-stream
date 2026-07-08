using Beacon.Core.Input;
using Beacon.Platform.Windows.Displays;

namespace Beacon.Platform.Windows.Input;

public sealed class WindowsClientInputSink(
    IWindowsDisplayApi displayApi,
    IWindowsInputApi inputApi) : IClientInputSink
{
    public async Task<ClientInputResult> ForwardAsync(ClientInputBatch batch, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        DisplayTopologySnapshot topology = await displayApi.QueryTopologyAsync(cancellationToken);
        DisplayPathSnapshot? display = topology.Paths.FirstOrDefault(path =>
            string.Equals(path.DisplayId, batch.DisplayId, StringComparison.Ordinal));
        if (display is null)
        {
            return ClientInputResult.Fail(
                $"Display '{batch.DisplayId}' is not active; refusing to send input for session '{batch.SessionId}'.");
        }

        if (display.Width <= 0 || display.Height <= 0)
        {
            return ClientInputResult.Fail(
                $"Display '{batch.DisplayId}' has invalid geometry {display.Width}x{display.Height}; refusing to send input.");
        }

        List<WindowsInputCommand> commands = [];
        foreach (ClientInputEvent inputEvent in batch.Events)
        {
            if (!TryAppendCommands(inputEvent, display, commands, out string? error))
            {
                return ClientInputResult.Fail(error);
            }
        }

        WindowsInputResult send = await inputApi.SendAsync(commands, cancellationToken);
        return send.Success
            ? ClientInputResult.Ok(batch.Events.Count)
            : ClientInputResult.Fail(send.Error ?? "Windows input dispatch failed.");
    }

    private static bool TryAppendCommands(
        ClientInputEvent inputEvent,
        DisplayPathSnapshot display,
        List<WindowsInputCommand> commands,
        out string error)
    {
        if (!string.Equals(inputEvent.Type, "pointer", StringComparison.OrdinalIgnoreCase))
        {
            error = $"Unsupported input event type '{inputEvent.Type}'.";
            return false;
        }

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
}

using Beacon.Core.Input;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace Beacon.Platform.Windows.Input;

public sealed record WindowsVirtualControllerResult(
    bool Success,
    string? Error,
    string ResultCode)
{
    public static WindowsVirtualControllerResult Ok() =>
        new(true, null, "controller-forwarded");

    public static WindowsVirtualControllerResult Fail(string error, string resultCode) =>
        new(false, error, resultCode);
}

public interface IWindowsVirtualControllerApi : IAsyncDisposable
{
    int ActiveSessionCount { get; }

    Task PrepareSessionAsync(string sessionId, CancellationToken cancellationToken);

    Task<WindowsVirtualControllerResult> ApplyAsync(
        string sessionId,
        IReadOnlyList<ClientControllerInput> events,
        CancellationToken cancellationToken);

    Task ReleaseSessionAsync(string sessionId, CancellationToken cancellationToken);
}

public sealed class WindowsVirtualControllerApi : IWindowsVirtualControllerApi
{
    private readonly IWindowsXboxControllerFactory factory;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, IWindowsXboxController> targets =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> endedSessions = new(StringComparer.Ordinal);
    private int activeTargetCount;
    private bool disposed;

    public WindowsVirtualControllerApi()
        : this(new VigemXboxControllerFactory())
    {
    }

    internal WindowsVirtualControllerApi(IWindowsXboxControllerFactory factory)
    {
        this.factory = factory;
    }

    public int ActiveSessionCount => Volatile.Read(ref activeTargetCount);

    public async Task PrepareSessionAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            endedSessions.Remove(sessionId);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<WindowsVirtualControllerResult> ApplyAsync(
        string sessionId,
        IReadOnlyList<ClientControllerInput> events,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || events.Count == 0 ||
            events.Any(input => !IsValid(input)))
        {
            return WindowsVirtualControllerResult.Fail(
                "Controller input must use controller index 0 and the stable Beacon control ranges.",
                "controller-input-invalid");
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        IWindowsXboxController? target = null;
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (endedSessions.Contains(sessionId))
            {
                return WindowsVirtualControllerResult.Fail(
                    "Controller input arrived after the Beacon session ended.",
                    "controller-session-ended");
            }
            if (!targets.TryGetValue(sessionId, out target))
            {
                target = factory.Create();
                target.Connect();
                targets.Add(sessionId, target);
                Interlocked.Increment(ref activeTargetCount);
            }

            foreach (ClientControllerInput input in events)
            {
                Apply(target, input);
            }
            target.SubmitReport();
            return WindowsVirtualControllerResult.Ok();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception failure)
        {
            if (target is not null)
            {
                if (targets.Remove(sessionId))
                {
                    Interlocked.Decrement(ref activeTargetCount);
                }
                TryDispose(target);
            }
            return WindowsVirtualControllerResult.Fail(
                $"ViGEm virtual Xbox controller is unavailable: {failure.Message}",
                "vigem-unavailable");
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ReleaseSessionAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            endedSessions.Add(sessionId);
            if (targets.Remove(sessionId, out IWindowsXboxController? target))
            {
                Interlocked.Decrement(ref activeTargetCount);
                TryDispose(target);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed) return;
            disposed = true;
            foreach (IWindowsXboxController target in targets.Values)
            {
                TryDispose(target);
            }
            targets.Clear();
            endedSessions.Clear();
            Interlocked.Exchange(ref activeTargetCount, 0);
            factory.Dispose();
        }
        finally
        {
            gate.Release();
        }
    }

    private static bool IsValid(ClientControllerInput input) =>
        input.ControllerIndex == 0 && (input.ControlId switch
        {
            >= 1 and <= 15 => input.Value is 0 or 1,
            16 or 17 => input.Value is >= byte.MinValue and <= byte.MaxValue,
            >= 18 and <= 21 => input.Value is >= short.MinValue and <= short.MaxValue,
            _ => false,
        });

    private static void Apply(IWindowsXboxController target, ClientControllerInput input)
    {
        switch (input.ControlId)
        {
            case >= 1 and <= 15:
                target.SetButton((WindowsXboxButton)input.ControlId, input.Value == 1);
                break;
            case 16:
                target.SetTrigger(WindowsXboxTrigger.LeftTrigger, (byte)input.Value);
                break;
            case 17:
                target.SetTrigger(WindowsXboxTrigger.RightTrigger, (byte)input.Value);
                break;
            case 18:
                target.SetAxis(WindowsXboxAxis.LeftThumbX, (short)input.Value);
                break;
            case 19:
                target.SetAxis(WindowsXboxAxis.LeftThumbY, (short)input.Value);
                break;
            case 20:
                target.SetAxis(WindowsXboxAxis.RightThumbX, (short)input.Value);
                break;
            case 21:
                target.SetAxis(WindowsXboxAxis.RightThumbY, (short)input.Value);
                break;
        }
    }

    private static void TryDispose(IWindowsXboxController target)
    {
        try
        {
            target.Disconnect();
        }
        catch (Exception)
        {
        }
        try
        {
            target.Dispose();
        }
        catch (Exception)
        {
        }
    }
}

internal enum WindowsXboxButton : uint
{
    Up = 1,
    Down = 2,
    Left = 3,
    Right = 4,
    Start = 5,
    Back = 6,
    LeftThumb = 7,
    RightThumb = 8,
    LeftShoulder = 9,
    RightShoulder = 10,
    Guide = 11,
    A = 12,
    B = 13,
    X = 14,
    Y = 15,
}

internal enum WindowsXboxAxis
{
    LeftThumbX,
    LeftThumbY,
    RightThumbX,
    RightThumbY,
}

internal enum WindowsXboxTrigger
{
    LeftTrigger,
    RightTrigger,
}

internal interface IWindowsXboxControllerFactory : IDisposable
{
    IWindowsXboxController Create();
}

internal interface IWindowsXboxController : IDisposable
{
    void Connect();

    void SetButton(WindowsXboxButton button, bool pressed);

    void SetAxis(WindowsXboxAxis axis, short value);

    void SetTrigger(WindowsXboxTrigger trigger, byte value);

    void SubmitReport();

    void Disconnect();
}

internal sealed class VigemXboxControllerFactory : IWindowsXboxControllerFactory
{
    private ViGEmClient? client;

    public IWindowsXboxController Create()
    {
        client ??= new ViGEmClient();
        return new VigemXboxController(client.CreateXbox360Controller());
    }

    public void Dispose()
    {
        client?.Dispose();
        client = null;
    }
}

internal sealed class VigemXboxController(IXbox360Controller target) : IWindowsXboxController
{
    public void Connect()
    {
        target.AutoSubmitReport = false;
        target.Connect();
    }

    public void SetButton(WindowsXboxButton button, bool pressed) =>
        target.SetButtonState(button switch
        {
            WindowsXboxButton.Up => Xbox360Button.Up,
            WindowsXboxButton.Down => Xbox360Button.Down,
            WindowsXboxButton.Left => Xbox360Button.Left,
            WindowsXboxButton.Right => Xbox360Button.Right,
            WindowsXboxButton.Start => Xbox360Button.Start,
            WindowsXboxButton.Back => Xbox360Button.Back,
            WindowsXboxButton.LeftThumb => Xbox360Button.LeftThumb,
            WindowsXboxButton.RightThumb => Xbox360Button.RightThumb,
            WindowsXboxButton.LeftShoulder => Xbox360Button.LeftShoulder,
            WindowsXboxButton.RightShoulder => Xbox360Button.RightShoulder,
            WindowsXboxButton.Guide => Xbox360Button.Guide,
            WindowsXboxButton.A => Xbox360Button.A,
            WindowsXboxButton.B => Xbox360Button.B,
            WindowsXboxButton.X => Xbox360Button.X,
            WindowsXboxButton.Y => Xbox360Button.Y,
            _ => throw new ArgumentOutOfRangeException(nameof(button)),
        }, pressed);

    public void SetAxis(WindowsXboxAxis axis, short value) =>
        target.SetAxisValue(axis switch
        {
            WindowsXboxAxis.LeftThumbX => Xbox360Axis.LeftThumbX,
            WindowsXboxAxis.LeftThumbY => Xbox360Axis.LeftThumbY,
            WindowsXboxAxis.RightThumbX => Xbox360Axis.RightThumbX,
            WindowsXboxAxis.RightThumbY => Xbox360Axis.RightThumbY,
            _ => throw new ArgumentOutOfRangeException(nameof(axis)),
        }, value);

    public void SetTrigger(WindowsXboxTrigger trigger, byte value) =>
        target.SetSliderValue(trigger switch
        {
            WindowsXboxTrigger.LeftTrigger => Xbox360Slider.LeftTrigger,
            WindowsXboxTrigger.RightTrigger => Xbox360Slider.RightTrigger,
            _ => throw new ArgumentOutOfRangeException(nameof(trigger)),
        }, value);

    public void SubmitReport() => target.SubmitReport();

    public void Disconnect() => target.Disconnect();

    public void Dispose()
    {
    }
}

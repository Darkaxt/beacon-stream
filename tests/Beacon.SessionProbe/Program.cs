using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Beacon.SessionProbe;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        string? evidencePath = Environment.GetEnvironmentVariable("BEACON_SESSION_PROBE_EVIDENCE");
        string runId = Environment.GetEnvironmentVariable("BEACON_SESSION_PROBE_RUN_ID") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(evidencePath) || string.IsNullOrWhiteSpace(runId))
        {
            Environment.ExitCode = 64;
            return;
        }

        ApplicationConfiguration.Initialize();
        using var evidence = new ProbeEvidence(evidencePath, runId);
        using var form = new ProbeForm(evidence);
        Application.Run(form);
    }
}

internal sealed class ProbeForm : Form
{
    private readonly ProbeEvidence evidence;
    private readonly System.Windows.Forms.Timer animation;
    private readonly bool[] controllerAPressed = new bool[4];
    private int frame;
    private bool controllerStateInitialized;
    private bool controllerEvidenceWritten;

    public ProbeForm(ProbeEvidence evidence)
    {
        this.evidence = evidence;
        Text = "Beacon Production Session Probe";
        BackColor = Color.Black;
        ForeColor = Color.White;
        DoubleBuffered = true;
        KeyPreview = true;
        StartPosition = FormStartPosition.WindowsDefaultLocation;
        WindowState = FormWindowState.Maximized;
        animation = new System.Windows.Forms.Timer { Interval = 16 };
        animation.Tick += (_, _) =>
        {
            frame++;
            PollControllers();
            Invalidate();
        };
        Shown += OnShown;
        KeyDown += OnKeyDown;
        FormClosed += OnFormClosed;
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        int width = Math.Max(1, ClientSize.Width);
        int height = Math.Max(1, ClientSize.Height);
        int offset = frame % width;
        eventArgs.Graphics.Clear(Color.FromArgb(12, 18, 26));
        using var cyan = new SolidBrush(Color.FromArgb(31, 194, 214));
        using var yellow = new SolidBrush(Color.FromArgb(245, 196, 55));
        using var red = new SolidBrush(Color.FromArgb(231, 76, 60));
        eventArgs.Graphics.FillRectangle(cyan, offset - width / 3, 0, width / 3, height);
        eventArgs.Graphics.FillRectangle(yellow, offset, 0, width / 3, height);
        eventArgs.Graphics.FillRectangle(red, offset + width / 3, 0, width / 3, height);
        eventArgs.Graphics.FillRectangle(cyan, offset - width - width / 3, 0, width / 3, height);
        eventArgs.Graphics.FillRectangle(yellow, offset - width, 0, width / 3, height);
        eventArgs.Graphics.FillRectangle(red, offset - width + width / 3, 0, width / 3, height);

        string label = $"Beacon production frame {frame}";
        using var font = new Font(FontFamily.GenericSansSerif, 36, FontStyle.Bold, GraphicsUnit.Pixel);
        SizeF textSize = eventArgs.Graphics.MeasureString(label, font);
        eventArgs.Graphics.DrawString(
            label,
            font,
            Brushes.White,
            Math.Max(16, (width - textSize.Width) / 2),
            Math.Max(16, (height - textSize.Height) / 2));
    }

    private void OnShown(object? sender, EventArgs eventArgs)
    {
        Screen screen = Screen.FromControl(this);
        evidence.Write("shown", new
        {
            processId = Environment.ProcessId,
            monitor = screen.DeviceName,
            primary = screen.Primary,
            bounds = new { screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height },
        });
        Activate();
        Focus();
        animation.Start();
    }

    private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.KeyCode != Keys.F12)
        {
            return;
        }

        evidence.Write("input", new { key = "F12", frame });
        eventArgs.Handled = true;
    }

    private void OnFormClosed(object? sender, FormClosedEventArgs eventArgs)
    {
        animation.Stop();
        evidence.Write("closed", new { reason = eventArgs.CloseReason.ToString(), frame });
    }

    private void PollControllers()
    {
        for (uint userIndex = 0; userIndex < controllerAPressed.Length; userIndex++)
        {
            bool pressed = XInputGetState(userIndex, out XInputState state) == 0
                && (state.Gamepad.Buttons & XInputGamepadA) != 0;
            if (controllerStateInitialized && pressed && !controllerAPressed[userIndex]
                && !controllerEvidenceWritten)
            {
                controllerEvidenceWritten = true;
                evidence.Write("controller", new
                {
                    userIndex,
                    button = "A",
                    pressed = true,
                    frame,
                });
            }
            controllerAPressed[userIndex] = pressed;
        }
        controllerStateInitialized = true;
    }

    private const ushort XInputGamepadA = 0x1000;

    [DllImport("xinput1_4.dll")]
    private static extern uint XInputGetState(uint userIndex, out XInputState state);

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short LeftThumbX;
        public short LeftThumbY;
        public short RightThumbX;
        public short RightThumbY;
    }
}

internal sealed class ProbeEvidence(string path, string runId) : IDisposable
{
    private readonly Lock gate = new();
    private readonly string evidencePath = Path.GetFullPath(path);

    public void Write(string eventName, object details)
    {
        var record = new
        {
            schemaVersion = 1,
            runId,
            eventName,
            observedAt = DateTimeOffset.UtcNow,
            details,
        };
        string line = JsonSerializer.Serialize(record) + Environment.NewLine;
        lock (gate)
        {
            string? directory = Path.GetDirectoryName(evidencePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.AppendAllText(evidencePath, line);
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}

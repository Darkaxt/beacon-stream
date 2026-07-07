using System.Runtime.InteropServices;

namespace Beacon.Platform.Windows.Displays;

public sealed class WindowsDisplayApi : IWindowsDisplayApi
{
    private const uint DisplayDeviceActive = 0x00000001;
    private const uint DisplayDevicePrimaryDevice = 0x00000004;
    private const int EnumCurrentSettings = -1;

    public DisplayDriverStatus GetDriverStatus() =>
        new(
            Ready: false,
            Diagnostic: "SudoVDA driver create/remove operations are not wired in Beacon yet.");

    public Task<DisplayApiResult> CreateVirtualDisplayAsync(
        string displayId,
        int width,
        int height,
        int refreshHz,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(DisplayApiResult.Fail(
            $"SudoVDA create is not wired yet for {displayId} {width}x{height}@{refreshHz}."));
    }

    public Task<DisplayTopologySnapshot> QueryTopologyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(QueryActiveTopology());
    }

    public Task<DisplayApiResult> SetVirtualPrimaryAsync(string displayId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(DisplayApiResult.Fail(
            $"Virtual-primary topology apply is not wired yet for {displayId}."));
    }

    public Task<DisplayApiResult> RestorePhysicalPrimaryAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(DisplayApiResult.Fail(
            "Physical-primary topology restore is not wired yet."));
    }

    public Task<DisplayApiResult> RemoveVirtualDisplayAsync(string displayId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(DisplayApiResult.Fail(
            $"SudoVDA remove is not wired yet for {displayId}."));
    }

    public Task<DisplayHdrCapability> QueryHdrCapabilityAsync(string displayId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new DisplayHdrCapability(
            Supported: false,
            Enabled: false,
            Reason: $"HDR capability query is not wired yet for {displayId}."));
    }

    public static DisplayPathKind ClassifyDisplayKind(string deviceString, string deviceId)
    {
        string evidence = $"{deviceString} {deviceId}";
        return ContainsOrdinalIgnoreCase(evidence, "sudovda") ||
            ContainsOrdinalIgnoreCase(evidence, "sudomaker") ||
            ContainsOrdinalIgnoreCase(evidence, "virtual display") ||
            ContainsOrdinalIgnoreCase(evidence, "indirect display")
                ? DisplayPathKind.Virtual
                : DisplayPathKind.Physical;
    }

    private static DisplayTopologySnapshot QueryActiveTopology()
    {
        var paths = new List<DisplayPathSnapshot>();

        for (uint index = 0; ; index++)
        {
            DisplayDevice device = DisplayDevice.Create();
            if (!NativeMethods.EnumDisplayDevices(null, index, ref device, 0))
            {
                break;
            }

            if ((device.StateFlags & DisplayDeviceActive) == 0)
            {
                continue;
            }

            DevMode mode = DevMode.Create();
            if (!NativeMethods.EnumDisplaySettings(device.DeviceName, EnumCurrentSettings, ref mode))
            {
                continue;
            }

            paths.Add(new DisplayPathSnapshot(
                device.DeviceName,
                ClassifyDisplayKind(device.DeviceString, device.DeviceId),
                checked((int)mode.PelsWidth),
                checked((int)mode.PelsHeight),
                checked((int)mode.DisplayFrequency),
                IsPrimary: (device.StateFlags & DisplayDevicePrimaryDevice) != 0,
                X: mode.Position.X,
                Y: mode.Position.Y));
        }

        return DisplayTopologySnapshot.FromPaths(paths);
    }

    private static bool ContainsOrdinalIgnoreCase(string value, string fragment) =>
        value.Contains(fragment, StringComparison.OrdinalIgnoreCase);

    private static class NativeMethods
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumDisplayDevices(
            string? lpDevice,
            uint iDevNum,
            ref DisplayDevice lpDisplayDevice,
            uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumDisplaySettings(
            string lpszDeviceName,
            int iModeNum,
            ref DevMode lpDevMode);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Cb;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public uint StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;

        public static DisplayDevice Create() =>
            new()
            {
                Cb = Marshal.SizeOf<DisplayDevice>(),
                DeviceName = string.Empty,
                DeviceString = string.Empty,
                DeviceId = string.Empty,
                DeviceKey = string.Empty
            };
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        public ushort SpecVersion;
        public ushort DriverVersion;
        public ushort Size;
        public ushort DriverExtra;
        public uint Fields;
        public PointL Position;
        public uint DisplayOrientation;
        public uint DisplayFixedOutput;
        public short Color;
        public short Duplex;
        public short YResolution;
        public short TTOption;
        public short Collate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FormName;

        public ushort LogPixels;
        public uint BitsPerPel;
        public uint PelsWidth;
        public uint PelsHeight;
        public uint DisplayFlags;
        public uint DisplayFrequency;

        public static DevMode Create() =>
            new()
            {
                DeviceName = string.Empty,
                Size = (ushort)Marshal.SizeOf<DevMode>(),
                FormName = string.Empty
            };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointL
    {
        public int X;

        public int Y;
    }
}

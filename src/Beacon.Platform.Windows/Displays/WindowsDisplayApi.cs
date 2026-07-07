using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Beacon.Platform.Windows.Displays;

public sealed class WindowsDisplayApi : IWindowsDisplayApi
{
    private const uint DisplayDeviceActive = 0x00000001;
    private const uint DisplayDevicePrimaryDevice = 0x00000004;
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;
    private const uint FileDeviceUnknown = 0x00000022;
    private const uint FileAnyAccess = 0;
    private const uint MethodBuffered = 0;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint IoctlGetProtocolVersion = 0x8FF;
    private const byte ExpectedProtocolMajor = 0;
    private const byte ExpectedProtocolMinor = 2;
    private const int EnumCurrentSettings = -1;
    private static readonly Guid SudoVdaInterfaceGuid = new("e5bcc234-1e0c-418a-a0d4-ef8b7501414d");

    public DisplayDriverStatus GetDriverStatus()
    {
        using SafeFileHandle? handle = OpenSudoVdaDevice(out string openDiagnostic);
        if (handle is null)
        {
            return new DisplayDriverStatus(Ready: false, Diagnostic: openDiagnostic);
        }

        if (!TryGetProtocolVersion(handle, out SudoVdaProtocolVersion version, out string versionDiagnostic))
        {
            return new DisplayDriverStatus(Ready: false, Diagnostic: versionDiagnostic);
        }

        bool compatible = version.Major == ExpectedProtocolMajor && version.Minor >= ExpectedProtocolMinor;
        return compatible
            ? new DisplayDriverStatus(Ready: true, Diagnostic: $"SudoVDA driver is ready. Protocol {version.Major}.{version.Minor}.{version.Incremental}.")
            : new DisplayDriverStatus(Ready: false, Diagnostic: $"SudoVDA protocol {version.Major}.{version.Minor}.{version.Incremental} is incompatible with Beacon protocol {ExpectedProtocolMajor}.{ExpectedProtocolMinor}.");
    }

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

    public static uint BuildSudoVdaControlCode(uint function) =>
        (FileDeviceUnknown << 16) | (FileAnyAccess << 14) | (function << 2) | MethodBuffered;

    public static Guid CreateDeterministicDisplayGuid(string displayId)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"BeaconStream:SudoVDA:{displayId}"));
        var guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, guidBytes.Length);
        guidBytes[7] = (byte)((guidBytes[7] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
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

    private static SafeFileHandle? OpenSudoVdaDevice(out string diagnostic)
    {
        Guid interfaceGuid = SudoVdaInterfaceGuid;
        IntPtr deviceInfoSet = NativeMethods.SetupDiGetClassDevs(
            ref interfaceGuid,
            IntPtr.Zero,
            IntPtr.Zero,
            DigcfPresent | DigcfDeviceInterface);

        if (deviceInfoSet == new IntPtr(-1))
        {
            diagnostic = $"Unable to query SudoVDA device interfaces. Win32={Marshal.GetLastWin32Error()}.";
            return null;
        }

        try
        {
            for (uint index = 0; ; index++)
            {
                SpDeviceInterfaceData interfaceData = SpDeviceInterfaceData.Create();
                if (!NativeMethods.SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref interfaceGuid, index, ref interfaceData))
                {
                    diagnostic = $"Unable to open SudoVDA device. Make sure the SudoVDA driver is installed and enabled. Win32={Marshal.GetLastWin32Error()}.";
                    return null;
                }

                NativeMethods.SetupDiGetDeviceInterfaceDetail(
                    deviceInfoSet,
                    ref interfaceData,
                    IntPtr.Zero,
                    0,
                    out uint requiredSize,
                    IntPtr.Zero);

                IntPtr detailData = Marshal.AllocHGlobal(checked((int)requiredSize));
                try
                {
                    Marshal.WriteInt32(detailData, IntPtr.Size == 8 ? 8 : 6);
                    if (!NativeMethods.SetupDiGetDeviceInterfaceDetail(
                        deviceInfoSet,
                        ref interfaceData,
                        detailData,
                        requiredSize,
                        out _,
                        IntPtr.Zero))
                    {
                        continue;
                    }

                    string? devicePath = Marshal.PtrToStringUni(IntPtr.Add(detailData, 4));
                    if (string.IsNullOrWhiteSpace(devicePath))
                    {
                        continue;
                    }

                    SafeFileHandle handle = NativeMethods.CreateFile(
                        devicePath,
                        GenericRead | GenericWrite,
                        FileShareRead | FileShareWrite,
                        IntPtr.Zero,
                        OpenExisting,
                        FileAttributeNormal,
                        IntPtr.Zero);

                    if (!handle.IsInvalid)
                    {
                        diagnostic = "SudoVDA driver handle opened.";
                        return handle;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detailData);
                }
            }
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    private static bool TryGetProtocolVersion(
        SafeFileHandle handle,
        out SudoVdaProtocolVersion version,
        out string diagnostic)
    {
        bool success = NativeMethods.DeviceIoControl(
            handle,
            BuildSudoVdaControlCode(IoctlGetProtocolVersion),
            IntPtr.Zero,
            0,
            out SudoVdaProtocolVersionOut output,
            Marshal.SizeOf<SudoVdaProtocolVersionOut>(),
            out _,
            IntPtr.Zero);

        version = output.Version;
        diagnostic = success
            ? "SudoVDA protocol version read."
            : $"Unable to read SudoVDA protocol version. Win32={Marshal.GetLastWin32Error()}.";
        return success;
    }

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

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevs(
            ref Guid classGuid,
            IntPtr enumerator,
            IntPtr hwndParent,
            uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiEnumDeviceInterfaces(
            IntPtr deviceInfoSet,
            IntPtr deviceInfoData,
            ref Guid interfaceClassGuid,
            uint memberIndex,
            ref SpDeviceInterfaceData deviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr deviceInfoSet,
            ref SpDeviceInterfaceData deviceInterfaceData,
            IntPtr deviceInterfaceDetailData,
            uint deviceInterfaceDetailDataSize,
            out uint requiredSize,
            IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeviceIoControl(
            SafeFileHandle device,
            uint ioControlCode,
            IntPtr inBuffer,
            int inBufferSize,
            out SudoVdaProtocolVersionOut outBuffer,
            int outBufferSize,
            out uint bytesReturned,
            IntPtr overlapped);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public int CbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public UIntPtr Reserved;

        public static SpDeviceInterfaceData Create() =>
            new()
            {
                CbSize = Marshal.SizeOf<SpDeviceInterfaceData>()
            };
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
    private struct SudoVdaProtocolVersion
    {
        public byte Major;
        public byte Minor;
        public byte Incremental;

        [MarshalAs(UnmanagedType.I1)]
        public bool TestBuild;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SudoVdaProtocolVersionOut
    {
        public SudoVdaProtocolVersion Version;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointL
    {
        public int X;

        public int Y;
    }
}

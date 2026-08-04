using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Beacon.HostAgent.DriverUpdates;

internal sealed record WindowsDisplayDeviceProperties(
    string DeviceInstanceId,
    string HardwareId,
    string PublishedInf,
    string DriverVersion,
    string Provider,
    uint ProblemCode);

internal interface IWindowsDisplayDevicePropertySource
{
    IReadOnlyList<WindowsDisplayDeviceProperties> EnumeratePresentDisplayDevices();
}

internal sealed class WindowsSudoVdaDeviceInventory : ISudoVdaDeviceInventory
{
    private const string ExpectedHardwareId = @"root\sudomaker\sudovda";
    private const string ExpectedProvider = "SudoMaker";
    private readonly IWindowsDisplayDevicePropertySource source;
    private readonly string activeBinaryPath;

    public WindowsSudoVdaDeviceInventory()
        : this(
            new SetupApiDisplayDevicePropertySource(),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32",
                "drivers",
                "UMDF",
                "SudoVDA.dll"))
    {
    }

    internal WindowsSudoVdaDeviceInventory(
        IWindowsDisplayDevicePropertySource source,
        string activeBinaryPath)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        this.activeBinaryPath = string.IsNullOrWhiteSpace(activeBinaryPath)
            ? throw new ArgumentException("Active SudoVDA binary path is required.", nameof(activeBinaryPath))
            : Path.GetFullPath(activeBinaryPath);
    }

    public SudoVdaInstalledDevice QueryActiveDevice()
    {
        WindowsDisplayDeviceProperties[] matches = source.EnumeratePresentDisplayDevices()
            .Where(device =>
                string.Equals(device.HardwareId, ExpectedHardwareId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(device.Provider, ExpectedProvider, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidDataException(
                matches.Length == 0
                    ? "No present SudoVDA display device was found."
                    : "Multiple present SudoVDA display devices were found.");
        }

        WindowsDisplayDeviceProperties match = matches[0];
        return new SudoVdaInstalledDevice(
            match.DeviceInstanceId,
            match.HardwareId,
            match.PublishedInf,
            match.DriverVersion,
            match.Provider,
            activeBinaryPath,
            DeviceHealthy: match.ProblemCode == 0);
    }
}

internal sealed class SetupApiDisplayDevicePropertySource : IWindowsDisplayDevicePropertySource
{
    private const uint DigcfPresent = 0x00000002;
    private const int ErrorNoMoreItems = 259;
    private const int ErrorInsufficientBuffer = 122;
    private static readonly Guid DisplayClass = new("4D36E968-E325-11CE-BFC1-08002BE10318");
    private static readonly DevPropKey DriverVersion = DriverProperty(3);
    private static readonly DevPropKey DriverInfPath = DriverProperty(5);
    private static readonly DevPropKey MatchingDeviceId = DriverProperty(8);
    private static readonly DevPropKey DriverProvider = DriverProperty(9);
    private static readonly DevPropKey ProblemCode = new(
        new Guid("4340a6c5-93fa-4706-972c-7b648008a5a7"),
        3);

    public IReadOnlyList<WindowsDisplayDeviceProperties> EnumeratePresentDisplayDevices()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("SetupAPI display inventory requires Windows.");
        }

        Guid displayClass = DisplayClass;
        using SafeDeviceInfoSetHandle devices = SetupDiGetClassDevsW(
            ref displayClass,
            enumerator: null,
            parent: nint.Zero,
            DigcfPresent);
        if (devices.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to enumerate display devices.");
        }

        var results = new List<WindowsDisplayDeviceProperties>();
        for (uint index = 0; ; index++)
        {
            var device = new SpDevInfoData { Size = (uint)Marshal.SizeOf<SpDevInfoData>() };
            if (!SetupDiEnumDeviceInfo(devices, index, ref device))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == ErrorNoMoreItems)
                {
                    break;
                }
                throw new Win32Exception(error, "Unable to enumerate a display device.");
            }

            results.Add(new WindowsDisplayDeviceProperties(
                ReadInstanceId(devices, ref device),
                ReadStringProperty(devices, ref device, MatchingDeviceId),
                ReadStringProperty(devices, ref device, DriverInfPath),
                ReadStringProperty(devices, ref device, DriverVersion),
                ReadStringProperty(devices, ref device, DriverProvider),
                ReadUInt32Property(devices, ref device, ProblemCode)));
        }
        return results;
    }

    private static string ReadInstanceId(
        SafeDeviceInfoSetHandle devices,
        ref SpDevInfoData device)
    {
        _ = SetupDiGetDeviceInstanceIdW(devices, ref device, null, 0, out uint required);
        int error = Marshal.GetLastWin32Error();
        if (required == 0 || error != ErrorInsufficientBuffer)
        {
            throw new Win32Exception(error, "Unable to size a display device instance id.");
        }
        var value = new StringBuilder(checked((int)required));
        if (!SetupDiGetDeviceInstanceIdW(devices, ref device, value, required, out _))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Unable to read a display device instance id.");
        }
        return value.ToString();
    }

    private static string ReadStringProperty(
        SafeDeviceInfoSetHandle devices,
        ref SpDevInfoData device,
        DevPropKey key)
    {
        byte[] value = ReadProperty(devices, ref device, key, out uint propertyType);
        const uint DevPropTypeString = 0x00000012;
        if (propertyType != DevPropTypeString)
        {
            throw new InvalidDataException("A SudoVDA device property has an unexpected type.");
        }
        return Encoding.Unicode.GetString(value).TrimEnd('\0');
    }

    private static uint ReadUInt32Property(
        SafeDeviceInfoSetHandle devices,
        ref SpDevInfoData device,
        DevPropKey key)
    {
        byte[] value = ReadProperty(devices, ref device, key, out uint propertyType);
        const uint DevPropTypeUInt32 = 0x00000007;
        if (propertyType != DevPropTypeUInt32 || value.Length < sizeof(uint))
        {
            throw new InvalidDataException("A SudoVDA device property has an unexpected type.");
        }
        return BitConverter.ToUInt32(value);
    }

    private static byte[] ReadProperty(
        SafeDeviceInfoSetHandle devices,
        ref SpDevInfoData device,
        DevPropKey key,
        out uint propertyType)
    {
        _ = SetupDiGetDevicePropertyW(
            devices,
            ref device,
            ref key,
            out propertyType,
            null,
            0,
            out uint required,
            0);
        int error = Marshal.GetLastWin32Error();
        if (required == 0 || error != ErrorInsufficientBuffer)
        {
            throw new Win32Exception(error, "Unable to size a display device property.");
        }
        byte[] value = new byte[required];
        if (!SetupDiGetDevicePropertyW(
            devices,
            ref device,
            ref key,
            out propertyType,
            value,
            checked((uint)value.Length),
            out _,
            0))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Unable to read a display device property.");
        }
        return value;
    }

    private static DevPropKey DriverProperty(uint propertyId) => new(
        new Guid("a8b865dd-2e3d-4094-ad97-e593a70c75d6"),
        propertyId);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeDeviceInfoSetHandle SetupDiGetClassDevsW(
        ref Guid classGuid,
        string? enumerator,
        nint parent,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInfo(
        SafeDeviceInfoSetHandle deviceInfoSet,
        uint memberIndex,
        ref SpDevInfoData deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInstanceIdW(
        SafeDeviceInfoSetHandle deviceInfoSet,
        ref SpDevInfoData deviceInfoData,
        StringBuilder? deviceInstanceId,
        uint deviceInstanceIdSize,
        out uint requiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDevicePropertyW(
        SafeDeviceInfoSetHandle deviceInfoSet,
        ref SpDevInfoData deviceInfoData,
        ref DevPropKey propertyKey,
        out uint propertyType,
        byte[]? propertyBuffer,
        uint propertyBufferSize,
        out uint requiredSize,
        uint flags);

    [DllImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(nint deviceInfoSet);

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevInfoData
    {
        public uint Size;
        public Guid ClassGuid;
        public uint DeviceInstance;
        public nint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DevPropKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId = formatId;
        public uint PropertyId = propertyId;
    }

    private sealed class SafeDeviceInfoSetHandle()
        : SafeHandleZeroOrMinusOneIsInvalid(ownsHandle: true)
    {
        protected override bool ReleaseHandle() => SetupDiDestroyDeviceInfoList(handle);
    }
}

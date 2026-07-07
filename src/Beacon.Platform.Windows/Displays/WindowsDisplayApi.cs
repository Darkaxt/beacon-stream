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
    private const uint ErrorSuccess = 0;
    private const uint ErrorGenFailure = 31;
    private const uint QdcAllPaths = 0x00000001;
    private const uint QdcOnlyActivePaths = 0x00000002;
    private const uint QdcVirtualModeAware = 0x00000010;
    private const uint SdcUseSuppliedDisplayConfig = 0x00000020;
    private const uint SdcApply = 0x00000080;
    private const uint SdcSaveToDatabase = 0x00000200;
    private const uint SdcAllowChanges = 0x00000400;
    private const uint SdcTopologySupplied = 0x00000010;
    private const uint SdcAllowPathOrderChanges = 0x00002000;
    private const uint SdcVirtualModeAware = 0x00008000;
    private const uint DisplayConfigDeviceInfoGetSourceName = 1;
    private const uint DisplayConfigPathActive = 0x00000001;
    private const uint DisplayConfigPathModeIdxInvalid = 0xFFFFFFFF;
    private const uint DisplayConfigPathSourceModeIdxInvalid = 0xFFFF;
    private const uint DisplayConfigModeInfoTypeSource = 1;
    private const uint DisplayConfigPixelFormat32Bpp = 4;
    private const uint IoctlAddVirtualDisplay = 0x800;
    private const uint IoctlRemoveVirtualDisplay = 0x801;
    private const uint IoctlGetProtocolVersion = 0x8FF;
    private const byte ExpectedProtocolMajor = 0;
    private const byte ExpectedProtocolMinor = 2;
    private const int EnumCurrentSettings = -1;
    private static readonly Guid SudoVdaInterfaceGuid = new("e5bcc234-1e0c-418a-a0d4-ef8b7501414d");
    private readonly Lock displayMapLock = new();
    private readonly Dictionary<string, string> displayNameByDisplayId = new(StringComparer.Ordinal);

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
        IReadOnlyList<string> beforeDisplayNames = EnumerateDisplayNames(activeOnly: false);

        using SafeFileHandle? handle = OpenSudoVdaDevice(out string diagnostic);
        if (handle is null)
        {
            return Task.FromResult(DisplayApiResult.Fail(diagnostic));
        }

        Guid monitorGuid = CreateDeterministicDisplayGuid(displayId);
        var parameters = new VirtualDisplayAddParams
        {
            Width = checked((uint)width),
            Height = checked((uint)height),
            RefreshRate = checked((uint)refreshHz),
            MonitorGuid = monitorGuid,
            DeviceName = "BeaconStream",
            SerialNumber = "beaconstream"
        };

        bool success = NativeMethods.DeviceIoControl(
            handle,
            BuildSudoVdaControlCode(IoctlAddVirtualDisplay),
            ref parameters,
            Marshal.SizeOf<VirtualDisplayAddParams>(),
            out VirtualDisplayAddOut addOutput,
            Marshal.SizeOf<VirtualDisplayAddOut>(),
            out _,
            IntPtr.Zero);

        if (success)
        {
            IReadOnlyList<DisplayPathSnapshot> afterDisplayPaths = EnumerateDisplayNamesWithState(activeOnly: false);
            IReadOnlyList<string> afterDisplayNames = afterDisplayPaths.Select(path => path.DisplayId).ToArray();
            string? displayName = TryGetDisplayNameForTarget(addOutput, out string? targetDisplayName)
                ? targetDisplayName
                : SelectAddedDisplayName(beforeDisplayNames, afterDisplayNames) ??
                SelectSingleVirtualDisplayName(afterDisplayPaths);

            if (displayName is null)
            {
                RemoveVirtualDisplay(handle, monitorGuid);
                return Task.FromResult(DisplayApiResult.Fail(
                    $"SudoVDA create succeeded for {displayId}, but Windows did not expose a mappable virtual display name. Before=[{string.Join(", ", beforeDisplayNames)}] After=[{string.Join(", ", afterDisplayNames)}]."));
            }

            RememberDisplayName(displayId, displayName);
            DisplayApiResult activationResult = ActivateDisplayMode(addOutput, displayName, width, height, refreshHz);
            if (!activationResult.Success)
            {
                RemoveVirtualDisplay(handle, monitorGuid);
                ForgetDisplayName(displayId);
                return Task.FromResult(activationResult);
            }
        }

        return Task.FromResult(success
            ? DisplayApiResult.Ok()
            : DisplayApiResult.Fail($"SudoVDA create failed for {displayId}. Win32={Marshal.GetLastWin32Error()}."));
    }

    public Task<DisplayTopologySnapshot> QueryTopologyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(QueryActiveTopology());
    }

    public Task<DisplayApiResult> SetVirtualPrimaryAsync(string displayId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(TrySetPrimaryDisplay(displayId, out string diagnostic)
            ? DisplayApiResult.Ok()
            : DisplayApiResult.Fail(diagnostic));
    }

    public Task<DisplayApiResult> RestorePhysicalPrimaryAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DisplayPathSnapshot? physicalDisplay = EnumerateActiveDisplayPaths(displayIdByDisplayName: null)
            .FirstOrDefault(path => path.Kind == DisplayPathKind.Physical);

        if (physicalDisplay is null)
        {
            return Task.FromResult(DisplayApiResult.Fail("No active physical display was found for restore."));
        }

        return Task.FromResult(TrySetPrimaryDisplay(physicalDisplay.DisplayId, out string diagnostic)
            ? DisplayApiResult.Ok()
            : DisplayApiResult.Fail(diagnostic));
    }

    public Task<DisplayApiResult> RemoveVirtualDisplayAsync(string displayId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SafeFileHandle? handle = OpenSudoVdaDevice(out string diagnostic);
        if (handle is null)
        {
            return Task.FromResult(DisplayApiResult.Fail(diagnostic));
        }

        bool success = RemoveVirtualDisplay(handle, CreateDeterministicDisplayGuid(displayId));

        if (success)
        {
            ForgetDisplayName(displayId);
        }

        return Task.FromResult(success
            ? DisplayApiResult.Ok()
            : DisplayApiResult.Fail($"SudoVDA remove failed for {displayId}. Win32={Marshal.GetLastWin32Error()}."));
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

    public static string? SelectAddedDisplayName(IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        HashSet<string> beforeSet = new(before, StringComparer.OrdinalIgnoreCase);
        string[] added = after
            .Where(name => !beforeSet.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return added.Length == 1 ? added[0] : null;
    }

    public static string? SelectSingleVirtualDisplayName(IReadOnlyList<DisplayPathSnapshot> paths)
    {
        string[] virtualDisplayNames = paths
            .Where(path => path.Kind == DisplayPathKind.Virtual)
            .Select(path => path.DisplayId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return virtualDisplayNames.Length == 1 ? virtualDisplayNames[0] : null;
    }

    private DisplayTopologySnapshot QueryActiveTopology()
    {
        Dictionary<string, string> displayIdByDisplayName = CreateDisplayIdByDisplayNameSnapshot();
        return DisplayTopologySnapshot.FromPaths(EnumerateActiveDisplayPaths(displayIdByDisplayName));
    }

    private static IReadOnlyList<string> EnumerateDisplayNames(bool activeOnly) =>
        EnumerateDisplayNamesWithState(activeOnly)
            .Select(path => path.DisplayId)
            .ToArray();

    private static IReadOnlyList<DisplayPathSnapshot> EnumerateActiveDisplayPaths(
        IReadOnlyDictionary<string, string>? displayIdByDisplayName)
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

            string displayId = displayIdByDisplayName is not null &&
                displayIdByDisplayName.TryGetValue(device.DeviceName, out string? mappedDisplayId)
                    ? mappedDisplayId
                    : device.DeviceName;

            paths.Add(new DisplayPathSnapshot(
                displayId,
                ClassifyDisplayKind(device.DeviceString, device.DeviceId),
                checked((int)mode.PelsWidth),
                checked((int)mode.PelsHeight),
                checked((int)mode.DisplayFrequency),
                IsPrimary: (device.StateFlags & DisplayDevicePrimaryDevice) != 0,
                X: mode.Position.X,
                Y: mode.Position.Y));
        }

        return paths;
    }

    private static IReadOnlyList<DisplayPathSnapshot> EnumerateDisplayNamesWithState(bool activeOnly)
    {
        var paths = new List<DisplayPathSnapshot>();

        for (uint index = 0; ; index++)
        {
            DisplayDevice device = DisplayDevice.Create();
            if (!NativeMethods.EnumDisplayDevices(null, index, ref device, 0))
            {
                break;
            }

            bool active = (device.StateFlags & DisplayDeviceActive) != 0;
            if (activeOnly && !active)
            {
                continue;
            }

            paths.Add(new DisplayPathSnapshot(
                device.DeviceName,
                ClassifyDisplayKind(device.DeviceString, device.DeviceId),
                Width: 0,
                Height: 0,
                RefreshHz: 0,
                IsPrimary: (device.StateFlags & DisplayDevicePrimaryDevice) != 0,
                X: 0,
                Y: 0));
        }

        return paths;
    }

    private static DisplayApiResult ActivateDisplayMode(
        VirtualDisplayAddOut addOutput,
        string displayName,
        int width,
        int height,
        int refreshHz)
    {
        DisplayApiResult activeResult = EnsureDisplayConfigTargetActive(addOutput, displayName);
        if (!activeResult.Success)
        {
            return activeResult;
        }

        return ApplyDisplayConfigMode(displayName, width, height, refreshHz);
    }

    private static DisplayApiResult EnsureDisplayConfigTargetActive(
        VirtualDisplayAddOut addOutput,
        string displayName)
    {
        if (!TryQueryDisplayConfig(
            QdcOnlyActivePaths | QdcVirtualModeAware,
            out DisplayConfigPathInfo[] activePaths,
            out _,
            out string activeDiagnostic))
        {
            return DisplayApiResult.Fail(activeDiagnostic);
        }

        if (activePaths.Any(path => IsTargetPath(path, addOutput)))
        {
            return DisplayApiResult.Ok();
        }

        if (!TryQueryDisplayConfig(
            QdcAllPaths | QdcVirtualModeAware,
            out DisplayConfigPathInfo[] allPaths,
            out _,
            out string allDiagnostic))
        {
            return DisplayApiResult.Fail(allDiagnostic);
        }

        DisplayConfigPathInfo targetPath = allPaths.FirstOrDefault(path => IsTargetPath(path, addOutput));
        if (!IsTargetPath(targetPath, addOutput))
        {
            return DisplayApiResult.Fail(
                $"SudoVDA target for {displayName} was not present in DisplayConfig. Adapter={FormatLuid(addOutput.AdapterLuid)} Target={addOutput.TargetId}.");
        }

        var requestedPaths = new List<DisplayConfigPathInfo>();
        uint groupId = 0;
        foreach (DisplayConfigPathInfo activePath in activePaths.Where(path => !IsTargetPath(path, addOutput)))
        {
            DisplayConfigPathInfo selectedPath = allPaths.FirstOrDefault(path => IsSameDisplayPath(path, activePath));
            requestedPaths.Add(PrepareTopologyPath(
                IsSameDisplayPath(selectedPath, activePath) ? selectedPath : activePath,
                groupId++));
        }

        requestedPaths.Add(PrepareTopologyPath(targetPath, groupId));

        uint status = NativeMethods.SetDisplayConfigWithoutModes(
            checked((uint)requestedPaths.Count),
            requestedPaths.ToArray(),
            0,
            IntPtr.Zero,
            SdcApply | SdcTopologySupplied | SdcAllowPathOrderChanges | SdcVirtualModeAware);

        if (status == ErrorGenFailure)
        {
            status = NativeMethods.SetDisplayConfigWithoutModes(
                checked((uint)requestedPaths.Count),
                requestedPaths.ToArray(),
                0,
                IntPtr.Zero,
                SdcApply | SdcUseSuppliedDisplayConfig | SdcAllowChanges | SdcVirtualModeAware | SdcSaveToDatabase);
        }

        return status == ErrorSuccess
            ? DisplayApiResult.Ok()
            : DisplayApiResult.Fail(
                $"Unable to activate DisplayConfig target for {displayName}. Adapter={FormatLuid(addOutput.AdapterLuid)} Target={addOutput.TargetId} Result={status}.");
    }

    private static DisplayApiResult ApplyDisplayConfigMode(string displayName, int width, int height, int refreshHz)
    {
        if (!TryQueryDisplayConfig(
            QdcOnlyActivePaths | QdcVirtualModeAware,
            out DisplayConfigPathInfo[] paths,
            out DisplayConfigModeInfo[] modes,
            out string diagnostic))
        {
            return DisplayApiResult.Fail(diagnostic);
        }

        for (int pathIndex = 0; pathIndex < paths.Length; pathIndex++)
        {
            DisplayConfigPathInfo path = paths[pathIndex];
            var sourceName = DisplayConfigSourceDeviceName.Create(path.SourceInfo.AdapterId, path.SourceInfo.Id);
            uint nameStatus = NativeMethods.DisplayConfigGetDeviceInfo(ref sourceName);
            if (nameStatus != ErrorSuccess ||
                !string.Equals(sourceName.ViewGdiDeviceName, displayName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            uint sourceModeIndex = SourceModeInfoIndex(path);
            if (sourceModeIndex == DisplayConfigPathSourceModeIdxInvalid ||
                sourceModeIndex >= modes.Length ||
                modes[sourceModeIndex].InfoType != DisplayConfigModeInfoTypeSource)
            {
                return DisplayApiResult.Fail($"Active DisplayConfig path for {displayName} did not expose a source mode.");
            }

            DisplayConfigSourceMode sourceMode = modes[sourceModeIndex].SourceMode;
            sourceMode.Width = checked((uint)width);
            sourceMode.Height = checked((uint)height);
            if (sourceMode.PixelFormat == 0)
            {
                sourceMode.PixelFormat = DisplayConfigPixelFormat32Bpp;
            }
            modes[sourceModeIndex].SourceMode = sourceMode;

            path.TargetInfo.RefreshRate = new DisplayConfigRational
            {
                Numerator = checked((uint)refreshHz),
                Denominator = 1
            };
            path.TargetInfo.ModeInfoIdx = DisplayConfigPathModeIdxInvalid;
            paths[pathIndex] = path;

            uint status = NativeMethods.SetDisplayConfig(
                checked((uint)paths.Length),
                paths,
                checked((uint)modes.Length),
                modes,
                SuppliedDisplayConfigApplyFlags());

            return status == ErrorSuccess
                ? DisplayApiResult.Ok()
                : DisplayApiResult.Fail($"Unable to apply DisplayConfig mode for {displayName} at {width}x{height}@{refreshHz}. Result={status}.");
        }

        return DisplayApiResult.Fail($"DisplayConfig did not expose active source {displayName} after SudoVDA activation.");
    }

    private static bool TryGetDisplayNameForTarget(VirtualDisplayAddOut addOutput, out string? displayName)
    {
        displayName = null;
        uint pathCount = 0;
        uint modeCount = 0;
        uint flags = QdcAllPaths | QdcVirtualModeAware;

        uint sizeStatus = NativeMethods.GetDisplayConfigBufferSizes(flags, ref pathCount, ref modeCount);
        if (sizeStatus != ErrorSuccess || pathCount == 0)
        {
            return false;
        }

        var paths = new DisplayConfigPathInfo[pathCount];
        var modes = new DisplayConfigModeInfo[modeCount];
        uint queryStatus = NativeMethods.QueryDisplayConfig(
            flags,
            ref pathCount,
            paths,
            ref modeCount,
            modes,
            IntPtr.Zero);
        if (queryStatus != ErrorSuccess)
        {
            return false;
        }

        for (int index = 0; index < pathCount; index++)
        {
            DisplayConfigPathInfo path = paths[index];
            if (!path.TargetInfo.AdapterId.Equals(addOutput.AdapterLuid) || path.TargetInfo.Id != addOutput.TargetId)
            {
                continue;
            }

            var sourceName = DisplayConfigSourceDeviceName.Create(path.SourceInfo.AdapterId, path.SourceInfo.Id);
            uint nameStatus = NativeMethods.DisplayConfigGetDeviceInfo(ref sourceName);
            if (nameStatus != ErrorSuccess || string.IsNullOrWhiteSpace(sourceName.ViewGdiDeviceName))
            {
                return false;
            }

            displayName = sourceName.ViewGdiDeviceName;
            return true;
        }

        return false;
    }

    private static bool TryQueryDisplayConfig(
        uint flags,
        out DisplayConfigPathInfo[] paths,
        out DisplayConfigModeInfo[] modes,
        out string diagnostic)
    {
        paths = [];
        modes = [];

        uint pathCount = 0;
        uint modeCount = 0;
        uint sizeStatus = NativeMethods.GetDisplayConfigBufferSizes(flags, ref pathCount, ref modeCount);
        if (sizeStatus != ErrorSuccess)
        {
            diagnostic = $"GetDisplayConfigBufferSizes failed. Flags=0x{flags:X} Result={sizeStatus}.";
            return false;
        }

        paths = new DisplayConfigPathInfo[pathCount];
        modes = new DisplayConfigModeInfo[modeCount];
        uint queryStatus = NativeMethods.QueryDisplayConfig(
            flags,
            ref pathCount,
            paths,
            ref modeCount,
            modes,
            IntPtr.Zero);
        if (queryStatus != ErrorSuccess)
        {
            diagnostic = $"QueryDisplayConfig failed. Flags=0x{flags:X} Result={queryStatus}.";
            return false;
        }

        Array.Resize(ref paths, checked((int)pathCount));
        Array.Resize(ref modes, checked((int)modeCount));
        diagnostic = "DisplayConfig queried.";
        return true;
    }

    private static uint SuppliedDisplayConfigApplyFlags() =>
        SdcApply |
        SdcUseSuppliedDisplayConfig |
        SdcSaveToDatabase |
        SdcVirtualModeAware;

    private static bool IsTargetPath(DisplayConfigPathInfo path, VirtualDisplayAddOut addOutput) =>
        path.TargetInfo.Id == addOutput.TargetId &&
        path.TargetInfo.AdapterId.Equals(addOutput.AdapterLuid);

    private static bool IsSameDisplayPath(DisplayConfigPathInfo left, DisplayConfigPathInfo right) =>
        left.SourceInfo.Id == right.SourceInfo.Id &&
        left.TargetInfo.Id == right.TargetInfo.Id &&
        left.SourceInfo.AdapterId.Equals(right.SourceInfo.AdapterId) &&
        left.TargetInfo.AdapterId.Equals(right.TargetInfo.AdapterId);

    private static uint SourceModeInfoIndex(DisplayConfigPathInfo path) =>
        path.SourceInfo.ModeInfoIdx >> 16;

    private static bool TryGetSourceMode(
        DisplayConfigModeInfo[] modes,
        uint sourceModeIndex,
        out DisplayConfigSourceMode sourceMode)
    {
        sourceMode = default;
        if (sourceModeIndex == DisplayConfigPathSourceModeIdxInvalid ||
            sourceModeIndex >= modes.Length ||
            modes[sourceModeIndex].InfoType != DisplayConfigModeInfoTypeSource)
        {
            return false;
        }

        sourceMode = modes[sourceModeIndex].SourceMode;
        return true;
    }

    private static bool IsOrigin(PointL position) =>
        position.X == 0 && position.Y == 0;

    private static DisplayConfigPathInfo PrepareTopologyPath(DisplayConfigPathInfo path, uint groupId)
    {
        path.Flags |= DisplayConfigPathActive;
        path.SourceInfo.ModeInfoIdx = (DisplayConfigPathSourceModeIdxInvalid << 16) | (groupId & 0xFFFF);
        path.TargetInfo.ModeInfoIdx = DisplayConfigPathModeIdxInvalid;
        return path;
    }

    private static string FormatLuid(Luid luid) => $"{luid.HighPart:X8}:{luid.LowPart:X8}";

    private static bool RemoveVirtualDisplay(SafeFileHandle handle, Guid monitorGuid)
    {
        var parameters = new VirtualDisplayRemoveParams
        {
            MonitorGuid = monitorGuid
        };

        return NativeMethods.DeviceIoControl(
            handle,
            BuildSudoVdaControlCode(IoctlRemoveVirtualDisplay),
            ref parameters,
            Marshal.SizeOf<VirtualDisplayRemoveParams>(),
            IntPtr.Zero,
            0,
            out _,
            IntPtr.Zero);
    }

    private bool TrySetPrimaryDisplay(string displayId, out string diagnostic)
    {
        if (!TryResolveDisplayName(displayId, out string? primaryDisplayName) || primaryDisplayName is null)
        {
            diagnostic = $"Unable to resolve Windows display name for {displayId}.";
            return false;
        }

        if (!TryQueryDisplayConfig(
            QdcOnlyActivePaths | QdcVirtualModeAware,
            out DisplayConfigPathInfo[] paths,
            out DisplayConfigModeInfo[] modes,
            out diagnostic))
        {
            return false;
        }

        uint? primarySourceModeIndex = null;
        PointL origin = default;

        foreach (DisplayConfigPathInfo path in paths)
        {
            var sourceName = DisplayConfigSourceDeviceName.Create(path.SourceInfo.AdapterId, path.SourceInfo.Id);
            uint nameStatus = NativeMethods.DisplayConfigGetDeviceInfo(ref sourceName);
            if (nameStatus != ErrorSuccess ||
                !string.Equals(sourceName.ViewGdiDeviceName, primaryDisplayName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            uint sourceModeIndex = SourceModeInfoIndex(path);
            if (!TryGetSourceMode(modes, sourceModeIndex, out DisplayConfigSourceMode sourceMode))
            {
                diagnostic = $"Active DisplayConfig path for {primaryDisplayName} did not expose a source mode.";
                return false;
            }

            if (IsOrigin(sourceMode.Position))
            {
                diagnostic = $"Display {primaryDisplayName} is already primary.";
                return true;
            }

            primarySourceModeIndex = sourceModeIndex;
            origin = sourceMode.Position;
            break;
        }

        if (primarySourceModeIndex is null)
        {
            diagnostic = $"DisplayConfig did not expose active source {primaryDisplayName}.";
            return false;
        }

        var modifiedSourceModeIndexes = new HashSet<uint>();
        for (int pathIndex = 0; pathIndex < paths.Length; pathIndex++)
        {
            uint sourceModeIndex = SourceModeInfoIndex(paths[pathIndex]);
            if (!modifiedSourceModeIndexes.Add(sourceModeIndex))
            {
                continue;
            }

            if (!TryGetSourceMode(modes, sourceModeIndex, out DisplayConfigSourceMode sourceMode))
            {
                diagnostic = $"Active DisplayConfig path at index {pathIndex} did not expose a source mode.";
                return false;
            }

            sourceMode.Position.X -= origin.X;
            sourceMode.Position.Y -= origin.Y;
            modes[sourceModeIndex].SourceMode = sourceMode;
        }

        uint status = NativeMethods.SetDisplayConfig(
            checked((uint)paths.Length),
            paths,
            checked((uint)modes.Length),
            modes,
            SuppliedDisplayConfigApplyFlags());

        diagnostic = status == ErrorSuccess
            ? $"Display {primaryDisplayName} set primary."
            : $"DisplayConfig primary apply failed for {primaryDisplayName}. Result={status}.";
        return status == ErrorSuccess;
    }

    private bool TryResolveDisplayName(string displayId, out string? displayName)
    {
        if (displayId.StartsWith(@"\\.\DISPLAY", StringComparison.OrdinalIgnoreCase))
        {
            displayName = displayId;
            return true;
        }

        lock (displayMapLock)
        {
            return displayNameByDisplayId.TryGetValue(displayId, out displayName);
        }
    }

    private static bool ContainsOrdinalIgnoreCase(string value, string fragment) =>
        value.Contains(fragment, StringComparison.OrdinalIgnoreCase);

    private void RememberDisplayName(string displayId, string displayName)
    {
        lock (displayMapLock)
        {
            displayNameByDisplayId[displayId] = displayName;
        }
    }

    private void ForgetDisplayName(string displayId)
    {
        lock (displayMapLock)
        {
            displayNameByDisplayId.Remove(displayId);
        }
    }

    private Dictionary<string, string> CreateDisplayIdByDisplayNameSnapshot()
    {
        lock (displayMapLock)
        {
            return displayNameByDisplayId.ToDictionary(
                pair => pair.Value,
                pair => pair.Key,
                StringComparer.OrdinalIgnoreCase);
        }
    }

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

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeviceIoControl(
            SafeFileHandle device,
            uint ioControlCode,
            ref VirtualDisplayAddParams inBuffer,
            int inBufferSize,
            out VirtualDisplayAddOut outBuffer,
            int outBufferSize,
            out uint bytesReturned,
            IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeviceIoControl(
            SafeFileHandle device,
            uint ioControlCode,
            ref VirtualDisplayRemoveParams inBuffer,
            int inBufferSize,
            IntPtr outBuffer,
            int outBufferSize,
            out uint bytesReturned,
            IntPtr overlapped);

        [DllImport("user32.dll")]
        public static extern uint GetDisplayConfigBufferSizes(
            uint flags,
            ref uint numPathArrayElements,
            ref uint numModeInfoArrayElements);

        [DllImport("user32.dll")]
        public static extern uint QueryDisplayConfig(
            uint flags,
            ref uint numPathArrayElements,
            [Out] DisplayConfigPathInfo[] pathArray,
            ref uint numModeInfoArrayElements,
            [Out] DisplayConfigModeInfo[] modeInfoArray,
            IntPtr currentTopologyId);

        [DllImport("user32.dll")]
        public static extern uint DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName requestPacket);

        [DllImport("user32.dll")]
        public static extern uint SetDisplayConfig(
            uint numPathArrayElements,
            [In] DisplayConfigPathInfo[] pathArray,
            uint numModeInfoArrayElements,
            [In] DisplayConfigModeInfo[] modeInfoArray,
            uint flags);

        [DllImport("user32.dll", EntryPoint = "SetDisplayConfig")]
        public static extern uint SetDisplayConfigWithoutModes(
            uint numPathArrayElements,
            [In] DisplayConfigPathInfo[] pathArray,
            uint numModeInfoArrayElements,
            IntPtr modeInfoArray,
            uint flags);
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct VirtualDisplayAddParams
    {
        public uint Width;
        public uint Height;
        public uint RefreshRate;
        public Guid MonitorGuid;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string SerialNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VirtualDisplayRemoveParams
    {
        public Guid MonitorGuid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VirtualDisplayAddOut
    {
        public Luid AdapterLuid;
        public uint TargetId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathInfo
    {
        public DisplayConfigPathSourceInfo SourceInfo;
        public DisplayConfigPathTargetInfo TargetInfo;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathSourceInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathTargetInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public DisplayConfigRational RefreshRate;
        public uint ScanLineOrdering;

        [MarshalAs(UnmanagedType.Bool)]
        public bool TargetAvailable;

        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigRational
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigModeInfo
    {
        public uint InfoType;
        public uint Id;
        public Luid AdapterId;
        public DisplayConfigModeInfoUnion Union;

        public DisplayConfigSourceMode SourceMode
        {
            readonly get => Union.SourceMode;
            set => Union.SourceMode = value;
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct DisplayConfigModeInfoUnion
    {
        [FieldOffset(0)]
        public DisplayConfigTargetMode TargetMode;

        [FieldOffset(0)]
        public DisplayConfigSourceMode SourceMode;

        [FieldOffset(0)]
        public DisplayConfigDesktopImageInfo DesktopImageInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigTargetMode
    {
        public DisplayConfigVideoSignalInfo TargetVideoSignalInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigVideoSignalInfo
    {
        public ulong PixelRate;
        public DisplayConfigRational HSyncFreq;
        public DisplayConfigRational VSyncFreq;
        public DisplayConfig2DRegion ActiveSize;
        public DisplayConfig2DRegion TotalSize;
        public uint VideoStandard;
        public uint ScanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfig2DRegion
    {
        public uint Cx;
        public uint Cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigSourceMode
    {
        public uint Width;
        public uint Height;
        public uint PixelFormat;
        public PointL Position;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigDesktopImageInfo
    {
        public PointL PathSourceSize;
        public RectL DesktopImageRegion;
        public RectL DesktopImageClip;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectL
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigDeviceInfoHeader
    {
        public uint Type;
        public uint Size;
        public Luid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayConfigSourceDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ViewGdiDeviceName;

        public static DisplayConfigSourceDeviceName Create(Luid adapterId, uint sourceId)
        {
            return new DisplayConfigSourceDeviceName
            {
                Header = new DisplayConfigDeviceInfoHeader
                {
                    Type = DisplayConfigDeviceInfoGetSourceName,
                    Size = checked((uint)Marshal.SizeOf<DisplayConfigSourceDeviceName>()),
                    AdapterId = adapterId,
                    Id = sourceId
                },
                ViewGdiDeviceName = string.Empty
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointL
    {
        public int X;

        public int Y;
    }
}

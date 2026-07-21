using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Beacon.Platform.Windows.Displays;

public sealed class WindowsDisplayApi :
    IWindowsDisplayApi,
    IWindowsDisplayLeaseSession,
    IDisposable,
    IAsyncDisposable
{
    private const uint DisplayDeviceActive = 0x00000001;
    private const uint DisplayDevicePrimaryDevice = 0x00000004;
    private const uint DisplayConfigOutputTechnologyInternal = 0x80000000;
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
    private const uint QdcAllPaths = 0x00000001;
    private const uint QdcOnlyActivePaths = 0x00000002;
    private const uint QdcVirtualModeAware = 0x00000010;
    private const uint SdcUseSuppliedDisplayConfig = 0x00000020;
    private const uint SdcValidate = 0x00000040;
    private const uint SdcApply = 0x00000080;
    private const uint SdcSaveToDatabase = 0x00000200;
    private const uint SdcAllowChanges = 0x00000400;
    private const uint SdcTopologySupplied = 0x00000010;
    private const uint SdcAllowPathOrderChanges = 0x00002000;
    private const uint SdcVirtualModeAware = 0x00008000;
    private const uint DisplayConfigDeviceInfoGetSourceName = 1;
    private const uint DisplayConfigDeviceInfoGetAdvancedColorInfo = 9;
    private const uint DisplayConfigDeviceInfoGetAdvancedColorInfo2 = 15;
    private const uint DisplayConfigPathActive = 0x00000001;
    private const uint DisplayConfigPathModeIdxInvalid = 0xFFFFFFFF;
    private const uint DisplayConfigPathSourceModeIdxInvalid = 0xFFFF;
    private const uint DisplayConfigModeInfoTypeSource = 1;
    private const uint DisplayConfigPixelFormat32Bpp = 4;
    private const uint IoctlAddVirtualDisplay = 0x800;
    private const uint IoctlRemoveVirtualDisplay = 0x801;
    private const uint IoctlGetWatchdog = 0x803;
    private const uint IoctlDriverPing = 0x888;
    private const uint IoctlGetProtocolVersion = 0x8FF;
    private const byte ExpectedProtocolMajor = 0;
    private const byte ExpectedProtocolMinor = 2;
    private const int EnumCurrentSettings = -1;
    private const int EnumRegistrySettings = -2;
    private const uint DmPosition = 0x00000020;
    private const uint CdsUpdateRegistry = 0x00000001;
    private const uint CdsReset = 0x40000000;
    private static readonly Guid SudoVdaInterfaceGuid = new("e5bcc234-1e0c-418a-a0d4-ef8b7501414d");
    private readonly WindowsDisplayNameMap displayNameMap;
    private readonly SudoVdaDriverLeaseSession driverLeaseSession;
    private readonly WindowsVirtualDisplayArrivalGate virtualDisplayArrivalGate;
    private readonly WindowsInputDesktopExecutionContext inputDesktop;
    private readonly WindowsDisplayLeaseTopologyReconciler topologyReconciler;
    private readonly WindowsShellExtendedTopologyActivator extendedTopologyActivator = new();
    private readonly Action<string>? diagnostic;
    private readonly object leasedDisplayStateGate = new();
    private readonly Dictionary<string, LeasedVirtualDisplayState> leasedDisplays =
        new(StringComparer.Ordinal);

    public WindowsDisplayApi()
        : this(new WindowsDisplayNameMap(WindowsDisplayNameMapStore.Default))
    {
    }

    public WindowsDisplayApi(WindowsDisplayNameMap displayNameMap)
        : this(displayNameMap, new WindowsInputDesktopExecutionContext(), diagnostic: null)
    {
    }

    public WindowsDisplayApi(
        WindowsDisplayNameMap displayNameMap,
        Action<string> diagnostic)
        : this(displayNameMap, new WindowsInputDesktopExecutionContext(), diagnostic)
    {
    }

    private WindowsDisplayApi(
        WindowsDisplayNameMap displayNameMap,
        WindowsInputDesktopExecutionContext inputDesktop,
        Action<string>? diagnostic)
    {
        this.displayNameMap = displayNameMap;
        this.inputDesktop = inputDesktop;
        this.diagnostic = diagnostic;
        topologyReconciler = new WindowsDisplayLeaseTopologyReconciler(
            () => this.inputDesktop.Invoke(QueryActiveTopology),
            SnapshotLeasedDisplayRequirements,
            requirements => this.inputDesktop.Invoke(
                () => ReactivateLeasedDisplayTopology(requirements)));
        driverLeaseSession = new SudoVdaDriverLeaseSession(
            new WindowsSudoVdaDriverConnectionFactory(),
            new TaskDelaySudoVdaHeartbeatScheduler(),
            topologyReconciler.ReconcileAsync);
        virtualDisplayArrivalGate = new WindowsVirtualDisplayArrivalGate(
            () => driverLeaseSession.HeartbeatRevision,
            driverLeaseSession.WaitForHeartbeatAsync,
            diagnostic);
    }

    internal WindowsDisplayApi(
        WindowsDisplayNameMap displayNameMap,
        SudoVdaDriverLeaseSession driverLeaseSession)
        : this(displayNameMap, driverLeaseSession, new WindowsInputDesktopExecutionContext())
    {
    }

    internal WindowsDisplayApi(
        WindowsDisplayNameMap displayNameMap,
        SudoVdaDriverLeaseSession driverLeaseSession,
        WindowsInputDesktopExecutionContext inputDesktop)
    {
        this.displayNameMap = displayNameMap;
        this.driverLeaseSession = driverLeaseSession;
        this.inputDesktop = inputDesktop;
        diagnostic = null;
        virtualDisplayArrivalGate = new WindowsVirtualDisplayArrivalGate(
            () => this.driverLeaseSession.HeartbeatRevision,
            this.driverLeaseSession.WaitForHeartbeatAsync);
        topologyReconciler = new WindowsDisplayLeaseTopologyReconciler(
            () => this.inputDesktop.Invoke(QueryActiveTopology),
            SnapshotLeasedDisplayRequirements,
            requirements => this.inputDesktop.Invoke(
                () => ReactivateLeasedDisplayTopology(requirements)));
    }

    public SudoVdaDriverLeaseSessionSnapshot Snapshot => driverLeaseSession.Snapshot;

    public Task<SudoVdaDriverLeaseHoldResult> HoldAsync(
        string displayId,
        CancellationToken cancellationToken) =>
        driverLeaseSession.HoldAsync(displayId, cancellationToken);

    public Task ReleaseAsync(string displayId, CancellationToken cancellationToken) =>
        driverLeaseSession.ReleaseAsync(displayId, cancellationToken);

    public ValueTask DisposeAsync() => driverLeaseSession.DisposeAsync();

    public void Dispose() => driverLeaseSession.Dispose();

    public DisplayDriverStatus GetDriverStatus()
    {
        try
        {
            return inputDesktop.Invoke(GetDriverStatusOnInputDesktop);
        }
        catch (WindowsInputDesktopException error)
        {
            return new DisplayDriverStatus(
                Ready: false,
                Diagnostic: $"Windows display access is unavailable: {error.Message}");
        }
    }

    private static DisplayDriverStatus GetDriverStatusOnInputDesktop()
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
        if (!compatible)
        {
            return new DisplayDriverStatus(
                Ready: false,
                Diagnostic: $"SudoVDA protocol {version.Major}.{version.Minor}.{version.Incremental} is incompatible with Beacon protocol {ExpectedProtocolMajor}.{ExpectedProtocolMinor}.",
                version.Major,
                version.Minor,
                version.Incremental);
        }

        DisplayApiResult ccdAccess = ValidateDisplayConfigAccess();
        string driverDiagnostic =
            $"SudoVDA driver is ready. Protocol {version.Major}.{version.Minor}.{version.Incremental}.";
        return ccdAccess.Success
            ? new DisplayDriverStatus(
                Ready: true,
                Diagnostic: driverDiagnostic,
                version.Major,
                version.Minor,
                version.Incremental)
            : new DisplayDriverStatus(
                Ready: false,
                Diagnostic: $"{driverDiagnostic} {ccdAccess.Error}",
                version.Major,
                version.Minor,
                version.Incremental);
    }

    public async Task<DisplayApiResult> CreateVirtualDisplayAsync(
        string displayId,
        int width,
        int height,
        int refreshHz,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        VirtualDisplayCreationBaseline baseline = inputDesktop.Invoke(CaptureVirtualDisplayCreationBaseline);
        if (baseline.Error is not null)
        {
            return DisplayApiResult.Fail(baseline.Error);
        }

        Guid monitorGuid = CreateDeterministicDisplayGuid(displayId);
        SudoVdaVirtualDisplayCreateResult addResult =
            await driverLeaseSession.CreateVirtualDisplayAsync(
                displayId,
                new SudoVdaVirtualDisplayCreateRequest(
                    checked((uint)width),
                    checked((uint)height),
                    checked((uint)refreshHz),
                    monitorGuid,
                    "BeaconStream",
                    "beaconstream"),
                cancellationToken).ConfigureAwait(false);
        if (!addResult.Success)
        {
            return DisplayApiResult.Fail(
                addResult.Error ?? $"SudoVDA create failed for {displayId}.");
        }

        WriteDiagnostic(
            $"display-create display={displayId} phase=driver-created adapter={addResult.AdapterHighPart}:{addResult.AdapterLowPart} target={addResult.TargetId}");

        var addOutput = new VirtualDisplayAddOut
        {
            AdapterLuid = new Luid
            {
                LowPart = addResult.AdapterLowPart,
                HighPart = addResult.AdapterHighPart
            },
            TargetId = addResult.TargetId
        };

        DisplayApiResult initialTopology;
        try
        {
            initialTopology = await virtualDisplayArrivalGate.ApplyAfterNextHeartbeatAsync(
                () => inputDesktop.Invoke(ApplyExtendedTopology),
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await driverLeaseSession.RemoveVirtualDisplayAsync(
                displayId,
                monitorGuid,
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        if (!initialTopology.Success)
        {
            await driverLeaseSession.RemoveVirtualDisplayAsync(
                displayId,
                monitorGuid,
                CancellationToken.None).ConfigureAwait(false);
            return DisplayApiResult.Fail(
                $"Unable to compose the initial extended topology for {displayId}: {initialTopology.Error}");
        }

        WriteDiagnostic($"display-create display={displayId} phase=initial-topology-complete");

        VirtualDisplayTargetArrivalSnapshot stableTarget;
        try
        {
            stableTarget = await virtualDisplayArrivalGate.WaitForStableTargetAsync(
                () => inputDesktop.Invoke(() => QueryVirtualDisplayTargetArrival(addOutput)),
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await driverLeaseSession.RemoveVirtualDisplayAsync(
                displayId,
                monitorGuid,
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        WriteDiagnostic(
            $"display-create display={displayId} phase=target-arrived displayName={stableTarget.DisplayName}");

        VirtualDisplayActivationOutcome activation = inputDesktop.Invoke(() => ActivateCreatedVirtualDisplay(
            displayId,
            addOutput,
            stableTarget.DisplayName!,
            baseline));
        if (!activation.Result.Success)
        {
            await driverLeaseSession.RemoveVirtualDisplayAsync(
                displayId,
                monitorGuid,
                CancellationToken.None).ConfigureAwait(false);
            return activation.Result;
        }

        var requirement = new LeasedDisplayTopologyRequirement(
            displayId,
            width,
            height,
            refreshHz);
        try
        {
            DisplayApiResult extendedTopology =
                await virtualDisplayArrivalGate.WaitForStableExtendedTopologyAsync(
                    () => inputDesktop.Invoke(() => QueryVirtualDisplayDesiredTopology(
                        addOutput,
                        activation.DisplayName!,
                        requirement)),
                    _ => inputDesktop.Invoke(() => EnsureDisplayConfigTargetActive(
                        addOutput,
                        activation.DisplayName!)),
                    cancellationToken).ConfigureAwait(false);
            if (!extendedTopology.Success)
            {
                await RemoveFailedVirtualDisplayAsync(displayId, monitorGuid).ConfigureAwait(false);
                return DisplayApiResult.Fail(
                    $"Virtual display path topology did not converge for {displayId}: {extendedTopology.Error}");
            }

            DisplayApiResult modeResult = inputDesktop.Invoke(() => ApplyDisplayConfigMode(
                activation.DisplayName!,
                width,
                height,
                refreshHz));
            if (!modeResult.Success)
            {
                await RemoveFailedVirtualDisplayAsync(displayId, monitorGuid).ConfigureAwait(false);
                return modeResult;
            }

            DisplayApiResult exactTopology =
                await virtualDisplayArrivalGate.WaitForStableDesiredTopologyAsync(
                () => inputDesktop.Invoke(() => QueryVirtualDisplayDesiredTopology(
                    addOutput,
                    activation.DisplayName!,
                    requirement)),
                snapshot => inputDesktop.Invoke(() => snapshot.ExtendedTopology
                    ? ApplyDisplayConfigMode(
                        activation.DisplayName!,
                        width,
                        height,
                        refreshHz)
                    : EnsureDisplayConfigTargetActive(
                        addOutput,
                        activation.DisplayName!)),
                cancellationToken).ConfigureAwait(false);
            if (!exactTopology.Success)
            {
                await RemoveFailedVirtualDisplayAsync(displayId, monitorGuid).ConfigureAwait(false);
                return DisplayApiResult.Fail(
                    $"Virtual display topology did not converge for {displayId}: {exactTopology.Error}");
            }
        }
        catch
        {
            await RemoveFailedVirtualDisplayAsync(displayId, monitorGuid).ConfigureAwait(false);
            throw;
        }

        RememberLeasedDisplayState(new LeasedVirtualDisplayState(
            displayId,
            activation.DisplayName!,
            width,
            height,
            refreshHz,
            addOutput));

        return DisplayApiResult.Ok();
    }

    private static VirtualDisplayCreationBaseline CaptureVirtualDisplayCreationBaseline()
    {
        IReadOnlyList<string> displayNames = EnumerateDisplayNames(activeOnly: false);
        if (!TryQueryDisplayConfig(
            QdcOnlyActivePaths | QdcVirtualModeAware,
            out DisplayConfigPathInfo[] activePaths,
            out _,
            out string topologyDiagnostic))
        {
            return new VirtualDisplayCreationBaseline(displayNames, topologyDiagnostic);
        }

        return DescribeDisplayPaths(activePaths).Any(candidate => candidate.Kind == DisplayPathKind.Physical)
            ? new VirtualDisplayCreationBaseline(displayNames, Error: null)
            : new VirtualDisplayCreationBaseline(
                displayNames,
                "Refusing to create a virtual display because no active physical display path can be preserved.");
    }

    private VirtualDisplayActivationOutcome ActivateCreatedVirtualDisplay(
        string displayId,
        VirtualDisplayAddOut addOutput,
        string stableDisplayName,
        VirtualDisplayCreationBaseline baseline)
    {
        IReadOnlyList<DisplayPathSnapshot> afterDisplayPaths = EnumerateDisplayNamesWithState(activeOnly: false);
        IReadOnlyList<string> afterDisplayNames = afterDisplayPaths.Select(path => path.DisplayId).ToArray();
        string? displayName = afterDisplayNames.Contains(stableDisplayName, StringComparer.OrdinalIgnoreCase)
            ? stableDisplayName
            : SelectAddedDisplayName(baseline.DisplayNames, afterDisplayNames) ??
              SelectSingleVirtualDisplayName(afterDisplayPaths);

        if (displayName is null)
        {
            return new VirtualDisplayActivationOutcome(DisplayApiResult.Fail(
                $"SudoVDA create succeeded for {displayId}, but Windows did not expose a mappable virtual display name. Before=[{string.Join(", ", baseline.DisplayNames)}] After=[{string.Join(", ", afterDisplayNames)}]."),
                DisplayName: null);
        }

        RememberDisplayName(displayId, displayName);
        DisplayApiResult result = EnsureDisplayConfigTargetActive(
            addOutput,
            displayName);
        if (!result.Success)
        {
            ForgetDisplayName(displayId);
        }

        return new VirtualDisplayActivationOutcome(result, displayName);
    }

    private async Task RemoveFailedVirtualDisplayAsync(string displayId, Guid monitorGuid)
    {
        ForgetDisplayName(displayId);
        await driverLeaseSession.RemoveVirtualDisplayAsync(
            displayId,
            monitorGuid,
            CancellationToken.None).ConfigureAwait(false);
    }

    public Task<DisplayTopologySnapshot> QueryTopologyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(inputDesktop.Invoke(QueryActiveTopology));
    }

    public Task<DisplayApiResult> SetVirtualPrimaryAsync(string displayId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(inputDesktop.Invoke(() =>
            TrySetPrimaryDisplay(displayId, out string diagnostic)
                ? DisplayApiResult.Ok()
                : DisplayApiResult.Fail(diagnostic)));
    }

    public Task<DisplayApiResult> RestorePhysicalPrimaryAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(inputDesktop.Invoke(RestorePhysicalPrimaryOnInputDesktop));
    }

    private DisplayApiResult RestorePhysicalPrimaryOnInputDesktop()
    {
        DisplayPathSnapshot? physicalDisplay = EnumerateActiveDisplayPaths(displayIdByDisplayName: null)
            .FirstOrDefault(path => path.Kind == DisplayPathKind.Physical);

        if (physicalDisplay is null)
        {
            DisplayApiResult extended = ApplyExtendedTopology();
            if (extended.Success)
            {
                physicalDisplay = EnumerateActiveDisplayPaths(displayIdByDisplayName: null)
                    .FirstOrDefault(path => path.Kind == DisplayPathKind.Physical);
            }

            if (physicalDisplay is null)
            {
                DisplayApiResult forced = ForceAttachRegisteredPhysicalDisplay();
                if (!forced.Success)
                {
                    string extendedDiagnostic = extended.Success
                        ? "Extended topology did not expose a physical display."
                        : extended.Error ?? "Extended topology apply failed.";
                    return DisplayApiResult.Fail($"{extendedDiagnostic} {forced.Error}");
                }

                physicalDisplay = EnumerateActiveDisplayPaths(displayIdByDisplayName: null)
                    .FirstOrDefault(path => path.Kind == DisplayPathKind.Physical);
            }
        }

        string displayName;
        if (physicalDisplay is not null)
        {
            displayName = physicalDisplay.DisplayId;
        }
        else if (TrySelectPhysicalRestoreDisplayNameFromDisplayConfig(out string? fallbackDisplayName, out string restoreDiagnostic) &&
            fallbackDisplayName is not null)
        {
            displayName = fallbackDisplayName;
        }
        else
        {
            return DisplayApiResult.Fail(restoreDiagnostic);
        }

        return TrySetPrimaryDisplay(displayName, out string diagnostic)
            ? DisplayApiResult.Ok()
            : DisplayApiResult.Fail(diagnostic);
    }

    public async Task<DisplayApiResult> RemoveVirtualDisplayAsync(
        string displayId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SudoVdaDriverOperationResult result = await driverLeaseSession.RemoveVirtualDisplayAsync(
            displayId,
            CreateDeterministicDisplayGuid(displayId),
            cancellationToken).ConfigureAwait(false);

        if (result.Success)
        {
            ForgetLeasedDisplayState(displayId);
            ForgetDisplayName(displayId);
        }

        return result.Success
            ? DisplayApiResult.Ok()
            : DisplayApiResult.Fail(
                result.Error ?? $"SudoVDA remove failed for {displayId}.");
    }

    public Task<DisplayHdrCapability> QueryHdrCapabilityAsync(string displayId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(inputDesktop.Invoke(() => QueryHdrCapabilityOnInputDesktop(displayId)));
    }

    private DisplayHdrCapability QueryHdrCapabilityOnInputDesktop(string displayId)
    {
        if (!TryResolveDisplayName(displayId, out string? displayName) || displayName is null)
        {
            return new DisplayHdrCapability(
                Supported: false,
                Enabled: false,
                Reason: $"Unable to resolve Windows display name for HDR query on {displayId}.");
        }

        if (!TryFindActiveDisplayConfigPath(displayName, out DisplayConfigPathInfo path, out string pathDiagnostic))
        {
            return new DisplayHdrCapability(
                Supported: false,
                Enabled: false,
                Reason: pathDiagnostic);
        }

        if (TryQueryAdvancedColorInfo2(path.TargetInfo.AdapterId, path.TargetInfo.Id, out DisplayHdrCapability capability, out string info2Diagnostic))
        {
            return capability;
        }

        if (TryQueryLegacyAdvancedColorInfo(path.TargetInfo.AdapterId, path.TargetInfo.Id, out capability, out string legacyDiagnostic))
        {
            return capability with
            {
                Reason = $"{capability.Reason} AdvancedColorInfo2 unavailable: {info2Diagnostic}"
            };
        }

        return new DisplayHdrCapability(
            Supported: false,
            Enabled: false,
            Reason: $"Windows Advanced Color query failed for {displayName}. Info2={info2Diagnostic}; Legacy={legacyDiagnostic}.");
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

            DisplayPathKind kind = ClassifyDisplayKind(device.DeviceString, device.DeviceId);
            string displayId = kind == DisplayPathKind.Virtual &&
                displayIdByDisplayName is not null &&
                displayIdByDisplayName.TryGetValue(device.DeviceName, out string? mappedDisplayId)
                    ? mappedDisplayId
                    : device.DeviceName;

            paths.Add(new DisplayPathSnapshot(
                displayId,
                kind,
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

    private DisplayApiResult EnsureDisplayConfigTargetActive(
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

        IReadOnlyList<DisplayRestoreCandidate> activeDisplays = DescribeDisplayPaths(activePaths);
        DisplayTargetActivationAction activationAction =
            WindowsDisplayDiagnostics.PlanTargetActivation(
                activeDisplays,
                displayName,
                QueryActiveTopology().IsMirrorMode);
        if (activationAction == DisplayTargetActivationAction.None)
        {
            return DisplayApiResult.Ok();
        }

        if (activationAction == DisplayTargetActivationAction.ApplyExtendedTopology)
        {
            return ApplyExtendedTopology();
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

        if (WindowsDisplayDiagnostics.ShouldRetryWithSuppliedDisplayConfig(status))
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

    private DisplayApiResult ApplyExtendedTopology() => extendedTopologyActivator.Apply();

    private void WriteDiagnostic(string message)
    {
        try
        {
            diagnostic?.Invoke(message);
        }
        catch
        {
        }
    }

    private IReadOnlyList<LeasedDisplayTopologyRequirement> SnapshotLeasedDisplayRequirements()
    {
        lock (leasedDisplayStateGate)
        {
            return leasedDisplays.Values
                .OrderBy(state => state.DisplayId, StringComparer.Ordinal)
                .Select(state => new LeasedDisplayTopologyRequirement(
                    state.DisplayId,
                    state.Width,
                    state.Height,
                    state.RefreshHz))
                .ToArray();
        }
    }

    private DisplayApiResult ReactivateLeasedDisplayTopology(
        IReadOnlyList<LeasedDisplayTopologyRequirement> requirements)
    {
        DisplayTopologySnapshot current = QueryActiveTopology();
        LeasedDisplayRecoveryAction action = WindowsDisplayLeaseRecoveryPlanner.Plan(
            current,
            requirements);
        if (action == LeasedDisplayRecoveryAction.None)
        {
            return DisplayApiResult.Ok();
        }

        LeasedVirtualDisplayState[] states;
        lock (leasedDisplayStateGate)
        {
            states = new LeasedVirtualDisplayState[requirements.Count];
            for (int index = 0; index < requirements.Count; index++)
            {
                LeasedDisplayTopologyRequirement requirement = requirements[index];
                if (!leasedDisplays.TryGetValue(requirement.DisplayId, out LeasedVirtualDisplayState? state)
                    || state.Width != requirement.Width
                    || state.Height != requirement.Height
                    || state.RefreshHz != requirement.RefreshHz)
                {
                    return DisplayApiResult.Fail(
                        $"Leased display recovery state is unavailable for {requirement.DisplayId} " +
                        $"at {requirement.Width}x{requirement.Height}@{requirement.RefreshHz}.");
                }

                states[index] = state;
            }
        }

        LeasedVirtualDisplayState? missingPath = states.FirstOrDefault(state =>
            !HasExtendedVirtualDisplayTopology(current, state.DisplayId));
        if (missingPath is not null)
        {
            DisplayApiResult pathResult = EnsureDisplayConfigTargetActive(
                missingPath.AddOutput,
                missingPath.DisplayName);
            return pathResult.Success
                ? pathResult
                : DisplayApiResult.Fail(
                    $"Unable to recompose leased display paths for {missingPath.DisplayId}: {pathResult.Error}");
        }

        LeasedVirtualDisplayState? wrongMode = states.FirstOrDefault(state =>
            WindowsDisplayLeaseRecoveryPlanner.Plan(
                current,
                [new LeasedDisplayTopologyRequirement(
                    state.DisplayId,
                    state.Width,
                    state.Height,
                    state.RefreshHz)]) != LeasedDisplayRecoveryAction.None);
        if (wrongMode is null)
        {
            return DisplayApiResult.Ok();
        }

        DisplayApiResult modeResult = ApplyDisplayConfigMode(
            wrongMode.DisplayName,
            wrongMode.Width,
            wrongMode.Height,
            wrongMode.RefreshHz);
        return modeResult.Success
            ? modeResult
            : DisplayApiResult.Fail(
                $"Unable to restore leased display mode for {wrongMode.DisplayId} " +
                $"at {wrongMode.Width}x{wrongMode.Height}@{wrongMode.RefreshHz}: {modeResult.Error}");
    }

    private static bool HasExtendedVirtualDisplayTopology(
        DisplayTopologySnapshot topology,
        string displayId) =>
        !topology.IsMirrorMode &&
        topology.Paths.Any(path => path.Kind == DisplayPathKind.Physical) &&
        topology.Paths.Any(path =>
            path.Kind == DisplayPathKind.Virtual &&
            string.Equals(path.DisplayId, displayId, StringComparison.Ordinal));

    private void RememberLeasedDisplayState(LeasedVirtualDisplayState state)
    {
        lock (leasedDisplayStateGate)
        {
            leasedDisplays[state.DisplayId] = state;
        }
    }

    private void ForgetLeasedDisplayState(string displayId)
    {
        lock (leasedDisplayStateGate)
        {
            leasedDisplays.Remove(displayId);
        }
    }

    internal static uint SuppliedDisplayConfigValidateFlags() =>
        SdcValidate | SdcUseSuppliedDisplayConfig | SdcVirtualModeAware;

    internal static uint PhysicalDisplayResetFlags() =>
        CdsUpdateRegistry | CdsReset;

    private static DisplayApiResult ForceAttachRegisteredPhysicalDisplay()
    {
        HashSet<string> internalDisplayNames = FindInternalDisplayNames();
        var candidates = new List<(PhysicalDisplayAttachCandidate Candidate, DevMode Mode)>();
        for (uint index = 0; ; index++)
        {
            DisplayDevice device = DisplayDevice.Create();
            if (!NativeMethods.EnumDisplayDevices(null, index, ref device, 0)) break;
            if (ClassifyDisplayKind(device.DeviceString, device.DeviceId) == DisplayPathKind.Virtual)
            {
                continue;
            }

            DevMode mode = DevMode.Create();
            if (!NativeMethods.EnumDisplaySettings(
                    device.DeviceName,
                    EnumRegistrySettings,
                    ref mode) ||
                mode.PelsWidth == 0 ||
                mode.PelsHeight == 0)
            {
                continue;
            }

            candidates.Add((
                new PhysicalDisplayAttachCandidate(
                    device.DeviceName,
                    internalDisplayNames.Contains(device.DeviceName),
                    mode.PelsWidth,
                    mode.PelsHeight,
                    mode.DisplayFrequency),
                mode));
        }

        string? selectedDisplayName = WindowsDisplayDiagnostics.SelectPhysicalAttachCandidate(
            candidates.Select(value => value.Candidate).ToArray());
        if (selectedDisplayName is null)
        {
            return DisplayApiResult.Fail(
                "No physical Windows display source has a registered mode that can be force-attached.");
        }
        (PhysicalDisplayAttachCandidate Candidate, DevMode Mode) candidate = candidates.Single(
            value => string.Equals(
                value.Candidate.DisplayId,
                selectedDisplayName,
                StringComparison.OrdinalIgnoreCase));
        PhysicalDisplayAttachPosition? position =
            WindowsDisplayDiagnostics.SelectPhysicalAttachPosition(
                EnumerateActiveDisplayPaths(displayIdByDisplayName: null),
                candidate.Candidate.Width);
        if (position is null)
        {
            return DisplayApiResult.Fail(
                $"No valid adjacent desktop position is available to force-attach " +
                $"physical display {candidate.Candidate.DisplayId}.");
        }

        DevMode resetMode = candidate.Mode;
        resetMode.Position = new PointL
        {
            X = position.X,
            Y = position.Y
        };
        resetMode.Fields |= DmPosition;
        int status = NativeMethods.ChangeDisplaySettingsEx(
            candidate.Candidate.DisplayId,
            ref resetMode,
            IntPtr.Zero,
            PhysicalDisplayResetFlags(),
            IntPtr.Zero);
        return status == 0
            ? DisplayApiResult.Ok()
            : DisplayApiResult.Fail(
                $"Unable to force-attach physical display {candidate.Candidate.DisplayId} at " +
                $"{resetMode.PelsWidth}x{resetMode.PelsHeight}@{resetMode.DisplayFrequency} " +
                $"position=({position.X},{position.Y}). " +
                $"ChangeDisplaySettingsEx Result={status}.");
    }

    private static HashSet<string> FindInternalDisplayNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!TryQueryDisplayConfig(
                QdcAllPaths | QdcVirtualModeAware,
                out DisplayConfigPathInfo[] paths,
                out _,
                out _))
        {
            return names;
        }

        foreach (DisplayConfigPathInfo path in paths)
        {
            if (path.TargetInfo.OutputTechnology == DisplayConfigOutputTechnologyInternal &&
                TryGetSourceDisplayName(path, out string? displayName) &&
                displayName is not null)
            {
                names.Add(displayName);
            }
        }
        return names;
    }

    private static DisplayApiResult ValidateDisplayConfigAccess()
    {
        if (!TryQueryDisplayConfig(
            QdcOnlyActivePaths | QdcVirtualModeAware,
            out DisplayConfigPathInfo[] paths,
            out DisplayConfigModeInfo[] modes,
            out string diagnostic))
        {
            return DisplayApiResult.Fail(
                $"Windows CCD access validation could not query the active topology: {diagnostic}");
        }

        uint status = NativeMethods.SetDisplayConfig(
            checked((uint)paths.Length),
            paths,
            checked((uint)modes.Length),
            modes,
            SuppliedDisplayConfigValidateFlags());
        return status == ErrorSuccess
            ? DisplayApiResult.Ok()
            : DisplayApiResult.Fail(
                $"Windows CCD access validation failed. SetDisplayConfig Result={status}.");
    }

    private static IReadOnlyList<DisplayRestoreCandidate> DescribeDisplayPaths(
        IEnumerable<DisplayConfigPathInfo> paths)
    {
        var displays = new List<DisplayRestoreCandidate>();
        foreach (DisplayConfigPathInfo path in paths)
        {
            if (!TryGetSourceDisplayName(path, out string? displayName) || displayName is null)
            {
                continue;
            }

            displays.Add(new DisplayRestoreCandidate(
                displayName,
                TryClassifyDisplayName(displayName, out DisplayPathKind kind) ? kind : null,
                IsPrimary: false,
                X: 0,
                Y: 0));
        }

        return displays;
    }

    private static bool IsPhysicalDisplayPath(DisplayConfigPathInfo path) =>
        TryGetSourceDisplayName(path, out string? displayName) &&
        displayName is not null &&
        TryClassifyDisplayName(displayName, out DisplayPathKind kind) &&
        kind == DisplayPathKind.Physical;

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
            if (!TryGetSourceDisplayName(path, out string? sourceDisplayName) ||
                !string.Equals(sourceDisplayName, displayName, StringComparison.OrdinalIgnoreCase))
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

    private VirtualDisplayTargetArrivalSnapshot QueryVirtualDisplayTargetArrival(
        VirtualDisplayAddOut addOutput)
    {
        bool resolved = TryGetDisplayNameForTarget(
            addOutput,
            out string? displayName,
            out bool targetAvailable);
        return new VirtualDisplayTargetArrivalSnapshot(
            resolved && targetAvailable && displayName is not null,
            displayName,
            QueryActiveTopology().Fingerprint);
    }

    private VirtualDisplayTargetArrivalSnapshot QueryVirtualDisplayDesiredTopology(
        VirtualDisplayAddOut addOutput,
        string expectedDisplayName,
        LeasedDisplayTopologyRequirement requirement)
    {
        bool resolved = TryGetDisplayNameForTarget(
            addOutput,
            out string? displayName,
            out bool targetAvailable);
        DisplayTopologySnapshot topology = QueryActiveTopology();
        bool available = resolved &&
            targetAvailable &&
            string.Equals(displayName, expectedDisplayName, StringComparison.OrdinalIgnoreCase);
        bool extendedTopology = available &&
            HasExtendedVirtualDisplayTopology(topology, requirement.DisplayId);
        bool desiredTopology = available &&
            WindowsDisplayLeaseRecoveryPlanner.Plan(topology, [requirement]) ==
            LeasedDisplayRecoveryAction.None;
        return new VirtualDisplayTargetArrivalSnapshot(
            available,
            displayName,
            topology.Fingerprint,
            extendedTopology,
            desiredTopology);
    }

    private static bool TryGetDisplayNameForTarget(
        VirtualDisplayAddOut addOutput,
        out string? displayName,
        out bool targetAvailable)
    {
        displayName = null;
        targetAvailable = false;
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

            if (!TryGetSourceDisplayName(path, out string? sourceDisplayName))
            {
                return false;
            }

            displayName = sourceDisplayName;
            targetAvailable = path.TargetInfo.TargetAvailable;
            return true;
        }

        return false;
    }

    private static bool TryFindActiveDisplayConfigPath(
        string displayName,
        out DisplayConfigPathInfo displayPath,
        out string diagnostic)
    {
        displayPath = default;
        if (!TryQueryDisplayConfig(
            QdcOnlyActivePaths | QdcVirtualModeAware,
            out DisplayConfigPathInfo[] paths,
            out _,
            out diagnostic))
        {
            return false;
        }

        foreach (DisplayConfigPathInfo path in paths)
        {
            if (TryGetSourceDisplayName(path, out string? sourceDisplayName) &&
                string.Equals(sourceDisplayName, displayName, StringComparison.OrdinalIgnoreCase))
            {
                displayPath = path;
                diagnostic = $"DisplayConfig active path resolved for {displayName}.";
                return true;
            }
        }

        diagnostic = $"DisplayConfig did not expose active source {displayName} for HDR query.";
        return false;
    }

    private static bool TryGetSourceDisplayName(DisplayConfigPathInfo path, out string? displayName)
    {
        var sourceName = DisplayConfigSourceDeviceName.Create(path.SourceInfo.AdapterId, path.SourceInfo.Id);
        uint nameStatus = NativeMethods.DisplayConfigGetDeviceInfo(ref sourceName);
        if (nameStatus != ErrorSuccess || string.IsNullOrWhiteSpace(sourceName.ViewGdiDeviceName))
        {
            displayName = null;
            return false;
        }

        displayName = sourceName.ViewGdiDeviceName;
        return true;
    }

    private static bool TryQueryAdvancedColorInfo2(
        Luid adapterId,
        uint targetId,
        out DisplayHdrCapability capability,
        out string diagnostic)
    {
        var colorInfo = DisplayConfigGetAdvancedColorInfo2.Create(adapterId, targetId);
        uint status = NativeMethods.DisplayConfigGetAdvancedColorInfo2(ref colorInfo);
        if (status != ErrorSuccess)
        {
            capability = new DisplayHdrCapability(false, false, string.Empty);
            diagnostic = $"DisplayConfigGetDeviceInfo(GET_ADVANCED_COLOR_INFO_2) returned {status}.";
            return false;
        }

        capability = WindowsDisplayDiagnostics.FromAdvancedColorInfo2(
            colorInfo.Flags,
            colorInfo.BitsPerColorChannel,
            colorInfo.ActiveColorMode);
        diagnostic = "Advanced Color Info 2 queried.";
        return true;
    }

    private static bool TryQueryLegacyAdvancedColorInfo(
        Luid adapterId,
        uint targetId,
        out DisplayHdrCapability capability,
        out string diagnostic)
    {
        var colorInfo = DisplayConfigGetAdvancedColorInfo.Create(adapterId, targetId);
        uint status = NativeMethods.DisplayConfigGetAdvancedColorInfo(ref colorInfo);
        if (status != ErrorSuccess)
        {
            capability = new DisplayHdrCapability(false, false, string.Empty);
            diagnostic = $"DisplayConfigGetDeviceInfo(GET_ADVANCED_COLOR_INFO) returned {status}.";
            return false;
        }

        capability = WindowsDisplayDiagnostics.FromLegacyAdvancedColorInfo(
            colorInfo.Flags,
            colorInfo.BitsPerColorChannel);
        diagnostic = "Legacy Advanced Color Info queried.";
        return true;
    }

    private static bool TrySelectPhysicalRestoreDisplayNameFromDisplayConfig(
        out string? displayName,
        out string diagnostic)
    {
        displayName = null;
        if (!TryQueryDisplayConfig(
            QdcOnlyActivePaths | QdcVirtualModeAware,
            out DisplayConfigPathInfo[] paths,
            out DisplayConfigModeInfo[] modes,
            out diagnostic))
        {
            return false;
        }

        var candidates = new List<DisplayRestoreCandidate>();
        for (int pathIndex = 0; pathIndex < paths.Length; pathIndex++)
        {
            DisplayConfigPathInfo path = paths[pathIndex];
            if (!TryGetSourceDisplayName(path, out string? sourceDisplayName) ||
                sourceDisplayName is null)
            {
                continue;
            }

            uint sourceModeIndex = SourceModeInfoIndex(path);
            if (!TryGetSourceMode(modes, sourceModeIndex, out DisplayConfigSourceMode sourceMode))
            {
                continue;
            }

            string candidateDisplayName = sourceDisplayName;
            candidates.Add(new DisplayRestoreCandidate(
                candidateDisplayName,
                TryClassifyDisplayName(candidateDisplayName, out DisplayPathKind kind) ? kind : null,
                IsPrimary: IsOrigin(sourceMode.Position),
                X: sourceMode.Position.X,
                Y: sourceMode.Position.Y));
        }

        displayName = WindowsDisplayDiagnostics.SelectPhysicalRestoreCandidate(candidates);
        diagnostic = displayName is null
            ? "No active physical display was found for restore."
            : $"DisplayConfig selected {displayName} for physical primary restore.";
        return displayName is not null;
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

    private static bool TryClassifyDisplayName(string displayName, out DisplayPathKind kind)
    {
        for (uint index = 0; ; index++)
        {
            DisplayDevice device = DisplayDevice.Create();
            if (!NativeMethods.EnumDisplayDevices(null, index, ref device, 0))
            {
                kind = default;
                return false;
            }

            if (!string.Equals(device.DeviceName, displayName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            kind = ClassifyDisplayKind(device.DeviceString, device.DeviceId);
            return true;
        }
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
            if (!TryGetSourceDisplayName(path, out string? sourceDisplayName) ||
                !string.Equals(sourceDisplayName, primaryDisplayName, StringComparison.OrdinalIgnoreCase))
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

        return displayNameMap.TryResolveDisplayName(displayId, out displayName);
    }

    private static bool ContainsOrdinalIgnoreCase(string value, string fragment) =>
        value.Contains(fragment, StringComparison.OrdinalIgnoreCase);

    private void RememberDisplayName(string displayId, string displayName)
    {
        displayNameMap.Remember(displayId, displayName);
    }

    private void ForgetDisplayName(string displayId)
    {
        displayNameMap.Forget(displayId);
    }

    private Dictionary<string, string> CreateDisplayIdByDisplayNameSnapshot()
    {
        return displayNameMap.CreateDisplayIdByDisplayNameSnapshot();
    }

    internal static SafeFileHandle? OpenSudoVdaDevice(out string diagnostic)
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

    internal static SudoVdaWatchdogQueryResult QuerySudoVdaWatchdog(SafeFileHandle handle)
    {
        bool success = NativeMethods.DeviceIoControl(
            handle,
            BuildSudoVdaControlCode(IoctlGetWatchdog),
            IntPtr.Zero,
            0,
            out SudoVdaWatchdogOut output,
            Marshal.SizeOf<SudoVdaWatchdogOut>(),
            out _,
            IntPtr.Zero);

        return success
            ? SudoVdaWatchdogQueryResult.Ok(new SudoVdaWatchdogState(output.Timeout, output.Countdown))
            : SudoVdaWatchdogQueryResult.Fail(
                $"Unable to read SudoVDA watchdog state. Win32={Marshal.GetLastWin32Error()}.");
    }

    internal static SudoVdaDriverOperationResult PingSudoVdaDriver(SafeFileHandle handle)
    {
        bool success = NativeMethods.DeviceIoControl(
            handle,
            BuildSudoVdaControlCode(IoctlDriverPing),
            IntPtr.Zero,
            0,
            IntPtr.Zero,
            0,
            out _,
            IntPtr.Zero);

        return success
            ? SudoVdaDriverOperationResult.Ok()
            : SudoVdaDriverOperationResult.Fail(
                $"SudoVDA heartbeat was not acknowledged. Win32={Marshal.GetLastWin32Error()}.");
    }

    internal static SudoVdaVirtualDisplayCreateResult CreateSudoVdaVirtualDisplay(
        SafeFileHandle handle,
        SudoVdaVirtualDisplayCreateRequest request)
    {
        var parameters = new VirtualDisplayAddParams
        {
            Width = request.Width,
            Height = request.Height,
            RefreshRate = request.RefreshRate,
            MonitorGuid = request.MonitorGuid,
            DeviceName = request.DeviceName,
            SerialNumber = request.SerialNumber
        };
        bool success = NativeMethods.DeviceIoControl(
            handle,
            BuildSudoVdaControlCode(IoctlAddVirtualDisplay),
            ref parameters,
            Marshal.SizeOf<VirtualDisplayAddParams>(),
            out VirtualDisplayAddOut output,
            Marshal.SizeOf<VirtualDisplayAddOut>(),
            out _,
            IntPtr.Zero);

        return success
            ? SudoVdaVirtualDisplayCreateResult.Ok(
                output.AdapterLuid.LowPart,
                output.AdapterLuid.HighPart,
                output.TargetId)
            : SudoVdaVirtualDisplayCreateResult.Fail(
                $"SudoVDA create failed. Win32={Marshal.GetLastWin32Error()}.");
    }

    internal static SudoVdaDriverOperationResult RemoveSudoVdaVirtualDisplay(
        SafeFileHandle handle,
        Guid monitorGuid)
    {
        if (RemoveVirtualDisplay(handle, monitorGuid))
        {
            return SudoVdaDriverOperationResult.Ok();
        }

        int nativeError = Marshal.GetLastWin32Error();
        return SudoVdaDriverOperationResult.Fail(
            $"SudoVDA remove failed. Win32={nativeError}.",
            nativeError);
    }

    private sealed record VirtualDisplayCreationBaseline(
        IReadOnlyList<string> DisplayNames,
        string? Error);

    private sealed record VirtualDisplayActivationOutcome(
        DisplayApiResult Result,
        string? DisplayName);

    private sealed record LeasedVirtualDisplayState(
        string DisplayId,
        string DisplayName,
        int Width,
        int Height,
        int RefreshHz,
        VirtualDisplayAddOut AddOutput);

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

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int ChangeDisplaySettingsEx(
            string lpszDeviceName,
            ref DevMode lpDevMode,
            IntPtr hwnd,
            uint dwflags,
            IntPtr lParam);

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
            IntPtr inBuffer,
            int inBufferSize,
            out SudoVdaWatchdogOut outBuffer,
            int outBufferSize,
            out uint bytesReturned,
            IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeviceIoControl(
            SafeFileHandle device,
            uint ioControlCode,
            IntPtr inBuffer,
            int inBufferSize,
            IntPtr outBuffer,
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

        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
        public static extern uint DisplayConfigGetAdvancedColorInfo(ref DisplayConfigGetAdvancedColorInfo requestPacket);

        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
        public static extern uint DisplayConfigGetAdvancedColorInfo2(ref DisplayConfigGetAdvancedColorInfo2 requestPacket);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct SudoVdaWatchdogOut
    {
        public uint Timeout;
        public uint Countdown;
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
    private struct DisplayConfigGetAdvancedColorInfo
    {
        public DisplayConfigDeviceInfoHeader Header;
        public uint Flags;
        public uint ColorEncoding;
        public uint BitsPerColorChannel;

        public static DisplayConfigGetAdvancedColorInfo Create(Luid adapterId, uint targetId)
        {
            return new DisplayConfigGetAdvancedColorInfo
            {
                Header = new DisplayConfigDeviceInfoHeader
                {
                    Type = DisplayConfigDeviceInfoGetAdvancedColorInfo,
                    Size = checked((uint)Marshal.SizeOf<DisplayConfigGetAdvancedColorInfo>()),
                    AdapterId = adapterId,
                    Id = targetId
                }
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigGetAdvancedColorInfo2
    {
        public DisplayConfigDeviceInfoHeader Header;
        public uint Flags;
        public uint ColorEncoding;
        public uint BitsPerColorChannel;
        public uint ActiveColorMode;

        public static DisplayConfigGetAdvancedColorInfo2 Create(Luid adapterId, uint targetId)
        {
            return new DisplayConfigGetAdvancedColorInfo2
            {
                Header = new DisplayConfigDeviceInfoHeader
                {
                    Type = DisplayConfigDeviceInfoGetAdvancedColorInfo2,
                    Size = checked((uint)Marshal.SizeOf<DisplayConfigGetAdvancedColorInfo2>()),
                    AdapterId = adapterId,
                    Id = targetId
                }
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

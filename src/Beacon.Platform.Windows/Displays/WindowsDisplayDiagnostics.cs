namespace Beacon.Platform.Windows.Displays;

public static class WindowsDisplayDiagnostics
{
    public const uint AdvancedColorSupported = 0x00000001;
    public const uint AdvancedColorEnabled = 0x00000002;
    public const uint AdvancedColorForceDisabled = 0x00000008;
    public const uint HighDynamicRangeSupported = 0x00000010;
    public const uint HighDynamicRangeUserEnabled = 0x00000020;
    public const uint WideColorSupported = 0x00000040;
    public const uint WideColorUserEnabled = 0x00000080;
    public const uint AdvancedColorModeHdr = 2;

    public static DisplayHdrCapability FromAdvancedColorInfo2(
        uint flags,
        uint bitsPerColorChannel,
        uint activeColorMode)
    {
        bool hdrSupported = Has(flags, HighDynamicRangeSupported);
        bool hdrUserEnabled = Has(flags, HighDynamicRangeUserEnabled);
        bool hdrActive = activeColorMode == AdvancedColorModeHdr;

        if (!hdrSupported)
        {
            return new DisplayHdrCapability(
                Supported: false,
                Enabled: false,
                Reason: $"Windows Advanced Color 2 reports HDR unsupported. BitsPerChannel={bitsPerColorChannel}.");
        }

        if (hdrUserEnabled && hdrActive)
        {
            return new DisplayHdrCapability(
                Supported: true,
                Enabled: true,
                Reason: $"Windows Advanced Color 2 reports HDR active. BitsPerChannel={bitsPerColorChannel}.");
        }

        if (Has(flags, AdvancedColorForceDisabled))
        {
            return new DisplayHdrCapability(
                Supported: true,
                Enabled: false,
                Reason: $"Windows Advanced Color 2 reports HDR supported but limited by system policy. BitsPerChannel={bitsPerColorChannel}.");
        }

        string state = hdrUserEnabled
            ? $"user-enabled but active mode is {activeColorMode}"
            : "not user-enabled";

        return new DisplayHdrCapability(
            Supported: true,
            Enabled: false,
            Reason: $"Windows Advanced Color 2 reports HDR supported but {state}. BitsPerChannel={bitsPerColorChannel}.");
    }

    public static DisplayHdrCapability FromLegacyAdvancedColorInfo(
        uint flags,
        uint bitsPerColorChannel)
    {
        bool advancedColorSupported = Has(flags, AdvancedColorSupported);
        bool advancedColorEnabled = Has(flags, AdvancedColorEnabled);

        if (!advancedColorSupported)
        {
            return new DisplayHdrCapability(
                Supported: false,
                Enabled: false,
                Reason: $"Legacy Windows Advanced Color reports unsupported. BitsPerChannel={bitsPerColorChannel}.");
        }

        if (advancedColorEnabled)
        {
            return new DisplayHdrCapability(
                Supported: true,
                Enabled: true,
                Reason: $"Legacy Windows Advanced Color reports enabled. BitsPerChannel={bitsPerColorChannel}; HDR and WCG are not distinguished by this query.");
        }

        if (Has(flags, AdvancedColorForceDisabled))
        {
            return new DisplayHdrCapability(
                Supported: true,
                Enabled: false,
                Reason: $"Legacy Windows Advanced Color reports supported but force-disabled by policy. BitsPerChannel={bitsPerColorChannel}.");
        }

        return new DisplayHdrCapability(
            Supported: true,
            Enabled: false,
            Reason: $"Legacy Windows Advanced Color reports supported but disabled. BitsPerChannel={bitsPerColorChannel}.");
    }

    public static string? SelectPhysicalRestoreCandidate(IReadOnlyList<DisplayRestoreCandidate> candidates)
    {
        DisplayRestoreCandidate? knownPhysical = candidates.FirstOrDefault(candidate => candidate.Kind == DisplayPathKind.Physical);
        if (knownPhysical is not null)
        {
            return knownPhysical.DisplayId;
        }

        DisplayRestoreCandidate? origin = candidates.FirstOrDefault(candidate => candidate.X == 0 && candidate.Y == 0);
        if (origin?.Kind != DisplayPathKind.Virtual)
        {
            return null;
        }

        return candidates.FirstOrDefault(candidate => candidate.X != 0 || candidate.Y != 0)?.DisplayId;
    }

    public static bool RequiresExtendedTopologyRepair(
        IReadOnlyList<DisplayRestoreCandidate> activeDisplays,
        string requiredDisplayName) =>
        !activeDisplays.Any(candidate =>
            string.Equals(candidate.DisplayId, requiredDisplayName, StringComparison.OrdinalIgnoreCase)) ||
        !activeDisplays.Any(candidate => candidate.Kind == DisplayPathKind.Physical);

    public static bool ShouldRetryWithSuppliedDisplayConfig(uint topologyStatus) =>
        topologyStatus != 0;

    public static DisplayApiResult VerifyPhysicalRestore(string displayName, DisplayTopologySnapshot topology) =>
        topology.PhysicalPrimaryVerified
            ? DisplayApiResult.Ok()
            : DisplayApiResult.Fail(
                $"DisplayConfig apply reported success for {displayName}, but physical primary was not verified. LastTopology={topology.Fingerprint}.");

    private static bool Has(uint flags, uint mask) => (flags & mask) != 0;
}

public sealed record DisplayRestoreCandidate(
    string DisplayId,
    DisplayPathKind? Kind,
    bool IsPrimary,
    int X,
    int Y);

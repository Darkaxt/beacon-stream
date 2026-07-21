using System.Runtime.InteropServices;

namespace Beacon.Platform.Windows.Displays;

public sealed class WindowsUserDisplayTopologyTransition
{
    public const string CommandArgument = "--user-display-topology-extend";

    private const uint ErrorSuccess = 0;
    private const uint SdcApply = 0x00000080;
    private const uint SdcTopologyExtend = 0x00000004;
    private readonly Func<uint, uint> applyTopology;

    public WindowsUserDisplayTopologyTransition()
        : this(flags => NativeMethods.SetDisplayConfig(
            0,
            IntPtr.Zero,
            0,
            IntPtr.Zero,
            flags))
    {
    }

    internal WindowsUserDisplayTopologyTransition(Func<uint, uint> applyTopology)
    {
        this.applyTopology = applyTopology;
    }

    public DisplayApiResult ApplyExtended()
    {
        uint status = applyTopology(SdcApply | SdcTopologyExtend);
        return status == ErrorSuccess
            ? DisplayApiResult.Ok()
            : DisplayApiResult.Fail(
                $"Unable to apply the Windows extended display topology from the user session. Result={status}.");
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern uint SetDisplayConfig(
            uint numPathArrayElements,
            IntPtr pathArray,
            uint numModeInfoArrayElements,
            IntPtr modeInfoArray,
            uint flags);
    }
}

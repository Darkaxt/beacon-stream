using System.Text;
using Beacon.Core.Displays;
using Beacon.Platform.Windows.Displays;

namespace Beacon.DisplayProbe;

public static class DisplayProbeFormatter
{
    public static string FormatStatus(DisplayDriverStatus driverStatus, DisplayTopologySnapshot topology)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"driverReady={driverStatus.Ready} diagnostic={driverStatus.Diagnostic}");
        builder.AppendLine($"mirrorMode={topology.IsMirrorMode} physicalPrimaryVerified={topology.PhysicalPrimaryVerified}");

        foreach (DisplayPathSnapshot path in topology.Paths)
        {
            builder.AppendLine(
                $"display={path.DisplayId} kind={path.Kind} {path.Width}x{path.Height}@{path.RefreshHz} primary={path.IsPrimary} x={path.X} y={path.Y}");
        }

        return builder.ToString();
    }

    public static string FormatApiResult(string operation, DisplayApiResult result) =>
        result.Success
            ? $"{operation}: success"
            : $"{operation}: failed: {result.Error}";

    public static string FormatEnsureResult(DisplayEnsureResult result) =>
        result.Success
            ? $"ensure: success hdrEnabled={result.HdrEnabled} hdrReason={result.HdrReason}"
            : $"ensure: failed: {result.Error}";

    public static string FormatPrepareResult(DisplayEnsureResult result) =>
        result.Success
            ? $"prepare: success hdrEnabled={result.HdrEnabled} hdrReason={result.HdrReason}"
            : $"prepare: failed: {result.Error}";

    public static string FormatRestoreResult(DisplayRestoreResult result) =>
        result.Success
            ? "restore-physical: success verified=True"
            : $"restore-physical: failed: {result.Error}";

    public static string FormatRecoveryResult(DisplayRecoveryResult result) =>
        result.Success
            ? "recover: success"
            : $"recover: failed: {result.Error}";
}

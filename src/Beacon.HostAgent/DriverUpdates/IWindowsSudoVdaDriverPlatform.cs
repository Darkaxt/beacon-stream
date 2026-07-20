using Beacon.Platform.Windows.Displays;

namespace Beacon.HostAgent.DriverUpdates;

internal sealed record SudoVdaInstalledDevice(
    string DeviceInstanceId,
    string HardwareId,
    string PublishedInf,
    string DriverVersion,
    string Provider,
    string ActiveBinaryPath,
    bool DeviceHealthy);

internal interface ISudoVdaDeviceInventory
{
    SudoVdaInstalledDevice QueryActiveDevice();
}

internal interface ISudoVdaDisplayUpdateGuard
{
    DisplayDriverStatus GetDriverStatus();

    int GetActiveLeaseCount();

    Task<DisplayTopologySnapshot> QueryTopologyAsync();
}

internal sealed record PnpUtilResult(int ExitCode, string StandardOutput, string StandardError);

internal interface IPnpUtilRunner
{
    Task<PnpUtilResult> RunAsync(IReadOnlyList<string> arguments);
}

internal sealed class HostAgentSudoVdaDisplayUpdateGuard(IHostAgentDisplayExecutor displays)
    : ISudoVdaDisplayUpdateGuard
{
    public DisplayDriverStatus GetDriverStatus() => displays.GetDriverStatus();

    public int GetActiveLeaseCount() => displays.GetLeaseSnapshot().LeaseCount;

    public Task<DisplayTopologySnapshot> QueryTopologyAsync() =>
        displays.QueryAsync(CancellationToken.None);
}

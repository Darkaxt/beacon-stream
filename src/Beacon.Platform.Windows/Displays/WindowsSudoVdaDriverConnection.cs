using Microsoft.Win32.SafeHandles;

namespace Beacon.Platform.Windows.Displays;

internal sealed class WindowsSudoVdaDriverConnectionFactory : ISudoVdaDriverConnectionFactory
{
    public SudoVdaDriverConnectionOpenResult Open()
    {
        SafeFileHandle? handle = WindowsDisplayApi.OpenSudoVdaDevice(out string diagnostic);
        return handle is null
            ? SudoVdaDriverConnectionOpenResult.Fail(diagnostic)
            : SudoVdaDriverConnectionOpenResult.Ok(new WindowsSudoVdaDriverConnection(handle));
    }
}

internal sealed class WindowsSudoVdaDriverConnection(SafeFileHandle handle) : ISudoVdaDriverConnection
{
    public SudoVdaWatchdogQueryResult QueryWatchdog() =>
        WindowsDisplayApi.QuerySudoVdaWatchdog(handle);

    public SudoVdaDriverOperationResult Ping() =>
        WindowsDisplayApi.PingSudoVdaDriver(handle);

    public void Dispose() => handle.Dispose();
}

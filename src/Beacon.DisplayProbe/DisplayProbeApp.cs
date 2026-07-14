using Beacon.Core.Clients;
using Beacon.Core.Displays;
using Beacon.Platform.Windows.Displays;

namespace Beacon.DisplayProbe;

public static class DisplayProbeApp
{
    public static async Task<int> RunAsync(
        IWindowsDisplayApi api,
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter error)
    {
        try
        {
            DisplayProbeCommand command = DisplayProbeCommandLine.Parse(args);
            switch (command)
            {
                case StatusDisplayProbeCommand:
                    DisplayDriverStatus driverStatus = api.GetDriverStatus();
                    DisplayTopologySnapshot topology = await api.QueryTopologyAsync(CancellationToken.None);
                    output.Write(DisplayProbeFormatter.FormatStatus(driverStatus, topology));
                    return 0;

                case DriverSessionDisplayProbeCommand:
                    if (api is not IWindowsDisplayLeaseSession driverSession)
                    {
                        error.WriteLine("The active display API does not expose a SudoVDA driver session.");
                        return 2;
                    }

                    const string probeDisplayId = "probe-driver-session";
                    SudoVdaDriverLeaseHoldResult holdResult = await driverSession.HoldAsync(
                        probeDisplayId,
                        CancellationToken.None);
                    if (!holdResult.Success)
                    {
                        output.WriteLine($"driver-session: failed - {holdResult.Error}");
                        return 2;
                    }

                    try
                    {
                        SudoVdaDriverLeaseSessionSnapshot snapshot = driverSession.Snapshot;
                        string watchdog = snapshot.WatchdogTimeoutSeconds is uint timeoutSeconds
                            ? $"{timeoutSeconds}s"
                            : "unknown";
                        output.WriteLine(
                            $"driver-session: success leases={snapshot.LeaseCount} watchdog={watchdog} " +
                            $"heartbeat={(snapshot.HeartbeatActive ? "active" : "not-required")} " +
                            $"healthy={snapshot.Healthy.ToString().ToLowerInvariant()} - {snapshot.Diagnostic}");
                        return snapshot.Healthy ? 0 : 2;
                    }
                    finally
                    {
                        await driverSession.ReleaseAsync(probeDisplayId, CancellationToken.None);
                    }

                case PrepareDisplayProbeCommand prepare:
                    var prepareBackend = new WindowsDisplayBackend(api);
                    DisplayEnsureResult prepareResult = await prepareBackend.PrepareVirtualDisplayAsync(
                        ToDisplayId(prepare.ClientId),
                        prepare.Width,
                        prepare.Height,
                        prepare.RefreshHz,
                        ParseHdrPreference(prepare.Hdr),
                        CancellationToken.None);
                    output.WriteLine(DisplayProbeFormatter.FormatPrepareResult(prepareResult));
                    return prepareResult.Success ? 0 : 2;

                case EnsureDisplayProbeCommand ensure:
                    var ensureBackend = new WindowsDisplayBackend(api);
                    DisplayEnsureResult ensureResult = await ensureBackend.EnsureVirtualDisplayAsync(
                        ToDisplayId(ensure.ClientId),
                        ensure.Width,
                        ensure.Height,
                        ensure.RefreshHz,
                        ParseHdrPreference(ensure.Hdr),
                        CancellationToken.None);
                    output.WriteLine(DisplayProbeFormatter.FormatEnsureResult(ensureResult));
                    return ensureResult.Success ? 0 : 2;

                case PrimaryDisplayProbeCommand primary:
                    DisplayApiResult primaryResult = await api.SetVirtualPrimaryAsync(
                        ToDisplayId(primary.ClientId),
                        CancellationToken.None);
                    output.WriteLine(DisplayProbeFormatter.FormatApiResult("primary", primaryResult));
                    return primaryResult.Success ? 0 : 2;

                case RestorePhysicalDisplayProbeCommand:
                    var restoreBackend = new WindowsDisplayBackend(api);
                    DisplayRestoreResult restoreResult = await restoreBackend.RestorePhysicalPrimaryAsync(CancellationToken.None);
                    output.WriteLine(DisplayProbeFormatter.FormatRestoreResult(restoreResult));
                    return restoreResult.Success ? 0 : 2;

                case RecoverDisplayProbeCommand recover:
                    var recoveryBackend = new WindowsDisplayBackend(api);
                    var leases = new DisplayLeaseManager(recoveryBackend);
                    DisplayRecoveryResult recoveryResult = await leases.RecoverDisplayAsync(
                        ToDisplayId(recover.ClientId),
                        CancellationToken.None);
                    output.WriteLine(DisplayProbeFormatter.FormatRecoveryResult(recoveryResult));
                    return recoveryResult.Success ? 0 : 2;

                case RemoveDisplayProbeCommand remove:
                    DisplayApiResult removeResult = await api.RemoveVirtualDisplayAsync(
                        ToDisplayId(remove.ClientId),
                        CancellationToken.None);
                    output.WriteLine(DisplayProbeFormatter.FormatApiResult("remove", removeResult));
                    return removeResult.Success ? 0 : 2;

                default:
                    error.WriteLine($"Unsupported display probe command {command.GetType().Name}.");
                    return 2;
            }
        }
        catch (ArgumentException ex)
        {
            error.WriteLine(ex.Message);
            return 2;
        }
    }

    private static string ToDisplayId(string clientId) =>
        DisplayLease.CreateDisplayId(new ClientId(clientId));

    private static HdrPreference ParseHdrPreference(string value) =>
        Enum.TryParse(value, ignoreCase: true, out HdrPreference preference)
            ? preference
            : HdrPreference.Prefer;
}

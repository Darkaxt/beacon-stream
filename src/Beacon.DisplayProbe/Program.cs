using Beacon.Core.Clients;
using Beacon.Core.Displays;
using Beacon.DisplayProbe;
using Beacon.Platform.Windows.Displays;

IWindowsDisplayApi api = new WindowsDisplayApi();

try
{
    DisplayProbeCommand command = DisplayProbeCommandLine.Parse(args);
    switch (command)
    {
        case StatusDisplayProbeCommand:
            DisplayDriverStatus driverStatus = api.GetDriverStatus();
            DisplayTopologySnapshot topology = await api.QueryTopologyAsync(CancellationToken.None);
            Console.Write(DisplayProbeFormatter.FormatStatus(driverStatus, topology));
            return 0;

        case EnsureDisplayProbeCommand ensure:
            var backend = new WindowsDisplayBackend(api);
            DisplayEnsureResult ensureResult = await backend.EnsureVirtualDisplayAsync(
                ToDisplayId(ensure.ClientId),
                ensure.Width,
                ensure.Height,
                ensure.RefreshHz,
                ParseHdrPreference(ensure.Hdr),
                CancellationToken.None);
            Console.WriteLine(DisplayProbeFormatter.FormatEnsureResult(ensureResult));
            return ensureResult.Success ? 0 : 2;

        case PrimaryDisplayProbeCommand primary:
            DisplayApiResult primaryResult = await api.SetVirtualPrimaryAsync(
                ToDisplayId(primary.ClientId),
                CancellationToken.None);
            Console.WriteLine(DisplayProbeFormatter.FormatApiResult("primary", primaryResult));
            return primaryResult.Success ? 0 : 2;

        case RestorePhysicalDisplayProbeCommand:
            DisplayApiResult restoreResult = await api.RestorePhysicalPrimaryAsync(CancellationToken.None);
            Console.WriteLine(DisplayProbeFormatter.FormatApiResult("restore-physical", restoreResult));
            return restoreResult.Success ? 0 : 2;

        case RemoveDisplayProbeCommand remove:
            DisplayApiResult removeResult = await api.RemoveVirtualDisplayAsync(
                ToDisplayId(remove.ClientId),
                CancellationToken.None);
            Console.WriteLine(DisplayProbeFormatter.FormatApiResult("remove", removeResult));
            return removeResult.Success ? 0 : 2;

        default:
            Console.Error.WriteLine($"Unsupported display probe command {command.GetType().Name}.");
            return 2;
    }
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

static string ToDisplayId(string clientId) =>
    DisplayLease.CreateDisplayId(new ClientId(clientId));

static HdrPreference ParseHdrPreference(string value) =>
    Enum.TryParse(value, ignoreCase: true, out HdrPreference preference)
        ? preference
        : HdrPreference.Prefer;

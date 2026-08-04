using Beacon.Platform.Windows.Displays;
using Beacon.Platform.Windows.Games;
using Beacon.Platform.Windows.Sessions;
using Beacon.Platform.Windows.Streaming;

namespace Beacon.Server.Hosting;

public sealed record BeaconHostOptions(
    string ModeName,
    string DisplayBackendName,
    string GameLauncherName,
    string ActivityInspectorName,
    string StreamingBackendName)
{
    public static BeaconHostOptions Production { get; } =
        new(
            "windows",
            nameof(WindowsDisplayBackend),
            nameof(WindowsGameLauncher),
            nameof(WindowsSessionActivityInspector),
            nameof(StreamWorkerStreamingBackend));
}

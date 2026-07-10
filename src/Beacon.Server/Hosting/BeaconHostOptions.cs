using Beacon.Core.Displays;
using Beacon.Core.Games;
using Beacon.Core.Sessions;
using Beacon.Core.Streaming;
using Beacon.Platform.Windows.Displays;
using Beacon.Platform.Windows.Games;
using Beacon.Platform.Windows.Sessions;

namespace Beacon.Server.Hosting;

public sealed record BeaconHostOptions(
    BeaconHostMode Mode,
    string DisplayBackendName,
    string GameLauncherName,
    string ActivityInspectorName,
    string StreamingBackendName)
{
    public string ModeName => Mode.ToString().ToLowerInvariant();

    public static BeaconHostOptions Create(BeaconHostMode mode) => mode switch
    {
        BeaconHostMode.Fake => new BeaconHostOptions(
            BeaconHostMode.Fake,
            nameof(FakeDisplayBackend),
            nameof(FakeGameLauncher),
            nameof(FakeSessionActivityInspector),
            nameof(FakeStreamingBackend)),
        BeaconHostMode.Windows => new BeaconHostOptions(
            BeaconHostMode.Windows,
            nameof(WindowsDisplayBackend),
            nameof(WindowsGameLauncher),
            nameof(WindowsSessionActivityInspector),
            nameof(UnavailableStreamingBackend)),
        _ => throw new ArgumentOutOfRangeException(
            nameof(mode),
            mode,
            "Unsupported Beacon host mode.")
    };
}

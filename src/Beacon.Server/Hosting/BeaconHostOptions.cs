using Beacon.Core.Displays;
using Beacon.Core.Games;
using Beacon.Core.Sessions;
using Beacon.Core.Streaming;
using Beacon.Platform.Windows.Displays;
using Beacon.Platform.Windows.Games;
using Beacon.Platform.Windows.Sessions;
using Beacon.Platform.Windows.Streaming;

namespace Beacon.Server.Hosting;

public sealed record BeaconHostOptions(
    BeaconHostMode Mode,
    BeaconStreamingBackendMode StreamingBackendMode,
    string DisplayBackendName,
    string GameLauncherName,
    string ActivityInspectorName,
    string StreamingBackendName)
{
    public string ModeName => Mode.ToString().ToLowerInvariant();

    public string StreamingBackendModeName => StreamingBackendMode switch
    {
        BeaconStreamingBackendMode.Fake => "fake",
        BeaconStreamingBackendMode.ExternalProcess => "external-process",
        BeaconStreamingBackendMode.BeaconTest => "beacon-test",
        _ => StreamingBackendMode.ToString().ToLowerInvariant()
    };

    public static BeaconHostOptions Create(BeaconHostMode mode, BeaconStreamingBackendMode streamingBackendMode)
    {
        string streamingBackendName = streamingBackendMode switch
        {
            BeaconStreamingBackendMode.Fake => nameof(FakeStreamingBackend),
            BeaconStreamingBackendMode.ExternalProcess => nameof(ExternalProcessStreamingBackend),
            BeaconStreamingBackendMode.BeaconTest => nameof(BeaconTestStreamingBackend),
            _ => throw new ArgumentOutOfRangeException(nameof(streamingBackendMode), streamingBackendMode, "Unsupported Beacon streaming backend mode.")
        };

        return mode switch
        {
            BeaconHostMode.Fake => new BeaconHostOptions(
                BeaconHostMode.Fake,
                streamingBackendMode,
                nameof(FakeDisplayBackend),
                nameof(FakeGameLauncher),
                nameof(FakeSessionActivityInspector),
                streamingBackendName),
            BeaconHostMode.Windows => new BeaconHostOptions(
                BeaconHostMode.Windows,
                streamingBackendMode,
                nameof(WindowsDisplayBackend),
                nameof(WindowsGameLauncher),
                nameof(WindowsSessionActivityInspector),
                streamingBackendName),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported Beacon host mode.")
        };
    }
}

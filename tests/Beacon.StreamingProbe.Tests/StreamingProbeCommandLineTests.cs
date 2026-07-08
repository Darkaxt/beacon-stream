using Beacon.StreamingProbe;

namespace Beacon.StreamingProbe.Tests;

public sealed class StreamingProbeCommandLineTests
{
    [Fact]
    public void ParseUsesBackendArgumentsAndEnvironmentFallbacks()
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["BEACON_DISPLAY_ID"] = "client-z-fold-7",
            ["BEACON_STREAM_SESSION_DESCRIPTOR_PATH"] = "C:\\Beacon\\Runtime\\session.json",
            ["BEACON_CONNECTION_PROTOCOL"] = "gamestream",
            ["BEACON_CONNECTION_LAUNCH_URI"] = "moonlight://beacon/runtime/session",
            ["BEACON_CONNECTION_ENDPOINTS"] = "input=udp://127.0.0.1:48000;rtsp=rtsp://127.0.0.1:48010/beacon"
        };

        StreamingProbeCommand command = StreamingProbeCommandLine.Parse(
            ["--session", "z-fold-7-steam-shortcut:3767414131", "--once"],
            environment);

        Assert.Equal("z-fold-7-steam-shortcut:3767414131", command.SessionId);
        Assert.Equal("client-z-fold-7", command.DisplayId);
        Assert.Equal("C:\\Beacon\\Runtime\\session.json", command.DescriptorPath);
        Assert.Equal("gamestream", command.Protocol);
        Assert.Equal("moonlight://beacon/runtime/session", command.LaunchUri);
        Assert.Equal("udp://127.0.0.1:48000", command.Endpoints["input"]);
        Assert.Equal("rtsp://127.0.0.1:48010/beacon", command.Endpoints["rtsp"]);
        Assert.True(command.Once);
    }

    [Fact]
    public void ParseCreatesDeterministicDefaultsForLoopbackProbe()
    {
        StreamingProbeCommand command = StreamingProbeCommandLine.Parse(
            [
                "--session",
                "z-fold-7-steam-shortcut:3767414131",
                "--display",
                "client-z-fold-7",
                "--stream-session-descriptor",
                "C:\\Beacon\\Runtime\\session.json"
            ],
            new Dictionary<string, string>());

        Assert.Equal("gamestream", command.Protocol);
        Assert.Equal("moonlight://beacon/probe/z-fold-7-steam-shortcut%3A3767414131", command.LaunchUri);
        Assert.Equal("udp://127.0.0.1:48000", command.Endpoints["input"]);
        Assert.Equal("rtsp://127.0.0.1:48010/beacon", command.Endpoints["rtsp"]);
        Assert.False(command.Once);
    }
}

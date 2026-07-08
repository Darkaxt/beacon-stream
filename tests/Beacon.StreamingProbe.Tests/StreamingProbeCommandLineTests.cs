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
            ["BEACON_CONNECTION_ENDPOINTS"] = "input=udp://127.0.0.1:48000;rtsp=rtsp://127.0.0.1:48010/beacon",
            ["BEACON_WRAPPER_CHILD_EXECUTABLE"] = "C:\\Tools\\sunshine.exe",
            ["BEACON_WRAPPER_CHILD_ARGUMENTS"] = "--config sunshine.json"
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
        Assert.Equal("C:\\Tools\\sunshine.exe", command.ChildExecutable);
        Assert.Equal("--config sunshine.json", command.ChildArguments);
        Assert.NotNull(command.ChildEnvironment);
        Assert.Equal("client-z-fold-7", command.ChildEnvironment["BEACON_DISPLAY_ID"]);
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
        Assert.Null(command.ChildExecutable);
        Assert.Null(command.ChildArguments);
    }

    [Fact]
    public void ParseLetsChildProcessArgumentsOverrideEnvironment()
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["BEACON_DISPLAY_ID"] = "client-z-fold-7",
            ["BEACON_STREAM_SESSION_DESCRIPTOR_PATH"] = "C:\\Beacon\\Runtime\\session.json",
            ["BEACON_WRAPPER_CHILD_EXECUTABLE"] = "C:\\Tools\\sunshine-from-env.exe",
            ["BEACON_WRAPPER_CHILD_ARGUMENTS"] = "--env"
        };

        StreamingProbeCommand command = StreamingProbeCommandLine.Parse(
            [
                "--session",
                "session",
                "--child-executable",
                "C:\\Tools\\sunshine-from-args.exe",
                "--child-arguments",
                "--args"
            ],
            environment);

        Assert.Equal("C:\\Tools\\sunshine-from-args.exe", command.ChildExecutable);
        Assert.Equal("--args", command.ChildArguments);
    }
}

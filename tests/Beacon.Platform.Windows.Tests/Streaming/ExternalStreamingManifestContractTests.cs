using Beacon.Platform.Windows.Streaming;

namespace Beacon.Platform.Windows.Tests.Streaming;

public sealed class ExternalStreamingManifestContractTests
{
    [Fact]
    public void DocumentedExampleManifestMatchesWindowsReaderContract()
    {
        string root = FindRepositoryRoot();
        string manifestPath = Path.Combine(root, "docs", "examples", "external-streaming-manifest.example.json");
        var reader = new WindowsExternalStreamingManifestReader();

        Assert.True(reader.FileExists(manifestPath), $"Missing documented manifest example at {manifestPath}.");

        ExternalStreamingManifestReadResult result = reader.Read(manifestPath);

        Assert.True(result.Success, result.Error);
        ExternalStreamingManifest manifest = Assert.IsType<ExternalStreamingManifest>(result.Manifest);
        Assert.Equal("Beacon Sunshine-compatible wrapper", manifest.Name);
        Assert.Equal("gamestream", manifest.Protocol);
        Assert.Equal("moonlight://beacon/session", manifest.LaunchUri);
        Assert.NotNull(manifest.Endpoints);
        Assert.Equal("rtsp://127.0.0.1:48010/beacon", manifest.Endpoints["rtsp"]);
        Assert.Equal("udp://127.0.0.1:48000", manifest.Endpoints["input"]);
        Assert.Equal(["av1", "hevc", "h264"], manifest.Codecs);
        Assert.Equal(120, manifest.MaxFps);
        Assert.Equal(150, manifest.MaxBitrateMbps);
        Assert.True(manifest.Hdr10);
        Assert.Equal(["lan-direct"], manifest.Transports);
        Assert.Equal(["nvenc", "amf", "qsv"], manifest.Encoders);
        Assert.Equal(["dxgi", "windows-graphics-capture"], manifest.Capture);
        Assert.NotNull(manifest.Diagnostics);
        Assert.Contains("ready", manifest.Diagnostics);
    }

    [Fact]
    public void DocumentedRuntimeSessionDescriptorMatchesWindowsReaderContract()
    {
        string root = FindRepositoryRoot();
        string descriptorPath = Path.Combine(root, "docs", "examples", "external-streaming-runtime-session.example.json");
        var store = new WindowsExternalStreamingSessionDescriptorStore(Path.GetDirectoryName(descriptorPath)!);

        Assert.True(File.Exists(descriptorPath), $"Missing documented runtime session descriptor example at {descriptorPath}.");

        ExternalStreamingSessionDescriptorReadResult result = store.Read(descriptorPath);

        Assert.True(result.Success, result.Error);
        ExternalStreamingSessionDescriptor descriptor = Assert.IsType<ExternalStreamingSessionDescriptor>(result.Descriptor);
        Assert.Equal("gamestream", descriptor.Protocol);
        Assert.Equal("moonlight://beacon/runtime/z-fold-7-steam-shortcut:3767414131", descriptor.LaunchUri);
        Assert.NotNull(descriptor.Endpoints);
        Assert.Equal("rtsp://127.0.0.1:48010/beacon-runtime", descriptor.Endpoints["rtsp"]);
        Assert.Equal("udp://127.0.0.1:48000", descriptor.Endpoints["input"]);
        Assert.NotNull(descriptor.Metadata);
        Assert.Equal("already-paired", descriptor.Metadata["pairing"]);
        Assert.NotNull(descriptor.Diagnostics);
        Assert.Contains("runtime descriptor ready", descriptor.Diagnostics);
        Assert.NotNull(descriptor.NativeSession);
        Assert.True(descriptor.NativeSession.Validate().Success);
        Assert.Equal(2560, descriptor.NativeSession.Width);
        Assert.Equal(1600, descriptor.NativeSession.Height);
        Assert.Equal(120, descriptor.NativeSession.Fps);
        Assert.Equal("hevc-main10", descriptor.NativeSession.VideoFormat);
        Assert.Equal("AAECAwQFBgcICQoLDA0ODw==", descriptor.NativeSession.RemoteInputAesKey);
        Assert.Equal("EBESExQVFhcYGRobHB0eHw==", descriptor.NativeSession.RemoteInputAesIv);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Beacon.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}

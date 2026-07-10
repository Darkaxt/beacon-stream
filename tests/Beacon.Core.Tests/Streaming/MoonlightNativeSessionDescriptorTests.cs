using Beacon.Core.Streaming;

namespace Beacon.Core.Tests.Streaming;

public sealed class MoonlightNativeSessionDescriptorTests
{
    [Fact]
    public void ValidDescriptorPassesStrictValidation()
    {
        MoonlightNativeSessionValidationResult result = CreateDescriptor().Validate();

        Assert.True(result.Success, result.Error);
    }

    [Fact]
    public void ValidationRejectsRemoteInputKeyWithWrongDecodedLength()
    {
        MoonlightNativeSessionDescriptor descriptor = CreateDescriptor() with
        {
            RemoteInputAesKey = Convert.ToBase64String(new byte[15])
        };

        MoonlightNativeSessionValidationResult result = descriptor.Validate();

        Assert.False(result.Success);
        Assert.Contains("RemoteInputAesKey", result.Error, StringComparison.Ordinal);
        Assert.Contains("16 bytes", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidationRejectsUnsupportedVideoFormat()
    {
        MoonlightNativeSessionValidationResult result = (CreateDescriptor() with
        {
            VideoFormat = "vp9"
        }).Validate();

        Assert.False(result.Success);
        Assert.Contains("VideoFormat", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void StringRepresentationRedactsKeyMaterial()
    {
        MoonlightNativeSessionDescriptor descriptor = CreateDescriptor();

        string value = descriptor.ToString();

        Assert.DoesNotContain(descriptor.RemoteInputAesKey, value, StringComparison.Ordinal);
        Assert.DoesNotContain(descriptor.RemoteInputAesIv, value, StringComparison.Ordinal);
        Assert.Contains("[redacted]", value, StringComparison.Ordinal);
    }

    private static MoonlightNativeSessionDescriptor CreateDescriptor() =>
        new(
            Address: "192.168.8.10",
            ServerAppVersion: "7.1.431.0",
            ServerGfeVersion: "3.28.0.417",
            RtspSessionUrl: "rtsp://192.168.8.10:48010/session/42",
            ServerCodecModeSupport: 0x20200,
            Width: 2560,
            Height: 1600,
            Fps: 120,
            BitrateKbps: 65_000,
            PacketSize: 1392,
            StreamingMode: "local",
            AudioConfiguration: "stereo",
            VideoFormat: "h264",
            ClientRefreshRateX100: 12_000,
            ColorSpace: "rec709",
            ColorRange: "limited",
            EncryptionMode: "audio",
            RemoteInputAesKey: Convert.ToBase64String(Enumerable.Range(0, 16).Select(value => (byte)value).ToArray()),
            RemoteInputAesIv: Convert.ToBase64String(Enumerable.Range(16, 16).Select(value => (byte)value).ToArray()));
}

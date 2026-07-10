namespace Beacon.Core.Streaming;

public sealed record MoonlightNativeSessionDescriptor(
    string Address,
    string ServerAppVersion,
    string? ServerGfeVersion,
    string RtspSessionUrl,
    int ServerCodecModeSupport,
    int Width,
    int Height,
    int Fps,
    int BitrateKbps,
    int PacketSize,
    string StreamingMode,
    string AudioConfiguration,
    string VideoFormat,
    int ClientRefreshRateX100,
    string ColorSpace,
    string ColorRange,
    string EncryptionMode,
    string RemoteInputAesKey,
    string RemoteInputAesIv)
{
    private static readonly HashSet<string> StreamingModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "local",
        "remote",
        "auto"
    };

    private static readonly HashSet<string> AudioConfigurations = new(StringComparer.OrdinalIgnoreCase)
    {
        "stereo",
        "5.1",
        "7.1"
    };

    private static readonly HashSet<string> VideoFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "h264",
        "h264-high8-444",
        "hevc",
        "hevc-main10",
        "hevc-rext8-444",
        "hevc-rext10-444",
        "av1-main8",
        "av1-main10",
        "av1-high8-444",
        "av1-high10-444"
    };

    private static readonly HashSet<string> ColorSpaces = new(StringComparer.OrdinalIgnoreCase)
    {
        "rec601",
        "rec709",
        "rec2020"
    };

    private static readonly HashSet<string> ColorRanges = new(StringComparer.OrdinalIgnoreCase)
    {
        "limited",
        "full"
    };

    private static readonly HashSet<string> EncryptionModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "none",
        "audio",
        "video",
        "all"
    };

    public MoonlightNativeSessionValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(Address))
        {
            return Fail(nameof(Address), "is required");
        }

        if (string.IsNullOrWhiteSpace(ServerAppVersion))
        {
            return Fail(nameof(ServerAppVersion), "is required");
        }

        if (!Uri.TryCreate(RtspSessionUrl, UriKind.Absolute, out Uri? rtspUri)
            || !rtspUri.Scheme.Equals("rtsp", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(rtspUri.Host))
        {
            return Fail(nameof(RtspSessionUrl), "must be an absolute rtsp:// URI with a host");
        }

        if (ServerCodecModeSupport <= 0)
        {
            return Fail(nameof(ServerCodecModeSupport), "must be positive");
        }

        MoonlightNativeSessionValidationResult dimensions = ValidatePositive(Width, nameof(Width));
        if (!dimensions.Success)
        {
            return dimensions;
        }

        dimensions = ValidatePositive(Height, nameof(Height));
        if (!dimensions.Success)
        {
            return dimensions;
        }

        dimensions = ValidatePositive(Fps, nameof(Fps));
        if (!dimensions.Success)
        {
            return dimensions;
        }

        dimensions = ValidatePositive(BitrateKbps, nameof(BitrateKbps));
        if (!dimensions.Success)
        {
            return dimensions;
        }

        if (PacketSize <= 0 || PacketSize > ushort.MaxValue)
        {
            return Fail(nameof(PacketSize), $"must be between 1 and {ushort.MaxValue}");
        }

        if (!StreamingModes.Contains(StreamingMode ?? string.Empty))
        {
            return Fail(nameof(StreamingMode), "must be local, remote, or auto");
        }

        if (!AudioConfigurations.Contains(AudioConfiguration ?? string.Empty))
        {
            return Fail(nameof(AudioConfiguration), "must be stereo, 5.1, or 7.1");
        }

        if (!VideoFormats.Contains(VideoFormat ?? string.Empty))
        {
            return Fail(nameof(VideoFormat), "is not supported");
        }

        if (ClientRefreshRateX100 <= 0)
        {
            return Fail(nameof(ClientRefreshRateX100), "must be positive");
        }

        if (!ColorSpaces.Contains(ColorSpace ?? string.Empty))
        {
            return Fail(nameof(ColorSpace), "must be rec601, rec709, or rec2020");
        }

        if (!ColorRanges.Contains(ColorRange ?? string.Empty))
        {
            return Fail(nameof(ColorRange), "must be limited or full");
        }

        if (!EncryptionModes.Contains(EncryptionMode ?? string.Empty))
        {
            return Fail(nameof(EncryptionMode), "must be none, audio, video, or all");
        }

        MoonlightNativeSessionValidationResult key = ValidateKeyMaterial(RemoteInputAesKey, nameof(RemoteInputAesKey));
        if (!key.Success)
        {
            return key;
        }

        return ValidateKeyMaterial(RemoteInputAesIv, nameof(RemoteInputAesIv));
    }

    public override string ToString() =>
        $"MoonlightNativeSessionDescriptor {{ Address = {Address}, RtspSessionUrl = {RtspSessionUrl}, " +
        $"Video = {Width}x{Height}@{Fps} {VideoFormat}, RemoteInputAesKey = [redacted], " +
        "RemoteInputAesIv = [redacted] }";

    private static MoonlightNativeSessionValidationResult ValidatePositive(int value, string field) =>
        value > 0 ? MoonlightNativeSessionValidationResult.Ok() : Fail(field, "must be positive");

    private static MoonlightNativeSessionValidationResult ValidateKeyMaterial(string? value, string field)
    {
        Span<byte> decoded = stackalloc byte[16];
        return !string.IsNullOrWhiteSpace(value)
            && Convert.TryFromBase64String(value, decoded, out int bytesWritten)
            && bytesWritten == decoded.Length
            ? MoonlightNativeSessionValidationResult.Ok()
            : Fail(field, "must be Base64 that decodes to exactly 16 bytes");
    }

    private static MoonlightNativeSessionValidationResult Fail(string field, string message) =>
        MoonlightNativeSessionValidationResult.Fail($"{field} {message}.");
}

public sealed record MoonlightNativeSessionValidationResult(bool Success, string? Error)
{
    public static MoonlightNativeSessionValidationResult Ok() => new(true, null);

    public static MoonlightNativeSessionValidationResult Fail(string error) => new(false, error);
}

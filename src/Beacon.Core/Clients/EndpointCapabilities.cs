namespace Beacon.Core.Clients;

public sealed record EndpointCapabilities(
    bool Av1,
    bool Hevc,
    bool H264,
    bool Hdr10,
    bool VirtualDisplayHdrSupported,
    int MaxFps = 120,
    bool LowLatencyDecode = true,
    ClientDisplayMode? CurrentDisplayMode = null,
    IReadOnlyList<ClientDisplayMode>? SupportedDisplayModes = null);

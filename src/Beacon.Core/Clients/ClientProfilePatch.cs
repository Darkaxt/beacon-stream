using Beacon.Core.Displays;

namespace Beacon.Core.Clients;

public sealed record ClientProfilePatch(
    int? PreferredWidth,
    int? PreferredHeight,
    int? PreferredRefreshHz,
    HdrPreference? HdrPreference,
    string? CodecPreference,
    string? QualityMode,
    int? BitrateCapMbps,
    string? AudioMode,
    bool? KeepAppRunningOnDisconnect);

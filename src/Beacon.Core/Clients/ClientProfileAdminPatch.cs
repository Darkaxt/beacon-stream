using Beacon.Core.Displays;

namespace Beacon.Core.Clients;

public sealed record ClientProfileAdminPatch(
    int? PreferredWidth = null,
    int? PreferredHeight = null,
    int? PreferredRefreshHz = null,
    HdrPreference? HdrPreference = null,
    string? Mode = null,
    bool? RestorePhysicalDisplayOnEnd = null,
    bool? ForbidMirrorMode = null,
    string? CodecPreference = null,
    string? QualityMode = null,
    int? BitrateCapMbps = null,
    string? AudioMode = null,
    bool? KeepAppRunningOnDisconnect = null,
    bool? AllowEmergencyRestoreFromClient = null);

namespace Beacon.Core.Displays;

public sealed record DisplayPreferences(
    int PreferredWidth,
    int PreferredHeight,
    int PreferredRefreshHz,
    HdrPreference HdrPreference,
    string Mode,
    bool RestorePhysicalDisplayOnEnd,
    bool ForbidMirrorMode);

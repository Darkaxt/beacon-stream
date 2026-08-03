using Beacon.Core.Clients;

namespace Beacon.Core.Displays;

public sealed record DisplayPreferences(
    ClientDisplayMode? PreferredMode,
    ClientDisplayMode? SelectedMode,
    HdrPreference HdrPreference,
    string Mode,
    bool RestorePhysicalDisplayOnEnd,
    bool ForbidMirrorMode);

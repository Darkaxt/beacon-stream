using Beacon.Core.Displays;

namespace Beacon.Core.Clients;

public sealed record StreamPreferences(string QualityMode, string CodecPreference, int? BitrateCapMbps);

public sealed record AudioPreferences(string Mode);

public sealed record SessionPreferences(bool KeepAppRunningOnDisconnect, bool AllowEmergencyRestoreFromClient);

public sealed record ClientProfile(
    ClientId ClientId,
    string Name,
    DisplayPreferences Display,
    StreamPreferences Stream,
    AudioPreferences Audio,
    SessionPreferences Session)
{
    public static ClientProfile CreateZFold7Default() =>
        new(
            new ClientId("z-fold-7"),
            "Z Fold 7",
            new DisplayPreferences(2560, 1600, 120, HdrPreference.Prefer, "virtual-primary", true, true),
            new StreamPreferences("auto", "auto", null),
            new AudioPreferences("stereo"),
            new SessionPreferences(false, true));
}

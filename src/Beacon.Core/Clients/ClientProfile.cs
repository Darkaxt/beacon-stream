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
    public static ClientProfile CreateZFold7Default()
    {
        ClientProfile profile = CreateDefault(new ClientId("z-fold-7"), "Z Fold 7");
        var mode = new ClientDisplayMode(2560, 1600, 120);
        return profile with
        {
            Display = profile.Display with
            {
                PreferredMode = mode,
                SelectedMode = mode
            }
        };
    }

    public static ClientProfile CreateDefault(ClientId clientId, string name) =>
        new(
            clientId,
            name,
            new DisplayPreferences(null, null, HdrPreference.Prefer, "virtual-primary", true, true),
            new StreamPreferences("auto", "auto", null),
            new AudioPreferences("stereo"),
            new SessionPreferences(false, true));
}

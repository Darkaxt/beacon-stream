namespace Beacon.Core.Clients;

public sealed class InvalidClientProfilePatchException(string message) : InvalidOperationException(message);

public static class ClientProfilePatcher
{
    public static ClientProfile ApplyApkPatch(ClientProfile profile, ClientProfilePatch patch)
    {
        int width = patch.PreferredWidth ?? profile.Display.PreferredWidth;
        int height = patch.PreferredHeight ?? profile.Display.PreferredHeight;
        int refresh = patch.PreferredRefreshHz ?? profile.Display.PreferredRefreshHz;

        if (profile.ClientId.Value == "z-fold-7" && width == 2560 && height == 1440)
        {
            throw new InvalidClientProfilePatchException("Z Fold 7 profile must not collapse 2560x1600 intent to 2560x1440.");
        }

        var display = profile.Display with
        {
            PreferredWidth = width,
            PreferredHeight = height,
            PreferredRefreshHz = refresh,
            HdrPreference = patch.HdrPreference ?? profile.Display.HdrPreference
        };

        var stream = profile.Stream with
        {
            CodecPreference = patch.CodecPreference ?? profile.Stream.CodecPreference,
            QualityMode = patch.QualityMode ?? profile.Stream.QualityMode,
            BitrateCapMbps = patch.BitrateCapMbps ?? profile.Stream.BitrateCapMbps
        };

        var audio = profile.Audio with
        {
            Mode = patch.AudioMode ?? profile.Audio.Mode
        };

        var session = profile.Session with
        {
            KeepAppRunningOnDisconnect = patch.KeepAppRunningOnDisconnect ?? profile.Session.KeepAppRunningOnDisconnect
        };

        return profile with { Display = display, Stream = stream, Audio = audio, Session = session };
    }
}

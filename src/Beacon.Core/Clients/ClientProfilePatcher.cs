namespace Beacon.Core.Clients;

public sealed class InvalidClientProfilePatchException(string message) : InvalidOperationException(message);

public static class ClientProfilePatcher
{
    public static ClientProfile ApplyApkPatch(ClientProfile profile, ClientProfilePatch patch)
    {
        int width = patch.PreferredWidth ?? profile.Display.PreferredWidth;
        int height = patch.PreferredHeight ?? profile.Display.PreferredHeight;
        int refresh = patch.PreferredRefreshHz ?? profile.Display.PreferredRefreshHz;

        ValidateAspectRatioIntent(profile, width, height);

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

    public static ClientProfile ApplyAdminPatch(ClientProfile profile, ClientProfileAdminPatch patch)
    {
        int width = patch.PreferredWidth ?? profile.Display.PreferredWidth;
        int height = patch.PreferredHeight ?? profile.Display.PreferredHeight;
        int refresh = patch.PreferredRefreshHz ?? profile.Display.PreferredRefreshHz;

        ValidateAspectRatioIntent(profile, width, height);

        var display = profile.Display with
        {
            PreferredWidth = width,
            PreferredHeight = height,
            PreferredRefreshHz = refresh,
            HdrPreference = patch.HdrPreference ?? profile.Display.HdrPreference,
            Mode = string.IsNullOrWhiteSpace(patch.Mode) ? profile.Display.Mode : patch.Mode.Trim(),
            RestorePhysicalDisplayOnEnd = patch.RestorePhysicalDisplayOnEnd ?? profile.Display.RestorePhysicalDisplayOnEnd,
            ForbidMirrorMode = patch.ForbidMirrorMode ?? profile.Display.ForbidMirrorMode
        };

        var stream = profile.Stream with
        {
            CodecPreference = string.IsNullOrWhiteSpace(patch.CodecPreference) ? profile.Stream.CodecPreference : patch.CodecPreference.Trim(),
            QualityMode = string.IsNullOrWhiteSpace(patch.QualityMode) ? profile.Stream.QualityMode : patch.QualityMode.Trim(),
            BitrateCapMbps = patch.BitrateCapMbps ?? profile.Stream.BitrateCapMbps
        };

        var audio = profile.Audio with
        {
            Mode = string.IsNullOrWhiteSpace(patch.AudioMode) ? profile.Audio.Mode : patch.AudioMode.Trim()
        };

        var session = profile.Session with
        {
            KeepAppRunningOnDisconnect = patch.KeepAppRunningOnDisconnect ?? profile.Session.KeepAppRunningOnDisconnect,
            AllowEmergencyRestoreFromClient = patch.AllowEmergencyRestoreFromClient ?? profile.Session.AllowEmergencyRestoreFromClient
        };

        return profile with { Display = display, Stream = stream, Audio = audio, Session = session };
    }

    private static void ValidateAspectRatioIntent(ClientProfile profile, int width, int height)
    {
        if (profile.ClientId.Value == "z-fold-7" && width == 2560 && height == 1440)
        {
            throw new InvalidClientProfilePatchException("Z Fold 7 profile must not collapse 2560x1600 intent to 2560x1440.");
        }
    }
}

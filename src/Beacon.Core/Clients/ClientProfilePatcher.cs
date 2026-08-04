namespace Beacon.Core.Clients;

public sealed class InvalidClientProfilePatchException(string message) : InvalidOperationException(message);

public static class ClientProfilePatcher
{
    public static ClientProfile ApplyAdminPatch(ClientProfile profile, ClientProfileAdminPatch patch)
    {
        ClientDisplayMode? preferredMode = ApplyPreferredMode(
            profile,
            patch.PreferredWidth,
            patch.PreferredHeight,
            patch.PreferredRefreshHz);

        var display = profile.Display with
        {
            PreferredMode = preferredMode,
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

    private static ClientDisplayMode? ApplyPreferredMode(
        ClientProfile profile,
        int? width,
        int? height,
        int? refreshHz)
    {
        if (width is null && height is null && refreshHz is null)
        {
            return profile.Display.PreferredMode;
        }

        ClientDisplayMode? baseline = profile.Display.PreferredMode ?? profile.Display.SelectedMode;
        if ((width ?? baseline?.Width) is not { } resolvedWidth ||
            (height ?? baseline?.Height) is not { } resolvedHeight ||
            (refreshHz ?? baseline?.RefreshHz) is not { } resolvedRefresh)
        {
            throw new InvalidClientProfilePatchException(
                "Width, height, and refresh rate are all required before a client display preference can be selected.");
        }

        var mode = new ClientDisplayMode(resolvedWidth, resolvedHeight, resolvedRefresh);
        if (!mode.IsValid)
        {
            throw new InvalidClientProfilePatchException(
                "Client display width, height, and refresh rate must be positive.");
        }

        if (profile.ClientId.Value == "z-fold-7" && mode.Width == 2560 && mode.Height == 1440)
        {
            throw new InvalidClientProfilePatchException("Z Fold 7 profile must not collapse 2560x1600 intent to 2560x1440.");
        }

        return mode;
    }
}

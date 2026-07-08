using Beacon.Core.Clients;
using Beacon.Core.Displays;

namespace Beacon.Core.Tests.Clients;

public sealed class ClientProfilePatcherTests
{
    [Fact]
    public void ZFold7DefaultPreserves1600p120Preference()
    {
        ClientProfile profile = ClientProfile.CreateZFold7Default();

        Assert.Equal("z-fold-7", profile.ClientId.Value);
        Assert.Equal(2560, profile.Display.PreferredWidth);
        Assert.Equal(1600, profile.Display.PreferredHeight);
        Assert.Equal(120, profile.Display.PreferredRefreshHz);
        Assert.Equal(HdrPreference.Prefer, profile.Display.HdrPreference);
    }

    [Fact]
    public void AppliesOnlyApkEditableClientPreferences()
    {
        ClientProfile profile = ClientProfile.CreateZFold7Default();
        var patch = new ClientProfilePatch(
            PreferredWidth: 1920,
            PreferredHeight: 1200,
            PreferredRefreshHz: 60,
            HdrPreference: HdrPreference.Off,
            CodecPreference: "hevc",
            QualityMode: "balanced",
            BitrateCapMbps: 45,
            AudioMode: "stereo",
            KeepAppRunningOnDisconnect: true);

        ClientProfile updated = ClientProfilePatcher.ApplyApkPatch(profile, patch);

        Assert.Equal(1920, updated.Display.PreferredWidth);
        Assert.Equal(1200, updated.Display.PreferredHeight);
        Assert.Equal(60, updated.Display.PreferredRefreshHz);
        Assert.Equal(HdrPreference.Off, updated.Display.HdrPreference);
        Assert.Equal("virtual-primary", updated.Display.Mode);
        Assert.True(updated.Display.RestorePhysicalDisplayOnEnd);
        Assert.True(updated.Display.ForbidMirrorMode);
        Assert.Equal("hevc", updated.Stream.CodecPreference);
        Assert.Equal("balanced", updated.Stream.QualityMode);
        Assert.Equal(45, updated.Stream.BitrateCapMbps);
        Assert.True(updated.Session.KeepAppRunningOnDisconnect);
    }

    [Fact]
    public void RejectsInvalidAspectRatioCollapseTo1440pForZFold7()
    {
        ClientProfile profile = ClientProfile.CreateZFold7Default();
        var patch = new ClientProfilePatch(
            PreferredWidth: 2560,
            PreferredHeight: 1440,
            PreferredRefreshHz: 120,
            HdrPreference: HdrPreference.Prefer,
            CodecPreference: null,
            QualityMode: null,
            BitrateCapMbps: null,
            AudioMode: null,
            KeepAppRunningOnDisconnect: null);

        InvalidClientProfilePatchException error = Assert.Throws<InvalidClientProfilePatchException>(
            () => ClientProfilePatcher.ApplyApkPatch(profile, patch));

        Assert.Contains("2560x1440", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminPatchAppliesDisplayAndSessionPolicyFields()
    {
        ClientProfile profile = ClientProfile.CreateZFold7Default();
        var patch = new ClientProfileAdminPatch(
            PreferredWidth: 2560,
            PreferredHeight: 1600,
            PreferredRefreshHz: 90,
            HdrPreference: HdrPreference.Require,
            Mode: "physical-blackout",
            RestorePhysicalDisplayOnEnd: false,
            ForbidMirrorMode: false,
            CodecPreference: "hevc",
            QualityMode: "quality",
            BitrateCapMbps: 80,
            AudioMode: "surround",
            KeepAppRunningOnDisconnect: true,
            AllowEmergencyRestoreFromClient: false);

        ClientProfile updated = ClientProfilePatcher.ApplyAdminPatch(profile, patch);

        Assert.Equal(2560, updated.Display.PreferredWidth);
        Assert.Equal(1600, updated.Display.PreferredHeight);
        Assert.Equal(90, updated.Display.PreferredRefreshHz);
        Assert.Equal(HdrPreference.Require, updated.Display.HdrPreference);
        Assert.Equal("physical-blackout", updated.Display.Mode);
        Assert.False(updated.Display.RestorePhysicalDisplayOnEnd);
        Assert.False(updated.Display.ForbidMirrorMode);
        Assert.Equal("hevc", updated.Stream.CodecPreference);
        Assert.Equal("quality", updated.Stream.QualityMode);
        Assert.Equal(80, updated.Stream.BitrateCapMbps);
        Assert.Equal("surround", updated.Audio.Mode);
        Assert.True(updated.Session.KeepAppRunningOnDisconnect);
        Assert.False(updated.Session.AllowEmergencyRestoreFromClient);
    }
}

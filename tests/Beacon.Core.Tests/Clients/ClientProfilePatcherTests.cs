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
        Assert.Equal(new ClientDisplayMode(2560, 1600, 120), profile.Display.PreferredMode);
        Assert.Equal(new ClientDisplayMode(2560, 1600, 120), profile.Display.SelectedMode);
        Assert.Equal(HdrPreference.Prefer, profile.Display.HdrPreference);
    }

    [Fact]
    public void CoreDoesNotExposeAClientProfilePatchContract()
    {
        Assert.DoesNotContain(
            typeof(ClientProfilePatcher).Assembly.GetTypes(),
            type => type.Name == "ClientProfilePatch");
        Assert.DoesNotContain(
            typeof(ClientProfilePatcher).GetMethods(),
            method => method.Name == "ApplyApkPatch");
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

        Assert.Equal(new ClientDisplayMode(2560, 1600, 90), updated.Display.PreferredMode);
        Assert.Equal(new ClientDisplayMode(2560, 1600, 120), updated.Display.SelectedMode);
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

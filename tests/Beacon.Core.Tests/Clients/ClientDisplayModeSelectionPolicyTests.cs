using Beacon.Core.Clients;

namespace Beacon.Core.Tests.Clients;

public sealed class ClientDisplayModeSelectionPolicyTests
{
    [Fact]
    public void ExactSupportedServerPreferenceWins()
    {
        var preferred = new ClientDisplayMode(2560, 1600, 120);
        var current = new ClientDisplayMode(2208, 1768, 120);
        var capabilities = CreateCapabilities(
            current,
            supported:
            [
                current,
                new ClientDisplayMode(1920, 1200, 120),
                preferred,
                new ClientDisplayMode(2560, 1440, 120)
            ]);

        ClientDisplayMode selected = ClientDisplayModeSelectionPolicy.Select(preferred, capabilities);

        Assert.Equal(preferred, selected);
    }

    [Fact]
    public void SameAspectModeWinsBeforeCloserDifferentAspectMode()
    {
        var preferred = new ClientDisplayMode(2560, 1600, 120);
        var capabilities = CreateCapabilities(
            current: new ClientDisplayMode(2560, 1440, 120),
            supported:
            [
                new ClientDisplayMode(2560, 1440, 120),
                new ClientDisplayMode(1920, 1200, 60)
            ]);

        ClientDisplayMode selected = ClientDisplayModeSelectionPolicy.Select(preferred, capabilities);

        Assert.Equal(new ClientDisplayMode(1920, 1200, 60), selected);
    }

    [Fact]
    public void CurrentFullHdModeIsTheTargetWhenServerHasNoExplicitPreference()
    {
        var current = new ClientDisplayMode(1920, 1080, 60);
        var capabilities = CreateCapabilities(
            current,
            [
                new ClientDisplayMode(2560, 1440, 120),
                current,
                new ClientDisplayMode(1280, 720, 120)
            ]);

        ClientDisplayMode selected = ClientDisplayModeSelectionPolicy.Select(null, capabilities);

        Assert.Equal(current, selected);
    }

    [Fact]
    public void SelectionAlwaysReturnsOneCompleteReportedMode()
    {
        var preferred = new ClientDisplayMode(2300, 1500, 90);
        var supported = new[]
        {
            new ClientDisplayMode(1920, 1200, 60),
            new ClientDisplayMode(2560, 1600, 120)
        };
        var capabilities = CreateCapabilities(supported[0], supported);

        ClientDisplayMode selected = ClientDisplayModeSelectionPolicy.Select(preferred, capabilities);

        Assert.Contains(selected, supported);
        Assert.NotEqual(2300, selected.Width);
        Assert.NotEqual(1500, selected.Height);
        Assert.NotEqual(90, selected.RefreshHz);
    }

    [Fact]
    public void PortraitFactsAreNormalizedToOneLandscapeMode()
    {
        var capabilities = CreateCapabilities(
            current: new ClientDisplayMode(1080, 1920, 60),
            supported:
            [
                new ClientDisplayMode(1080, 1920, 60),
                new ClientDisplayMode(720, 1280, 120)
            ]);

        ClientDisplayMode selected = ClientDisplayModeSelectionPolicy.Select(null, capabilities);

        Assert.Equal(new ClientDisplayMode(1920, 1080, 60), selected);
    }

    [Fact]
    public void EqualDistanceFallbackAvoidsUpscaling()
    {
        var preferred = new ClientDisplayMode(1920, 1080, 60);
        var capabilities = CreateCapabilities(
            current: new ClientDisplayMode(1280, 720, 60),
            supported:
            [
                new ClientDisplayMode(1280, 720, 60),
                new ClientDisplayMode(2560, 1440, 60)
            ]);

        ClientDisplayMode selected = ClientDisplayModeSelectionPolicy.Select(preferred, capabilities);

        Assert.Equal(new ClientDisplayMode(1280, 720, 60), selected);
    }

    [Fact]
    public void EqualRefreshDistancePrefersRateNotExceedingTarget()
    {
        var preferred = new ClientDisplayMode(1920, 1080, 90);
        var capabilities = CreateCapabilities(
            current: new ClientDisplayMode(1920, 1080, 60),
            supported:
            [
                new ClientDisplayMode(1920, 1080, 60),
                new ClientDisplayMode(1920, 1080, 120)
            ]);

        ClientDisplayMode selected = ClientDisplayModeSelectionPolicy.Select(preferred, capabilities);

        Assert.Equal(new ClientDisplayMode(1920, 1080, 60), selected);
    }

    [Fact]
    public void CurrentModeMustBeOneOfTheReportedSupportedModes()
    {
        var capabilities = CreateCapabilities(
            current: new ClientDisplayMode(1920, 1080, 60),
            supported: [new ClientDisplayMode(1280, 720, 60)]);

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => ClientDisplayModeSelectionPolicy.Select(null, capabilities));

        Assert.Contains("current", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingStructuredDisplayFactsAreRejected()
    {
        var capabilities = new EndpointCapabilities(
            Av1: true,
            Hevc: true,
            H264: true,
            Hdr10: false,
            VirtualDisplayHdrSupported: false);

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => ClientDisplayModeSelectionPolicy.Select(null, capabilities));

        Assert.Contains("display", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static EndpointCapabilities CreateCapabilities(
        ClientDisplayMode current,
        IReadOnlyList<ClientDisplayMode> supported) =>
        new(
            Av1: true,
            Hevc: true,
            H264: true,
            Hdr10: false,
            VirtualDisplayHdrSupported: false,
            MaxFps: supported.Max(mode => mode.RefreshHz),
            LowLatencyDecode: true,
            CurrentDisplayMode: current,
            SupportedDisplayModes: supported);
}

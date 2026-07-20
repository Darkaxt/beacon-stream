using Beacon.Platform.Windows.Displays;

namespace Beacon.Platform.Windows.Tests.Displays;

public sealed class WindowsDisplayRestoreCandidateTests
{
    [Fact]
    public void SelectPhysicalRestoreCandidatePrefersKnownPhysicalDisplay()
    {
        string? selected = WindowsDisplayDiagnostics.SelectPhysicalRestoreCandidate(
            [
                new DisplayRestoreCandidate(@"\\.\DISPLAY9", DisplayPathKind.Virtual, IsPrimary: true, X: 0, Y: 0),
                new DisplayRestoreCandidate(@"\\.\DISPLAY5", DisplayPathKind.Physical, IsPrimary: false, X: -2560, Y: 0)
            ]);

        Assert.Equal(@"\\.\DISPLAY5", selected);
    }

    [Fact]
    public void SelectPhysicalRestoreCandidateUsesNonOriginUnknownWhenOriginIsVirtual()
    {
        string? selected = WindowsDisplayDiagnostics.SelectPhysicalRestoreCandidate(
            [
                new DisplayRestoreCandidate(@"\\.\DISPLAY9", DisplayPathKind.Virtual, IsPrimary: true, X: 0, Y: 0),
                new DisplayRestoreCandidate(@"\\.\DISPLAY5", Kind: null, IsPrimary: false, X: -2560, Y: 0)
            ]);

        Assert.Equal(@"\\.\DISPLAY5", selected);
    }

    [Fact]
    public void SelectPhysicalRestoreCandidateDoesNotGuessWhenOriginIsNotVirtual()
    {
        string? selected = WindowsDisplayDiagnostics.SelectPhysicalRestoreCandidate(
            [
                new DisplayRestoreCandidate(@"\\.\DISPLAY1", Kind: null, IsPrimary: true, X: 0, Y: 0),
                new DisplayRestoreCandidate(@"\\.\DISPLAY9", Kind: null, IsPrimary: false, X: 2560, Y: 0)
            ]);

        Assert.Null(selected);
    }

    [Fact]
    public void ExtendedTopologyRepairIsRequiredWhenVirtualTargetIsTheOnlyActiveDisplay()
    {
        DisplayRestoreCandidate[] virtualOnly =
        [
            new(@"\\.\DISPLAY9", DisplayPathKind.Virtual, IsPrimary: true, X: 0, Y: 0)
        ];
        DisplayRestoreCandidate[] extended =
        [
            new(@"\\.\DISPLAY5", DisplayPathKind.Physical, IsPrimary: true, X: 0, Y: 0),
            new(@"\\.\DISPLAY9", DisplayPathKind.Virtual, IsPrimary: false, X: 2560, Y: 0)
        ];

        Assert.True(WindowsDisplayDiagnostics.RequiresExtendedTopologyRepair(
            virtualOnly,
            @"\\.\DISPLAY9"));
        Assert.False(WindowsDisplayDiagnostics.RequiresExtendedTopologyRepair(
            extended,
            @"\\.\DISPLAY9"));
    }

    [Theory]
    [InlineData(5u, true)]
    [InlineData(31u, true)]
    [InlineData(0u, false)]
    public void FailedTopologyDatabaseApplyUsesSuppliedConfigurationFallback(
        uint topologyStatus,
        bool expected)
    {
        Assert.Equal(
            expected,
            WindowsDisplayDiagnostics.ShouldRetryWithSuppliedDisplayConfig(topologyStatus));
    }

    [Fact]
    public void ExtendedTopologyApplyUsesOnlyValidDatabaseTopologyFlags()
    {
        Assert.Equal(0x00000084u, WindowsDisplayApi.ExtendedTopologyApplyFlags());
    }

    [Fact]
    public void DisplayConfigAccessValidationUsesSuppliedVirtualAwareConfiguration()
    {
        Assert.Equal(0x00008060u, WindowsDisplayApi.SuppliedDisplayConfigValidateFlags());
    }
}

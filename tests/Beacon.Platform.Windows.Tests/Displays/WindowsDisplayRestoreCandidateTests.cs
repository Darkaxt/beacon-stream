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

    [Fact]
    public void MissingPhysicalPathUsesWindowsExtendedTopologyDatabase()
    {
        DisplayTargetActivationAction action = WindowsDisplayDiagnostics.PlanTargetActivation(
            [new(@"\\.\DISPLAY9", DisplayPathKind.Virtual, IsPrimary: true, X: 0, Y: 0)],
            @"\\.\DISPLAY9",
            mirrorMode: false);

        Assert.Equal(DisplayTargetActivationAction.ApplyExtendedTopology, action);
    }

    [Fact]
    public void MirroredPathsUseWindowsExtendedTopologyDatabase()
    {
        DisplayTargetActivationAction action = WindowsDisplayDiagnostics.PlanTargetActivation(
            [
                new(@"\\.\DISPLAY1", DisplayPathKind.Physical, IsPrimary: true, X: 0, Y: 0),
                new(@"\\.\DISPLAY9", DisplayPathKind.Virtual, IsPrimary: false, X: 0, Y: 0)
            ],
            @"\\.\DISPLAY9",
            mirrorMode: true);

        Assert.Equal(DisplayTargetActivationAction.ApplyExtendedTopology, action);
    }

    [Fact]
    public void MissingVirtualTargetUsesSuppliedPathComposition()
    {
        DisplayTargetActivationAction action = WindowsDisplayDiagnostics.PlanTargetActivation(
            [new(@"\\.\DISPLAY1", DisplayPathKind.Physical, IsPrimary: true, X: 0, Y: 0)],
            @"\\.\DISPLAY9",
            mirrorMode: false);

        Assert.Equal(DisplayTargetActivationAction.ApplySuppliedTopology, action);
    }

    [Fact]
    public void ActiveExtendedTargetRequiresNoPathTransition()
    {
        DisplayTargetActivationAction action = WindowsDisplayDiagnostics.PlanTargetActivation(
            [
                new(@"\\.\DISPLAY1", DisplayPathKind.Physical, IsPrimary: true, X: 0, Y: 0),
                new(@"\\.\DISPLAY9", DisplayPathKind.Virtual, IsPrimary: false, X: 2560, Y: 0)
            ],
            @"\\.\DISPLAY9",
            mirrorMode: false);

        Assert.Equal(DisplayTargetActivationAction.None, action);
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
    public void DisplayConfigAccessValidationUsesSuppliedVirtualAwareConfiguration()
    {
        Assert.Equal(0x00008460u, WindowsDisplayApi.SuppliedDisplayConfigValidateFlags());
    }

    [Fact]
    public void DetachedPhysicalRecoveryAttachesBeforeVerifiedPrimaryTransition()
    {
        Assert.Equal(0x40000001u, WindowsDisplayApi.PhysicalDisplayResetFlags());
    }

    [Fact]
    public void DetachedPhysicalRecoveryAttachesBesideTheActiveVirtualDesktop()
    {
        PhysicalDisplayAttachPosition? position =
            WindowsDisplayDiagnostics.SelectPhysicalAttachPosition(
                [
                    new DisplayPathSnapshot(
                        @"\\.\DISPLAY9",
                        DisplayPathKind.Virtual,
                        Width: 2560,
                        Height: 1600,
                        RefreshHz: 120,
                        IsPrimary: true,
                        X: 0,
                        Y: 0),
                ],
                physicalWidth: 2560);

        Assert.Equal(new PhysicalDisplayAttachPosition(X: -2560, Y: 0), position);
    }

    [Fact]
    public void DetachedPhysicalRecoveryPrefersInternalPanelOverLargerExternalMode()
    {
        string? selected = WindowsDisplayDiagnostics.SelectPhysicalAttachCandidate(
            [
                new(@"\\.\DISPLAY8", Internal: false, Width: 3840, Height: 2160, RefreshHz: 120),
                new(@"\\.\DISPLAY1", Internal: true, Width: 2560, Height: 1600, RefreshHz: 240),
                new(@"\\.\DISPLAY2", Internal: true, Width: 1920, Height: 1200, RefreshHz: 60),
            ]);

        Assert.Equal(@"\\.\DISPLAY1", selected);
    }

    [Fact]
    public void DetachedPhysicalRecoveryUsesDeterministicModePriority()
    {
        string? selected = WindowsDisplayDiagnostics.SelectPhysicalAttachCandidate(
            [
                new(@"\\.\DISPLAY3", Internal: true, Width: 2560, Height: 1600, RefreshHz: 120),
                new(@"\\.\DISPLAY2", Internal: true, Width: 2560, Height: 1600, RefreshHz: 240),
                new(@"\\.\DISPLAY1", Internal: true, Width: 2560, Height: 1600, RefreshHz: 240),
            ]);

        Assert.Equal(@"\\.\DISPLAY1", selected);
    }
}

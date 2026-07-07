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
}

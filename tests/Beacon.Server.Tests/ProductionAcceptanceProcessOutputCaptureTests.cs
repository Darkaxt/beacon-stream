using System.Diagnostics;
using Beacon.ProductionAcceptance;

namespace Beacon.Server.Tests;

public sealed class ProductionAcceptanceProcessOutputCaptureTests
{
    [Fact]
    public async Task WaitForDrainRetainsOutputQueuedAfterProcessExit()
    {
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(
            "1..20000 | ForEach-Object { Write-Output \"stdout-$_\" }; " +
            "[Console]::Error.WriteLine('final-error'); Write-Output 'final-output'");

        using var process = new Process { StartInfo = startInfo };
        using var capture = new ProcessOutputCapture();
        Assert.True(process.Start());
        capture.Attach(process);

        await process.WaitForExitAsync();
        capture.WaitForDrain();

        Assert.Contains("stdout-20000", capture.CombinedOutput, StringComparison.Ordinal);
        Assert.Contains("final-error", capture.CombinedOutput, StringComparison.Ordinal);
        Assert.Contains("final-output", capture.CombinedOutput, StringComparison.Ordinal);
    }
}

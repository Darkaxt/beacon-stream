namespace Beacon.Server.Tests;

public sealed class Gate5HarnessSafetyContractTests
{
    [Fact]
    public void RunnerRetainsTheExistingSetupAndHardensOnlyFinalRecovery()
    {
        string script = ReadRunner();
        int build = RequiredIndex(script, "if (-not $ArtifactsReady)");
        int arguments = RequiredIndex(script, "$arguments = @(", build);
        int outerTry = RequiredIndex(script, "try {", RequiredIndex(script, "$testFailure = $null"));
        int acceptance = RequiredIndex(script, "& dotnet @arguments", outerTry);
        int outerFinally = script.LastIndexOf("} finally {", StringComparison.Ordinal);
        int forcePhysical = RequiredIndex(script, "$displaySwitchProcess = Start-Process", outerFinally);
        int restoreBeforeGuard = RequiredIndex(script, "-Name 'restore-before-guard'", forcePhysical);
        int guardWait = RequiredIndex(script, "$guardProcess.WaitForExit()", restoreBeforeGuard);
        int remove = RequiredIndex(script, "-Name 'remove'", guardWait);
        int restoreAfterRemove = RequiredIndex(script, "-Name 'restore-after-remove'", remove);

        Assert.True(build < arguments && arguments < outerTry);
        Assert.True(outerTry < acceptance && acceptance < outerFinally);
        Assert.True(outerFinally < forcePhysical && forcePhysical < restoreBeforeGuard);
        Assert.True(restoreBeforeGuard < guardWait && guardWait < remove);
        Assert.True(remove < restoreAfterRemove);
        Assert.Contains("function Invoke-Gate5CleanupCommand", script, StringComparison.Ordinal);
        Assert.Contains("ExitCode = -1", script, StringComparison.Ordinal);
        Assert.Contains("Start-Process", script, StringComparison.Ordinal);
        Assert.Contains("-WindowStyle Hidden", script, StringComparison.Ordinal);
        Assert.Contains("-Name 'restore-before-guard'", script, StringComparison.Ordinal);
        Assert.Contains("-Name 'remove'", script, StringComparison.Ordinal);
        Assert.Contains("-Name 'restore-after-remove'", script, StringComparison.Ordinal);
        Assert.Contains("physicalPrimaryVerified=True", script, StringComparison.Ordinal);
        Assert.Contains("mirrorMode=False", script, StringComparison.Ordinal);
        Assert.Contains("kind=Virtual", script, StringComparison.Ordinal);
        Assert.Contains("leaseCount -eq 0", script, StringComparison.Ordinal);
        Assert.Contains("heartbeatActive -eq $false", script, StringComparison.Ordinal);
        Assert.Contains("mandatory-post-test-recovery.json", script, StringComparison.Ordinal);
        Assert.Contains("AggregateException", script, StringComparison.Ordinal);
        Assert.Contains("$removeOutput -ceq", script, StringComparison.Ordinal);
        Assert.Contains(
            "\"remove: failed: No active SudoVDA driver lease owns client-$clientId.\"",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain("$removeOutput.StartsWith(", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove-Item", script, StringComparison.Ordinal);
        Assert.DoesNotContain("WaitForExit(", script.Replace("WaitForExit()", string.Empty), StringComparison.Ordinal);
    }

    [Fact]
    public void RunnerPassesPhysicalModeAndHostWithoutOwningPackageDataClear()
    {
        string script = ReadRunner();
        string acceptance = ReadAcceptance();

        Assert.Contains("[ValidateSet('Emulator', 'Physical')]", script, StringComparison.Ordinal);
        Assert.Contains("[string]$AndroidClientKind = 'Emulator'", script, StringComparison.Ordinal);
        Assert.Contains("[string]$ServerHost", script, StringComparison.Ordinal);
        Assert.Contains("$clientId = \"gate5-$clientKind-$runId\"", script, StringComparison.Ordinal);
        Assert.Contains("'--android-client-kind'", script, StringComparison.Ordinal);
        Assert.Contains("@('--server-host', $ServerHost)", script, StringComparison.Ordinal);
        Assert.DoesNotContain("pm clear", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("options.CreateAndroidServerUrl(listening.Port)", acceptance, StringComparison.Ordinal);
        Assert.Contains(
            "\"shell\", \"toybox\", \"nc\", \"-z\", options.ServerHost",
            acceptance,
            StringComparison.Ordinal);

        int requiredCleanup = RequiredIndex(
            acceptance,
            "string cleanupOutput = await RunAndroidCleanupInstrumentationAsync(");
        int success = RequiredIndex(acceptance, "success = true;", requiredCleanup);
        Assert.True(requiredCleanup < success);
        Assert.Contains(
            "AndroidEvidencePath(\"beacon-gate3-client-credential\", clientId)",
            acceptance,
            StringComparison.Ordinal);
        Assert.Contains(
            "AndroidEvidencePath(\"beacon-gate3-ticket-evidence\", clientId)",
            acceptance,
            StringComparison.Ordinal);

    }

    [Fact]
    public void AndroidMarkersAreWrittenToTheCurrentInstrumentationInvocation()
    {
        string instrumentation = ReadAndroidInstrumentation();

        Assert.Contains(
            "status.putString(Instrumentation.REPORT_KEY_STREAMRESULT, marker + \"\\n\");",
            instrumentation,
            StringComparison.Ordinal);
        Assert.Contains(
            "InstrumentationRegistry.getInstrumentation().sendStatus(2, status);",
            instrumentation,
            StringComparison.Ordinal);
    }

    private static string ReadRunner()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Beacon.slnx")))
        {
            directory = directory.Parent;
        }
        Assert.NotNull(directory);
        string path = Path.Combine(directory!.FullName, "scripts", "test-gate5-production-session.ps1");
        return File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string ReadAcceptance()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Beacon.slnx")))
        {
            directory = directory.Parent;
        }
        Assert.NotNull(directory);
        string path = Path.Combine(
            directory!.FullName,
            "tests",
            "Beacon.ProductionAcceptance",
            "Program.cs");
        return File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string ReadAndroidInstrumentation()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Beacon.slnx")))
        {
            directory = directory.Parent;
        }
        Assert.NotNull(directory);
        string path = Path.Combine(
            directory!.FullName,
            "src",
            "Beacon.Android",
            "app",
            "src",
            "androidTest",
            "java",
            "dev",
            "beacon",
            "android",
            "BeaconStreamCoreInstrumentationTest.java");
        return File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static int RequiredIndex(string value, string marker, int start = 0)
    {
        int index = value.IndexOf(marker, start, StringComparison.Ordinal);
        Assert.True(index >= 0, $"Missing Gate 5 harness marker: {marker}");
        return index;
    }
}

using Beacon.ProductionAcceptance;

namespace Beacon.Server.Tests;

public sealed class Gate5AndroidClientSafetyTests
{
    private const string RunId = "0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task EmulatorPreparationRejectsNonEmulatorIdentityBeforePackageClear()
    {
        AcceptanceOptions options = ParseOptions(
            ["--run-id", RunId, "--serial", "physical-device"]);
        var commands = new List<string>();
        bool credentialTransferred = false;

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => AcceptanceAndroidClientOperations.PrepareAsync(
                options,
                "app.apk",
                "test.apk",
                args =>
                {
                    string command = string.Join(' ', args);
                    commands.Add(command);
                    return Task.FromResult(command == "shell getprop ro.kernel.qemu" ? "0\r\n" : string.Empty);
                },
                () =>
                {
                    credentialTransferred = true;
                    return Task.CompletedTask;
                }));

        Assert.Contains("ro.kernel.qemu=1", error.Message, StringComparison.Ordinal);
        Assert.Equal(["get-state", "shell getprop ro.kernel.qemu"], commands);
        Assert.DoesNotContain(commands, command => command.Contains("pm clear", StringComparison.Ordinal));
        Assert.False(credentialTransferred);
    }

    [Fact]
    public async Task EmulatorPreparationClearsPackageOnlyAfterExactEmulatorIdentity()
    {
        AcceptanceOptions options = ParseOptions(
            ["--run-id", RunId, "--serial", "emulator-5554"]);
        var events = new List<string>();

        await AcceptanceAndroidClientOperations.PrepareAsync(
            options,
            "app.apk",
            "test.apk",
            args =>
            {
                string command = string.Join(' ', args);
                events.Add(command);
                return Task.FromResult(command == "shell getprop ro.kernel.qemu" ? "1\n" : string.Empty);
            },
            () =>
            {
                events.Add("transfer-credential");
                return Task.CompletedTask;
            });

        Assert.Equal(
            [
                "get-state",
                "shell getprop ro.kernel.qemu",
                "install -r app.apk",
                "install -r test.apk",
                "shell pm clear dev.beacon.android",
                "transfer-credential",
                "logcat -c",
            ],
            events);
    }

    [Fact]
    public async Task PhysicalPreparationInstallsWithoutPackageOrLogcatClear()
    {
        AcceptanceOptions options = ParseOptions(
            [
                "--run-id", RunId,
                "--serial", "physical-device",
                "--android-client-kind", "physical",
                "--server-host", "192.0.2.10",
            ]);
        var events = new List<string>();

        await AcceptanceAndroidClientOperations.PrepareAsync(
            options,
            "app.apk",
            "test.apk",
            args =>
            {
                events.Add(string.Join(' ', args));
                return Task.FromResult(string.Empty);
            },
            () =>
            {
                events.Add("transfer-credential");
                return Task.CompletedTask;
            });

        Assert.Equal(
            [
                "get-state",
                "install -r app.apk",
                "install -r test.apk",
                "transfer-credential",
            ],
            events);
        Assert.DoesNotContain(events, command => command.Contains("pm clear", StringComparison.Ordinal));
        Assert.DoesNotContain(events, command => command == "logcat -c");
    }

    [Fact]
    public async Task PhysicalInstrumentationUsesOnlyCurrentInvocationOutput()
    {
        AcceptanceOptions options = ParseOptions(
            [
                "--run-id", RunId,
                "--android-client-kind", "physical",
                "--server-host", "192.0.2.10",
            ]);
        const string invocationOutput =
            "BEACON_GATE5_ANDROID_STATE_CLEANED gate5-physical-0123456789abcdef0123456789abcdef\nOK (1 test)";

        AndroidInstrumentationCapture capture =
            await AcceptanceAndroidClientOperations.CaptureInstrumentationAsync(
                options,
                () => Task.FromResult(invocationOutput),
                _ => throw new InvalidOperationException("Physical capture must not read or clear logcat."));

        Assert.Equal(invocationOutput, capture.InvocationOutput);
        Assert.Equal(invocationOutput, capture.Output);
    }

    [Fact]
    public async Task PhysicalInstrumentationParsesInterimMarkerAndTerminalJunitResult()
    {
        AcceptanceOptions options = ParseOptions(
            [
                "--run-id", RunId,
                "--android-client-kind", "physical",
                "--server-host", "192.0.2.10",
            ]);
        const string marker =
            "BEACON_GATE5_ANDROID_STATE_CLEANED gate5-physical-0123456789abcdef0123456789abcdef";
        const string rawOutput =
            "INSTRUMENTATION_STATUS: stream=" + marker + "\n" +
            "INSTRUMENTATION_STATUS_CODE: 2\n" +
            "INSTRUMENTATION_STATUS: class=dev.beacon.android.BeaconStreamCoreInstrumentationTest\n" +
            "INSTRUMENTATION_STATUS: current=1\n" +
            "INSTRUMENTATION_STATUS: id=AndroidJUnitRunner\n" +
            "INSTRUMENTATION_STATUS: numtests=1\n" +
            "INSTRUMENTATION_STATUS: test=gate5CleanupRunScopedClientState\n" +
            "INSTRUMENTATION_STATUS_CODE: 0\n" +
            "INSTRUMENTATION_RESULT: stream=\n\nTime: 0.012\n\nOK (1 test)\n\n" +
            "INSTRUMENTATION_CODE: -1";

        AndroidInstrumentationCapture capture =
            await AcceptanceAndroidClientOperations.CaptureInstrumentationAsync(
                options,
                () => Task.FromResult(rawOutput),
                _ => throw new InvalidOperationException("Physical capture must not use logcat."));

        Assert.Contains(marker, capture.Output, StringComparison.Ordinal);
        Assert.True(Beacon.ProductionAcceptance.Program.IsSuccessfulInstrumentationOutput(
            capture.InvocationOutput));
    }

    [Fact]
    public async Task EmulatorInstrumentationRetainsLogcatMarkerCapture()
    {
        AcceptanceOptions options = ParseOptions(["--run-id", RunId]);
        var commands = new List<string>();

        AndroidInstrumentationCapture capture =
            await AcceptanceAndroidClientOperations.CaptureInstrumentationAsync(
                options,
                () => Task.FromResult("OK (1 test)"),
                args =>
                {
                    string command = string.Join(' ', args);
                    commands.Add(command);
                    return Task.FromResult(command == "logcat -c" ? string.Empty : "BEACON_GATE5_MARKER");
                });

        Assert.Equal(
            ["logcat -c", "logcat -d -v raw -s BeaconGate3:I *:S"],
            commands);
        Assert.Equal("OK (1 test)", capture.InvocationOutput);
        Assert.Contains("BEACON_GATE5_MARKER", capture.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PhysicalFailureEvidenceOmitsDeviceLogcat()
    {
        AcceptanceOptions options = ParseOptions(
            [
                "--run-id", RunId,
                "--android-client-kind", "physical",
                "--server-host", "192.0.2.10",
            ]);
        bool wroteLogcat = false;

        await AcceptanceFailureEvidenceOperations.CaptureAndroidLogcatAsync(
            options,
            _ => throw new InvalidOperationException("Physical evidence must not read logcat."),
            _ => wroteLogcat = true);

        Assert.False(wroteLogcat);
    }

    [Fact]
    public async Task EmulatorFailureEvidenceRetainsDeviceLogcat()
    {
        AcceptanceOptions options = ParseOptions(["--run-id", RunId]);
        var commands = new List<string>();
        string? writtenLogcat = null;

        await AcceptanceFailureEvidenceOperations.CaptureAndroidLogcatAsync(
            options,
            args =>
            {
                commands.Add(string.Join(' ', args));
                return Task.FromResult("emulator-logcat");
            },
            value => writtenLogcat = value);

        Assert.Equal(["logcat -d -v time"], commands);
        Assert.Equal("emulator-logcat", writtenLogcat);
    }

    [Fact]
    public async Task FailureCleanupAttemptsTopologyBeforeWaitEvidenceAndAndroidDespiteFailures()
    {
        var events = new List<string>();
        Func<string, bool, Action> action = (name, fail) => () =>
        {
            events.Add(name);
            if (fail) throw new InvalidOperationException(name);
        };
        Func<string, bool, Func<Task>> asyncOperation = (name, fail) => () =>
        {
            events.Add(name);
            return fail ? Task.FromException(new InvalidOperationException(name)) : Task.CompletedTask;
        };

        await AcceptanceFailureCleanupSequencer.RunBestEffortAsync(
            new AcceptanceFailureCleanupOperations(
                action("initiate-owned-process-termination", true),
                action("initiate-server-termination", true),
                asyncOperation("restore-physical", true),
                asyncOperation("remove-exact-lease", true),
                asyncOperation("await-process-and-server-exit", true),
                action("drain-server-output", true),
                [
                    action("dispose-first-resource", true),
                    action("dispose-remaining-resources", false),
                ],
                asyncOperation("capture-evidence", false),
                asyncOperation("android-cleanup", false)));

        Assert.Equal(
            [
                "initiate-owned-process-termination",
                "initiate-server-termination",
                "restore-physical",
                "remove-exact-lease",
                "await-process-and-server-exit",
                "drain-server-output",
                "dispose-first-resource",
                "dispose-remaining-resources",
                "capture-evidence",
                "android-cleanup",
            ],
            events);
    }

    private static AcceptanceOptions ParseOptions(params string[] args) =>
        AcceptanceOptions.Parse(["--repository-root", Path.GetTempPath(), .. args]);
}

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Beacon.Core.Clients;
using Microsoft.Win32.SafeHandles;

namespace Beacon.ProductionAcceptance;

internal static partial class Program
{
    private const string AndroidPackage = "dev.beacon.android";
    private const string InstrumentationRunner =
        "dev.beacon.android.test/androidx.test.runner.AndroidJUnitRunner";
    private const string InstrumentationClass =
        "dev.beacon.android.BeaconStreamCoreInstrumentationTest";

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && string.Equals(args[0], "display-guard", StringComparison.Ordinal))
            {
                return await ProductionDisplayGuardCli.RunAsync(args[1..]).ConfigureAwait(false);
            }
            AcceptanceOptions options = AcceptanceOptions.Parse(args);
            await ProductionDisplayGuardHost.RunAsync(options, RunAsync).ConfigureAwait(false);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static async Task RunAsync(AcceptanceOptions options)
    {
        string repositoryRoot = Path.GetFullPath(options.RepositoryRoot);
        string workerPath = Path.Combine(
            repositoryRoot,
            "native", "out", "build", "windows-x64",
            "Beacon.StreamWorker", "Debug", "Beacon.StreamWorker.exe");
        string serverPath = Path.Combine(
            repositoryRoot,
            "src", "Beacon.Server", "bin", "Debug", "net10.0-windows",
            "Beacon.Server.dll");
        string probePath = Path.Combine(
            repositoryRoot,
            "tests", "Beacon.SessionProbe", "bin", "Debug", "net10.0-windows",
            "Beacon.SessionProbe.exe");
        string appApk = Path.Combine(
            repositoryRoot,
            "src", "Beacon.Android", "app", "build", "outputs", "apk", "debug",
            "app-debug.apk");
        string testApk = Path.Combine(
            repositoryRoot,
            "src", "Beacon.Android", "app", "build", "outputs", "apk",
            "androidTest", "debug", "app-debug-androidTest.apk");
        string displayProbeProject = Path.Combine(
            repositoryRoot, "src", "Beacon.DisplayProbe", "Beacon.DisplayProbe.csproj");
        string displayIdCaptureProbePath = Path.Combine(
            repositoryRoot,
            "native", "out", "build", "windows-x64",
            "Beacon.StreamWorker.Tests", "Debug", "BeaconStreamWorkerDisplayIdCaptureProbe.exe");
        foreach (string path in new[]
                 {
                     workerPath, serverPath, probePath, appApk, testApk,
                     displayIdCaptureProbePath
                 })
        {
            Require(File.Exists(path), $"Required Gate 5 artifact is unavailable: {path}");
        }

        string runId = options.RunId;
        string clientId = $"gate5-emulator-{runId}";
        string gameId = $"gate5-session-probe-{runId}";
        string ownedRoot = Path.Combine(Path.GetTempPath(), $"beacon-gate5-{runId}");
        string evidenceDirectory = options.ResolveEvidenceDirectory();
        string probeEvidencePath = Path.Combine(ownedRoot, "session-probe.jsonl");
        string identityPath = Path.Combine(ownedRoot, "server-identity.pfx");
        string credentialPath = Path.Combine(ownedRoot, "credentials.json");
        string profilePath = Path.Combine(ownedRoot, "profiles.json");
        string benchmarkPath = Path.Combine(ownedRoot, "benchmarks.json");
        string displayMapPath = Path.Combine(ownedRoot, "display-name-map.json");
        string manualGamesPath = Path.Combine(ownedRoot, "manual-games.json");
        Directory.CreateDirectory(ownedRoot);
        Directory.CreateDirectory(evidenceDirectory);
        WriteManualGame(manualGamesPath, gameId, probePath);
        string credential = CreateCredential(credentialPath, clientId);
        WriteRegisteredProfile(profilePath, clientId);

        Process? server = null;
        ProcessOutputCapture? serverOutput = null;
        OwnedProcessJob? serverJob = null;
        HttpClient? http = null;
        ProbeEvidenceWatcher? probeEvidence = null;
        int? probeProcessId = null;
        string? clientDisplayId = null;
        bool success = false;
        try
        {
            await RunDisplayProbeAsync(repositoryRoot, displayProbeProject, "status")
                .ConfigureAwait(false);
            IReadOnlySet<string> activeVirtualDisplaysBefore = CaptureActiveVirtualDisplayNames();

            ProcessStartInfo startInfo = CreateServerStartInfo(
                serverPath,
                workerPath,
                identityPath,
                credentialPath,
                profilePath,
                benchmarkPath,
                displayMapPath,
                manualGamesPath,
                probeEvidencePath,
                runId,
                ownedRoot);
            server = new Process { StartInfo = startInfo };
            serverJob = new OwnedProcessJob();
            serverOutput = new ProcessOutputCapture();
            Require(server.Start(), "Could not start the production Beacon Server.");
            serverJob.Assign(server);
            serverOutput.Attach(server);
            string address = await AwaitWithHeartbeatAsync(
                serverOutput.ListeningAddress,
                "production Server readiness").ConfigureAwait(false);
            var listening = new Uri(address);
            string hostUrl = $"https://127.0.0.1:{listening.Port}";
            string emulatorUrl = $"https://10.0.2.2:{listening.Port}";
            http = CreateHttpClient(hostUrl);
            JsonElement identity = await GetJsonAsync(http, "/identity").ConfigureAwait(false);
            string fingerprint = RequiredString(identity, "publicKeyFingerprint");
            Require(FingerprintPattern().IsMatch(fingerprint), "Server fingerprint is invalid.");
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Beacon", credential);
            http.DefaultRequestHeaders.Add("X-Beacon-Client-Id", clientId);

            Console.WriteLine("BEACON_GATE5_STAGE prepared-display");
            JsonElement activeBeacon = await PostJsonAsync(
                http,
                $"/clients/{clientId}/beacon",
                new { active = true }).ConfigureAwait(false);
            clientDisplayId = RequiredString(activeBeacon, "displayId");
            JsonElement preparedSnapshot = await GetJsonAsync(http, "/admin/snapshot")
                .ConfigureAwait(false);
            WriteJson(Path.Combine(evidenceDirectory, "prepared-snapshot.json"), preparedSnapshot);
            ValidatePreparedDisplaySnapshot(preparedSnapshot, clientDisplayId);
            string preparedDisplayName = ResolvePreparedDisplayName(activeVirtualDisplaysBefore);
            Console.WriteLine($"BEACON_GATE5_PREPARED_DISPLAY_OK {preparedDisplayName}");

            Console.WriteLine("BEACON_GATE5_STAGE emulator-environment");
            await RunAdbAsync(options.Serial, "get-state").ConfigureAwait(false);
            await RunAdbAsync(options.Serial, "install", "-r", appApk).ConfigureAwait(false);
            await RunAdbAsync(options.Serial, "install", "-r", testApk).ConfigureAwait(false);
            await RunAdbAsync(options.Serial, "shell", "pm", "clear", AndroidPackage)
                .ConfigureAwait(false);
            await TransferCredentialAsync(options.Serial, credential).ConfigureAwait(false);
            await RunAdbAsync(options.Serial, "logcat", "-c").ConfigureAwait(false);

            Console.WriteLine("BEACON_GATE5_STAGE emulator-tcp-preflight");
            await AwaitWithHeartbeatAsync(
                RunAdbAsync(
                    options.Serial,
                    "shell", "toybox", "nc", "-z", "10.0.2.2", listening.Port.ToString()),
                "emulator TCP preflight").ConfigureAwait(false);
            Console.WriteLine("BEACON_GATE5_EMULATOR_TCP_REACHABLE");

            Console.WriteLine("BEACON_GATE5_STAGE certified-benchmark");
            string benchmarkOutput = await RunInstrumentationAsync(
                options.Serial,
                "gate4CertifiedBenchmarkEvidence",
                emulatorUrl,
                clientId,
                fingerprint,
                gameId).ConfigureAwait(false);
            RequireMarker(benchmarkOutput, "BEACON_GATE4_BENCHMARK_COMPLETE");

            Console.WriteLine("BEACON_GATE5_STAGE session-preflight");
            string preflightOutput = await RunInstrumentationAsync(
                options.Serial,
                "gate4CertifiedSessionPreflight",
                emulatorUrl,
                clientId,
                fingerprint,
                gameId).ConfigureAwait(false);
            RequireMarker(preflightOutput, "BEACON_GATE4_SESSION_PREFLIGHT");

            if (!string.IsNullOrWhiteSpace(options.BeforeConnectSignalPath))
            {
                Console.WriteLine(
                    $"BEACON_GATE5_AWAITING_CONNECT_SIGNAL {options.BeforeConnectSignalPath}");
                await AwaitSignalFileAsync(options.BeforeConnectSignalPath)
                    .ConfigureAwait(false);
                Console.WriteLine("BEACON_GATE5_CONNECT_SIGNAL_RECEIVED");
            }

            probeEvidence = new ProbeEvidenceWatcher(probeEvidencePath, runId);
            Console.WriteLine("BEACON_GATE5_STAGE production-connect");
            Task<string> firstInvocation = RunInstrumentationAsync(
                options.Serial,
                "gate5ProductionConnectSendAndDisconnect",
                emulatorUrl,
                clientId,
                fingerprint,
                gameId);
            Task firstConnectSignal = await Task.WhenAny(
                probeEvidence.Shown,
                firstInvocation).ConfigureAwait(false);
            if (firstConnectSignal == firstInvocation)
            {
                string earlyOutput = await firstInvocation.ConfigureAwait(false);
                throw new InvalidOperationException(
                    $"First emulator stream ended before the catalog application window appeared.{Environment.NewLine}{earlyOutput}");
            }
            JsonElement shown = await probeEvidence.Shown.ConfigureAwait(false);
            probeProcessId = RequiredInt(shown.GetProperty("details"), "processId");
            using Process probeProcess = Process.GetProcessById(probeProcessId.Value);
            Task probeExit = probeProcess.WaitForExitAsync();
            string firstOutput = await AwaitUnlessProcessExitedAsync(
                firstInvocation,
                probeExit,
                "first emulator stream").ConfigureAwait(false);
            JsonElement input = await AwaitUnlessProcessExitedAsync(
                probeEvidence.Input,
                probeExit,
                "F12 input evidence").ConfigureAwait(false);
            Require(RequiredString(input.GetProperty("details"), "key") == "F12",
                "The catalog application did not receive F12.");
            RequireMarker(firstOutput, "BEACON_GATE5_MOVING_FRAMES 12");
            RequireMarker(firstOutput, "BEACON_GATE5_INPUT_SENT F12");
            RequireMarker(firstOutput, "BEACON_GATE5_ACTIVE_DISCONNECT");

            JsonElement activeSnapshot = await GetJsonAsync(http, "/admin/snapshot")
                .ConfigureAwait(false);
            ValidateActiveSnapshot(activeSnapshot, clientId, clientDisplayId, gameId);
            ValidateProbeDisplay(shown, preparedDisplayName);
            ValidateOwnedProcessTree(server.Id, workerPath, probePath);
            WriteJson(Path.Combine(evidenceDirectory, "active-snapshot.json"), activeSnapshot);

            Console.WriteLine("BEACON_GATE5_STAGE production-reconnect-quit");
            string reconnectOutput = await RunInstrumentationAsync(
                options.Serial,
                "gate5ProductionReconnectAndQuit",
                emulatorUrl,
                clientId,
                fingerprint,
                gameId).ConfigureAwait(false);
            RequireMarker(reconnectOutput, "BEACON_GATE5_RECONNECT_FRESH_TICKET");
            RequireMarker(reconnectOutput, "BEACON_GATE5_QUIT_INACTIVE");
            await AwaitWithHeartbeatAsync(probeExit, "owned catalog process exit")
                .ConfigureAwait(false);

            JsonElement restoredSnapshot = await GetJsonAsync(http, "/admin/snapshot")
                .ConfigureAwait(false);
            ValidateRestoredSnapshot(restoredSnapshot, clientId, clientDisplayId);
            ValidateDisplayNameReleased(preparedDisplayName);
            WriteJson(Path.Combine(evidenceDirectory, "restored-snapshot.json"), restoredSnapshot);

            File.Copy(probeEvidencePath, Path.Combine(evidenceDirectory, "session-probe.jsonl"), true);
            File.WriteAllText(
                Path.Combine(evidenceDirectory, "instrumentation.log"),
                string.Join(Environment.NewLine, benchmarkOutput, preflightOutput, firstOutput, reconnectOutput));
            success = true;
        }
        finally
        {
            if (!success)
            {
                await CaptureFailureEvidenceAsync(
                    http,
                    options.Serial,
                    evidenceDirectory,
                    probeEvidencePath,
                    repositoryRoot).ConfigureAwait(false);
            }
            if (http is not null)
            {
                await TryPostAsync(http, $"/clients/{clientId}/quit", new { clientActive = false })
                    .ConfigureAwait(false);
                await TryPostAsync(http, $"/clients/{clientId}/emergency-restore", new { })
                    .ConfigureAwait(false);
            }
            if (probeProcessId is int processId)
            {
                TryTerminateExactProcess(processId, probePath);
            }
            probeEvidence?.Dispose();
            http?.Dispose();
            if (server is not null && !server.HasExited)
            {
                server.Kill(entireProcessTree: true);
                await server.WaitForExitAsync().ConfigureAwait(false);
            }
            serverJob?.Dispose();
            if (serverOutput is not null)
            {
                if (server is not null && server.HasExited)
                {
                    serverOutput.WaitForDrain();
                }
                File.WriteAllText(
                    Path.Combine(evidenceDirectory, "server-output.log"),
                    serverOutput.CombinedOutput);
                serverOutput.Dispose();
            }
            server?.Dispose();
            await RunDisplayProbeBestEffortAsync(
                repositoryRoot, displayProbeProject, "restore-physical").ConfigureAwait(false);
            await RunDisplayProbeBestEffortAsync(
                repositoryRoot, displayProbeProject, "remove", clientId).ConfigureAwait(false);
            await RemoveAndroidEvidenceBestEffortAsync(options.Serial).ConfigureAwait(false);
            if (!success)
            {
                Console.Error.WriteLine($"BEACON_GATE5_FAILURE_EVIDENCE {evidenceDirectory}");
            }
            if (Directory.Exists(ownedRoot))
            {
                Directory.Delete(ownedRoot, recursive: true);
            }
        }
    }

    private static ProcessStartInfo CreateServerStartInfo(
        string serverPath,
        string workerPath,
        string identityPath,
        string credentialPath,
        string profilePath,
        string benchmarkPath,
        string displayMapPath,
        string manualGamesPath,
        string probeEvidencePath,
        string runId,
        string ownedRoot)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Path.GetDirectoryName(serverPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add(serverPath);
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add("https://0.0.0.0:0");
        startInfo.Environment["Beacon__Streaming__WorkerPath"] = workerPath;
        startInfo.Environment["Beacon__Security__IdentityPath"] = identityPath;
        startInfo.Environment["Beacon__Security__CredentialsPath"] = credentialPath;
        startInfo.Environment["Beacon__Profiles__Path"] = profilePath;
        startInfo.Environment["Beacon__Benchmarks__Path"] = benchmarkPath;
        startInfo.Environment["Beacon__Displays__NameMapPath"] = displayMapPath;
        startInfo.Environment["Beacon__Games__ManualPath"] = manualGamesPath;
        startInfo.Environment["Beacon__Games__SteamRoot"] = Path.Combine(ownedRoot, "steam");
        startInfo.Environment["Beacon__Games__HeroicRoot"] = Path.Combine(ownedRoot, "heroic");
        startInfo.Environment["Beacon__Games__HydraDatabasePath"] = Path.Combine(ownedRoot, "hydra.db");
        startInfo.Environment["BEACON_SESSION_PROBE_EVIDENCE"] = probeEvidencePath;
        startInfo.Environment["BEACON_SESSION_PROBE_RUN_ID"] = runId;
        startInfo.Environment["Logging__Console__FormatterName"] = "json";
        startInfo.Environment["Logging__LogLevel__Default"] = "Information";
        startInfo.Environment["Logging__LogLevel__Microsoft.AspNetCore"] = "Warning";
        return startInfo;
    }

    private static async Task<string> RunInstrumentationAsync(
        string serial,
        string method,
        string serverUrl,
        string clientId,
        string fingerprint,
        string gameId)
    {
        await RunAdbAsync(serial, "logcat", "-c").ConfigureAwait(false);
        string instrumentationOutput = await AwaitWithHeartbeatAsync(
            RunProcessAsync(
                "adb",
                [
                    "-s", serial,
                    "shell", "am", "instrument", "-w", "-r",
                    "-e", "class", $"{InstrumentationClass}#{method}",
                    "-e", "serverUrl", serverUrl,
                    "-e", "clientId", clientId,
                    "-e", "inputMarker", "BEACON-GATE5-INPUT",
                    "-e", "serverPublicKeyFingerprint", fingerprint,
                    "-e", "gameId", gameId,
                    InstrumentationRunner,
                ]),
            $"Android instrumentation {method}").ConfigureAwait(false);
        string markerOutput = await RunAdbAsync(
            serial,
            "logcat", "-d", "-v", "raw", "-s", "BeaconGate3:I", "*:S")
            .ConfigureAwait(false);
        string output = string.Join(
            Environment.NewLine,
            new[] { instrumentationOutput, markerOutput }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        Require(
            InstrumentationSuccessPattern().IsMatch(instrumentationOutput)
            && !InstrumentationFailurePattern().IsMatch(instrumentationOutput),
            $"Android instrumentation '{method}' failed.{Environment.NewLine}{output}");
        return output;
    }

    private static async Task<string> RunAdbAsync(string serial, params string[] args) =>
        await RunProcessAsync("adb", ["-s", serial, .. args]).ConfigureAwait(false);

    private static async Task TransferCredentialAsync(string serial, string credential)
    {
        await RunAdbAsync(serial, "shell", "run-as", AndroidPackage, "mkdir", "-p", "files")
            .ConfigureAwait(false);
        var startInfo = CreateProcessStartInfo(
            "adb",
            [
                "-s", serial,
                "exec-in", "run-as", AndroidPackage,
                "tee", "files/beacon-gate3-client-credential",
            ],
            redirectInput: true);
        using var process = new Process { StartInfo = startInfo };
        Require(process.Start(), "Could not start private Android credential transfer.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(credential).ConfigureAwait(false);
        process.StandardInput.Close();
        await process.WaitForExitAsync().ConfigureAwait(false);
        string errorText = await error.ConfigureAwait(false);
        _ = await output.ConfigureAwait(false);
        Require(process.ExitCode == 0, $"Private Android credential transfer failed: {errorText}");
    }

    private static async Task RemoveAndroidEvidenceBestEffortAsync(string serial)
    {
        try
        {
            await RunAdbAsync(
                serial,
                "shell", "run-as", AndroidPackage, "rm", "-f",
                "files/beacon-gate3-client-credential",
                "files/beacon-gate3-ticket-evidence").ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static async Task<string> RunProcessAsync(string fileName, IReadOnlyList<string> args)
    {
        ProcessStartInfo startInfo = CreateProcessStartInfo(fileName, args, redirectInput: false);
        using var process = new Process { StartInfo = startInfo };
        Require(process.Start(), $"Could not start '{fileName}'.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        string output = await standardOutput.ConfigureAwait(false);
        string error = await standardError.ConfigureAwait(false);
        string combined = string.Join(
            Environment.NewLine,
            new[] { output, error }.Where(value => !string.IsNullOrWhiteSpace(value)));
        Require(process.ExitCode == 0, $"'{fileName}' exited with {process.ExitCode}.{Environment.NewLine}{combined}");
        return combined;
    }

    private static ProcessStartInfo CreateProcessStartInfo(
        string fileName,
        IReadOnlyList<string> args,
        bool redirectInput)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = redirectInput,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string arg in args) startInfo.ArgumentList.Add(arg);
        return startInfo;
    }

    private static HttpClient CreateHttpClient(string baseAddress)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        return new HttpClient(handler)
        {
            BaseAddress = new Uri(baseAddress),
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path)
    {
        using HttpResponseMessage response = await client.GetAsync(path).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Require(response.IsSuccessStatusCode, $"GET {path} failed: {(int)response.StatusCode} {body}");
        using JsonDocument document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    private static async Task TryPostAsync(HttpClient client, string path, object body)
    {
        try
        {
            using HttpResponseMessage response = await client.PostAsJsonAsync(path, body)
                .ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static async Task<JsonElement> PostJsonAsync(HttpClient client, string path, object body)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(path, body)
            .ConfigureAwait(false);
        string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Require(
            response.IsSuccessStatusCode,
            $"POST {path} failed: {(int)response.StatusCode} {responseBody}");
        using JsonDocument document = JsonDocument.Parse(responseBody);
        return document.RootElement.Clone();
    }

    private static async Task CaptureFailureEvidenceAsync(
        HttpClient? client,
        string serial,
        string evidenceDirectory,
        string probeEvidencePath,
        string repositoryRoot)
    {
        var errors = new List<string>();
        if (client is not null)
        {
            try
            {
                JsonElement snapshot = await GetJsonAsync(client, "/admin/snapshot")
                    .ConfigureAwait(false);
                WriteJson(Path.Combine(evidenceDirectory, "failure-snapshot.json"), snapshot);
            }
            catch (Exception error)
            {
                errors.Add($"snapshot: {error.Message}");
            }
        }

        try
        {
            string logcat = await RunAdbAsync(serial, "logcat", "-d", "-v", "time")
                .ConfigureAwait(false);
            File.WriteAllText(Path.Combine(evidenceDirectory, "android-logcat.log"), logcat);
        }
        catch (Exception error)
        {
            errors.Add($"logcat: {error.Message}");
        }

        try
        {
            if (File.Exists(probeEvidencePath))
            {
                File.Copy(
                    probeEvidencePath,
                    Path.Combine(evidenceDirectory, "session-probe.jsonl"),
                    overwrite: true);
            }
        }
        catch (Exception error)
        {
            errors.Add($"probe: {error.Message}");
        }

        try
        {
            string? monitor = ReadLatestProbeMonitor(probeEvidencePath);
            if (monitor is not null)
            {
                string executable = Path.Combine(
                    repositoryRoot,
                    "native", "out", "build", "windows-x64",
                    "Beacon.StreamWorker.Tests", "Debug",
                    "BeaconStreamWorkerWgcCaptureProbe.exe");
                string output;
                try
                {
                    output = await RunProcessAsync(executable, [monitor, "1280", "720"])
                        .ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    output = error.ToString();
                }
                File.WriteAllText(
                    Path.Combine(evidenceDirectory, "wgc-capture-probe.log"),
                    output);

                string duplicationExecutable = Path.Combine(
                    repositoryRoot,
                    "native", "out", "build", "windows-x64",
                    "Beacon.StreamWorker.Tests", "Debug",
                    "BeaconStreamWorkerDxgiDuplicationProbe.exe");
                try
                {
                    output = await RunProcessAsync(duplicationExecutable, [monitor])
                        .ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    output = error.ToString();
                }
                File.WriteAllText(
                    Path.Combine(evidenceDirectory, "dxgi-duplication-probe.log"),
                    output);

                string displayIdExecutable = Path.Combine(
                    repositoryRoot,
                    "native", "out", "build", "windows-x64",
                    "Beacon.StreamWorker.Tests", "Debug",
                    "BeaconStreamWorkerDisplayIdCaptureProbe.exe");
                try
                {
                    output = await RunProcessAsync(displayIdExecutable, [monitor])
                        .ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    output = error.ToString();
                }
                File.WriteAllText(
                    Path.Combine(evidenceDirectory, "display-id-capture-probe.log"),
                    output);
            }
        }
        catch (Exception error)
        {
            errors.Add($"wgc-probe: {error.Message}");
        }

        if (errors.Count > 0)
        {
            File.WriteAllLines(Path.Combine(evidenceDirectory, "failure-capture-errors.log"), errors);
        }
    }

    private static string? ReadLatestProbeMonitor(string path)
    {
        if (!File.Exists(path)) return null;
        string? monitor = null;
        foreach (string line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement record = document.RootElement;
            if (record.TryGetProperty("eventName", out JsonElement eventName)
                && eventName.GetString() == "shown"
                && record.TryGetProperty("details", out JsonElement details)
                && details.TryGetProperty("monitor", out JsonElement value))
            {
                monitor = value.GetString();
            }
        }
        return monitor;
    }

    private static void ValidateActiveSnapshot(
        JsonElement snapshot,
        string clientId,
        string displayId,
        string gameId)
    {
        JsonElement host = snapshot.GetProperty("host");
        Require(RequiredString(host, "mode") == "windows", "Gate 5 did not use Windows production composition.");
        Require(
            RequiredString(host, "streamingBackend") == "StreamWorkerStreamingBackend",
            "Gate 5 did not use the production StreamWorker backend.");
        JsonElement session = snapshot.GetProperty("sessions").EnumerateArray()
            .Single(value => RequiredString(value, "appId") == gameId);
        JsonElement displayPlan = session.GetProperty("display");
        Require(RequiredString(displayPlan, "displayId") == displayId, "The plan targeted the wrong display lease.");
        JsonElement display = snapshot.GetProperty("display");
        ValidateDisplayHealth(display);
        JsonElement path = display.GetProperty("paths").EnumerateArray()
            .Single(value => RequiredString(value, "displayId") == displayId);
        Require(RequiredString(path, "kind") == "Virtual", "The planned display is not virtual.");
        Require(path.GetProperty("isPrimary").GetBoolean(), "The planned virtual display is not primary.");
        Require(path.GetProperty("width").GetInt32() == displayPlan.GetProperty("width").GetInt32(),
            "The active display width differs from the immutable plan.");
        Require(path.GetProperty("height").GetInt32() == displayPlan.GetProperty("height").GetInt32(),
            "The active display height differs from the immutable plan.");
        Require(snapshot.GetProperty("ownership").EnumerateArray().Any(value =>
            RequiredString(value, "appId") == gameId
            && value.GetProperty("launchedProcessRunning").GetBoolean()),
            "The launched catalog process is not owned by the session.");
        Require(snapshot.GetProperty("streams").EnumerateArray().Any(value =>
            RequiredString(value, "clientId") == clientId
            && RequiredString(value, "state") == "running"),
            "The production stream runtime was not retained across active disconnect.");
        Require(snapshot.GetProperty("diagnostics").EnumerateArray().Any(value =>
            RequiredString(value, "operation") == "input.forwarded"),
            "The authenticated input stream did not publish forwarding evidence.");
    }

    private static void ValidatePreparedDisplaySnapshot(JsonElement snapshot, string displayId)
    {
        JsonElement display = snapshot.GetProperty("display");
        ValidateDisplayHealth(display);
        Require(display.GetProperty("physicalPrimaryVerified").GetBoolean(),
            "The physical display was not primary while the virtual display was prepared.");
        JsonElement path = display.GetProperty("paths").EnumerateArray()
            .Single(value => RequiredString(value, "displayId") == displayId);
        Require(RequiredString(path, "kind") == "Virtual", "The prepared display is not virtual.");
        Require(!path.GetProperty("isPrimary").GetBoolean(),
            "The prepared virtual display became primary before session launch.");
        Require(path.GetProperty("width").GetInt32() == 2560
                && path.GetProperty("height").GetInt32() == 1600
                && path.GetProperty("refreshHz").GetInt32() == 120,
            "Beacon did not retain the prepared 2560x1600@120 display mode.");
    }

    private static void ValidateRestoredSnapshot(
        JsonElement snapshot,
        string clientId,
        string displayId)
    {
        JsonElement display = snapshot.GetProperty("display");
        ValidateDisplayHealth(display);
        Require(display.GetProperty("physicalPrimaryVerified").GetBoolean(),
            "Physical primary was not verified after inactive quit.");
        Require(!display.GetProperty("paths").EnumerateArray().Any(value =>
            RequiredString(value, "displayId") == displayId),
            "The per-client virtual display remained after inactive quit and owned-work termination.");
        Require(!snapshot.GetProperty("ownership").EnumerateArray().Any(value =>
            RequiredClientId(value) == clientId),
            "Session ownership remained after owned work termination.");
        Require(!snapshot.GetProperty("streams").EnumerateArray().Any(value =>
            RequiredString(value, "clientId") == clientId),
            "The streaming runtime remained after inactive quit.");
        Require(snapshot.GetProperty("diagnostics").EnumerateArray().Any(value =>
            RequiredString(value, "operation") == "lease.cleanup.removed"),
            "The inactive AND no-owned-work cleanup decision was not journaled.");
    }

    private static void ValidateDisplayHealth(JsonElement display)
    {
        Require(display.GetProperty("driverReady").GetBoolean(),
            $"SudoVDA was not ready: {RequiredString(display, "diagnostic")}");
        Require(display.GetProperty("topologyAvailable").GetBoolean(),
            $"Windows display topology was unavailable: {RequiredString(display, "diagnostic")}");
        Require(!display.GetProperty("mirrorMode").GetBoolean(), "Gate 5 entered mirror mode.");
    }

    private static void ValidateProbeDisplay(JsonElement shown, string preparedDisplayName)
    {
        JsonElement details = shown.GetProperty("details");
        Require(
            string.Equals(
                RequiredString(details, "monitor"),
                preparedDisplayName,
                StringComparison.OrdinalIgnoreCase),
            "The catalog application opened on a display other than the planned virtual display.");
        Require(details.GetProperty("primary").GetBoolean(),
            "The catalog application did not observe its virtual display as primary.");
    }

    private static void ValidateDisplayNameReleased(string preparedDisplayName)
    {
        Require(!CaptureActiveVirtualDisplayNames().Contains(preparedDisplayName),
            $"The released Windows display source {preparedDisplayName} remained active.");
    }

    private static void ValidateOwnedProcessTree(int serverProcessId, string workerPath, string probePath)
    {
        var children = Process.GetProcesses()
            .Where(process => TryGetParentProcessId(process, out int parent) && parent == serverProcessId)
            .ToArray();
        try
        {
            string[] childPaths = children.Select(RequiredProcessPath).ToArray();
            Require(childPaths.Count(path => SamePath(path, workerPath)) == 1,
                "The production Server did not own exactly one Beacon StreamWorker.");
            Require(childPaths.Count(path => SamePath(path, probePath)) == 1,
                "The production Server did not own exactly one catalog probe.");
            string[] unexpected = childPaths
                .Where(path => !IsExpectedProductionChildPath(path, workerPath, probePath))
                .ToArray();
            Require(unexpected.Length == 0,
                $"The production Server owned an undeclared child: {string.Join(", ", unexpected)}");
        }
        finally
        {
            foreach (Process child in children) child.Dispose();
        }
    }

    internal static bool IsExpectedProductionChildPath(
        string childPath,
        string workerPath,
        string probePath) =>
        SamePath(childPath, workerPath) ||
        SamePath(childPath, probePath) ||
        SamePath(childPath, Path.Combine(Environment.SystemDirectory, "conhost.exe"));

    private static async Task<string> RunDisplayProbeAsync(
        string repositoryRoot,
        string projectPath,
        params string[] command)
    {
        string output = await RunProcessAsync(
            "dotnet",
            ["run", "--project", projectPath, "--no-build", "--", .. command])
            .ConfigureAwait(false);
        Require(output.Contains("physicalPrimaryVerified=True", StringComparison.Ordinal),
            $"DisplayProbe did not verify physical primary.{Environment.NewLine}{output}");
        Require(output.Contains("driverReady=True", StringComparison.Ordinal),
            $"DisplayProbe did not verify SudoVDA readiness.{Environment.NewLine}{output}");
        Require(output.Contains("mirrorMode=False", StringComparison.Ordinal),
            $"DisplayProbe found mirror mode before or after Gate 5.{Environment.NewLine}{output}");
        return output;
    }

    private static string ResolvePreparedDisplayName(IReadOnlySet<string> activeVirtualDisplaysBefore)
    {
        string[] addedDisplays = CaptureActiveVirtualDisplayNames()
            .Where(displayName => !activeVirtualDisplaysBefore.Contains(displayName))
            .ToArray();
        Require(
            addedDisplays.Length == 1,
            $"Expected one prepared Windows display source, found [{string.Join(", ", addedDisplays)}].");
        return addedDisplays[0];
    }

    private static IReadOnlySet<string> CaptureActiveVirtualDisplayNames()
    {
        var displays = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (uint index = 0; ; index++)
        {
            DisplayDevice device = DisplayDevice.Create();
            if (!EnumDisplayDevices(null, index, ref device, 0)) break;
            if ((device.StateFlags & DisplayDeviceActive) != 0 &&
                device.DeviceString.Contains(
                    "SudoMaker Virtual Display Adapter",
                    StringComparison.OrdinalIgnoreCase))
            {
                displays.Add(device.DeviceName);
            }
        }
        return displays;
    }

    private static async Task AwaitSignalFileAsync(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)!;
        string fileName = Path.GetFileName(fullPath);
        Directory.CreateDirectory(directory);
        if (File.Exists(fullPath)) return;

        var signaled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new FileSystemWatcher(directory, fileName)
        {
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
        };
        FileSystemEventHandler observe = (_, _) => signaled.TrySetResult();
        RenamedEventHandler observeRename = (_, _) => signaled.TrySetResult();
        watcher.Created += observe;
        watcher.Renamed += observeRename;
        if (File.Exists(fullPath)) signaled.TrySetResult();
        await AwaitWithHeartbeatAsync(signaled.Task, "before-connect signal")
            .ConfigureAwait(false);
    }

    private static async Task RunDisplayProbeBestEffortAsync(
        string repositoryRoot,
        string projectPath,
        params string[] command)
    {
        _ = repositoryRoot;
        try
        {
            await RunProcessAsync(
                "dotnet",
                ["run", "--project", projectPath, "--no-build", "--", .. command])
                .ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static async Task<T> AwaitUnlessProcessExitedAsync<T>(
        Task<T> operation,
        Task processExit,
        string description)
    {
        while (!operation.IsCompleted)
        {
            Task heartbeat = Task.Delay(TimeSpan.FromSeconds(15));
            Task completed = await Task.WhenAny(operation, processExit, heartbeat).ConfigureAwait(false);
            if (completed == processExit)
            {
                throw new InvalidOperationException($"The catalog application exited while awaiting {description}.");
            }
            if (completed == heartbeat)
            {
                Console.WriteLine($"BEACON_GATE5_HEARTBEAT awaiting {description}");
            }
        }
        return await operation.ConfigureAwait(false);
    }

    private static async Task<T> AwaitWithHeartbeatAsync<T>(Task<T> operation, string description)
    {
        while (!operation.IsCompleted)
        {
            Task heartbeat = Task.Delay(TimeSpan.FromSeconds(15));
            if (await Task.WhenAny(operation, heartbeat).ConfigureAwait(false) == heartbeat)
            {
                Console.WriteLine($"BEACON_GATE5_HEARTBEAT awaiting {description}");
            }
        }
        return await operation.ConfigureAwait(false);
    }

    private static async Task AwaitWithHeartbeatAsync(Task operation, string description)
    {
        while (!operation.IsCompleted)
        {
            Task heartbeat = Task.Delay(TimeSpan.FromSeconds(15));
            if (await Task.WhenAny(operation, heartbeat).ConfigureAwait(false) == heartbeat)
            {
                Console.WriteLine($"BEACON_GATE5_HEARTBEAT awaiting {description}");
            }
        }
        await operation.ConfigureAwait(false);
    }

    private static string CreateCredential(string path, string clientId)
    {
        byte[] credential = RandomNumberGenerator.GetBytes(32);
        byte[] salt = RandomNumberGenerator.GetBytes(32);
        byte[] material = new byte[salt.Length + credential.Length];
        byte[]? hash = null;
        try
        {
            salt.CopyTo(material, 0);
            credential.CopyTo(material, salt.Length);
            hash = SHA256.HashData(material);
            File.WriteAllText(path, JsonSerializer.Serialize(new[]
            {
                new
                {
                    clientId,
                    salt = Convert.ToBase64String(salt),
                    hash = Convert.ToBase64String(hash),
                    revoked = false,
                },
            }));
            return Convert.ToBase64String(credential);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(credential);
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(material);
            if (hash is not null) CryptographicOperations.ZeroMemory(hash);
        }
    }

    private static void WriteRegisteredProfile(string path, string clientId)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            version = 1,
            profiles = new[]
            {
                ClientProfile.CreateDefault(new ClientId(clientId), clientId),
            },
        }, options));
    }

    private static void WriteManualGame(string path, string gameId, string probePath)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            games = new[]
            {
                new
                {
                    id = gameId,
                    title = "Beacon Production Session Probe",
                    source = "manual",
                    launch = new { type = "process", command = probePath },
                    processHints = new
                    {
                        executableName = Path.GetFileName(probePath),
                        workingDirectory = Path.GetDirectoryName(probePath),
                    },
                },
            },
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void WriteJson(string path, JsonElement value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));

    private static void RequireMarker(string output, string marker) =>
        Require(output.Contains(marker, StringComparison.Ordinal), $"Missing acceptance marker: {marker}");

    private static string RequiredString(JsonElement value, string property)
    {
        string? result = value.GetProperty(property).GetString();
        Require(!string.IsNullOrWhiteSpace(result), $"Required JSON property '{property}' is empty.");
        return result!;
    }

    private static int RequiredInt(JsonElement value, string property) =>
        value.GetProperty(property).GetInt32();

    private static string RequiredClientId(JsonElement ownership)
    {
        JsonElement clientId = ownership.GetProperty("clientId");
        return clientId.ValueKind == JsonValueKind.String
            ? clientId.GetString() ?? string.Empty
            : RequiredString(clientId, "value");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static string RequiredProcessPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName
                ?? throw new InvalidOperationException($"Process {process.Id} has no executable path.");
        }
        catch (Win32Exception error)
        {
            throw new InvalidOperationException($"Could not inspect process {process.Id}.", error);
        }
    }

    private static void TryTerminateExactProcess(int processId, string expectedPath)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            if (!SamePath(RequiredProcessPath(process), expectedPath)) return;
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static bool TryGetParentProcessId(Process process, out int parentProcessId)
    {
        parentProcessId = 0;
        try
        {
            var information = new ProcessBasicInformation();
            int status = NtQueryInformationProcess(
                process.Handle,
                0,
                ref information,
                Marshal.SizeOf<ProcessBasicInformation>(),
                out _);
            if (status != 0) return false;
            parentProcessId = information.InheritedFromUniqueProcessId.ToInt32();
            return parentProcessId > 0;
        }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception or OverflowException)
        {
            return false;
        }
    }

    [GeneratedRegex("^[0-9A-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex FingerprintPattern();

    [GeneratedRegex("INSTRUMENTATION_STATUS:\\s+numtests=[1-9][0-9]*[\\s\\S]*INSTRUMENTATION_CODE:\\s+-1", RegexOptions.CultureInvariant)]
    private static partial Regex InstrumentationSuccessPattern();

    [GeneratedRegex("FAILURES!!!|INSTRUMENTATION_FAILED|INSTRUMENTATION_ABORTED|Process crashed", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex InstrumentationFailurePattern();

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2A;
        public IntPtr Reserved2B;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        ref ProcessBasicInformation processInformation,
        int processInformationLength,
        out int returnLength);

    private const uint DisplayDeviceActive = 0x00000001;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(
        string? device,
        uint deviceNumber,
        ref DisplayDevice displayDevice,
        uint flags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Cb;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public uint StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;

        public static DisplayDevice Create() => new()
        {
            Cb = Marshal.SizeOf<DisplayDevice>(),
            DeviceName = string.Empty,
            DeviceString = string.Empty,
            DeviceId = string.Empty,
            DeviceKey = string.Empty,
        };
    }
}

internal sealed record AcceptanceOptions(
    string RepositoryRoot,
    string RunId,
    string Serial,
    string? EvidenceDirectory,
    string? BeforeConnectSignalPath)
{
    public string ResolveEvidenceDirectory() => string.IsNullOrWhiteSpace(EvidenceDirectory)
        ? Path.Combine(Path.GetFullPath(RepositoryRoot), ".artifacts", $"gate5-production-{RunId}")
        : Path.GetFullPath(EvidenceDirectory);

    public static AcceptanceOptions Parse(IReadOnlyList<string> args)
    {
        string? repositoryRoot = null;
        string runId = Guid.NewGuid().ToString("N");
        string serial = "emulator-5554";
        string? evidenceDirectory = null;
        string? beforeConnectSignalPath = null;
        for (int index = 0; index < args.Count; index++)
        {
            string value = args[index];
            string ReadValue()
            {
                if (++index >= args.Count) throw new ArgumentException($"Missing value after {value}.");
                return args[index];
            }
            switch (value)
            {
                case "--repository-root":
                    repositoryRoot = ReadValue();
                    break;
                case "--serial":
                    serial = ReadValue();
                    break;
                case "--run-id":
                    runId = ReadValue();
                    break;
                case "--evidence-directory":
                    evidenceDirectory = ReadValue();
                    break;
                case "--before-connect-signal":
                    beforeConnectSignalPath = ReadValue();
                    break;
                default:
                    throw new ArgumentException($"Unknown Gate 5 option '{value}'.");
            }
        }
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            throw new ArgumentException("--repository-root is required.");
        }
        if (!Guid.TryParseExact(runId, "N", out Guid parsedRunId) ||
            !string.Equals(runId, parsedRunId.ToString("N"), StringComparison.Ordinal))
        {
            throw new ArgumentException("--run-id must be a lowercase 32-character GUID.");
        }
        return new AcceptanceOptions(
            repositoryRoot,
            runId,
            serial,
            evidenceDirectory,
            beforeConnectSignalPath);
    }
}

internal sealed class ProbeEvidenceWatcher : IDisposable
{
    private readonly string path;
    private readonly string runId;
    private readonly FileSystemWatcher watcher;
    private readonly Lock gate = new();
    private readonly TaskCompletionSource<JsonElement> shown = NewSource();
    private readonly TaskCompletionSource<JsonElement> input = NewSource();

    public ProbeEvidenceWatcher(string path, string runId)
    {
        this.path = Path.GetFullPath(path);
        this.runId = runId;
        string directory = Path.GetDirectoryName(this.path)!;
        Directory.CreateDirectory(directory);
        watcher = new FileSystemWatcher(directory, Path.GetFileName(this.path))
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        watcher.Created += OnEvidenceChanged;
        watcher.Changed += OnEvidenceChanged;
        Refresh();
    }

    public Task<JsonElement> Shown => shown.Task;
    public Task<JsonElement> Input => input.Task;

    public void Dispose()
    {
        watcher.EnableRaisingEvents = false;
        watcher.Created -= OnEvidenceChanged;
        watcher.Changed -= OnEvidenceChanged;
        watcher.Dispose();
    }

    private void OnEvidenceChanged(object sender, FileSystemEventArgs eventArgs) => Refresh();

    private void Refresh()
    {
        lock (gate)
        {
            if (!File.Exists(path)) return;
            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (IOException)
            {
                return;
            }
            foreach (string line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    using JsonDocument document = JsonDocument.Parse(line);
                    JsonElement record = document.RootElement;
                    if (!string.Equals(record.GetProperty("runId").GetString(), runId, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    string? eventName = record.GetProperty("eventName").GetString();
                    if (eventName == "shown") shown.TrySetResult(record.Clone());
                    if (eventName == "input") input.TrySetResult(record.Clone());
                }
                catch (JsonException)
                {
                }
            }
        }
    }

    private static TaskCompletionSource<JsonElement> NewSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class ProcessOutputCapture : IDisposable
{
    private readonly ConcurrentQueue<string> standardOutput = new();
    private readonly ConcurrentQueue<string> standardError = new();
    private readonly TaskCompletionSource<string> listening =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object callbackGate = new();
    private Process? process;
    private int activeCallbacks;
    private bool finalizing;

    public Task<string> ListeningAddress => listening.Task;
    public string CombinedOutput => string.Join(
        Environment.NewLine,
        new[]
        {
            string.Join(Environment.NewLine, standardOutput),
            string.Join(Environment.NewLine, standardError),
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

    public void Attach(Process value)
    {
        if (process is not null) throw new InvalidOperationException("Process capture can be attached once.");
        process = value;
        process.EnableRaisingEvents = true;
        process.Exited += OnExited;
        process.OutputDataReceived += OnOutput;
        process.ErrorDataReceived += OnError;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (process.HasExited) OnExited(process, EventArgs.Empty);
    }

    public void WaitForDrain()
    {
        if (process is null || !process.HasExited)
        {
            throw new InvalidOperationException("Process output can be drained only after exit.");
        }
        lock (callbackGate)
        {
            finalizing = true;
        }
        try { process.CancelOutputRead(); } catch (InvalidOperationException) { }
        try { process.CancelErrorRead(); } catch (InvalidOperationException) { }
        lock (callbackGate)
        {
            while (activeCallbacks != 0) Monitor.Wait(callbackGate);
        }
    }

    public void Dispose()
    {
        if (process is not null)
        {
            process.Exited -= OnExited;
            process.OutputDataReceived -= OnOutput;
            process.ErrorDataReceived -= OnError;
            process = null;
        }
    }

    private void OnExited(object? sender, EventArgs eventArgs)
    {
        if (!TryEnter()) return;
        try
        {
            listening.TrySetException(new InvalidOperationException(
                "Production Beacon Server exited before structured Kestrel readiness."));
        }
        finally
        {
            Exit();
        }
    }

    private void OnOutput(object sender, DataReceivedEventArgs eventArgs)
    {
        if (!TryEnter()) return;
        try
        {
            if (eventArgs.Data is null) return;
            standardOutput.Enqueue(eventArgs.Data);
            if (TryGetListeningAddress(eventArgs.Data, out string? address))
            {
                listening.TrySetResult(address!);
            }
        }
        finally
        {
            Exit();
        }
    }

    private void OnError(object sender, DataReceivedEventArgs eventArgs)
    {
        if (!TryEnter()) return;
        try
        {
            if (eventArgs.Data is not null) standardError.Enqueue(eventArgs.Data);
        }
        finally
        {
            Exit();
        }
    }

    private bool TryEnter()
    {
        lock (callbackGate)
        {
            if (finalizing) return false;
            activeCallbacks++;
            return true;
        }
    }

    private void Exit()
    {
        lock (callbackGate)
        {
            activeCallbacks--;
            if (activeCallbacks == 0) Monitor.PulseAll(callbackGate);
        }
    }

    private static bool TryGetListeningAddress(string line, out string? address)
    {
        address = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("EventId", out JsonElement eventId)
                || eventId.GetInt32() != 14
                || !root.TryGetProperty("State", out JsonElement state)
                || !state.TryGetProperty("address", out JsonElement value))
            {
                return false;
            }
            address = value.GetString();
            return !string.IsNullOrWhiteSpace(address);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

internal sealed class OwnedProcessJob : IDisposable
{
    private const uint JobObjectLimitKillOnClose = 0x00002000;
    private IntPtr handle;

    public OwnedProcessJob()
    {
        handle = CreateJobObject(IntPtr.Zero, null);
        if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        var information = new ExtendedLimitInformation();
        information.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnClose;
        int length = Marshal.SizeOf<ExtendedLimitInformation>();
        IntPtr buffer = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(information, buffer, false);
            if (!SetInformationJobObject(handle, 9, buffer, checked((uint)length)))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        catch
        {
            CloseHandle(handle);
            handle = IntPtr.Zero;
            throw;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Assign(Process process)
    {
        if (!AssignProcessToJobObject(handle, process.Handle))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public void Dispose()
    {
        if (handle == IntPtr.Zero) return;
        IntPtr value = handle;
        handle = IntPtr.Zero;
        CloseHandle(value);
        GC.SuppressFinalize(this);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public IntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformation
    {
        public BasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        IntPtr job,
        int informationClass,
        IntPtr information,
        uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr value);
}

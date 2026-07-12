using System.Diagnostics;
using Beacon.FakeEndpoint;

namespace Beacon.Server.Tests;

public sealed class FakeEndpointLiveTests
{
    [Fact]
    public async Task FailedServerStartupDeletesIsolatedState()
    {
        string stateDirectory = Path.Combine(
            Path.GetTempPath(),
            $"beacon-fake-endpoint-failure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stateDirectory);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BeaconServerProcess.StartAsync(
                stateDirectory,
                Path.Combine(stateDirectory, "missing-server.dll")));

        Assert.False(Directory.Exists(stateDirectory));
    }

    [Fact]
    public async Task ZFoldScriptCompletesAgainstRealServerProcess()
    {
        string stateDirectory = Path.Combine(
            Path.GetTempPath(),
            $"beacon-fake-endpoint-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stateDirectory);

        await using var server = await BeaconServerProcess.StartAsync(stateDirectory);
        using var client = new HttpClient { BaseAddress = server.Address };
        var runner = new FakeEndpointRunner(client);

        FakeEndpointResult result = await runner.RunAsync(
            FakeEndpointScript.CreateZFold7Default(),
            CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal(
            [
                "POST /clients/hello",
                "GET /clients/z-fold-7/profile",
                "PATCH /clients/z-fold-7/profile",
                "POST /clients/z-fold-7/capabilities",
                "POST /clients/z-fold-7/telemetry",
                "POST /clients/z-fold-7/beacon",
                "POST /clients/z-fold-7/plan",
                "POST /clients/z-fold-7/launch",
                "POST /clients/z-fold-7/input",
                "POST /clients/z-fold-7/reconnect",
                "POST /clients/z-fold-7/disconnect",
                "POST /clients/z-fold-7/plan",
                "POST /clients/z-fold-7/quit",
                "POST /clients/z-fold-7/emergency-restore"
            ],
            result.Operations);
    }

    private sealed class BeaconServerProcess : IAsyncDisposable
    {
        private readonly Process process;
        private readonly string stateDirectory;
        private readonly DataReceivedEventHandler observeOutput;

        private BeaconServerProcess(
            Process process,
            Uri address,
            string stateDirectory,
            DataReceivedEventHandler observeOutput)
        {
            this.process = process;
            this.stateDirectory = stateDirectory;
            this.observeOutput = observeOutput;
            Address = address;
        }

        public Uri Address { get; }

        public static Task<BeaconServerProcess> StartAsync(string stateDirectory) =>
            StartAsync(stateDirectory, typeof(Program).Assembly.Location);

        public static async Task<BeaconServerProcess> StartAsync(
            string stateDirectory,
            string serverAssembly)
        {
            var listeningAddress = new TaskCompletionSource<Uri>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var output = new List<string>();
            var outputLock = new object();
            var startInfo = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(serverAssembly)!
            };
            startInfo.ArgumentList.Add(serverAssembly);
            startInfo.ArgumentList.Add("--urls");
            startInfo.ArgumentList.Add("http://127.0.0.1:0");
            startInfo.ArgumentList.Add("--Beacon:HostMode=fake");
            startInfo.ArgumentList.Add("--Beacon:StreamingMode=fake");
            startInfo.ArgumentList.Add("--Beacon:Security:TestHost=true");
            startInfo.ArgumentList.Add(
                $"--Beacon:Security:IdentityPath={Path.Combine(stateDirectory, "identity.pfx")}");
            startInfo.ArgumentList.Add(
                $"--Beacon:Security:CredentialsPath={Path.Combine(stateDirectory, "credentials.json")}");

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            DataReceivedEventHandler observeOutput = (_, args) =>
            {
                if (args.Data is null)
                {
                    return;
                }

                lock (outputLock)
                {
                    output.Add(args.Data);
                }

                const string marker = "Now listening on: ";
                int markerIndex = args.Data.IndexOf(marker, StringComparison.Ordinal);
                if (markerIndex >= 0
                    && Uri.TryCreate(args.Data[(markerIndex + marker.Length)..].Trim(), UriKind.Absolute, out Uri? uri))
                {
                    listeningAddress.TrySetResult(uri);
                }
            };
            process.OutputDataReceived += observeOutput;
            process.ErrorDataReceived += observeOutput;
            bool started = false;
            try
            {
                started = process.Start();
                if (!started)
                {
                    throw new InvalidOperationException("Beacon.Server process did not start.");
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                Task exited = process.WaitForExitAsync();
                Task completed = await Task.WhenAny(listeningAddress.Task, exited);
                if (completed == exited)
                {
                    lock (outputLock)
                    {
                        throw new InvalidOperationException(
                            $"Beacon.Server exited before listening.{Environment.NewLine}{string.Join(Environment.NewLine, output)}");
                    }
                }

                return new BeaconServerProcess(
                    process,
                    await listeningAddress.Task,
                    stateDirectory,
                    observeOutput);
            }
            catch
            {
                if (started)
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    await process.WaitForExitAsync();
                }
                process.OutputDataReceived -= observeOutput;
                process.ErrorDataReceived -= observeOutput;
                process.Dispose();
                DeleteIsolatedState(stateDirectory);
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync();
            process.OutputDataReceived -= observeOutput;
            process.ErrorDataReceived -= observeOutput;
            process.Dispose();
            DeleteIsolatedState(stateDirectory);
        }

        private static void DeleteIsolatedState(string path)
        {
            string resolved = Path.GetFullPath(path);
            string temporaryRoot = Path.GetFullPath(Path.GetTempPath());
            if (!resolved.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(resolved).StartsWith(
                    "beacon-fake-endpoint-",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Refusing to delete non-isolated FakeEndpoint state.");
            }
            if (Directory.Exists(resolved))
            {
                Directory.Delete(resolved, recursive: true);
            }
        }
    }
}

using System.Diagnostics;
using System.Security.Principal;
using Beacon.HostAgent.Update;

namespace Beacon.HostAgent.Bootstrap;

internal sealed class WindowsBootstrapPlatform : IBootstrapPlatform
{
    public IBootstrapChildLaunch Launch(
        SecurityIdentifier owner,
        HostAgentSelectedVersion version,
        string executablePath)
    {
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("Selected Host Agent executable is missing.", executablePath);
        }

        var readiness = new AuthenticatedReadinessServer(owner, version.VersionId);
        try
        {
            var start = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(executablePath)
                    ?? throw new InvalidDataException("Host Agent version directory is invalid."),
                CreateNoWindow = true
            };
            start.ArgumentList.Add("--owner-sid");
            start.ArgumentList.Add(owner.Value);
            start.ArgumentList.Add("--bootstrap-ready-pipe");
            start.ArgumentList.Add(readiness.PipeName);
            start.ArgumentList.Add("--version-id");
            start.ArgumentList.Add(version.VersionId);
            Process process = Process.Start(start)
                ?? throw new InvalidOperationException("Host Agent child process did not start.");
            return new WindowsBootstrapChildLaunch(process, readiness);
        }
        catch
        {
            readiness.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }
    }

    private sealed class WindowsBootstrapChildLaunch : IBootstrapChildLaunch
    {
        private readonly Process process;
        private readonly AuthenticatedReadinessServer readiness;

        public WindowsBootstrapChildLaunch(
            Process process,
            AuthenticatedReadinessServer readiness)
        {
            this.process = process;
            this.readiness = readiness;
            Readiness = readiness.WaitAsync(process.Id);
            ExitCode = WaitForExitAsync();
        }

        public Task Readiness { get; }

        public Task<int> ExitCode { get; }

        public async ValueTask DisposeAsync()
        {
            await readiness.DisposeAsync().ConfigureAwait(false);
            process.Dispose();
        }

        private async Task<int> WaitForExitAsync()
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            return process.ExitCode;
        }
    }
}

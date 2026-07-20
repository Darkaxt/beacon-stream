using System.Diagnostics;

namespace Beacon.HostAgent.DriverUpdates;

internal sealed class WindowsPnpUtilRunner : IPnpUtilRunner
{
    private readonly string executablePath;

    public WindowsPnpUtilRunner()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "pnputil.exe"))
    {
    }

    internal WindowsPnpUtilRunner(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !Path.IsPathFullyQualified(executablePath))
        {
            throw new ArgumentException("A fully qualified PnPUtil path is required.", nameof(executablePath));
        }
        this.executablePath = Path.GetFullPath(executablePath);
    }

    public async Task<PnpUtilResult> RunAsync(IReadOnlyList<string> arguments)
    {
        ProcessStartInfo startInfo = CreateStartInfo(arguments);
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start the Windows driver utility.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        return new PnpUtilResult(
            process.ExitCode,
            await standardOutput.ConfigureAwait(false),
            await standardError.ConfigureAwait(false));
    }

    internal ProcessStartInfo CreateStartInfo(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count == 0
            || arguments.Any(argument =>
                string.IsNullOrWhiteSpace(argument)
                || argument.Contains('\r', StringComparison.Ordinal)
                || argument.Contains('\n', StringComparison.Ordinal)))
        {
            throw new ArgumentException("PnPUtil arguments are invalid.", nameof(arguments));
        }

        var value = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in arguments)
        {
            value.ArgumentList.Add(argument);
        }
        return value;
    }
}

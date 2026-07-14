namespace Beacon.Server.TestHost;

public sealed record HostedBenchmarkWorkerOptions(string ExecutablePath, string IdentityPath)
{
    public const string ExecutablePathConfigurationKey = "Beacon:HostedBenchmarkWorker:Path";

    public static HostedBenchmarkWorkerOptions Create(string? executablePath, string? identityPath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException(
                "Hosted benchmark Worker executable path is required.",
                nameof(executablePath));
        }
        if (string.IsNullOrWhiteSpace(identityPath))
        {
            throw new ArgumentException(
                "Hosted benchmark Worker identity path is required.",
                nameof(identityPath));
        }
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                "Hosted benchmark Worker executable was not found.",
                executablePath);
        }
        if (!File.Exists(identityPath))
        {
            throw new FileNotFoundException(
                "Hosted benchmark Worker identity was not found.",
                identityPath);
        }

        return new HostedBenchmarkWorkerOptions(executablePath, identityPath);
    }
}

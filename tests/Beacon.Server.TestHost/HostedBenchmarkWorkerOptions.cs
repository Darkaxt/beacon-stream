namespace Beacon.Server.TestHost;

public sealed record HostedBenchmarkWorkerOptions(
    string ExecutablePath,
    string IdentityPath,
    string? Video720pPath,
    string? Video360pPath)
{
    public const string ExecutablePathConfigurationKey = "Beacon:HostedBenchmarkWorker:Path";
    public const string Video720pPathConfigurationKey = "Beacon:HostedBenchmarkWorker:Video720pPath";
    public const string Video360pPathConfigurationKey = "Beacon:HostedBenchmarkWorker:Video360pPath";

    public bool VideoEnabled => Video720pPath is not null && Video360pPath is not null;

    public static HostedBenchmarkWorkerOptions Create(
        string? executablePath,
        string? identityPath,
        string? video720pPath = null,
        string? video360pPath = null)
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

        video720pPath = NormalizeOptionalPath(video720pPath);
        video360pPath = NormalizeOptionalPath(video360pPath);
        if ((video720pPath is null) != (video360pPath is null))
        {
            throw new ArgumentException("Both hosted video vectors are required when hosted video is enabled.");
        }
        if (video720pPath is not null && !File.Exists(video720pPath))
        {
            throw new FileNotFoundException("Hosted 720p video vector was not found.", video720pPath);
        }
        if (video360pPath is not null && !File.Exists(video360pPath))
        {
            throw new FileNotFoundException("Hosted 360p video vector was not found.", video360pPath);
        }

        return new HostedBenchmarkWorkerOptions(
            executablePath,
            identityPath,
            video720pPath,
            video360pPath);
    }

    private static string? NormalizeOptionalPath(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}

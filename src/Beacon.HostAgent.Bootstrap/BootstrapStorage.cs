namespace Beacon.HostAgent.Bootstrap;

internal sealed class BootstrapStorage
{
    public BootstrapStorage(string installRoot, string dataRoot)
    {
        InstallRoot = RequireRoot(installRoot, nameof(installRoot));
        DataRoot = RequireRoot(dataRoot, nameof(dataRoot));
        VersionsRoot = CreateChild(InstallRoot, "HostAgent", "Versions");
        StateRoot = CreateChild(DataRoot, "State");
        StagedPackagesRoot = CreateChild(DataRoot, "StagedHostAgent");
        TransactionsRoot = CreateChild(DataRoot, "HostAgentTransactions");
        LogsRoot = CreateChild(DataRoot, "Logs");
    }

    public static BootstrapStorage Default => new(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "BeaconStream"),
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Beacon",
            "HostAgent"));

    public string InstallRoot { get; }

    public string DataRoot { get; }

    public string VersionsRoot { get; }

    public string StateRoot { get; }

    public string StagedPackagesRoot { get; }

    public string TransactionsRoot { get; }

    public string LogsRoot { get; }

    public string GetVersionRoot(string versionId) =>
        ResolveChild(VersionsRoot, RequireVersionId(versionId));

    public string GetVersionExecutable(string versionId) =>
        Path.Combine(GetVersionRoot(versionId), "Beacon.HostAgent.exe");

    private static string RequireRoot(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
        {
            throw new ArgumentException("A fully qualified bootstrap root is required.", parameterName);
        }
        string root = Path.GetFullPath(value)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        Directory.CreateDirectory(root);
        return root;
    }

    private static string CreateChild(string root, params string[] segments)
    {
        string path = Path.Combine([root, .. segments]);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string RequireVersionId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || value is "." or "..")
        {
            throw new InvalidDataException("Host Agent version id is invalid.");
        }
        return value;
    }

    internal static string ResolveChild(string root, string child)
    {
        string canonicalRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string candidate = Path.GetFullPath(Path.Combine(canonicalRoot, child));
        string prefix = $"{canonicalRoot}{Path.DirectorySeparatorChar}";
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? candidate
            : throw new InvalidDataException("Bootstrap path escapes its protected root.");
    }
}

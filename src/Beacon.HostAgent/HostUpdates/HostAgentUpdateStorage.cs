namespace Beacon.HostAgent.HostUpdates;

internal sealed class HostAgentUpdateStorage
{
    public HostAgentUpdateStorage(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
        {
            throw new ArgumentException(
                "A fully qualified Host Agent update storage root is required.",
                nameof(root));
        }
        Root = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        Directory.CreateDirectory(Root);
        Inbox = CreateChild("Inbox");
        StagedPackages = CreateChild("StagedHostAgent");
        Transactions = CreateChild("HostAgentTransactions");
        State = CreateChild("State");
    }

    public static HostAgentUpdateStorage Default => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Beacon",
        "HostAgent"));

    public string Root { get; }

    public string Inbox { get; }

    public string StagedPackages { get; }

    public string Transactions { get; }

    public string State { get; }

    private string CreateChild(string name)
    {
        string path = Path.Combine(Root, name);
        Directory.CreateDirectory(path);
        return path;
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beacon.HostAgent.Update;

public sealed record HostAgentSelectedVersion(string VersionId, string SourceCommit);

public sealed record HostAgentPendingUpdate(
    Guid TransactionId,
    string PackageId,
    string SourceCommit,
    string PreviousVersionId,
    string PreviousSourceCommit,
    string StagedPackageRoot);

public sealed class HostAgentUpdateStateStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };
    private readonly string root;

    public HostAgentUpdateStateStore(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
        {
            throw new ArgumentException("A fully qualified Host Agent state root is required.", nameof(root));
        }
        this.root = Path.GetFullPath(root);
        Directory.CreateDirectory(this.root);
    }

    public string CurrentVersionPath => Path.Combine(root, "current-version.json");

    public string PendingUpdatePath => Path.Combine(root, "pending-update.json");

    public HostAgentSelectedVersion? ReadCurrent() =>
        Read<HostAgentSelectedVersion>(CurrentVersionPath);

    public void WriteCurrent(HostAgentSelectedVersion value) =>
        Write(CurrentVersionPath, ValidateCurrent(value));

    public HostAgentPendingUpdate? ReadPending() =>
        Read<HostAgentPendingUpdate>(PendingUpdatePath);

    public void WritePending(HostAgentPendingUpdate value) =>
        Write(PendingUpdatePath, ValidatePending(value));

    public void ClearPending()
    {
        if (File.Exists(PendingUpdatePath))
        {
            File.Delete(PendingUpdatePath);
        }
    }

    private static HostAgentSelectedVersion ValidateCurrent(HostAgentSelectedVersion value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (string.IsNullOrWhiteSpace(value.VersionId)
            || string.IsNullOrWhiteSpace(value.SourceCommit))
        {
            throw new InvalidDataException("Selected Host Agent version state is incomplete.");
        }
        return value;
    }

    private static HostAgentPendingUpdate ValidatePending(HostAgentPendingUpdate value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.TransactionId == Guid.Empty
            || string.IsNullOrWhiteSpace(value.PackageId)
            || string.IsNullOrWhiteSpace(value.SourceCommit)
            || string.IsNullOrWhiteSpace(value.PreviousVersionId)
            || string.IsNullOrWhiteSpace(value.PreviousSourceCommit)
            || string.IsNullOrWhiteSpace(value.StagedPackageRoot)
            || !Path.IsPathFullyQualified(value.StagedPackageRoot))
        {
            throw new InvalidDataException("Pending Host Agent update state is incomplete.");
        }
        return value;
    }

    private static T? Read<T>(string path)
    {
        if (!File.Exists(path))
        {
            return default;
        }
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), Json)
                ?? throw new InvalidDataException("Host Agent update state file is empty.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("Host Agent update state file is invalid.", error);
        }
    }

    private static void Write<T>(string path, T value)
    {
        byte[] content = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        string temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16_384,
                FileOptions.WriteThrough))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}

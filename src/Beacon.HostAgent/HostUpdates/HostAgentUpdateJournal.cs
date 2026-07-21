using System.Text.Json;
using System.Text.Json.Serialization;
using Beacon.HostAgent.Contracts;

namespace Beacon.HostAgent.HostUpdates;

internal sealed class HostAgentUpdateJournal
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };
    private readonly Lock gate = new();
    private readonly string root;

    public HostAgentUpdateJournal(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
        {
            throw new ArgumentException(
                "A fully qualified Host Agent transaction root is required.",
                nameof(root));
        }
        this.root = Path.GetFullPath(root);
        Directory.CreateDirectory(this.root);
    }

    public void Write(HostAgentUpdatePayload value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.TransactionId == Guid.Empty)
        {
            throw new ArgumentException("Host Agent update transaction id is required.", nameof(value));
        }

        lock (gate)
        {
            WriteAtomic(GetPath(value.TransactionId), JsonSerializer.SerializeToUtf8Bytes(value, Json));
        }
    }

    public bool TryRead(Guid transactionId, out HostAgentUpdatePayload? value)
    {
        if (transactionId == Guid.Empty)
        {
            value = null;
            return false;
        }
        lock (gate)
        {
            string path = GetPath(transactionId);
            if (!File.Exists(path))
            {
                value = null;
                return false;
            }
            value = JsonSerializer.Deserialize<HostAgentUpdatePayload>(File.ReadAllBytes(path), Json)
                ?? throw new InvalidDataException("Host Agent update journal entry is empty.");
            if (value.TransactionId != transactionId)
            {
                throw new InvalidDataException("Host Agent update journal identity is invalid.");
            }
            return true;
        }
    }

    private string GetPath(Guid transactionId) =>
        Path.Combine(root, $"{transactionId:D}.json");

    private static void WriteAtomic(string path, byte[] content)
    {
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

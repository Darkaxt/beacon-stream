using System.Text.Json;
using System.Text.Json.Serialization;
using Beacon.HostAgent.Contracts;

namespace Beacon.HostAgent.DriverUpdates;

internal sealed class SudoVdaUpdateJournal
{
    private static readonly JsonSerializerOptions Json = CreateJson();
    private readonly Lock gate = new();
    private readonly string root;

    public SudoVdaUpdateJournal(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("Driver update journal root is required.", nameof(root));
        }
        this.root = Path.GetFullPath(root);
        Directory.CreateDirectory(this.root);
    }

    public void Write(SudoVdaUpdatePayload value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.TransactionId == Guid.Empty)
        {
            throw new ArgumentException("Driver update transaction id is required.", nameof(value));
        }

        byte[] content = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        string finalPath = GetPath(value.TransactionId);
        string temporaryPath = $"{finalPath}.{Guid.NewGuid():N}.tmp";
        lock (gate)
        {
            try
            {
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 16_384,
                    FileOptions.WriteThrough))
                {
                    stream.Write(content);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporaryPath, finalPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }

    public bool TryRead(Guid transactionId, out SudoVdaUpdatePayload? value)
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
            value = JsonSerializer.Deserialize<SudoVdaUpdatePayload>(File.ReadAllBytes(path), Json)
                ?? throw new InvalidDataException("Driver update journal entry is empty.");
            if (value.TransactionId != transactionId)
            {
                throw new InvalidDataException("Driver update journal identity does not match its file.");
            }
            return true;
        }
    }

    private string GetPath(Guid transactionId) =>
        Path.Combine(root, $"{transactionId:D}.json");

    private static JsonSerializerOptions CreateJson() => new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };
}

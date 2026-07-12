using System.Text.Json;
using System.Text.Json.Serialization;
using Beacon.Core.Benchmarks;

namespace Beacon.Server.State;

public sealed class FileBenchmarkEvidenceRepository : IBenchmarkEvidenceRepository, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly object gate = new();
    private readonly string path;
    private readonly FileStream writerLease;
    private bool disposed;

    static FileBenchmarkEvidenceRepository()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public FileBenchmarkEvidenceRepository(string path)
    {
        this.path = Path.GetFullPath(path);
        writerLease = AcquireWriterLease(this.path);
    }

    public string Kind => "file";

    public string? Location => path;

    public IReadOnlyList<BenchmarkEvidence> LoadEvidence()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            if (!File.Exists(path))
            {
                return [];
            }

            try
            {
                using FileStream stream = File.OpenRead(path);
                BenchmarkEvidenceDocument? document = JsonSerializer.Deserialize<BenchmarkEvidenceDocument>(stream, JsonOptions);
                if (document is null)
                {
                    throw new InvalidOperationException($"Benchmark evidence store '{path}' is empty.");
                }

                if (document.Version != 1)
                {
                    throw new InvalidOperationException(
                        $"Benchmark evidence store '{path}' uses unsupported version {document.Version}.");
                }

                foreach (BenchmarkEvidence evidence in document.Evidence)
                {
                    BenchmarkEvidenceValidator.Validate(evidence);
                }

                return document.Evidence;
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Benchmark evidence store '{path}' is not valid JSON.", ex);
            }
        }
    }

    public void SaveEvidence(IReadOnlyList<BenchmarkEvidence> evidence)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
            var document = new BenchmarkEvidenceDocument(Version: 1, Evidence: evidence);

            try
            {
                using (FileStream stream = File.Create(temporaryPath))
                {
                    JsonSerializer.Serialize(stream, document, JsonOptions);
                }

                File.Move(temporaryPath, path, overwrite: true);
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

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            writerLease.Dispose();
        }
    }

    private static FileStream AcquireWriterLease(string evidencePath)
    {
        string writerLeasePath = $"{evidencePath}.writer.lock";

        try
        {
            string? directory = Path.GetDirectoryName(writerLeasePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return new FileStream(
                writerLeasePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Cannot open benchmark evidence store '{evidencePath}' because its exclusive writer lease " +
                $"'{writerLeasePath}' could not be acquired. Another live Beacon Server instance may already " +
                "be using this benchmark evidence path.",
                ex);
        }
    }
}

internal sealed record BenchmarkEvidenceDocument(int Version, IReadOnlyList<BenchmarkEvidence> Evidence);

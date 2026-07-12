using System.Text.Json;
using System.Text.Json.Serialization;
using Beacon.Core.Benchmarks;

namespace Beacon.Server.State;

public sealed class FileBenchmarkEvidenceRepository(string path) : IBenchmarkEvidenceRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly object gate = new();

    static FileBenchmarkEvidenceRepository()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public string Kind => "file";

    public string? Location => path;

    public IReadOnlyList<BenchmarkEvidence> LoadEvidence()
    {
        lock (gate)
        {
            if (!File.Exists(path))
            {
                return [];
            }

            try
            {
                using FileStream stream = File.OpenRead(path);
                BenchmarkEvidenceDocument? document = JsonSerializer.Deserialize<BenchmarkEvidenceDocument>(stream, JsonOptions);
                return document?.Evidence ?? [];
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
}

internal sealed record BenchmarkEvidenceDocument(int Version, IReadOnlyList<BenchmarkEvidence> Evidence);

using System.Text.Json;

namespace Beacon.Platform.Windows.Streaming;

public sealed class WindowsExternalStreamingManifestReader : IExternalStreamingManifestReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public bool FileExists(string path) => File.Exists(path);

    public ExternalStreamingManifestReadResult Read(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            ExternalStreamingManifest? manifest = JsonSerializer.Deserialize<ExternalStreamingManifest>(json, JsonOptions);
            return manifest is null
                ? ExternalStreamingManifestReadResult.Fail($"External streaming manifest '{path}' is empty or invalid.")
                : ExternalStreamingManifestReadResult.Ok(manifest);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return ExternalStreamingManifestReadResult.Fail($"External streaming manifest '{path}' could not be read: {ex.Message}");
        }
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Beacon.Platform.Windows.Streaming;

public sealed class WindowsExternalStreamingSessionDescriptorStore : IExternalStreamingSessionDescriptorStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string rootPath;

    public WindowsExternalStreamingSessionDescriptorStore()
        : this(CreateDefaultRootPath())
    {
    }

    public WindowsExternalStreamingSessionDescriptorStore(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Runtime session descriptor root path is required.", nameof(rootPath));
        }

        this.rootPath = rootPath;
    }

    public string? PrepareDescriptorPath(string sessionId)
    {
        Directory.CreateDirectory(rootPath);
        string path = Path.Combine(rootPath, $"{SanitizeSessionId(sessionId)}.json");
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return path;
    }

    public ExternalStreamingSessionDescriptorReadResult Read(string path)
    {
        if (!File.Exists(path))
        {
            return ExternalStreamingSessionDescriptorReadResult.NotFound();
        }

        try
        {
            string json = File.ReadAllText(path);
            ExternalStreamingSessionDescriptor? descriptor =
                JsonSerializer.Deserialize<ExternalStreamingSessionDescriptor>(json, JsonOptions);
            return descriptor is null
                ? ExternalStreamingSessionDescriptorReadResult.Fail($"External streaming session descriptor '{path}' is empty or invalid.")
                : ExternalStreamingSessionDescriptorReadResult.Ok(descriptor);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return ExternalStreamingSessionDescriptorReadResult.Fail(
                $"External streaming session descriptor '{path}' could not be read: {ex.Message}");
        }
    }

    public void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string CreateDefaultRootPath()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string basePath = string.IsNullOrWhiteSpace(localAppData)
            ? Path.GetTempPath()
            : localAppData;
        return Path.Combine(basePath, "Beacon", "StreamingSessions");
    }

    private static string SanitizeSessionId(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return "session";
        }

        HashSet<char> invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder(sessionId.Length);
        foreach (char value in sessionId)
        {
            builder.Append(invalid.Contains(value) ? '_' : value);
        }

        string sanitized = builder.ToString().Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "session";
        }

        if (sanitized.Length <= 120)
        {
            return sanitized;
        }

        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sessionId)))[..12];
        return $"{sanitized[..100]}-{hash}";
    }
}

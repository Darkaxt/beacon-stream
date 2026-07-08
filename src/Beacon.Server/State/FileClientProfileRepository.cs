using System.Text.Json;
using System.Text.Json.Serialization;
using Beacon.Core.Clients;

namespace Beacon.Server.State;

public sealed class FileClientProfileRepository(string path) : IClientProfileRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly object gate = new();

    static FileClientProfileRepository()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public string Kind => "file";

    public string? Location => path;

    public IReadOnlyList<ClientProfile> LoadProfiles()
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
                ClientProfileDocument? document = JsonSerializer.Deserialize<ClientProfileDocument>(stream, JsonOptions);
                return document?.Profiles ?? [];
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Client profile store '{path}' is not valid JSON.", ex);
            }
        }
    }

    public void SaveProfiles(IReadOnlyList<ClientProfile> profiles)
    {
        lock (gate)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
            var document = new ClientProfileDocument(Version: 1, Profiles: profiles);

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

internal sealed record ClientProfileDocument(int Version, IReadOnlyList<ClientProfile> Profiles);

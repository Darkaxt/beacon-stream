using System.Text.Json;
using System.Text.Json.Serialization;
using Beacon.Core.Clients;

namespace Beacon.Server.State;

public sealed class FileClientProfileRepository(string path) : IClientProfileRepository
{
    private const int CurrentDocumentVersion = 2;

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
                using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(path));
                ClientProfileDocument? document = json.RootElement.Deserialize<ClientProfileDocument>(JsonOptions);
                if (document is null)
                {
                    return [];
                }

                ClientProfile[] profiles = document.Profiles?.ToArray() ?? [];
                if (document.Version == 1)
                {
                    profiles = MigrateLegacyDisplayModes(json.RootElement, profiles);
                    WriteProfiles(profiles);
                }

                return profiles;
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
            WriteProfiles(profiles);
        }
    }

    private static ClientProfile[] MigrateLegacyDisplayModes(
        JsonElement root,
        ClientProfile[] profiles)
    {
        if (!root.TryGetProperty("profiles", out JsonElement persistedProfiles) ||
            persistedProfiles.ValueKind != JsonValueKind.Array)
        {
            return profiles;
        }

        JsonElement[] persisted = persistedProfiles.EnumerateArray().ToArray();
        for (int index = 0; index < profiles.Length && index < persisted.Length; index++)
        {
            ClientProfile profile = profiles[index];
            if (profile.Display.PreferredMode is not null || profile.Display.SelectedMode is not null)
            {
                continue;
            }

            if (!persisted[index].TryGetProperty("display", out JsonElement display) ||
                display.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            bool hasWidth = display.TryGetProperty("preferredWidth", out JsonElement widthElement);
            bool hasHeight = display.TryGetProperty("preferredHeight", out JsonElement heightElement);
            bool hasRefresh = display.TryGetProperty("preferredRefreshHz", out JsonElement refreshElement);
            if (!hasWidth && !hasHeight && !hasRefresh)
            {
                continue;
            }

            if (!hasWidth || !hasHeight || !hasRefresh ||
                !widthElement.TryGetInt32(out int width) || width <= 0 ||
                !heightElement.TryGetInt32(out int height) || height <= 0 ||
                !refreshElement.TryGetInt32(out int refreshHz) || refreshHz <= 0)
            {
                throw new InvalidOperationException(
                    $"Client profile '{profile.ClientId.Value}' has an incomplete or invalid legacy display mode.");
            }

            var mode = new ClientDisplayMode(width, height, refreshHz);
            profiles[index] = profile with
            {
                Display = profile.Display with
                {
                    PreferredMode = mode,
                    SelectedMode = mode
                }
            };
        }

        return profiles;
    }

    private void WriteProfiles(IReadOnlyList<ClientProfile> profiles)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        var document = new ClientProfileDocument(CurrentDocumentVersion, profiles);

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

internal sealed record ClientProfileDocument(int Version, IReadOnlyList<ClientProfile> Profiles);

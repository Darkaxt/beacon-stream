using System.Text.Json;

namespace Beacon.Platform.Windows.Displays;

public interface IWindowsDisplayNameResolver
{
    bool TryResolveDisplayName(string displayId, out string? displayName);
}

public sealed class WindowsDisplayNameMap : IWindowsDisplayNameResolver
{
    private readonly Lock gate = new();
    private readonly WindowsDisplayNameMapStore store;
    private readonly Dictionary<string, string> displayNameByDisplayId;

    public WindowsDisplayNameMap(WindowsDisplayNameMapStore store)
    {
        this.store = store;
        displayNameByDisplayId = new Dictionary<string, string>(
            store.Load(),
            StringComparer.Ordinal);
    }

    public bool TryResolveDisplayName(string displayId, out string? displayName)
    {
        lock (gate)
        {
            return displayNameByDisplayId.TryGetValue(displayId, out displayName);
        }
    }

    public void Remember(string displayId, string displayName)
    {
        lock (gate)
        {
            string[] displacedDisplayIds = displayNameByDisplayId
                .Where(pair =>
                    !string.Equals(pair.Key, displayId, StringComparison.Ordinal) &&
                    string.Equals(pair.Value, displayName, StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Key)
                .ToArray();
            foreach (string displacedDisplayId in displacedDisplayIds)
            {
                displayNameByDisplayId.Remove(displacedDisplayId);
            }
            displayNameByDisplayId[displayId] = displayName;
            store.Save(displayNameByDisplayId);
        }
    }

    public void Forget(string displayId)
    {
        lock (gate)
        {
            if (displayNameByDisplayId.Remove(displayId))
            {
                store.Save(displayNameByDisplayId);
            }
        }
    }

    public Dictionary<string, string> CreateDisplayIdByDisplayNameSnapshot()
    {
        lock (gate)
        {
            var displayIdByDisplayName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> pair in displayNameByDisplayId)
            {
                displayIdByDisplayName[pair.Value] = pair.Key;
            }

            return displayIdByDisplayName;
        }
    }
}

public sealed class WindowsDisplayNameMapStore(string path)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public static string DefaultPath { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BeaconStream",
        "display-name-map.json");

    public static WindowsDisplayNameMapStore Default { get; } = new(DefaultPath);

    public string Path { get; } = path;

    public IReadOnlyDictionary<string, string> Load()
    {
        try
        {
            if (!File.Exists(Path))
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            Dictionary<string, string>? loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(Path));

            return loaded is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : loaded
                    .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    public void Save(IReadOnlyDictionary<string, string> mappings)
    {
        try
        {
            string? directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            Dictionary<string, string> stableMappings = mappings
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

            File.WriteAllText(Path, JsonSerializer.Serialize(stableMappings, SerializerOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }
    }
}

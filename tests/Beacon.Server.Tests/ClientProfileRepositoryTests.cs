using System.Text.Json;
using Beacon.Core.Clients;
using Beacon.Server.State;

namespace Beacon.Server.Tests;

public sealed class ClientProfileRepositoryTests
{
    [Fact]
    public void LoadProfilesMigratesLegacyScalarDisplayModeAndPersistsStructuredDocument()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"beacon-profile-migration-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "profiles.json");

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, CreateProfileDocument(
                """
                "preferredWidth": 2560,
                "preferredHeight": 1600,
                "preferredRefreshHz": 120,
                """));
            var repository = new FileClientProfileRepository(path);

            ClientProfile profile = Assert.Single(repository.LoadProfiles());

            Assert.Equal(new ClientDisplayMode(2560, 1600, 120), profile.Display.PreferredMode);
            Assert.Equal(new ClientDisplayMode(2560, 1600, 120), profile.Display.SelectedMode);

            using JsonDocument persisted = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = persisted.RootElement;
            Assert.Equal(2, root.GetProperty("version").GetInt32());
            JsonElement display = root.GetProperty("profiles")[0].GetProperty("display");
            Assert.Equal(2560, display.GetProperty("preferredMode").GetProperty("width").GetInt32());
            Assert.Equal(1600, display.GetProperty("selectedMode").GetProperty("height").GetInt32());
            Assert.Equal(120, display.GetProperty("selectedMode").GetProperty("refreshHz").GetInt32());
            Assert.False(display.TryGetProperty("preferredWidth", out _));
            Assert.False(display.TryGetProperty("preferredHeight", out _));
            Assert.False(display.TryGetProperty("preferredRefreshHz", out _));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void LoadProfilesKeepsStructuredModeWhenLegacyScalarsAreAlsoPresent()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"beacon-profile-structured-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "profiles.json");

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, CreateProfileDocument(
                """
                "preferredMode": { "width": 1920, "height": 1080, "refreshHz": 60 },
                "selectedMode": { "width": 1920, "height": 1080, "refreshHz": 120 },
                "preferredWidth": 2560,
                "preferredHeight": 1600,
                "preferredRefreshHz": 120,
                """));
            var repository = new FileClientProfileRepository(path);

            ClientProfile profile = Assert.Single(repository.LoadProfiles());

            Assert.Equal(new ClientDisplayMode(1920, 1080, 60), profile.Display.PreferredMode);
            Assert.Equal(new ClientDisplayMode(1920, 1080, 120), profile.Display.SelectedMode);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void SaveProfilesWritesCurrentStructuredDocumentVersion()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"beacon-profile-current-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "profiles.json");

        try
        {
            var repository = new FileClientProfileRepository(path);

            repository.SaveProfiles([ClientProfile.CreateZFold7Default()]);

            using JsonDocument persisted = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(2, persisted.RootElement.GetProperty("version").GetInt32());
            ClientProfile loaded = Assert.Single(repository.LoadProfiles());
            Assert.Equal(new ClientDisplayMode(2560, 1600, 120), loaded.Display.PreferredMode);
            Assert.Equal(new ClientDisplayMode(2560, 1600, 120), loaded.Display.SelectedMode);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void LoadProfilesRejectsIncompleteLegacyDisplayModeWithoutRewriting()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"beacon-profile-incomplete-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "profiles.json");

        try
        {
            Directory.CreateDirectory(directory);
            string original = CreateProfileDocument(
                """
                "preferredWidth": 2560,
                "preferredHeight": 1600,
                """);
            File.WriteAllText(path, original);
            var repository = new FileClientProfileRepository(path);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(repository.LoadProfiles);

            Assert.Contains("legacy display mode", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(original, File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static string CreateProfileDocument(string displayProperties) => $$"""
        {
          "version": 1,
          "profiles": [
            {
              "clientId": "z-fold-7",
              "name": "Z Fold 7",
              "display": {
                {{displayProperties}}
                "hdrPreference": "Prefer",
                "mode": "virtual-primary",
                "restorePhysicalDisplayOnEnd": true,
                "forbidMirrorMode": true
              },
              "stream": {
                "qualityMode": "auto",
                "codecPreference": "auto",
                "bitrateCapMbps": null
              },
              "audio": { "mode": "stereo" },
              "session": {
                "keepAppRunningOnDisconnect": false,
                "allowEmergencyRestoreFromClient": true
              }
            }
          ]
        }
        """;
}

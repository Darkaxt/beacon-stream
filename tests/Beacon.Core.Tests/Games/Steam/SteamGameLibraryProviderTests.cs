using System.Text;
using Beacon.Core.Games;
using Beacon.Core.Games.Steam;

namespace Beacon.Core.Tests.Games.Steam;

public sealed class SteamGameLibraryProviderTests
{
    [Fact]
    public async Task DiscoversOfficialAppsAndNonSteamShortcuts()
    {
        using SteamProviderFixture fixture = SteamProviderFixture.Create();

        IGameLibraryProvider provider = new SteamGameLibraryProvider(fixture.SteamRoot);
        GameLibrarySnapshot snapshot = await provider.ScanAsync(CancellationToken.None);

        Assert.Contains(snapshot.Games, game =>
            game.Id == "steam:1086940" &&
            game.Title == "Baldur's Gate 3" &&
            game.Launch.Type == "steam-app" &&
            game.Launch.Command == "steam://run/1086940" &&
            game.Source == "steam");

        Assert.Contains(snapshot.Games, game =>
            game.Id == "steam-shortcut:4261190003" &&
            game.Title == "DRAGON QUEST III HD-2D Remake" &&
            game.Launch.Type == "steam-rungameid" &&
            game.Launch.Command == "steam://rungameid/18301671704960696320" &&
            game.ProcessHints.ExecutableName == "DQIIIHD2DRemake.exe");
    }

    private sealed class SteamProviderFixture : IDisposable
    {
        private SteamProviderFixture(string steamRoot)
        {
            SteamRoot = steamRoot;
        }

        public string SteamRoot { get; }

        public static SteamProviderFixture Create()
        {
            string root = Path.Combine(Path.GetTempPath(), $"beacon-steam-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "config"));
            Directory.CreateDirectory(Path.Combine(root, "steamapps"));
            Directory.CreateDirectory(Path.Combine(root, "userdata", "123", "config"));

            string escapedRoot = root.Replace(@"\", @"\\", StringComparison.Ordinal);
            File.WriteAllText(
                Path.Combine(root, "config", "libraryfolders.vdf"),
                $$"""
                "libraryfolders"
                {
                    "0"
                    {
                        "path" "{{escapedRoot}}"
                        "apps"
                        {
                            "1086940" "159037966816"
                        }
                    }
                }
                """);

            File.WriteAllText(
                Path.Combine(root, "steamapps", "appmanifest_1086940.acf"),
                """
                "AppState"
                {
                    "appid" "1086940"
                    "name" "Baldur's Gate 3"
                    "installdir" "Baldurs Gate 3"
                    "StateFlags" "4"
                }
                """);

            File.WriteAllBytes(
                Path.Combine(root, "userdata", "123", "config", "shortcuts.vdf"),
                CreateShortcut(
                    appId: unchecked((int)0xFDFC9973),
                    appName: "DRAGON QUEST III HD-2D Remake",
                    exe: "\"D:\\Games\\Dragon Quest III HD-2D Remake\\DQIIIHD2DRemake.exe\"",
                    startDir: "\"D:\\Games\\Dragon Quest III HD-2D Remake\""));

            return new SteamProviderFixture(root);
        }

        public void Dispose()
        {
            Directory.Delete(SteamRoot, recursive: true);
        }

        private static byte[] CreateShortcut(int appId, string appName, string exe, string startDir)
        {
            using var stream = new MemoryStream();
            WriteByte(stream, 0);
            WriteString(stream, "shortcuts");
            WriteByte(stream, 0);
            WriteString(stream, "0");
            WriteByte(stream, 2);
            WriteString(stream, "appid");
            stream.Write(BitConverter.GetBytes(appId));
            WriteByte(stream, 1);
            WriteString(stream, "appname");
            WriteString(stream, appName);
            WriteByte(stream, 1);
            WriteString(stream, "Exe");
            WriteString(stream, exe);
            WriteByte(stream, 1);
            WriteString(stream, "StartDir");
            WriteString(stream, startDir);
            WriteByte(stream, 8);
            WriteByte(stream, 8);
            return stream.ToArray();
        }

        private static void WriteByte(Stream stream, byte value) => stream.WriteByte(value);

        private static void WriteString(Stream stream, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            stream.Write(bytes);
            stream.WriteByte(0);
        }
    }
}

using Beacon.Core.Games.Steam;

namespace Beacon.Core.Tests.Games.Steam;

public sealed class SteamTextVdfParserTests
{
    [Fact]
    public void ReadsLibraryFoldersAndAppIds()
    {
        const string text = """
        "libraryfolders"
        {
            "0"
            {
                "path" "D:\\Steam"
                "apps"
                {
                    "1086940" "159037966816"
                    "1449690" "48816593394"
                }
            }
        }
        """;

        IReadOnlyList<SteamLibraryFolder> folders = SteamLibraryFoldersParser.Parse(text);

        SteamLibraryFolder folder = Assert.Single(folders);
        Assert.Equal(@"D:\Steam", folder.Path);
        Assert.Equal([1086940, 1449690], folder.AppIds);
    }

    [Fact]
    public void ReadsAppManifestTitleAndInstallDir()
    {
        const string text = """
        "AppState"
        {
            "appid" "1086940"
            "name" "Baldur's Gate 3"
            "installdir" "Baldurs Gate 3"
            "StateFlags" "4"
        }
        """;

        SteamAppManifest manifest = SteamAppManifestParser.Parse(text, @"D:\Steam\steamapps\appmanifest_1086940.acf");

        Assert.Equal(1086940, manifest.AppId);
        Assert.Equal("Baldur's Gate 3", manifest.Name);
        Assert.Equal(@"D:\Steam\steamapps\common\Baldurs Gate 3", manifest.InstallPath);
        Assert.True(manifest.Installed);
    }
}

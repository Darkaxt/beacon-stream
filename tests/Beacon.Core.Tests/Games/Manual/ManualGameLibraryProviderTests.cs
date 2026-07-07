using Beacon.Core.Games;
using Beacon.Core.Games.Manual;

namespace Beacon.Core.Tests.Games.Manual;

public sealed class ManualGameLibraryProviderTests
{
    [Fact]
    public async Task ReadsManualGameJson()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-manual-games.json");
        try
        {
            await File.WriteAllTextAsync(
                path,
                """
                {
                  "games": [
                    {
                      "id": "manual:dispatch",
                      "title": "Dispatch",
                      "source": "manual",
                      "launch": { "type": "process", "command": "D:/Games/Dispatch/Dispatch.exe" },
                      "installed": true,
                      "processHints": { "executableName": "Dispatch.exe", "workingDirectory": "D:/Games/Dispatch" }
                    }
                  ]
                }
                """);

            IGameLibraryProvider provider = new ManualGameLibraryProvider(path);
            GameLibrarySnapshot snapshot = await provider.ScanAsync(CancellationToken.None);

            GameDescriptor game = Assert.Single(snapshot.Games);
            Assert.Equal("manual:dispatch", game.Id);
            Assert.Equal("Dispatch", game.Title);
            Assert.Equal("process", game.Launch.Type);
            Assert.Equal("D:/Games/Dispatch/Dispatch.exe", game.Launch.Command);
            Assert.Equal("Dispatch.exe", game.ProcessHints.ExecutableName);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}

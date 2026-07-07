using Beacon.Core.Games;
using Beacon.Core.Games.Hydra;
using Microsoft.Data.Sqlite;

namespace Beacon.Core.Tests.Games.Hydra;

public sealed class HydraGameLibraryProviderTests
{
    [Fact]
    public async Task ReadsInstalledHydraGamesFromSqlite()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
            {
                await connection.OpenAsync();
                SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                create table game (id integer primary key, objectID text not null, title text not null, executablePath text, shop text not null, status text, isDeleted boolean not null default 0);
                insert into game (objectID,title,executablePath,shop,status,isDeleted) values ('hydra-1','Persona 5 Royal','D:/Games/Persona5/P5R.exe','hydra','complete',0);
                """;
                await command.ExecuteNonQueryAsync();
            }

            IGameLibraryProvider provider = new HydraGameLibraryProvider(dbPath);
            GameLibrarySnapshot snapshot = await provider.ScanAsync(CancellationToken.None);

            GameDescriptor game = Assert.Single(snapshot.Games);
            Assert.Equal("hydra:hydra-1", game.Id);
            Assert.Equal("Persona 5 Royal", game.Title);
            Assert.Equal("process", game.Launch.Type);
            Assert.Equal("D:/Games/Persona5/P5R.exe", game.Launch.Command);
            Assert.Equal("P5R.exe", game.ProcessHints.ExecutableName);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }
}

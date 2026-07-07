using Microsoft.Data.Sqlite;

namespace Beacon.Core.Games.Hydra;

public sealed class HydraGameLibraryProvider(string databasePath) : IGameLibraryProvider
{
    public string Name => "hydra";

    public async Task<GameLibrarySnapshot> ScanAsync(CancellationToken cancellationToken)
    {
        var games = new List<GameDescriptor>();
        var diagnostics = new List<string>();

        if (!File.Exists(databasePath))
        {
            diagnostics.Add($"Hydra database '{databasePath}' does not exist.");
            return new GameLibrarySnapshot(games, diagnostics);
        }

        try
        {
            await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
            await connection.OpenAsync(cancellationToken);

            SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                select objectID, title, executablePath, shop, status, isDeleted
                from game
                where coalesce(isDeleted, 0) = 0
                """;

            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                string objectId = ReadString(reader, 0);
                string title = ReadString(reader, 1);
                string executablePath = ReadString(reader, 2);
                string shop = ReadString(reader, 3);
                string status = ReadString(reader, 4);

                if (string.IsNullOrWhiteSpace(objectId) || string.IsNullOrWhiteSpace(title))
                {
                    diagnostics.Add("Skipped Hydra row without objectID or title.");
                    continue;
                }

                games.Add(new GameDescriptor(
                    Id: $"hydra:{objectId}",
                    Title: title,
                    Source: string.IsNullOrWhiteSpace(shop) ? "hydra" : $"hydra-{shop}",
                    Launch: new GameLaunchIntent("process", executablePath),
                    Artwork: new GameArtwork(null, "none"),
                    Installed: IsInstalled(executablePath, status),
                    ProcessHints: new GameProcessHints(Path.GetFileName(executablePath), Path.GetDirectoryName(executablePath))));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
        {
            diagnostics.Add($"Could not read Hydra database '{databasePath}': {ex.Message}");
        }

        return new GameLibrarySnapshot(games, diagnostics);
    }

    private static bool IsInstalled(string executablePath, string status) =>
        !string.IsNullOrWhiteSpace(executablePath) &&
        !status.Equals("deleted", StringComparison.OrdinalIgnoreCase) &&
        !status.Equals("removed", StringComparison.OrdinalIgnoreCase) &&
        !status.Equals("uninstalled", StringComparison.OrdinalIgnoreCase);

    private static string ReadString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
}

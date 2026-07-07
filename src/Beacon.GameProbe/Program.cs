using System.Text.Json;
using Beacon.Core.Games;
using Beacon.Core.Games.Artwork;
using Beacon.Core.Games.Steam;
using Beacon.GameProbe;

try
{
    GameProbeCommand command = GameProbeCommandLine.Parse(args);
    switch (command)
    {
        case ScanGameProbeCommand scan:
            IReadOnlyList<IGameLibraryProvider> providers = GameProbeProviderFactory.CreateProviders(scan);
            var service = new GameLibraryService(providers, new NoArtworkProvider());
            GameLibrarySnapshot snapshot = await service.ScanAsync(CancellationToken.None);
            Console.Write(scan.Json ? ToJson(snapshot) : GameProbeFormatter.FormatTable(snapshot));
            return 0;

        case SteamShortcutsGameProbeCommand shortcuts:
            IReadOnlyList<SteamShortcut> parsed = SteamShortcutBinaryParser.Parse(await File.ReadAllBytesAsync(shortcuts.Path));
            Console.Write(GameProbeFormatter.FormatShortcuts(parsed));
            return 0;

        default:
            Console.Error.WriteLine($"Unsupported game probe command {command.GetType().Name}.");
            return 2;
    }
}
catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or FormatException or JsonException)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

static string ToJson(GameLibrarySnapshot snapshot) =>
    JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    });

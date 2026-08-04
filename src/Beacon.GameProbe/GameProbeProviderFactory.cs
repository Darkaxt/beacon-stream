using Beacon.Core.Games;
using Beacon.Platform.Windows.Games;

namespace Beacon.GameProbe;

public static class GameProbeProviderFactory
{
    public static IReadOnlyList<IGameLibraryProvider> CreateProviders(ScanGameProbeCommand command)
        => WindowsGameLibraryProviderFactory.CreateProviders(new(
            command.SteamRoot,
            command.HeroicRoot,
            command.HydraDatabasePath,
            command.ManualGamesPath));
}

namespace Beacon.Core.Games;

public interface IGameLibraryProvider
{
    string Name { get; }

    Task<GameLibrarySnapshot> ScanAsync(CancellationToken cancellationToken);
}

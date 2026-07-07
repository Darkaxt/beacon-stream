namespace Beacon.Core.Games;

public sealed class StaticGameLibraryProvider(string name, IReadOnlyList<GameDescriptor> games) : IGameLibraryProvider
{
    public string Name { get; } = name;

    public Task<GameLibrarySnapshot> ScanAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new GameLibrarySnapshot(games, []));
}

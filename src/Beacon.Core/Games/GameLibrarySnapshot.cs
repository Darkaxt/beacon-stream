namespace Beacon.Core.Games;

public sealed record GameLibrarySnapshot(IReadOnlyList<GameDescriptor> Games, IReadOnlyList<string> Diagnostics);

namespace Beacon.Core.Games;

public sealed record GameDescriptor(
    string Id,
    string Title,
    string Source,
    GameLaunchIntent Launch,
    GameArtwork Artwork,
    bool Installed,
    GameProcessHints ProcessHints);

using Beacon.Core.Games;

namespace Beacon.Server.Api;

public static class GameEndpoints
{
    public static IEndpointRouteBuilder MapGameEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/games", async (GameLibraryService games, CancellationToken cancellationToken) =>
        {
            GameLibrarySnapshot snapshot = await games.ScanAsync(cancellationToken);
            return Results.Ok(snapshot);
        });

        return endpoints;
    }
}

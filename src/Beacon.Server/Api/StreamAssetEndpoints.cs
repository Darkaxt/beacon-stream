using Beacon.Core.Streaming;

namespace Beacon.Server.Api;

public static class StreamAssetEndpoints
{
    private const string BeaconTestColorBarsAsset = "beacon-test-color-bars.h264";
    private const string H264ContentType = "video/H264";

    public static IEndpointRouteBuilder MapStreamAssetEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(BeaconTestStreamingBackend.EncodedVideoEndpoint, () =>
        {
            string assetPath = Path.Combine(AppContext.BaseDirectory, "Assets", BeaconTestColorBarsAsset);
            if (!File.Exists(assetPath))
            {
                return Results.Problem(
                    $"Beacon test encoded-video asset '{BeaconTestColorBarsAsset}' is missing.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Results.File(File.ReadAllBytes(assetPath), H264ContentType);
        });

        return endpoints;
    }
}

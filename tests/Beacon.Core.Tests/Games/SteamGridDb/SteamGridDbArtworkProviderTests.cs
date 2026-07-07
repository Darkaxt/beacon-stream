using System.Net;
using Beacon.Core.Games;
using Beacon.Core.Games.Artwork;
using Beacon.Core.Games.SteamGridDb;

namespace Beacon.Core.Tests.Games.SteamGridDb;

public sealed class SteamGridDbArtworkProviderTests
{
    [Fact]
    public async Task ChoosesFirstExactCandidateWithUsableGrid()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var handler = new FakeSteamGridDbHandler()
            .WithJson("/api/v2/search/autocomplete/Dispatch", """{"success":true,"data":[{"id":1,"name":"Dispatch"},{"id":2,"name":"Dispatch"}]}""")
            .WithJson("/api/v2/grids/game/1", """{"success":true,"data":[]}""")
            .WithJson("/api/v2/grids/game/2", """{"success":true,"data":[{"url":"https://cdn.example/dispatch.png","width":600,"height":900}]}""")
            .WithBytes("https://cdn.example/dispatch.png", [1, 2, 3]);

        try
        {
            var provider = new SteamGridDbArtworkProvider(
                new HttpClient(handler) { BaseAddress = new Uri("https://www.steamgriddb.com") },
                "test-key",
                root);

            GameArtwork artwork = await provider.GetArtworkAsync(
                new GameArtworkRequest("Dispatch", "manual:dispatch", null),
                CancellationToken.None);

            Assert.Equal("steamgriddb", artwork.Source);
            Assert.NotNull(artwork.CoverPath);
            Assert.EndsWith(".png", artwork.CoverPath, StringComparison.OrdinalIgnoreCase);
            Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(artwork.CoverPath));
            Assert.Contains(handler.Requests, request => request.Headers.Authorization?.Parameter == "test-key");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class FakeSteamGridDbHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, HttpResponseMessage> _responses = new(StringComparer.OrdinalIgnoreCase);

        public List<HttpRequestMessage> Requests { get; } = [];

        public FakeSteamGridDbHandler WithJson(string pathAndQuery, string json)
        {
            _responses[pathAndQuery] = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            };
            return this;
        }

        public FakeSteamGridDbHandler WithBytes(string absoluteUri, byte[] bytes)
        {
            _responses[absoluteUri] = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            };
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            string key = request.RequestUri!.Host.Equals("www.steamgriddb.com", StringComparison.OrdinalIgnoreCase)
                ? request.RequestUri.PathAndQuery
                : request.RequestUri.AbsoluteUri;

            if (_responses.TryGetValue(key, out HttpResponseMessage? response))
            {
                return Task.FromResult(Clone(response));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Clone(HttpResponseMessage response) =>
            new(response.StatusCode)
            {
                Content = response.Content
            };
    }
}

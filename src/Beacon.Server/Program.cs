using System.Text.Json.Serialization;
using Beacon.Core.Displays;
using Beacon.Core.Games;
using Beacon.Core.Games.Artwork;
using Beacon.Core.Sessions;
using Beacon.Core.Streaming;
using Beacon.Server.Api;
using Beacon.Server.State;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddSingleton<InMemoryClientStore>();
builder.Services.AddSingleton<InMemorySessionStore>();
builder.Services.AddSingleton<IDisplayBackend, FakeDisplayBackend>();
builder.Services.AddSingleton<DisplayLeaseManager>();
builder.Services.AddSingleton<IStreamingBackend, FakeStreamingBackend>();
builder.Services.AddSingleton<IGameLauncher, FakeGameLauncher>();
builder.Services.AddSingleton<FakeSessionActivityInspector>();
builder.Services.AddSingleton<ISessionActivityInspector>(sp => sp.GetRequiredService<FakeSessionActivityInspector>());
builder.Services.AddSingleton<ISessionOwnershipTracker, SessionOwnershipTracker>();
builder.Services.AddSingleton<IGameLibraryProvider>(_ => new StaticGameLibraryProvider(
    "seed",
    [
        new GameDescriptor(
            "steam-shortcut:3767414131",
            "Dispatch",
            "steam-shortcut",
            new GameLaunchIntent("steam-rungameid", "steam://rungameid/16180920483166814208"),
            new GameArtwork(null, "none"),
            Installed: true,
            new GameProcessHints(null, null))
    ]));
builder.Services.AddSingleton<IArtworkProvider, NoArtworkProvider>();
builder.Services.AddSingleton(sp => new GameLibraryService(
    sp.GetServices<IGameLibraryProvider>().ToArray(),
    sp.GetRequiredService<IArtworkProvider>()));

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapAdminEndpoints();
app.MapGameEndpoints();
app.MapClientEndpoints();

app.Run();

public partial class Program;

using System.Text.Json.Serialization;
using Beacon.Core.Displays;
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

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapClientEndpoints();

app.Run();

public partial class Program;

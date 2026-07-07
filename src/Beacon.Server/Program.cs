using System.Text.Json.Serialization;
using Beacon.Server.Api;
using Beacon.Server.Hosting;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddBeaconServices(builder.Configuration);

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapAdminEndpoints();
app.MapGameEndpoints();
app.MapClientEndpoints();

app.Run();

public partial class Program;

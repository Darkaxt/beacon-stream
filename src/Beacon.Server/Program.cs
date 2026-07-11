using System.Text.Json.Serialization;
using Beacon.Server.Api;
using Beacon.Server.Hosting;
using Beacon.Server.Security;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddBeaconServices(builder.Configuration);
builder.WebHost.ConfigureKestrel(options =>
    options.ConfigureHttpsDefaults(https =>
        https.ServerCertificate = options.ApplicationServices
            .GetRequiredService<BeaconServerIdentity>()
            .Certificate));

var app = builder.Build();

app.UseBeaconSecurity();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/identity", (BeaconServerIdentity identity) => Results.Ok(new
{
    algorithm = identity.Algorithm,
    publicKeyFingerprint = identity.PublicKeyFingerprint,
}));
app.MapAdminEndpoints();
app.MapGameEndpoints();
app.MapClientEndpoints();

app.Run();

public partial class Program;

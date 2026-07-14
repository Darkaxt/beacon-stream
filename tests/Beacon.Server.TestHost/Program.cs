using System.Text.Json.Serialization;
using Beacon.Server.Api;
using Beacon.Server.Hosting;
using Beacon.Server.Security;

namespace Beacon.Server.TestHost;

public static class TestHostProgram
{
    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        builder.Services.AddBeaconServices(builder.Configuration);
        builder.Services.UseBeaconFakeRuntime();
        builder.WebHost.ConfigureKestrel(options =>
            options.ConfigureHttpsDefaults(https =>
                https.ServerCertificate = options.ApplicationServices
                    .GetRequiredService<BeaconServerIdentity>()
                    .Certificate));

        WebApplication app = builder.Build();

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
    }
}

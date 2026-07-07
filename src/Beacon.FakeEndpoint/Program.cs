using Beacon.FakeEndpoint;

FakeEndpointCommandLineOptions options = FakeEndpointCommandLine.Parse(args);
using var client = new HttpClient { BaseAddress = options.ServerUri };
var runner = new FakeEndpointRunner(client);

FakeEndpointResult result = await runner.RunAsync(options.Script, CancellationToken.None);
foreach (string operation in result.Operations)
{
    Console.WriteLine(operation);
}

if (!result.Success)
{
    Console.Error.WriteLine(result.Error);
    return 1;
}

return 0;

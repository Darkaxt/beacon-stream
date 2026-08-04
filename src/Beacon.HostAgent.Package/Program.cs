using System.Text;
using Beacon.HostAgent.Update;

namespace Beacon.HostAgent.Package;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 11 && string.Equals(args[0], "build", StringComparison.Ordinal))
            {
                IReadOnlyDictionary<string, string> values = ParsePairs(args[1..]);
                string privateKey = ReadPrivateKey();
                try
                {
                    await HostAgentUpdatePackageBuilder.BuildAsync(
                        Require(values, "--payload-root"),
                        Require(values, "--package-root"),
                        new HostAgentUpdateBuildIdentity(
                            Require(values, "--package-id"),
                            Require(values, "--source-commit"),
                            Require(values, "--minimum-bootstrap-version")),
                        privateKey,
                        CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    privateKey = string.Empty;
                }
                return 0;
            }
            if (args.Length == 5 && string.Equals(args[0], "verify", StringComparison.Ordinal))
            {
                IReadOnlyDictionary<string, string> values = ParsePairs(args[1..]);
                var validator = new HostAgentUpdatePackageValidator(
                    HostAgentUpdateTrust.PublicKeyPem,
                    Version.Parse(Require(values, "--bootstrap-version")));
                HostAgentValidatedPackage package = await validator.ValidateAsync(
                    Require(values, "--package-root"),
                    CancellationToken.None).ConfigureAwait(false);
                Console.WriteLine(
                    $"verified package={package.Manifest.PackageId} source={package.Manifest.SourceCommit}");
                return 0;
            }
            throw new ArgumentException(
                "Usage: build <fixed options> | verify --package-root <path> --bootstrap-version <version>.");
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static IReadOnlyDictionary<string, string> ParsePairs(IReadOnlyList<string> arguments)
    {
        if (arguments.Count % 2 != 0)
        {
            throw new ArgumentException("Host Agent package options must be name/value pairs.");
        }
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < arguments.Count; index += 2)
        {
            if (!arguments[index].StartsWith("--", StringComparison.Ordinal)
                || !values.TryAdd(arguments[index], arguments[index + 1]))
            {
                throw new ArgumentException("Host Agent package option is invalid or duplicated.");
            }
        }
        return values;
    }

    private static string Require(IReadOnlyDictionary<string, string> values, string name) =>
        values.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Required Host Agent package option '{name}' is missing.");

    private static string ReadPrivateKey()
    {
        string encoded = Environment.GetEnvironmentVariable(
            "BEACON_HOST_AGENT_UPDATE_SIGNING_KEY_PEM_B64")
            ?? throw new InvalidOperationException("Host Agent package signing key is unavailable.");
        return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
    }
}

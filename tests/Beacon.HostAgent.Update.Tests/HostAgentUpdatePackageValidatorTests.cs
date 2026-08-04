using System.Security.Cryptography;
using System.Text.Json;
using Beacon.HostAgent.Update;

namespace Beacon.HostAgent.Update.Tests;

public sealed class HostAgentUpdatePackageValidatorTests
{
    [Fact]
    public async Task SignedExactPackageIsAccepted()
    {
        using var fixture = new PackageFixture();
        await fixture.BuildAsync();

        HostAgentValidatedPackage package = await fixture.ValidateAsync();

        Assert.Equal("agent-0123456789abcdef", package.Manifest.PackageId);
        Assert.Equal(new string('A', 40), package.Manifest.SourceCommit);
        Assert.Equal("Beacon.HostAgent.exe", package.Manifest.EntryPoint);
        Assert.Equal(2, package.Manifest.Files.Count);
    }

    [Fact]
    public async Task BuilderProducesDeterministicManifestBytes()
    {
        using var first = new PackageFixture();
        using var second = new PackageFixture();

        HostAgentBuiltPackage firstPackage = await first.BuildAsync();
        HostAgentBuiltPackage secondPackage = await second.BuildAsync();

        Assert.Equal(firstPackage.ManifestBytes, secondPackage.ManifestBytes);
    }

    [Fact]
    public async Task InvalidSignatureIsRejected()
    {
        using var fixture = new PackageFixture();
        await fixture.BuildAsync();
        File.WriteAllBytes(Path.Combine(fixture.PackageRoot, "manifest.sig"), [1, 2, 3]);

        await fixture.AssertRejectedAsync("package-signature-invalid");
    }

    [Fact]
    public async Task UnknownManifestFieldIsRejected()
    {
        using var fixture = new PackageFixture();
        await fixture.BuildAsync();
        fixture.RewriteManifest(json => json.Replace(
            "\"schemaVersion\":1,",
            "\"schemaVersion\":1,\"command\":\"calc.exe\","));

        await fixture.AssertRejectedAsync("invalid-package-manifest");
    }

    [Theory]
    [InlineData("target", "other-product", "package-target-mismatch")]
    [InlineData("architecture", "win-arm64", "package-architecture-mismatch")]
    [InlineData("minimumBootstrapVersion", "99.0.0", "package-bootstrap-incompatible")]
    [InlineData("entryPoint", "Missing.exe", "package-entry-point-mismatch")]
    public async Task IncompatibleManifestIdentityIsRejected(
        string property,
        string value,
        string expectedCode)
    {
        using var fixture = new PackageFixture();
        await fixture.BuildAsync();
        fixture.RewriteManifestProperty(property, value, resign: true);

        await fixture.AssertRejectedAsync(expectedCode);
    }

    [Theory]
    [InlineData("../Beacon.HostAgent.exe")]
    [InlineData("/Beacon.HostAgent.exe")]
    [InlineData("C:/Beacon.HostAgent.exe")]
    [InlineData("folder\\Beacon.HostAgent.exe")]
    [InlineData("folder//Beacon.HostAgent.exe")]
    public async Task UnsafeManifestPathIsRejected(string path)
    {
        using var fixture = new PackageFixture();
        await fixture.BuildAsync();
        fixture.RewriteFirstFilePath(path);

        await fixture.AssertRejectedAsync("invalid-package-path");
    }

    [Fact]
    public async Task CaseCollidingManifestPathsAreRejected()
    {
        using var fixture = new PackageFixture();
        await fixture.BuildAsync();
        fixture.DuplicateFirstFileWithUppercasePath();

        await fixture.AssertRejectedAsync("duplicate-package-file");
    }

    [Theory]
    [InlineData("extra-file", "unexpected-package-file")]
    [InlineData("missing-file", "unexpected-package-file")]
    [InlineData("wrong-length", "package-length-mismatch")]
    [InlineData("wrong-hash", "package-hash-mismatch")]
    public async Task PackageContentMismatchIsRejected(string mutation, string expectedCode)
    {
        using var fixture = new PackageFixture();
        await fixture.BuildAsync();
        fixture.MutateContent(mutation);

        await fixture.AssertRejectedAsync(expectedCode);
    }

    [Theory]
    [InlineData(true, false, "package-reparse-point")]
    [InlineData(false, true, "package-alternate-data-stream")]
    public async Task UnsafeFilesystemEntryIsRejected(
        bool reparsePoint,
        bool alternateStream,
        string expectedCode)
    {
        using var fixture = new PackageFixture();
        await fixture.BuildAsync();
        string unsafePath = Path.Combine(fixture.PackageRoot, "payload", "Beacon.HostAgent.exe");
        var inspector = new SelectiveUnsafeEntryInspector(
            unsafePath,
            reparsePoint,
            alternateStream);

        await fixture.AssertRejectedAsync(expectedCode, inspector);
    }

    private sealed class PackageFixture : IDisposable
    {
        private readonly string root = Path.Combine(
            Path.GetTempPath(),
            $"beacon-host-agent-update-{Guid.NewGuid():N}");
        private readonly ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        public PackageFixture()
        {
            PayloadRoot = Path.Combine(root, "source");
            PackageRoot = Path.Combine(root, "package");
            Directory.CreateDirectory(PayloadRoot);
            File.WriteAllText(Path.Combine(PayloadRoot, "Beacon.HostAgent.exe"), "agent-binary");
            Directory.CreateDirectory(Path.Combine(PayloadRoot, "support"));
            File.WriteAllText(Path.Combine(PayloadRoot, "support", "runtime.dll"), "runtime-binary");
        }

        public string PayloadRoot { get; }

        public string PackageRoot { get; }

        public Task<HostAgentBuiltPackage> BuildAsync() =>
            HostAgentUpdatePackageBuilder.BuildAsync(
                PayloadRoot,
                PackageRoot,
                new HostAgentUpdateBuildIdentity(
                    "agent-0123456789abcdef",
                    new string('A', 40),
                    "1.0.0"),
                signingKey.ExportPkcs8PrivateKeyPem(),
                CancellationToken.None);

        public Task<HostAgentValidatedPackage> ValidateAsync(
            IHostAgentPackageEntryInspector? inspector = null) =>
            new HostAgentUpdatePackageValidator(
                signingKey.ExportSubjectPublicKeyInfoPem(),
                new Version(1, 0, 0),
                inspector).ValidateAsync(PackageRoot, CancellationToken.None);

        public async Task AssertRejectedAsync(
            string expectedCode,
            IHostAgentPackageEntryInspector? inspector = null)
        {
            HostAgentUpdateValidationException error = await Assert.ThrowsAsync<
                HostAgentUpdateValidationException>(() => ValidateAsync(inspector));
            Assert.Equal(expectedCode, error.Code);
        }

        public void RewriteManifest(Func<string, string> rewrite, bool resign = false)
        {
            string path = Path.Combine(PackageRoot, "manifest.json");
            string content = rewrite(File.ReadAllText(path));
            File.WriteAllText(path, content);
            if (resign)
            {
                SignManifest();
            }
        }

        public void RewriteManifestProperty(string property, string value, bool resign)
        {
            using JsonDocument source = JsonDocument.Parse(
                File.ReadAllBytes(Path.Combine(PackageRoot, "manifest.json")));
            var properties = source.RootElement.EnumerateObject()
                .ToDictionary(item => item.Name, item => item.Value.Clone(), StringComparer.Ordinal);
            using JsonDocument replacement = JsonDocument.Parse(JsonSerializer.Serialize(value));
            properties[property] = replacement.RootElement.Clone();
            RewriteManifest(
                _ => JsonSerializer.Serialize(properties),
                resign);
        }

        public void RewriteFirstFilePath(string path)
        {
            HostAgentUpdateManifest manifest = ReadManifest();
            HostAgentUpdateFile[] files = manifest.Files.ToArray();
            files[0] = files[0] with { Path = path };
            WriteManifest(manifest with { Files = files });
        }

        public void DuplicateFirstFileWithUppercasePath()
        {
            HostAgentUpdateManifest manifest = ReadManifest();
            HostAgentUpdateFile first = manifest.Files[0];
            WriteManifest(manifest with
            {
                Files = [.. manifest.Files, first with { Path = first.Path.ToUpperInvariant() }]
            });
        }

        public void MutateContent(string mutation)
        {
            string payload = Path.Combine(PackageRoot, "payload", "Beacon.HostAgent.exe");
            switch (mutation)
            {
                case "extra-file":
                    File.WriteAllText(Path.Combine(PackageRoot, "payload", "extra.txt"), "extra");
                    break;
                case "missing-file":
                    File.Delete(payload);
                    break;
                case "wrong-length":
                    File.AppendAllText(payload, "x");
                    break;
                case "wrong-hash":
                    byte[] bytes = File.ReadAllBytes(payload);
                    bytes[0] ^= 0x1;
                    File.WriteAllBytes(payload, bytes);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mutation));
            }
        }

        public void Dispose()
        {
            signingKey.Dispose();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private HostAgentUpdateManifest ReadManifest() =>
            JsonSerializer.Deserialize<HostAgentUpdateManifest>(
                File.ReadAllBytes(Path.Combine(PackageRoot, "manifest.json")),
                HostAgentUpdateJson.Options)
            ?? throw new InvalidDataException("Manifest is empty.");

        private void WriteManifest(HostAgentUpdateManifest manifest)
        {
            File.WriteAllBytes(
                Path.Combine(PackageRoot, "manifest.json"),
                JsonSerializer.SerializeToUtf8Bytes(manifest, HostAgentUpdateJson.Options));
            SignManifest();
        }

        private void SignManifest()
        {
            byte[] manifest = File.ReadAllBytes(Path.Combine(PackageRoot, "manifest.json"));
            File.WriteAllBytes(
                Path.Combine(PackageRoot, "manifest.sig"),
                signingKey.SignData(manifest, HashAlgorithmName.SHA256));
        }
    }

    private sealed class SelectiveUnsafeEntryInspector(
        string unsafePath,
        bool reparsePoint,
        bool alternateStream) : IHostAgentPackageEntryInspector
    {
        public bool IsReparsePoint(string path) =>
            reparsePoint && Path.GetFullPath(path) == Path.GetFullPath(unsafePath);

        public bool HasAlternateDataStream(string path) =>
            alternateStream && Path.GetFullPath(path) == Path.GetFullPath(unsafePath);
    }
}

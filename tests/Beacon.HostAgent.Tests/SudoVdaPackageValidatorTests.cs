using System.Security.Cryptography;
using System.Text.Json;
using Beacon.HostAgent.DriverUpdates;

namespace Beacon.HostAgent.Tests;

public sealed class SudoVdaPackageValidatorTests
{
    [Fact]
    public async Task ValidCompletePackageIsCopiedAndReverifiedInProtectedStaging()
    {
        using var fixture = new PackageFixture();
        fixture.WriteValidPackage();

        SudoVdaValidatedPackage result = await fixture.Validator.ValidateAndStageAsync(
            fixture.PackageId,
            fixture.TransactionId,
            CancellationToken.None);

        Assert.Equal("22.48.58.193", result.PackageVersion);
        Assert.Equal("0.2.0", result.ProtocolVersion);
        Assert.Equal("ROOT\\SudoMaker\\SudoVDA", result.HardwareId);
        Assert.Equal(fixture.StagedPackagePath, result.PackageRoot);
        Assert.Equal("SudoVDA.inf", Path.GetFileName(result.InfPath));
        Assert.Equal(
            new[] { "manifest.json", "sudovda.cat", "SudoVDA.dll", "SudoVDA.inf" },
            Directory.EnumerateFiles(result.PackageRoot)
                .Select(Path.GetFileName)
                .Order(StringComparer.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../outside")]
    [InlineData("..\\outside")]
    [InlineData("C:\\outside")]
    [InlineData("package/child")]
    public async Task PackageIdentifierCannotSelectAPath(string packageId)
    {
        using var fixture = new PackageFixture();

        SudoVdaPackageValidationException error = await Assert.ThrowsAsync<
            SudoVdaPackageValidationException>(() => fixture.Validator.ValidateAndStageAsync(
                packageId,
                fixture.TransactionId,
                CancellationToken.None));

        Assert.Equal("invalid-package-id", error.Code);
    }

    [Fact]
    public async Task UnexpectedFileIsRejected()
    {
        using var fixture = new PackageFixture();
        fixture.WriteValidPackage();
        File.WriteAllText(Path.Combine(fixture.InboxPackagePath, "payload.exe"), "unexpected");

        await fixture.AssertRejectedAsync("unexpected-package-file");
    }

    [Fact]
    public async Task HashMismatchIsRejected()
    {
        using var fixture = new PackageFixture();
        fixture.WriteValidPackage();
        File.AppendAllText(Path.Combine(fixture.InboxPackagePath, "SudoVDA.dll"), "tampered");

        await fixture.AssertRejectedAsync("package-hash-mismatch");
    }

    [Fact]
    public async Task AbsoluteManifestPathIsRejected()
    {
        using var fixture = new PackageFixture();
        fixture.WriteValidPackage(filesTransform: files =>
        {
            files[0] = files[0] with { Path = @"C:\\SudoVDA.inf" };
            return files;
        });

        await fixture.AssertRejectedAsync("invalid-package-file-path");
    }

    [Fact]
    public async Task AlternateDataStreamIsRejected()
    {
        using var fixture = new PackageFixture();
        fixture.WriteValidPackage();
        File.WriteAllText(
            Path.Combine(fixture.InboxPackagePath, "SudoVDA.dll:concealed"),
            "unexpected");

        await fixture.AssertRejectedAsync("package-alternate-data-stream");
    }

    [Fact]
    public async Task ReparsePointIsRejected()
    {
        using var fixture = new PackageFixture();
        fixture.WriteValidPackage();
        string external = Path.Combine(fixture.Root, "external");
        Directory.CreateDirectory(external);
        Directory.CreateSymbolicLink(
            Path.Combine(fixture.InboxPackagePath, "linked"),
            external);

        await fixture.AssertRejectedAsync("package-reparse-point");
    }

    [Theory]
    [InlineData("arm64", "package-architecture-mismatch")]
    [InlineData("x86", "package-architecture-mismatch")]
    public async Task NonX64ArchitectureIsRejected(string architecture, string expectedCode)
    {
        using var fixture = new PackageFixture();
        fixture.WriteValidPackage(manifestTransform: manifest =>
            manifest with { Architecture = architecture });

        await fixture.AssertRejectedAsync(expectedCode);
    }

    [Fact]
    public async Task WrongHardwareIdentityIsRejected()
    {
        using var fixture = new PackageFixture();
        fixture.WriteValidPackage(manifestTransform: manifest =>
            manifest with { HardwareId = @"ROOT\\Other\\Display" });

        await fixture.AssertRejectedAsync("package-hardware-id-mismatch");
    }

    [Fact]
    public async Task IncompatibleProtocolIsRejected()
    {
        using var fixture = new PackageFixture();
        fixture.WriteValidPackage(manifestTransform: manifest =>
            manifest with { ProtocolVersion = "0.1.9" });

        await fixture.AssertRejectedAsync("package-protocol-incompatible");
    }

    [Fact]
    public async Task ManifestSignerOutsideAllowlistIsRejected()
    {
        using var fixture = new PackageFixture();
        fixture.WriteValidPackage(manifestTransform: manifest => manifest with
        {
            SignerSubject = "CN=Untrusted",
            SignerThumbprint = "BAD0"
        });

        await fixture.AssertRejectedAsync("package-signer-mismatch");
    }

    [Fact]
    public async Task InvalidCatalogSignatureIsRejected()
    {
        using var fixture = new PackageFixture(signatureValid: false);
        fixture.WriteValidPackage();

        await fixture.AssertRejectedAsync("package-signature-invalid");
    }

    [Fact]
    public async Task InfMustDescribeTheExpectedSudoVdaPackage()
    {
        using var fixture = new PackageFixture();
        fixture.WriteValidPackage(infTransform: inf =>
            inf.Replace("Class=Display", "Class=SoftwareComponent", StringComparison.Ordinal));

        await fixture.AssertRejectedAsync("package-inf-mismatch");
    }

    private sealed class PackageFixture : IDisposable
    {
        private const string SignerSubject = "CN=Beacon Test Driver";
        private const string SignerThumbprint = "0123456789ABCDEF";
        private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

        public PackageFixture(bool signatureValid = true)
        {
            Root = Path.Combine(Path.GetTempPath(), $"beacon-driver-package-{Guid.NewGuid():N}");
            string inbox = Path.Combine(Root, "Inbox");
            string staged = Path.Combine(Root, "Staged");
            Directory.CreateDirectory(inbox);
            Directory.CreateDirectory(staged);
            Paths = new SudoVdaPackagePaths(inbox, staged);
            Signatures = new FakeSignatureVerifier
            {
                Result = new SudoVdaSignatureEvidence(
                    signatureValid,
                    SignerSubject,
                    SignerThumbprint,
                    signatureValid ? "valid" : "invalid")
            };
            Validator = new SudoVdaPackageValidator(
                Paths,
                new SudoVdaPackagePolicy(
                    "x64",
                    @"ROOT\SudoMaker\SudoVDA",
                    new Version(0, 2, 0),
                    SignerSubject,
                    SignerThumbprint),
                Signatures);
        }

        public string Root { get; }

        public string PackageId { get; } = "sudovda-22.48.58.193";

        public Guid TransactionId { get; } = Guid.NewGuid();

        public SudoVdaPackagePaths Paths { get; }

        public FakeSignatureVerifier Signatures { get; }

        public SudoVdaPackageValidator Validator { get; }

        public string InboxPackagePath => Path.Combine(Paths.Inbox, PackageId);

        public string StagedPackagePath => Path.Combine(Paths.Staged, TransactionId.ToString("D"));

        public void WriteValidPackage(
            Func<string, string>? infTransform = null,
            Func<SudoVdaPackageManifest, SudoVdaPackageManifest>? manifestTransform = null,
            Func<SudoVdaPackageFile[], SudoVdaPackageFile[]>? filesTransform = null)
        {
            Directory.CreateDirectory(InboxPackagePath);
            string inf = infTransform?.Invoke(ValidInf) ?? ValidInf;
            File.WriteAllText(Path.Combine(InboxPackagePath, "SudoVDA.inf"), inf);
            File.WriteAllText(Path.Combine(InboxPackagePath, "sudovda.cat"), "catalog");
            File.WriteAllText(Path.Combine(InboxPackagePath, "SudoVDA.dll"), "driver-binary");

            SudoVdaPackageFile[] files =
            [
                PackageFile("SudoVDA.inf"),
                PackageFile("sudovda.cat"),
                PackageFile("SudoVDA.dll")
            ];
            files = filesTransform?.Invoke(files) ?? files;
            var manifest = new SudoVdaPackageManifest(
                SchemaVersion: 1,
                PackageVersion: "22.48.58.193",
                Architecture: "x64",
                HardwareId: @"ROOT\SudoMaker\SudoVDA",
                ProtocolVersion: "0.2.0",
                SignerSubject,
                SignerThumbprint,
                InfPath: "SudoVDA.inf",
                Files: files);
            manifest = manifestTransform?.Invoke(manifest) ?? manifest;
            File.WriteAllText(
                Path.Combine(InboxPackagePath, "manifest.json"),
                JsonSerializer.Serialize(manifest, json));
        }

        public async Task AssertRejectedAsync(string code)
        {
            SudoVdaPackageValidationException error = await Assert.ThrowsAsync<
                SudoVdaPackageValidationException>(() => Validator.ValidateAndStageAsync(
                    PackageId,
                    TransactionId,
                    CancellationToken.None));

            Assert.Equal(code, error.Code);
            Assert.False(Directory.Exists(StagedPackagePath));
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private SudoVdaPackageFile PackageFile(string path) =>
            new(path, Convert.ToHexString(SHA256.HashData(
                File.ReadAllBytes(Path.Combine(InboxPackagePath, path)))));

        private const string ValidInf = """
            [Version]
            Signature="$Windows NT$"
            ClassGUID={4D36E968-E325-11CE-BFC1-08002BE10318}
            Class=Display
            Provider=%ManufacturerName%
            CatalogFile=sudovda.cat
            DriverVer=07/20/2026,22.48.58.193

            [Manufacturer]
            %ManufacturerName%=Standard,NTamd64

            [Standard.NTamd64]
            %DeviceName%=SudoVDA_Install, Root\SudoMaker\SudoVDA

            [SourceDisksFiles]
            SudoVDA.dll=1

            [Strings]
            ManufacturerName="SudoMaker"
            DeviceName="SudoMaker Virtual Display Adapter"
            """;
    }

    private sealed class FakeSignatureVerifier : ISudoVdaSignatureVerifier
    {
        public required SudoVdaSignatureEvidence Result { get; init; }

        public SudoVdaSignatureEvidence Verify(string path) => Result;
    }
}

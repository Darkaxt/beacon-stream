using System.Security.Cryptography;
using Beacon.HostAgent.DriverUpdates;
using Beacon.Platform.Windows.Displays;

namespace Beacon.HostAgent.Tests;

public sealed class WindowsSudoVdaDriverPlatformTests
{
    [Fact]
    public async Task QueryActiveEvidenceCombinesStructuredDeviceProtocolSignatureAndHash()
    {
        using var fixture = new PlatformFixture();

        SudoVdaDriverEvidence evidence = await fixture.Platform.QueryActiveEvidenceAsync();

        Assert.Equal(@"ROOT\DISPLAY\0000", evidence.DeviceInstanceId);
        Assert.Equal(@"root\sudomaker\sudovda", evidence.HardwareId);
        Assert.Equal("oem163.inf", evidence.PublishedInf);
        Assert.Equal("22.48.58.193", evidence.DriverVersion);
        Assert.Equal("0.2.0", evidence.ProtocolVersion);
        Assert.Equal("CN=Beacon Test Driver", evidence.SignerSubject);
        Assert.Equal("0123456789ABCDEF", evidence.SignerThumbprint);
        Assert.Equal(fixture.ActiveBinaryHash, evidence.BinarySha256);
        Assert.True(evidence.DeviceHealthy);
    }

    [Fact]
    public async Task QueryHostStateCountsAgentLeasesAndActiveVirtualPaths()
    {
        using var fixture = new PlatformFixture();
        fixture.Display.LeaseCount = 2;
        fixture.Display.Topology = new DisplayTopologySnapshot(
            [
                new DisplayPathSnapshot(
                    "physical-1",
                    DisplayPathKind.Physical,
                    2560,
                    1600,
                    240,
                    true,
                    0,
                    0),
                new DisplayPathSnapshot(
                    "client-z-fold-7",
                    DisplayPathKind.Virtual,
                    2560,
                    1600,
                    120,
                    false,
                    2560,
                    0)
            ],
            IsMirrorMode: false);

        SudoVdaUpdateHostState state = await fixture.Platform.QueryHostStateAsync();

        Assert.Equal(2, state.ActiveLeaseCount);
        Assert.Equal(1, state.ActiveVirtualDisplayCount);
    }

    [Fact]
    public async Task FixedPnpUtilOperationsUseOnlyValidatedInternalEvidence()
    {
        using var fixture = new PlatformFixture();
        SudoVdaValidatedPackage package = fixture.CreatePackage();
        string exportRoot = Path.Combine(fixture.Root, "export");

        await fixture.Platform.ExportActivePackageAsync(fixture.PreviousEvidence, exportRoot);
        await fixture.Platform.InstallAsync(package);
        await fixture.Platform.RestartAsync(fixture.PreviousEvidence.DeviceInstanceId);

        Assert.Equal(
            new[]
            {
                new[] { "/export-driver", "oem163.inf", exportRoot },
                new[] { "/add-driver", package.InfPath, "/install" },
                new[] { "/restart-device", @"ROOT\DISPLAY\0000" }
            },
            fixture.Runner.Invocations);
    }

    [Fact]
    public async Task ExportRejectsSuccessWithoutAUsableRollbackPackage()
    {
        using var fixture = new PlatformFixture();
        fixture.Runner.MaterializeExportedPackage = false;

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Platform.ExportActivePackageAsync(
                fixture.PreviousEvidence,
                Path.Combine(fixture.Root, "export")));

        Assert.Contains("rollback package", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RollbackRemovesOnlyResolvedCandidateAndInstallsExportedInf()
    {
        using var fixture = new PlatformFixture();
        fixture.Inventory.Device = fixture.Inventory.Device with { PublishedInf = "oem200.inf" };
        string exportRoot = Path.Combine(fixture.Root, "export");
        Directory.CreateDirectory(exportRoot);
        string exportedInf = Path.Combine(exportRoot, "SudoVDA.inf");
        File.WriteAllText(exportedInf, "exported");

        await fixture.Platform.RollbackAsync(exportRoot, fixture.PreviousEvidence);

        Assert.Equal(
            new[]
            {
                new[] { "/delete-driver", "oem200.inf", "/uninstall", "/force" },
                new[] { "/add-driver", exportedInf, "/install" }
            },
            fixture.Runner.Invocations);
    }

    [Theory]
    [InlineData("not-an-inf")]
    [InlineData("oem1.inf /force")]
    [InlineData("..\\oem1.inf")]
    public async Task PublishedInfCannotInjectPnpUtilArguments(string publishedInf)
    {
        using var fixture = new PlatformFixture();
        SudoVdaDriverEvidence evidence = fixture.PreviousEvidence with
        {
            PublishedInf = publishedInf
        };

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Platform.ExportActivePackageAsync(
                evidence,
                Path.Combine(fixture.Root, "export")));

        Assert.Empty(fixture.Runner.Invocations);
    }

    [Fact]
    public async Task RestartRejectsDeviceNotResolvedAsTheActiveSudoVdaInstance()
    {
        using var fixture = new PlatformFixture();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Platform.RestartAsync(@"ROOT\OTHER\0000"));

        Assert.Empty(fixture.Runner.Invocations);
    }

    [Fact]
    public async Task InstallRejectsInfOutsideValidatedPackageRoot()
    {
        using var fixture = new PlatformFixture();
        SudoVdaValidatedPackage package = fixture.CreatePackage() with
        {
            InfPath = Path.Combine(fixture.Root, "outside.inf")
        };
        File.WriteAllText(package.InfPath, "outside");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Platform.InstallAsync(package));

        Assert.Empty(fixture.Runner.Invocations);
    }

    private sealed class PlatformFixture : IDisposable
    {
        public PlatformFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"beacon-windows-driver-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
            string activeBinary = Path.Combine(Root, "SudoVDA.dll");
            File.WriteAllText(activeBinary, "active-driver");
            ActiveBinaryHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(activeBinary)));
            Inventory = new FakeInventory
            {
                Device = new SudoVdaInstalledDevice(
                    @"ROOT\DISPLAY\0000",
                    @"root\sudomaker\sudovda",
                    "oem163.inf",
                    "22.48.58.193",
                    "SudoMaker",
                    activeBinary,
                    DeviceHealthy: true)
            };
            Signatures = new FakeSignatures
            {
                Evidence = new SudoVdaSignatureEvidence(
                    Valid: true,
                    Subject: "CN=Beacon Test Driver",
                    Thumbprint: "0123456789ABCDEF",
                    Diagnostic: "valid")
            };
            Display = new FakeDisplayGuard();
            Runner = new FakePnpUtilRunner();
            Platform = new WindowsSudoVdaDriverPlatform(
                Inventory,
                Signatures,
                Display,
                Runner);
            PreviousEvidence = new SudoVdaDriverEvidence(
                Inventory.Device.DeviceInstanceId,
                Inventory.Device.HardwareId,
                Inventory.Device.PublishedInf,
                Inventory.Device.DriverVersion,
                "0.2.0",
                Signatures.Evidence.Subject,
                Signatures.Evidence.Thumbprint,
                ActiveBinaryHash,
                DeviceHealthy: true);
        }

        public string Root { get; }

        public string ActiveBinaryHash { get; }

        public FakeInventory Inventory { get; }

        public FakeSignatures Signatures { get; }

        public FakeDisplayGuard Display { get; }

        public FakePnpUtilRunner Runner { get; }

        public WindowsSudoVdaDriverPlatform Platform { get; }

        public SudoVdaDriverEvidence PreviousEvidence { get; }

        public SudoVdaValidatedPackage CreatePackage()
        {
            string packageRoot = Path.Combine(Root, "package");
            Directory.CreateDirectory(packageRoot);
            string infPath = Path.Combine(packageRoot, "SudoVDA.inf");
            File.WriteAllText(infPath, "candidate");
            return new SudoVdaValidatedPackage(
                "sudovda-22.48.58.193",
                "22.48.58.193",
                "0.2.0",
                @"ROOT\SudoMaker\SudoVDA",
                packageRoot,
                infPath,
                Signatures.Evidence.Subject,
                Signatures.Evidence.Thumbprint,
                new Dictionary<string, string>
                {
                    ["SudoVDA.dll"] = "candidate-hash"
                });
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private sealed class FakeInventory : ISudoVdaDeviceInventory
    {
        public required SudoVdaInstalledDevice Device { get; set; }

        public SudoVdaInstalledDevice QueryActiveDevice() => Device;
    }

    private sealed class FakeSignatures : ISudoVdaSignatureVerifier
    {
        public required SudoVdaSignatureEvidence Evidence { get; init; }

        public SudoVdaSignatureEvidence Verify(string path) => Evidence;
    }

    private sealed class FakeDisplayGuard : ISudoVdaDisplayUpdateGuard
    {
        public int LeaseCount { get; set; }

        public DisplayTopologySnapshot Topology { get; set; } =
            new([], IsMirrorMode: false);

        public DisplayDriverStatus GetDriverStatus() =>
            new(
                Ready: true,
                Diagnostic: "ready",
                ProtocolMajor: 0,
                ProtocolMinor: 2,
                ProtocolIncremental: 0);

        public int GetActiveLeaseCount() => LeaseCount;

        public Task<DisplayTopologySnapshot> QueryTopologyAsync() => Task.FromResult(Topology);
    }

    private sealed class FakePnpUtilRunner : IPnpUtilRunner
    {
        public List<IReadOnlyList<string>> Invocations { get; } = [];

        public bool MaterializeExportedPackage { get; set; } = true;

        public Task<PnpUtilResult> RunAsync(IReadOnlyList<string> arguments)
        {
            Invocations.Add(arguments.ToArray());
            if (MaterializeExportedPackage
                && arguments.Count == 3
                && string.Equals(arguments[0], "/export-driver", StringComparison.Ordinal))
            {
                string packageRoot = Path.Combine(arguments[2], "sudovda.inf_amd64_test");
                Directory.CreateDirectory(packageRoot);
                File.WriteAllText(Path.Combine(packageRoot, "SudoVDA.inf"), "inf");
                File.WriteAllText(Path.Combine(packageRoot, "SudoVDA.cat"), "cat");
                File.WriteAllText(Path.Combine(packageRoot, "SudoVDA.dll"), "dll");
            }
            return Task.FromResult(new PnpUtilResult(0, "ok", string.Empty));
        }
    }
}

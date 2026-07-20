using System.Diagnostics;
using Beacon.HostAgent.DriverUpdates;

namespace Beacon.HostAgent.Tests;

public sealed class WindowsDriverInteropTests
{
    [Fact]
    public void LiveSudoVdaInventoryAndSignatureAreReadableWhenEnabled()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable("BEACON_TEST_LIVE_SUDOVDA"),
            "1",
            StringComparison.Ordinal))
        {
            return;
        }

        var inventory = new WindowsSudoVdaDeviceInventory();
        var signatures = new WindowsSudoVdaSignatureVerifier();

        SudoVdaInstalledDevice device = inventory.QueryActiveDevice();
        SudoVdaSignatureEvidence signature = signatures.Verify(device.ActiveBinaryPath);

        Assert.Equal(@"root\sudomaker\sudovda", device.HardwareId, ignoreCase: true);
        Assert.Matches("^oem[0-9]+\\.inf$", device.PublishedInf);
        Assert.True(device.DeviceHealthy);
        Assert.True(File.Exists(device.ActiveBinaryPath));
        Assert.True(signature.Valid, signature.Diagnostic);
        Assert.False(string.IsNullOrWhiteSpace(signature.Subject));
        Assert.False(string.IsNullOrWhiteSpace(signature.Thumbprint));
    }

    [Fact]
    public void InventorySelectsOnlyPresentExpectedSudoVdaDevice()
    {
        string binaryPath = Path.Combine(Path.GetTempPath(), "SudoVDA.dll");
        var source = new FakeDeviceSource
        {
            Devices =
            [
                new WindowsDisplayDeviceProperties(
                    @"PCI\GPU\0000",
                    "pci-display",
                    "oem1.inf",
                    "1.0.0.0",
                    "Vendor",
                    ProblemCode: 0),
                new WindowsDisplayDeviceProperties(
                    @"ROOT\DISPLAY\0000",
                    @"root\sudomaker\sudovda",
                    "oem163.inf",
                    "22.48.58.193",
                    "SudoMaker",
                    ProblemCode: 0)
            ]
        };
        var inventory = new WindowsSudoVdaDeviceInventory(source, binaryPath);

        SudoVdaInstalledDevice result = inventory.QueryActiveDevice();

        Assert.Equal(@"ROOT\DISPLAY\0000", result.DeviceInstanceId);
        Assert.Equal("oem163.inf", result.PublishedInf);
        Assert.Equal(binaryPath, result.ActiveBinaryPath);
        Assert.True(result.DeviceHealthy);
    }

    [Fact]
    public void InventoryRejectsAmbiguousSudoVdaDevices()
    {
        var expected = new WindowsDisplayDeviceProperties(
            @"ROOT\DISPLAY\0000",
            @"root\sudomaker\sudovda",
            "oem163.inf",
            "22.48.58.193",
            "SudoMaker",
            ProblemCode: 0);
        var source = new FakeDeviceSource { Devices = [expected, expected] };
        var inventory = new WindowsSudoVdaDeviceInventory(source, "SudoVDA.dll");

        Assert.Throws<InvalidDataException>(() => inventory.QueryActiveDevice());
    }

    [Fact]
    public void PnpUtilStartInfoUsesFixedSystemExecutableAndLiteralArgumentList()
    {
        string executable = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "pnputil.exe");
        var runner = new WindowsPnpUtilRunner(executable);
        string[] arguments = ["/restart-device", @"ROOT\DISPLAY\0000"];

        ProcessStartInfo info = runner.CreateStartInfo(arguments);

        Assert.Equal(executable, info.FileName);
        Assert.Equal(arguments, info.ArgumentList);
        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow);
        Assert.True(info.RedirectStandardOutput);
        Assert.True(info.RedirectStandardError);
        Assert.Empty(info.Arguments);
    }

    [Fact]
    public void UnsignedFileDoesNotProduceTrustedSignerEvidence()
    {
        string path = Path.Combine(Path.GetTempPath(), $"beacon-unsigned-{Guid.NewGuid():N}.bin");
        File.WriteAllText(path, "unsigned");
        try
        {
            var verifier = new WindowsSudoVdaSignatureVerifier();

            SudoVdaSignatureEvidence result = verifier.Verify(path);

            Assert.False(result.Valid);
            Assert.Empty(result.Subject);
            Assert.Empty(result.Thumbprint);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class FakeDeviceSource : IWindowsDisplayDevicePropertySource
    {
        public IReadOnlyList<WindowsDisplayDeviceProperties> Devices { get; init; } = [];

        public IReadOnlyList<WindowsDisplayDeviceProperties> EnumeratePresentDisplayDevices() =>
            Devices;
    }
}

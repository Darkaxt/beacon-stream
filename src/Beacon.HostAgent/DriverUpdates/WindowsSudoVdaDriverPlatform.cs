using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Beacon.Platform.Windows.Displays;

namespace Beacon.HostAgent.DriverUpdates;

internal sealed partial class WindowsSudoVdaDriverPlatform(
    ISudoVdaDeviceInventory inventory,
    ISudoVdaSignatureVerifier signatures,
    ISudoVdaDisplayUpdateGuard displays,
    IPnpUtilRunner pnpUtil) : ISudoVdaUpdatePlatform
{
    private const string ExpectedHardwareId = @"root\sudomaker\sudovda";
    private const string ExpectedProvider = "SudoMaker";

    public async Task<SudoVdaUpdateHostState> QueryHostStateAsync()
    {
        DisplayTopologySnapshot topology = await displays.QueryTopologyAsync()
            .ConfigureAwait(false);
        return new SudoVdaUpdateHostState(
            displays.GetActiveLeaseCount(),
            topology.Paths.Count(path => path.Kind == DisplayPathKind.Virtual));
    }

    public async Task<SudoVdaDriverEvidence> QueryActiveEvidenceAsync()
    {
        SudoVdaInstalledDevice device = QueryVerifiedDevice();
        if (!File.Exists(device.ActiveBinaryPath))
        {
            throw new InvalidDataException("The active SudoVDA UMDF binary is unavailable.");
        }

        SudoVdaSignatureEvidence signature = signatures.Verify(device.ActiveBinaryPath);
        DisplayDriverStatus status = displays.GetDriverStatus();
        string protocol = status.ProtocolMajor is byte major
            && status.ProtocolMinor is byte minor
            && status.ProtocolIncremental is byte incremental
                ? $"{major}.{minor}.{incremental}"
                : string.Empty;
        string hash = await HashFileAsync(device.ActiveBinaryPath).ConfigureAwait(false);
        return new SudoVdaDriverEvidence(
            device.DeviceInstanceId,
            device.HardwareId,
            RequirePublishedInf(device.PublishedInf),
            device.DriverVersion,
            protocol,
            signature.Subject,
            signature.Thumbprint,
            hash,
            device.DeviceHealthy && status.Ready && signature.Valid);
    }

    public async Task ExportActivePackageAsync(
        SudoVdaDriverEvidence evidence,
        string destinationRoot)
    {
        string publishedInf = RequirePublishedInf(evidence.PublishedInf);
        SudoVdaInstalledDevice current = QueryVerifiedDevice();
        if (!string.Equals(
            current.DeviceInstanceId,
            evidence.DeviceInstanceId,
            StringComparison.OrdinalIgnoreCase)
            || !string.Equals(current.PublishedInf, publishedInf, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Active SudoVDA evidence changed before package export.");
        }

        string root = RequireAbsoluteDirectory(destinationRoot);
        Directory.CreateDirectory(root);
        PnpUtilResult result = await pnpUtil.RunAsync(
            ["/export-driver", publishedInf, root]).ConfigureAwait(false);
        RequireExitCode(result, 0, "SudoVDA package export failed.");
        RequireUsableRollbackPackage(root);
    }

    public async Task InstallAsync(SudoVdaValidatedPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        string packageRoot = Path.GetFullPath(package.PackageRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string infPath = Path.GetFullPath(package.InfPath);
        if (!infPath.StartsWith(packageRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(infPath)
            || !string.Equals(Path.GetExtension(infPath), ".inf", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The candidate INF is outside its validated package.");
        }

        PnpUtilResult result = await pnpUtil.RunAsync(
            ["/add-driver", infPath, "/install"]).ConfigureAwait(false);
        RequireExitCode(result, [0, 259, 3010], "SudoVDA package installation failed.");
    }

    public async Task RestartAsync(string deviceInstanceId)
    {
        SudoVdaInstalledDevice current = QueryVerifiedDevice();
        if (string.IsNullOrWhiteSpace(deviceInstanceId)
            || !string.Equals(
                current.DeviceInstanceId,
                deviceInstanceId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The requested device is not the active SudoVDA instance.");
        }

        PnpUtilResult result = await pnpUtil.RunAsync(
            ["/restart-device", current.DeviceInstanceId]).ConfigureAwait(false);
        RequireExitCode(result, [0, 3010], "SudoVDA device restart failed.");
    }

    public async Task RollbackAsync(
        string exportedPackageRoot,
        SudoVdaDriverEvidence previousEvidence)
    {
        string root = RequireAbsoluteDirectory(exportedPackageRoot);
        if (!Directory.Exists(root))
        {
            throw new InvalidDataException("The exported rollback package is unavailable.");
        }
        string[] infFiles = Directory.EnumerateFiles(root, "*.inf", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .ToArray();
        if (infFiles.Length != 1)
        {
            throw new InvalidDataException("The exported rollback package must contain one INF.");
        }

        SudoVdaInstalledDevice current = QueryVerifiedDevice();
        string currentInf = RequirePublishedInf(current.PublishedInf);
        string previousInf = RequirePublishedInf(previousEvidence.PublishedInf);
        if (!string.Equals(currentInf, previousInf, StringComparison.OrdinalIgnoreCase))
        {
            PnpUtilResult delete = await pnpUtil.RunAsync(
                ["/delete-driver", currentInf, "/uninstall", "/force"]).ConfigureAwait(false);
            RequireExitCode(delete, 0, "Candidate SudoVDA package removal failed.");
        }

        PnpUtilResult install = await pnpUtil.RunAsync(
            ["/add-driver", infFiles[0], "/install"]).ConfigureAwait(false);
        RequireExitCode(install, [0, 259, 3010], "Previous SudoVDA package installation failed.");
    }

    private SudoVdaInstalledDevice QueryVerifiedDevice()
    {
        SudoVdaInstalledDevice device = inventory.QueryActiveDevice();
        if (!string.Equals(device.HardwareId, ExpectedHardwareId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(device.Provider, ExpectedProvider, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The active display device is not the expected SudoVDA device.");
        }
        _ = RequirePublishedInf(device.PublishedInf);
        return device;
    }

    private static string RequirePublishedInf(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !PublishedInfPattern().IsMatch(value))
        {
            throw new InvalidDataException("Published SudoVDA INF identity is invalid.");
        }
        return value;
    }

    private static string RequireAbsoluteDirectory(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
        {
            throw new InvalidDataException("A protected absolute driver package directory is required.");
        }
        return Path.GetFullPath(value);
    }

    private static void RequireUsableRollbackPackage(string root)
    {
        string[] files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => new FileInfo(path).Length > 0)
            .ToArray();
        int infCount = files.Count(path =>
            string.Equals(Path.GetExtension(path), ".inf", StringComparison.OrdinalIgnoreCase));
        bool hasCatalog = files.Any(path =>
            string.Equals(Path.GetExtension(path), ".cat", StringComparison.OrdinalIgnoreCase));
        bool hasBinary = files.Any(path =>
            string.Equals(Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase));
        if (infCount != 1 || !hasCatalog || !hasBinary)
        {
            throw new InvalidDataException(
                "The exported SudoVDA rollback package is incomplete.");
        }
    }

    private static void RequireExitCode(
        PnpUtilResult result,
        int expected,
        string diagnostic) =>
        RequireExitCode(result, [expected], diagnostic);

    private static void RequireExitCode(
        PnpUtilResult result,
        IReadOnlyCollection<int> expected,
        string diagnostic)
    {
        if (!expected.Contains(result.ExitCode))
        {
            throw new IOException($"{diagnostic} ExitCode={result.ExitCode}.");
        }
    }

    private static async Task<string> HashFileAsync(string path)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 131_072,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, CancellationToken.None)
            .ConfigureAwait(false));
    }

    [GeneratedRegex("^oem[0-9]+\\.inf$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PublishedInfPattern();
}

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Beacon.HostAgent.DriverUpdates;

internal sealed class WindowsSudoVdaSignatureVerifier : ISudoVdaSignatureVerifier
{
    private static readonly Guid GenericVerifyV2 = new(
        "00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    public SudoVdaSignatureEvidence Verify(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Authenticode verification requires Windows.");
        }
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new SudoVdaSignatureEvidence(false, string.Empty, string.Empty, "file-unavailable");
        }

        string fullPath = Path.GetFullPath(path);
        nint fileInfoPointer = nint.Zero;
        try
        {
            var fileInfo = new WinTrustFileInfo
            {
                Size = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                FilePath = fullPath
            };
            fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, fDeleteOld: false);
            var trustData = new WinTrustData
            {
                Size = (uint)Marshal.SizeOf<WinTrustData>(),
                UiChoice = 2,
                RevocationChecks = 0,
                UnionChoice = 1,
                FileInfo = fileInfoPointer,
                StateAction = 0,
                ProviderFlags = 0x00001000,
                UiContext = 0
            };
            Guid action = GenericVerifyV2;
            int trustResult = WinVerifyTrust(new nint(-1), ref action, ref trustData);
            if (trustResult != 0)
            {
                return new SudoVdaSignatureEvidence(
                    false,
                    string.Empty,
                    string.Empty,
                    $"wintrust-0x{trustResult:X8}");
            }

#pragma warning disable SYSLIB0057
            using X509Certificate certificate = X509Certificate.CreateFromSignedFile(fullPath);
#pragma warning restore SYSLIB0057
            return new SudoVdaSignatureEvidence(
                true,
                certificate.Subject,
                certificate.GetCertHashString(),
                "valid");
        }
        catch (CryptographicException error)
        {
            return new SudoVdaSignatureEvidence(
                false,
                string.Empty,
                string.Empty,
                $"certificate-error-0x{error.HResult:X8}");
        }
        finally
        {
            if (fileInfoPointer != nint.Zero)
            {
                Marshal.DestroyStructure<WinTrustFileInfo>(fileInfoPointer);
                Marshal.FreeHGlobal(fileInfoPointer);
            }
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int WinVerifyTrust(
        nint window,
        ref Guid action,
        ref WinTrustData trustData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint Size;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string FilePath;

        public nint FileHandle;
        public nint KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint Size;
        public nint PolicyCallbackData;
        public nint SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public nint FileInfo;
        public uint StateAction;
        public nint StateData;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? UrlReference;

        public uint ProviderFlags;
        public uint UiContext;
        public nint SignatureSettings;
    }
}

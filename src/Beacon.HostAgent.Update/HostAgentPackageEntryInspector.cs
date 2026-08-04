using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Beacon.HostAgent.Update;

public interface IHostAgentPackageEntryInspector
{
    bool IsReparsePoint(string path);

    bool HasAlternateDataStream(string path);
}

internal sealed class WindowsHostAgentPackageEntryInspector : IHostAgentPackageEntryInspector
{
    private static readonly nint InvalidHandleValue = new(-1);

    public bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    public bool HasAlternateDataStream(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        nint handle = FindFirstStreamW(path, 0, out FindStreamData data, 0);
        if (handle == InvalidHandleValue)
        {
            int error = Marshal.GetLastWin32Error();
            if (error is 2 or 38)
            {
                return false;
            }
            throw new Win32Exception(error, "Unable to inspect Host Agent package data streams.");
        }

        try
        {
            do
            {
                if (!string.Equals(data.StreamName, "::$DATA", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            while (FindNextStreamW(handle, out data));

            int error = Marshal.GetLastWin32Error();
            if (error != 38)
            {
                throw new Win32Exception(
                    error,
                    "Unable to enumerate Host Agent package data streams.");
            }
            return false;
        }
        finally
        {
            _ = FindClose(handle);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint FindFirstStreamW(
        string lpFileName,
        int infoLevel,
        out FindStreamData lpFindStreamData,
        int dwFlags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindNextStreamW(
        nint hFindStream,
        out FindStreamData lpFindStreamData);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindClose(nint hFindFile);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FindStreamData
    {
        public long StreamSize;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 296)]
        public string StreamName;
    }
}

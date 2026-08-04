namespace Beacon.HostAgent.Bootstrap;

internal static class BootstrapDiagnostics
{
    private static readonly Lock Gate = new();

    public static void Write(string message)
    {
        try
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Beacon",
                "HostAgent",
                "Logs");
            Directory.CreateDirectory(root);
            string line = $"{DateTimeOffset.UtcNow:O} pid={Environment.ProcessId} {message}{Environment.NewLine}";
            lock (Gate)
            {
                File.AppendAllText(Path.Combine(root, "host-agent-bootstrap.log"), line);
            }
        }
        catch (Exception)
        {
        }
    }
}

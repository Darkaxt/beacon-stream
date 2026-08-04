namespace Beacon.HostAgent.Contracts;

public static class HostAgentPipeName
{
    public static string Create(string ownerSid)
    {
        if (string.IsNullOrWhiteSpace(ownerSid))
        {
            throw new ArgumentException("Host Agent owner SID is required.", nameof(ownerSid));
        }

        return $"beacon-host-agent-{ownerSid.Trim()}-v{HostAgentProtocol.CurrentVersion}";
    }
}

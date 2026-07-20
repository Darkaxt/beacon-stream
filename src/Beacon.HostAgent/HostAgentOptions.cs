using System.Security.Principal;

namespace Beacon.HostAgent;

internal sealed record HostAgentOptions(SecurityIdentifier Owner)
{
    public static HostAgentOptions Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count != 2
            || !string.Equals(arguments[0], "--owner-sid", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Usage: Beacon.HostAgent.exe --owner-sid <Windows SID>.",
                nameof(arguments));
        }

        try
        {
            return new HostAgentOptions(new SecurityIdentifier(arguments[1]));
        }
        catch (Exception error) when (error is ArgumentException or SystemException)
        {
            throw new ArgumentException("The Host Agent owner SID is invalid.", nameof(arguments), error);
        }
    }
}

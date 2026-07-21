using System.Security.Principal;

namespace Beacon.HostAgent.Bootstrap;

internal sealed record BootstrapOptions(SecurityIdentifier Owner)
{
    public static BootstrapOptions Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count != 2
            || !string.Equals(arguments[0], "--owner-sid", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Usage: Beacon.HostAgent.Bootstrap.exe --owner-sid <Windows SID>.",
                nameof(arguments));
        }
        try
        {
            return new BootstrapOptions(new SecurityIdentifier(arguments[1]));
        }
        catch (Exception error) when (error is ArgumentException or SystemException)
        {
            throw new ArgumentException("Bootstrap owner SID is invalid.", nameof(arguments), error);
        }
    }
}

using System.Security.Principal;

namespace Beacon.HostAgent;

internal sealed record HostAgentOptions(
    SecurityIdentifier Owner,
    string? BootstrapReadyPipe,
    string? VersionId)
{
    public static HostAgentOptions Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count is not (2 or 6) || arguments.Count % 2 != 0)
        {
            throw new ArgumentException(
                "Usage: Beacon.HostAgent.exe --owner-sid <Windows SID>.",
                nameof(arguments));
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < arguments.Count; index += 2)
        {
            string name = arguments[index];
            if (name is not ("--owner-sid" or "--bootstrap-ready-pipe" or "--version-id")
                || !values.TryAdd(name, arguments[index + 1]))
            {
                throw new ArgumentException(
                    "Host Agent arguments contain an unknown or duplicate option.",
                    nameof(arguments));
            }
        }
        if (!values.TryGetValue("--owner-sid", out string? ownerSid))
        {
            throw new ArgumentException("Host Agent owner SID is required.", nameof(arguments));
        }

        bool hasReadyPipe = values.TryGetValue("--bootstrap-ready-pipe", out string? readyPipe);
        bool hasVersion = values.TryGetValue("--version-id", out string? versionId);
        if (hasReadyPipe != hasVersion
            || (hasReadyPipe && (!IsSafeToken(readyPipe!) || !IsSafeToken(versionId!))))
        {
            throw new ArgumentException(
                "Host Agent bootstrap readiness arguments are incomplete or invalid.",
                nameof(arguments));
        }

        try
        {
            return new HostAgentOptions(
                new SecurityIdentifier(ownerSid),
                readyPipe,
                versionId);
        }
        catch (Exception error) when (error is ArgumentException or SystemException)
        {
            throw new ArgumentException("The Host Agent owner SID is invalid.", nameof(arguments), error);
        }
    }

    private static bool IsSafeToken(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 200
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '.');
}

namespace Beacon.HostAgent.Control;

internal enum HostAgentControlCommand
{
    Status,
    Install,
    Query
}

internal sealed record HostAgentControlOptions(
    HostAgentControlCommand Command,
    string? PackageId,
    Guid? TransactionId)
{
    public static HostAgentControlOptions Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count == 1
            && string.Equals(arguments[0], "status", StringComparison.Ordinal))
        {
            return new HostAgentControlOptions(
                HostAgentControlCommand.Status,
                PackageId: null,
                TransactionId: null);
        }
        if (arguments.Count == 3
            && string.Equals(arguments[0], "query", StringComparison.Ordinal)
            && string.Equals(arguments[1], "--transaction-id", StringComparison.Ordinal)
            && Guid.TryParseExact(arguments[2], "D", out Guid queryTransaction)
            && queryTransaction != Guid.Empty)
        {
            return new HostAgentControlOptions(
                HostAgentControlCommand.Query,
                PackageId: null,
                queryTransaction);
        }
        if (arguments.Count == 5
            && string.Equals(arguments[0], "install", StringComparison.Ordinal))
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int index = 1; index < arguments.Count; index += 2)
            {
                if (arguments[index] is not ("--package-id" or "--transaction-id")
                    || !values.TryAdd(arguments[index], arguments[index + 1]))
                {
                    throw Invalid();
                }
            }
            if (values.TryGetValue("--package-id", out string? packageId)
                && IsSafePackageId(packageId)
                && values.TryGetValue("--transaction-id", out string? transactionValue)
                && Guid.TryParseExact(transactionValue, "D", out Guid transactionId)
                && transactionId != Guid.Empty)
            {
                return new HostAgentControlOptions(
                    HostAgentControlCommand.Install,
                    packageId,
                    transactionId);
            }
        }
        throw Invalid();
    }

    private static bool IsSafePackageId(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 128
        && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '.');

    private static ArgumentException Invalid() => new(
        "Usage: status | install --package-id <id> --transaction-id <guid> | query --transaction-id <guid>.");
}

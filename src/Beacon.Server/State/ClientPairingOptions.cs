namespace Beacon.Server.State;

public sealed record ClientPairingOptions(string? Token)
{
    public bool Enabled => !string.IsNullOrWhiteSpace(Token);

    public bool Allows(string? submittedToken) =>
        Enabled &&
        !string.IsNullOrWhiteSpace(submittedToken) &&
        string.Equals(Token, submittedToken, StringComparison.Ordinal);
}

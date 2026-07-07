using Beacon.Core.Clients;

namespace Beacon.Core.Displays;

public sealed record DisplayLease(
    string DisplayId,
    ClientId ClientId,
    int Width,
    int Height,
    int RefreshHz)
{
    public static string CreateDisplayId(ClientId clientId) => $"client-{clientId.Value}";
}

public sealed record DisplayLeaseResult(bool Success, DisplayLease? Lease, string? Error);

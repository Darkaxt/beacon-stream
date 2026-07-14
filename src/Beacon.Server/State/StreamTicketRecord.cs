namespace Beacon.Server.State;

public sealed class StreamTicketRecord
{
    public required string TicketId { get; init; }

    public required byte[] TicketHash { get; init; }

    public required string ClientId { get; init; }

    public required string SessionId { get; init; }

    public required ulong PlanRevision { get; init; }

    public required byte[] RuntimeInstanceId { get; init; }

    public required long RuntimeGeneration { get; init; }

    public required DateTimeOffset IssuedAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public bool Consumed { get; set; }

    public bool Revoked { get; set; }

    public bool RuntimeRevocationSent { get; set; }

    public override string ToString() =>
        $"Stream ticket {TicketId} for {ClientId}/{SessionId} revision {PlanRevision}: " +
        $"consumed={Consumed}, revoked={Revoked}.";
}

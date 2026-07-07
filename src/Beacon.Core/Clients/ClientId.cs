namespace Beacon.Core.Clients;

public readonly record struct ClientId(string Value)
{
    public override string ToString() => Value;
}

using System.Text.Json.Serialization;

namespace Beacon.Core.Clients;

[JsonConverter(typeof(ClientIdJsonConverter))]
public readonly record struct ClientId(string Value)
{
    public override string ToString() => Value;
}

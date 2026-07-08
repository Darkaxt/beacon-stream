using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beacon.Core.Clients;

public sealed class ClientIdJsonConverter : JsonConverter<ClientId>
{
    public override ClientId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new ClientId(reader.GetString() ?? string.Empty);
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            if (document.RootElement.TryGetProperty("value", out JsonElement value) &&
                value.ValueKind == JsonValueKind.String)
            {
                return new ClientId(value.GetString() ?? string.Empty);
            }
        }

        throw new JsonException("ClientId must be a string.");
    }

    public override void Write(Utf8JsonWriter writer, ClientId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beacon.HostAgent.Contracts;

public enum HostAgentOperation
{
    GetStatus,
    HoldDisplayLease,
    ReleaseDisplayLease,
    CreateVirtualDisplay,
    QueryTopology,
    SetVirtualPrimary,
    RestorePhysicalPrimary,
    RemoveVirtualDisplay,
    QueryHdrCapability,
    InstallStagedSudoVdaPackage,
    QuerySudoVdaUpdate,
    InstallStagedHostAgentPackage,
    QueryHostAgentUpdate
}

public sealed record HostAgentRequest(
    int ProtocolVersion,
    Guid RequestId,
    HostAgentOperation Operation,
    JsonElement Payload);

public sealed record HostAgentResponse(
    int ProtocolVersion,
    Guid RequestId,
    bool Success,
    string ResultCode,
    string Diagnostic,
    JsonElement Payload);

public sealed record EmptyHostAgentPayload;

public sealed record HoldDisplayLeasePayload(string DisplayId);

public static class HostAgentProtocol
{
    public const int CurrentVersion = 1;
    public const int MaximumFrameBytes = 65_536;

    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public static JsonElement CreatePayload<T>(T payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return JsonSerializer.SerializeToElement(payload, SerializerOptions);
    }

    public static T ReadPayload<T>(JsonElement payload) =>
        payload.Deserialize<T>(SerializerOptions)
        ?? throw new JsonException($"Host Agent payload '{typeof(T).Name}' was null.");

    public static byte[] SerializeRequest(HostAgentRequest request) =>
        JsonSerializer.SerializeToUtf8Bytes(request, SerializerOptions);

    public static HostAgentRequest DeserializeRequest(ReadOnlySpan<byte> value) =>
        JsonSerializer.Deserialize<HostAgentRequest>(value, SerializerOptions)
        ?? throw new JsonException("Host Agent request was null.");

    public static byte[] SerializeResponse(HostAgentResponse response) =>
        JsonSerializer.SerializeToUtf8Bytes(response, SerializerOptions);

    public static HostAgentResponse DeserializeResponse(ReadOnlySpan<byte> value) =>
        JsonSerializer.Deserialize<HostAgentResponse>(value, SerializerOptions)
        ?? throw new JsonException("Host Agent response was null.");

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(
            JsonNamingPolicy.CamelCase,
            allowIntegerValues: false));
        return options;
    }
}

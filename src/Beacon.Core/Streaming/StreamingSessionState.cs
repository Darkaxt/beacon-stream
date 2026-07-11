using System.Text.Json.Serialization;

namespace Beacon.Core.Streaming;

public sealed record StreamingSessionState(
    string SessionId,
    string ClientId,
    string AppId,
    string DisplayId,
    string Codec,
    int Fps,
    int InitialBitrateMbps,
    string State,
    string? Error,
    int? ActiveListenerPort = null,
    [property: JsonIgnore] Guid RuntimeGeneration = default);
